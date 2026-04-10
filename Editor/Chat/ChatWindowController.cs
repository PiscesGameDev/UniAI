using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 对话窗口业务控制器 — 管理会话、Client、消息流和上下文
    /// 与 UI 通过事件/回调通信，不依赖 EditorWindow
    /// </summary>
    internal class ChatWindowController : IDisposable
    {
        // ─── 事件（UI 订阅） ───

        /// <summary>通用状态变更，UI 应调用 Repaint</summary>
        public event Action OnStateChanged;

        /// <summary>流式状态切换</summary>
        public event Action<bool> OnStreamingChanged;

        /// <summary>需要滚动到底部</summary>
        public event Action OnScrollToBottom;

        /// <summary>AI 头像变更</summary>
        public event Action<Texture2D> OnAIAvatarChanged;

        // ─── 状态（UI 只读访问） ───

        public AIConfig Config => _config;
        public ChatHistoryManager History => _history;
        public ChatSession ActiveSession => _activeSession;
        public bool IsStreaming => _isStreaming;
        public string CurrentModelId => _currentModelId;
        public string[] ModelNames => _modelNames;
        public int SelectedModelIndex => _selectedModelIndex;
        public IReadOnlyList<AgentDefinition> AvailableAgents => _availableAgents;

        /// <summary>当前 Agent 的 MCP 连接状态文本（null 表示未启用 MCP）</summary>
        public string McpStatus => _mcpStatus;

        // ─── 内部状态 ───

        private AIConfig _config;
        private AIClient _client;
        private IConversationRunner _runner;
        private ContextPipeline _contextPipeline;
        private ChatHistoryManager _history;
        private ChatSession _activeSession;
        private bool _isStreaming;
        private CancellationTokenSource _streamCts;

        private int _selectedModelIndex;
        private string _currentModelId;
        private string[] _modelNames;
        private List<string> _modelEntries;
        private List<AgentDefinition> _availableAgents;
        private string _mcpStatus;
        private UniTask _mcpInitTask;

        // ─── 初始化 ───

        public void Initialize()
        {
            _config = AIConfigManager.LoadConfig();
            _history = new ChatHistoryManager(new EditorChatHistoryStorage());
            _history.Load();

            var prefs = AIConfigManager.Prefs;
            _currentModelId = prefs.LastSelectedModelId;

            RebuildModelCache();
            RebuildAgentCache();
            EnsureRunner();
        }

        // ─── 会话管理 ───

        /// <summary>
        /// 创建新会话，可选传入 AgentDefinition 锁定会话类型
        /// </summary>
        public void CreateNewSession(AgentDefinition agent = null)
        {
            string modelId = ResolveModelForAgent(agent);
            _activeSession = ChatSession.Create(modelId);
            _activeSession.AgentId = agent != null ? agent.Id : "";
            _history.Save(_activeSession);
            EnsureRunner();
            NotifyAIAvatarChanged();
            OnStateChanged?.Invoke();
        }

        public void SwitchToSession(ChatSession session)
        {
            _activeSession = session;

            // 恢复 Session 记录的模型选择
            if (!string.IsNullOrEmpty(session.ModelId) && _modelEntries != null)
            {
                for (int i = 0; i < _modelEntries.Count; i++)
                {
                    if (_modelEntries[i] == session.ModelId)
                    {
                        _selectedModelIndex = i;
                        _currentModelId = session.ModelId;
                        break;
                    }
                }
            }

            EnsureRunner();
            NotifyAIAvatarChanged();

            if (!string.IsNullOrEmpty(session.AgentId) && FindAgentById(session.AgentId) == null)
            {
                Debug.LogWarning(
                    $"[UniAI Chat] 会话 \"{session.Title}\" 关联的 Agent \"{session.AgentId}\" 已被删除，" +
                    "该会话将以纯 Chat 模式运行。");
            }

            OnStateChanged?.Invoke();
        }

        public void DeleteSession(string sessionId)
        {
            if (_activeSession != null && _activeSession.Id == sessionId)
                _activeSession = null;
            _history.Delete(sessionId);
            OnStateChanged?.Invoke();
        }

        public void DeleteAllSessions()
        {
            _activeSession = null;
            _history.DeleteAll();
            OnStateChanged?.Invoke();
        }

        // ─── 模型管理 ───

        /// <summary>
        /// 解析 Agent 会话应使用的模型：Agent 指定 > 当前选择 > 默认
        /// </summary>
        private string ResolveModelForAgent(AgentDefinition agent)
        {
            if (agent != null && !string.IsNullOrEmpty(agent.SpecifyModel))
            {
                // Agent 指定的模型在可用列表中，则切换到该模型
                if (_modelEntries != null)
                {
                    for (int i = 0; i < _modelEntries.Count; i++)
                    {
                        if (_modelEntries[i] == agent.SpecifyModel)
                        {
                            _selectedModelIndex = i;
                            _currentModelId = agent.SpecifyModel;
                            return _currentModelId;
                        }
                    }
                }
                // Agent 指定的模型不可用，打印警告，回退到当前模型
                Debug.LogWarning(
                    $"[UniAI Chat] Agent \"{agent.AgentName}\" 指定模型 \"{agent.SpecifyModel}\" 不在可用渠道中，已回退到默认模型。");
            }

            return _currentModelId ?? "";
        }

        public void SelectModel(int index)
        {
            if (_modelEntries == null || index < 0 || index >= _modelEntries.Count) return;
            if (index == _selectedModelIndex) return;

            _selectedModelIndex = index;
            _currentModelId = _modelEntries[index];
            EnsureRunner();

            if (_activeSession != null)
                _activeSession.ModelId = _currentModelId;

            AIConfigManager.Prefs.LastSelectedModelId = _currentModelId;
            AIConfigManager.SavePrefs();

            OnStateChanged?.Invoke();
        }

        // ─── 消息发送与流式响应 ───

        /// <summary>
        /// 删除单条消息。若为 Assistant 消息，同时删除其后紧邻的 ToolCall 消息（同一轮对话）
        /// </summary>
        public void DeleteMessage(ChatMessage message)
        {
            if (_activeSession == null) return;

            var messages = _activeSession.Messages;
            int index = messages.IndexOf(message);
            if (index < 0) return;

            if (!message.IsToolCall && message.Role == AIRole.Assistant)
            {
                // 计算该 Assistant 消息之后连续的 ToolCall 数量
                int removeCount = 1;
                for (int i = index + 1; i < messages.Count; i++)
                {
                    if (messages[i].IsToolCall)
                        removeCount++;
                    else
                        break;
                }
                messages.RemoveRange(index, removeCount);
            }
            else
            {
                messages.RemoveAt(index);
            }

            _history.Save(_activeSession);
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// 重新生成：删除指定 AI 消息及其后所有消息，重新发送最后一条用户消息
        /// </summary>
        public void RegenerateFromMessage(ChatMessage message, ContextCollector.ContextSlot contextSlots)
        {
            if (_activeSession == null || _isStreaming) return;

            int index = _activeSession.Messages.IndexOf(message);
            if (index < 0) return;

            // 找到该 index 之前最近的 User 消息
            string lastUserContent = null;
            for (int i = index - 1; i >= 0; i--)
            {
                var m = _activeSession.Messages[i];
                if (!m.IsToolCall && m.Role == AIRole.User)
                {
                    lastUserContent = m.Content;
                    break;
                }
            }

            if (string.IsNullOrEmpty(lastUserContent)) return;

            // 删除从 index 开始到末尾的所有消息
            _activeSession.Messages.RemoveRange(index, _activeSession.Messages.Count - index);
            _history.Save(_activeSession);
            OnStateChanged?.Invoke();

            // 重新触发流式响应
            StreamResponseAsync(contextSlots).Forget();
        }

        public void SendMessage(string text, ContextCollector.ContextSlot contextSlots)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            if (_activeSession == null)
                CreateNewSession();
            if (_activeSession == null) return;

            _activeSession.Messages.Add(new ChatMessage
            {
                Role = AIRole.User,
                Content = text.Trim(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            OnScrollToBottom?.Invoke();

            StreamResponseAsync(contextSlots).Forget();
        }

        public void CancelStream()
        {
            _streamCts?.Cancel();
        }

        private async UniTaskVoid StreamResponseAsync(ContextCollector.ContextSlot contextSlots)
        {
            if (_runner == null)
            {
                AddErrorMessage("未配置 AI 提供商，请打开设置进行配置。");
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
            _activeSession.Messages.Add(assistantMsg);

            EditorAgentGuard guard = null;
            try
            {
                if (_runner is AIAgentRunner { HasTools: true })
                {
                    guard = new EditorAgentGuard();
                    guard.Lock();
                }

                var aiMessages = BuildAIMessages();

                // 注入 Unity 上下文到消息列表
                string context = ContextCollector.Collect(contextSlots);
                if (!string.IsNullOrEmpty(context))
                {
                    aiMessages.Insert(0, AIMessage.User($"[Unity Context]\n{context}"));
                    aiMessages.Insert(1, AIMessage.Assistant("收到上下文信息，我会结合这些信息回答你的问题。"));
                }

                // 上下文窗口管理
                if (_contextPipeline != null && _config?.General?.ContextWindow != null)
                {
                    string systemPrompt = null;
                    if (_runner is AIAgentRunner)
                    {
                        var agent = FindAgentById(_activeSession?.AgentId);
                        systemPrompt = agent?.SystemPrompt;
                    }
                    aiMessages = await _contextPipeline.ProcessAsync(
                        aiMessages, systemPrompt, _currentModelId,
                        _config.General.ContextWindow, _activeSession, _streamCts.Token);
                }

                var ct = _streamCts.Token;
                await foreach (var evt in _runner.RunStreamAsync(aiMessages, ct: ct))
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
                            _activeSession.Messages.Add(new ChatMessage
                            {
                                Role = AIRole.Assistant,
                                IsToolCall = true,
                                ToolUseId = evt.ToolCall?.Id,
                                ToolName = evt.ToolCall?.Name ?? "unknown",
                                ToolArguments = evt.ToolCall?.Arguments ?? "",
                                Content = $"调用工具: {evt.ToolCall?.Name}",
                                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            });
                            OnScrollToBottom?.Invoke();
                            OnStateChanged?.Invoke();
                            break;

                        case AgentEventType.ToolCallResult:
                        {
                            for (int i = _activeSession.Messages.Count - 1; i >= 0; i--)
                            {
                                var m = _activeSession.Messages[i];
                                if (m.IsToolCall && m.ToolName == evt.ToolName && string.IsNullOrEmpty(m.ToolResult))
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

                _history.Save(_activeSession);
                OnStateChanged?.Invoke();

                if (_activeSession.Messages.Count >= 1 &&
                    (string.IsNullOrEmpty(_activeSession.Title) || _activeSession.Title == "新对话"))
                    GenerateTitle();
            }
        }

        // ─── 辅助方法 ───

        public AgentDefinition FindAgentById(string agentId)
        {
            if (string.IsNullOrEmpty(agentId) || _availableAgents == null)
                return null;

            foreach (var agent in _availableAgents)
            {
                if (agent != null && agent.Id == agentId)
                    return agent;
            }

            return null;
        }

        public void ExecuteQuickAction(ContextCollector.ContextSlot requiredSlot, string message,
            ref ContextCollector.ContextSlot contextSlots)
        {
            if (requiredSlot != ContextCollector.ContextSlot.None)
                contextSlots |= requiredSlot;

            if (_activeSession == null)
                CreateNewSession();

            SendMessage(message, contextSlots);
        }

        public void ReloadConfig()
        {
            _config = AIConfigManager.LoadConfig();
            RebuildModelCache();
            RebuildAgentCache();
            EnsureRunner();
            OnStateChanged?.Invoke();
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

        /// <summary>
        /// 匹配 Markdown 图片语法 ![alt](data:image/...;base64,...) 的正则。
        /// 用于在构建 AI 消息时剥离巨大的 base64 图片数据，避免浪费 token。
        /// </summary>
        private static readonly Regex _dataImageRegex = new(
            @"!\[([^\]]*)\]\(data:image/[^)]+\)",
            RegexOptions.Compiled);

        private List<AIMessage> BuildAIMessages()
        {
            var messages = new List<AIMessage>();
            AIMessage pendingAssistant = null;

            foreach (var msg in _activeSession.Messages)
            {
                if (msg.IsStreaming && string.IsNullOrEmpty(msg.Content))
                    continue;

                if (msg.IsToolCall)
                {
                    if (pendingAssistant == null)
                    {
                        pendingAssistant = new AIMessage { Role = AIRole.Assistant, Contents = new List<AIContent>() };
                        messages.Add(pendingAssistant);
                    }
                    pendingAssistant.Contents.Add(new AIToolUseContent
                    {
                        Id = msg.ToolUseId,
                        Name = msg.ToolName,
                        Arguments = msg.ToolArguments
                    });

                    if (!string.IsNullOrEmpty(msg.ToolResult))
                    {
                        messages.Add(AIMessage.ToolResult(msg.ToolUseId, msg.ToolResult, msg.IsToolError));
                    }
                    continue;
                }

                pendingAssistant = null;

                string content = msg.Content;

                // 剥离 Assistant 消息中的 base64 图片数据，替换为占位描述
                if (msg.Role == AIRole.Assistant && !string.IsNullOrEmpty(content)
                    && content.Contains("data:image/"))
                {
                    content = _dataImageRegex.Replace(content, m =>
                    {
                        string alt = m.Groups[1].Value;
                        return string.IsNullOrWhiteSpace(alt)
                            ? "[已生成图片]"
                            : $"[已生成图片: {alt}]";
                    });
                }

                if (msg.Role == AIRole.User)
                {
                    messages.Add(AIMessage.User(content));
                }
                else
                {
                    var assistantMsg = AIMessage.Assistant(content);
                    messages.Add(assistantMsg);
                    pendingAssistant = assistantMsg;
                }
            }

            return messages;
        }

        private void EnsureRunner()
        {
            EnsureClient();

            // 释放旧的 runner（如有 MCP 连接需要清理）
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

            var agent = FindAgentById(_activeSession?.AgentId);
            if (agent != null)
            {
                var agentRunner = new AIAgentRunner(_client, agent)
                {
                    ToolTimeoutSeconds = EditorPreferences.instance.ToolTimeout,
                    McpSettings = _config.General.Mcp
                };
                _runner = agentRunner;

                if (agent.HasMcpServers && EditorPreferences.instance.McpAutoConnect)
                    _mcpInitTask = InitializeMcpAsync(agentRunner, agent);
            }
            else
            {
                _runner = new ChatRunner(_client);
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
                if (EditorPreferences.instance.McpResourceInjection
                    && agentRunner.McpManager != null && _contextPipeline != null)
                {
                    foreach (var provider in agentRunner.McpManager.GetResourceProviders())
                        _contextPipeline.AddProvider(provider);
                }

                int connected = agentRunner.McpManager?.Clients.Count ?? 0;
                int total = 0;
                foreach (var cfg in agent.McpServers)
                    if (cfg != null && cfg.Enabled) total++;

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

        private void EnsureClient()
        {
            if (_modelEntries == null || _modelEntries.Count == 0)
            {
                _client = null;
                return;
            }

            if (_selectedModelIndex >= _modelEntries.Count)
                _selectedModelIndex = 0;

            _currentModelId = _modelEntries[_selectedModelIndex];

            try
            {
                _client = AIClient.Create(_config, _currentModelId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI Chat] Failed to create client: {e.Message}");
                _client = null;
            }
        }

        private void RebuildModelCache()
        {
            _modelEntries = _config.GetAllModels();
            _modelNames = new string[_modelEntries.Count];
            for (int i = 0; i < _modelEntries.Count; i++)
            {
                var entry = ModelRegistry.Get(_modelEntries[i]);
                string vendor = !string.IsNullOrEmpty(entry?.Vendor) ? entry.Vendor : "Unknown";
                _modelNames[i] = $"{vendor}/{_modelEntries[i]}";
            }

            if (!string.IsNullOrEmpty(_currentModelId))
            {
                for (int i = 0; i < _modelEntries.Count; i++)
                {
                    if (_modelEntries[i] == _currentModelId)
                    {
                        _selectedModelIndex = i;
                        return;
                    }
                }
            }

            _selectedModelIndex = 0;
            _currentModelId = _modelEntries.Count > 0 ? _modelEntries[0] : null;
        }

        private void RebuildAgentCache()
        {
            _availableAgents = AgentManager.GetAllAgents();
        }

        private void AddErrorMessage(string error)
        {
            _activeSession?.Messages.Add(new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = $"[错误] {error}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            OnScrollToBottom?.Invoke();
            OnStateChanged?.Invoke();
        }

        private void GenerateTitle()
        {
            if (_activeSession == null || _activeSession.Messages.Count < 1) return;

            string userText = null;
            foreach (var msg in _activeSession.Messages)
            {
                if (!msg.IsToolCall && msg.Role == AIRole.User)
                {
                    userText = msg.Content;
                    break;
                }
            }

            if (string.IsNullOrEmpty(userText)) return;

            _activeSession.Title = userText.Length <= 15 ? userText : userText.Substring(0, 15) + "…";
            _history.Save(_activeSession);
            OnStateChanged?.Invoke();
        }

        private void NotifyAIAvatarChanged()
        {
            var agent = FindAgentById(_activeSession?.AgentId);
            Texture2D avatar = agent != null ? agent.Icon : AIConfigManager.Prefs.AiAvatar;
            OnAIAvatarChanged?.Invoke(avatar);
        }
    }
}
