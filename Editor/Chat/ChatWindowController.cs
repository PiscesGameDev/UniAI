using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 对话窗口业务控制器 — Facade，协调 ModelSelector / StreamingController / ChatHistoryManager
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

        // ─── 子模块 ───

        private ModelSelector _modelSelector;
        private StreamingController _streaming;
        private ChatHistoryManager _history;
        private AIConfig _config;
        private ChatSession _activeSession;
        private List<AgentDefinition> _availableAgents;

        // ─── 状态（UI 只读访问） ───

        public AIConfig Config => _config;
        public ChatHistoryManager History => _history;
        public ChatSession ActiveSession => _activeSession;
        public bool IsStreaming => _streaming.IsStreaming;
        public string CurrentModelId => _modelSelector.CurrentModelId;
        public string[] ModelNames => _modelSelector.ModelNames;
        public int SelectedModelIndex => _modelSelector.SelectedModelIndex;
        public IReadOnlyList<AgentDefinition> AvailableAgents => _availableAgents;
        public string McpStatus => _streaming.McpStatus;

        // ─── 初始化 ───

        public void Initialize()
        {
            _config = AIConfigManager.LoadConfig();
            _history = new ChatHistoryManager(
                new FileChatHistoryStorage("Library/UniAI/History", AIConfigManager.Prefs.MaxHistorySessions));
            _history.Load();

            _modelSelector = new ModelSelector(AIConfigManager.Prefs.LastSelectedModelId);
            _modelSelector.RebuildCache(_config);

            _streaming = new StreamingController();
            _streaming.OnStreamingChanged += v => OnStreamingChanged?.Invoke(v);
            _streaming.OnScrollToBottom += () => OnScrollToBottom?.Invoke();
            _streaming.OnStateChanged += () => OnStateChanged?.Invoke();

            RebuildAgentCache();
            _streaming.EnsureRunner(_config, _modelSelector, FindAgentById(_activeSession?.AgentId));
        }

        // ─── 会话管理 ───

        /// <summary>
        /// 创建新会话，可选传入 AgentDefinition 锁定会话类型
        /// </summary>
        public void CreateNewSession(AgentDefinition agent = null)
        {
            string modelId = _modelSelector.ResolveForAgent(agent);
            _activeSession = ChatSession.Create(modelId);
            _activeSession.AgentId = agent != null ? agent.Id : "";
            _history.Save(_activeSession);
            _streaming.EnsureRunner(_config, _modelSelector, agent);
            NotifyAIAvatarChanged();
            OnStateChanged?.Invoke();
        }

        public void SwitchToSession(ChatSession session)
        {
            _activeSession = session;
            _modelSelector.RestoreFromSession(session);
            _streaming.EnsureRunner(_config, _modelSelector, FindAgentById(session.AgentId));
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

        public void SelectModel(int index)
        {
            if (!_modelSelector.Select(index)) return;

            _streaming.EnsureRunner(_config, _modelSelector, FindAgentById(_activeSession?.AgentId));

            if (_activeSession != null)
                _activeSession.ModelId = _modelSelector.CurrentModelId;

            AIConfigManager.Prefs.LastSelectedModelId = _modelSelector.CurrentModelId;
            AIConfigManager.SavePrefs();

            OnStateChanged?.Invoke();
        }

        // ─── 消息管理 ───

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
            if (_activeSession == null || IsStreaming) return;

            int index = _activeSession.Messages.IndexOf(message);
            if (index < 0) return;

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

            _activeSession.Messages.RemoveRange(index, _activeSession.Messages.Count - index);
            _history.Save(_activeSession);
            OnStateChanged?.Invoke();

            _streaming.StreamResponseAsync(
                _activeSession, contextSlots, _config, _modelSelector.CurrentModelId,
                FindAgentById, _history, GenerateTitle).Forget();
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

            _streaming.StreamResponseAsync(
                _activeSession, contextSlots, _config, _modelSelector.CurrentModelId,
                FindAgentById, _history, GenerateTitle).Forget();
        }

        public void CancelStream() => _streaming.CancelStream();

        // ─── Quick Action / Agent / Config ───

        public void ExecuteQuickAction(ContextCollector.ContextSlot requiredSlot, string message,
            ref ContextCollector.ContextSlot contextSlots)
        {
            if (requiredSlot != ContextCollector.ContextSlot.None)
                contextSlots |= requiredSlot;

            if (_activeSession == null)
                CreateNewSession();

            SendMessage(message, contextSlots);
        }

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

        public void ReloadConfig()
        {
            _config = AIConfigManager.LoadConfig();
            _modelSelector.RebuildCache(_config);
            RebuildAgentCache();
            _streaming.EnsureRunner(_config, _modelSelector, FindAgentById(_activeSession?.AgentId));
            OnStateChanged?.Invoke();
        }

        public void Dispose()
        {
            _streaming?.Dispose();
        }

        // ─── 内部 ───

        private void RebuildAgentCache()
        {
            _availableAgents = AgentManager.GetAllAgents();
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
