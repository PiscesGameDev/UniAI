using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// 对话编排设置
    /// </summary>
    public class ChatOrchestratorSettings
    {
        public float ToolTimeoutSeconds = 30f;
        public bool McpAutoConnect = true;
        public bool McpResourceInjection = true;
    }

    /// <summary>
    /// 对话编排器 — 管理 Runner/Client/ContextPipeline 生命周期和流式对话。
    /// Runtime 自足，Editor 通过委托注入平台特定行为。
    /// </summary>
    public class ChatOrchestrator : IDisposable
    {
        // ─── 事件 ───

        public event Action<bool> OnStreamingChanged;
        public event Action OnScrollToBottom;
        public event Action OnStateChanged;

        // ─── 可注入行为 ───

        /// <summary>
        /// 创建 Tool 执行守卫（Editor 注入 EditorAgentGuard，Runtime 返回 null）
        /// </summary>
        public Func<IDisposable> GuardFactory;

        /// <summary>
        /// 采集外部上下文（Editor 注入 ContextCollector.Collect）
        /// </summary>
        public Func<int, string> ContextCollector;

        // ─── 状态 ───

        private AIClient _client;
        private IConversationRunner _runner;
        private ContextPipeline _contextPipeline;
        private bool _isStreaming;
        private CancellationTokenSource _streamCts;
        private string _mcpStatus;
        private UniTask _mcpInitTask;
        private ChatOrchestratorSettings _settings;
        private string _modelId;
        private AIConfig _lastConfig;
        private string _lastAgentId;

        // ─── 属性 ───

        public bool IsStreaming => _isStreaming;
        public string McpStatus => _mcpStatus;

        // ─── Runner 管理 ───

        /// <summary>
        /// 根据当前配置重建 Client / Runner / ContextPipeline（仅在必要时重建）
        /// </summary>
        public void EnsureRunner(AIConfig config, ModelSelector modelSelector, AgentDefinition agent,
            ChatOrchestratorSettings settings = null)
        {
            _settings = settings ?? new ChatOrchestratorSettings();
            _modelId = modelSelector.EnsureValid();

            // 仅在 Client 或 Agent 发生变化时重建，切模型不触发重建
            if (_client != null && !NeedsRebuild(config, agent))
                return;

            _lastConfig = config;
            _lastAgentId = agent != null ? agent.Id : null;

            EnsureClient(config);

            if (_runner is IDisposable disposableRunner)
            {
                try { disposableRunner.Dispose(); }
                catch (Exception e) { Debug.LogWarning($"[UniAI Chat] Dispose runner failed: {e.Message}"); }
            }

            _runner = null;
            _mcpStatus = null;
            _mcpInitTask = default;

            if (_client == null)
            {
                _contextPipeline = null;
                return;
            }

            _contextPipeline = new ContextPipeline(_client);

            if (agent != null)
            {
                var agentRunner = new AIAgentRunner(_client, agent)
                {
                    ToolTimeoutSeconds = _settings.ToolTimeoutSeconds,
                    McpSettings = config.General.Mcp
                };
                _runner = agentRunner;

                if (agent.HasMcpServers && _settings.McpAutoConnect)
                    _mcpInitTask = InitializeMcpAsync(agentRunner, agent);
            }
            else
            {
                _runner = new ChatRunner(_client);
            }
        }

        /// <summary>
        /// 仅更新模型选择，不重建 Client / Runner
        /// </summary>
        public void UpdateModel(ModelSelector modelSelector)
        {
            _modelId = modelSelector.EnsureValid();
        }

        private bool NeedsRebuild(AIConfig config, AgentDefinition agent)
        {
            if (_lastConfig != config) return true;
            string agentId = agent != null ? agent.Id : null;
            return agentId != _lastAgentId;
        }

        /// <summary>
        /// 发送消息并驱动流式响应
        /// </summary>
        public async UniTaskVoid StreamResponseAsync(
            ChatSession session, int contextSlots,
            AIConfig config, string modelId,
            Func<string, AgentDefinition> findAgent,
            ChatHistoryManager history, Action generateTitle)
        {
            if (_runner == null)
            {
                AddErrorMessage(session, "未配置 AI 提供商，请打开设置进行配置。");
                return;
            }

            _isStreaming = true;
            OnStreamingChanged?.Invoke(true);
            _streamCts = new CancellationTokenSource();

            // 等待 MCP 初始化完成（如果有），避免首次消息丢失工具
            try { await _mcpInitTask; }
            catch { /* 已在 InitializeMcpAsync 中记录 */ }

            var assistantMsg = new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = "",
                IsStreaming = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            session.Messages.Add(assistantMsg);

            IDisposable guard = null;
            try
            {
                if (_runner is AIAgentRunner { HasTools: true })
                    guard = GuardFactory?.Invoke();

                var aiMessages = session.BuildAIMessages();

                // 注入外部上下文到消息列表
                string context = ContextCollector?.Invoke(contextSlots);
                if (!string.IsNullOrEmpty(context))
                {
                    ContextPipeline.InjectContextPair(aiMessages, "Unity Context", context,
                        "收到上下文信息，我会结合这些信息回答你的问题。");
                }

                // 上下文窗口管理
                if (_contextPipeline != null && config?.General?.ContextWindow != null)
                {
                    string systemPrompt = null;
                    if (_runner is AIAgentRunner)
                    {
                        var agent = findAgent(session.AgentId);
                        systemPrompt = agent?.SystemPrompt;
                    }

                    aiMessages = await _contextPipeline.ProcessAsync(
                        aiMessages, systemPrompt, modelId,
                        config.General.ContextWindow, session, _streamCts.Token);
                }

                var ct = _streamCts.Token;
                var requestOverride = new AIRequest { Model = modelId };
                await foreach (var evt in _runner.RunStreamAsync(aiMessages, requestOverride, ct))
                {
                    if (ct.IsCancellationRequested) break;

                    switch (evt.Type)
                    {
                        case AgentEventType.TextDelta:
                            assistantMsg.Content += evt.Text;
                            OnScrollToBottom?.Invoke();
                            OnStateChanged?.Invoke();
                            break;

                        case AgentEventType.ToolCallStart:
                            session.Messages.Add(new ChatMessage
                            {
                                Role = AIRole.Assistant,
                                IsToolCall = true,
                                ToolUseId = evt.ToolCall?.Id,
                                ToolName = evt.ToolCall?.Name ?? "unknown",
                                ToolArguments = evt.ToolCall?.Arguments ?? "",
                                ReasoningContent = evt.ReasoningContent,
                                Content = $"调用工具: {evt.ToolCall?.Name}",
                                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            });
                            OnScrollToBottom?.Invoke();
                            OnStateChanged?.Invoke();
                            break;

                        case AgentEventType.ToolCallResult:
                        {
                            string toolUseId = evt.ToolCall?.Id;
                            for (int i = session.Messages.Count - 1; i >= 0; i--)
                            {
                                var m = session.Messages[i];
                                if (!m.IsToolCall || !string.IsNullOrEmpty(m.ToolResult))
                                    continue;

                                // 优先按 ToolUseId 精确匹配，回退到 ToolName 匹配
                                bool idMatch = !string.IsNullOrEmpty(toolUseId)
                                    && m.ToolUseId == toolUseId;
                                bool nameMatch = string.IsNullOrEmpty(toolUseId)
                                    && m.ToolName == evt.ToolName;

                                if (idMatch || nameMatch)
                                {
                                    m.ToolResult = evt.ToolResult;
                                    m.IsToolError = evt.IsToolError;
                                    break;
                                }
                            }

                            OnScrollToBottom?.Invoke();
                            OnStateChanged?.Invoke();
                            break;
                        }

                        case AgentEventType.TurnComplete:
                            if (evt.Usage != null)
                            {
                                assistantMsg.InputTokens += evt.Usage.InputTokens;
                                assistantMsg.OutputTokens += evt.Usage.OutputTokens;
                            }

                            break;

                        case AgentEventType.Error:
                            assistantMsg.Content += $"\n\n[错误: {evt.Text}]";
                            OnStateChanged?.Invoke();
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                assistantMsg.Content += "\n\n[已停止]";
            }
            catch (Exception e)
            {
                assistantMsg.Content += $"\n\n[错误: {e.Message}]";
                Debug.LogWarning($"[UniAI Chat] Stream error: {e}");
            }
            finally
            {
                guard?.Dispose();

                assistantMsg.IsStreaming = false;
                _isStreaming = false;
                OnStreamingChanged?.Invoke(false);
                _streamCts?.Dispose();
                _streamCts = null;

                history.Save(session);
                OnStateChanged?.Invoke();

                if (session.Messages.Count >= 1 &&
                    (string.IsNullOrEmpty(session.Title) || session.Title == "新对话"))
                    generateTitle();
            }
        }

        public void CancelStream()
        {
            _streamCts?.Cancel();
        }

        public void Dispose()
        {
            CancelStream();
            if (_runner is IDisposable disposableRunner)
            {
                try { disposableRunner.Dispose(); }
                catch { }
            }

            _runner = null;
        }

        // ─── 内部 ───

        private void EnsureClient(AIConfig config)
        {
            if (_modelId == null)
            {
                _client = null;
                return;
            }

            try
            {
                _client = AIClient.Create(config);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI Chat] Failed to create client: {e.Message}");
                _client = null;
            }
        }

        private async UniTask InitializeMcpAsync(AIAgentRunner agentRunner, AgentDefinition agent)
        {
            _mcpStatus = "MCP: 连接中...";
            OnStateChanged?.Invoke();

            try
            {
                await agentRunner.InitializeMcpAsync();

                // 把 MCP Resource 注入到 ContextPipeline
                if (_settings.McpResourceInjection
                    && agentRunner.McpManager != null && _contextPipeline != null)
                {
                    foreach (var provider in agentRunner.McpManager.GetResourceProviders())
                        _contextPipeline.AddProvider(provider);
                }

                int connected = agentRunner.McpManager?.Clients.Count ?? 0;
                int total = 0;
                foreach (var cfg in agent.McpServers)
                    if (cfg != null && cfg.Enabled)
                        total++;

                string summary = agentRunner.McpManager?.GetConnectionSummary() ?? "未连接";
                _mcpStatus = $"MCP: {connected}/{total} — {summary}";
            }
            catch (Exception e)
            {
                _mcpStatus = $"MCP: 初始化失败 — {e.Message}";
                Debug.LogWarning($"[UniAI Chat] MCP init failed: {e}");
            }
            finally
            {
                OnStateChanged?.Invoke();
            }
        }

        private void AddErrorMessage(ChatSession session, string error)
        {
            session?.Messages.Add(new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = $"[错误] {error}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            OnScrollToBottom?.Invoke();
            OnStateChanged?.Invoke();
        }
    }
}
