using System;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void ExecuteQuickAction(ContextCollector.ContextSlot requiredSlot, string message)
        {
            if (requiredSlot != ContextCollector.ContextSlot.None)
                _contextSlots |= requiredSlot;

            if (_activeSession == null)
                CreateNewSession();

            _inputText = message;
            SendMessage();
        }

        // ─── Session Management ───

        /// <summary>
        /// 创建新会话，可选传入 AgentDefinition 锁定会话类型
        /// </summary>
        /// <param name="agent">null = 纯 Chat 模式；非 null = Agent 模式</param>
        private void CreateNewSession(AgentDefinition agent = null)
        {
            string modelId = _currentModelId ?? "";
            _activeSession = ChatSession.Create(modelId);
            _activeSession.AgentId = agent != null ? agent.Id : "";
            _history.Save(_activeSession);
            _chatScroll = Vector2.zero;
            EnsureRunner();
            GUI.FocusControl("ChatInput");
            Repaint();
        }

        private void SwitchToSession(ChatSession session)
        {
            _activeSession = session;
            _chatScroll.y = float.MaxValue;

            // 恢复 Session 记录的模型选择
            if (!string.IsNullOrEmpty(session.ModelId) && _modelEntries != null)
            {
                for (int i = 0; i < _modelEntries.Count; i++)
                {
                    if (_modelEntries[i].ModelId == session.ModelId)
                    {
                        _selectedModelIndex = i;
                        _currentModelId = session.ModelId;
                        break;
                    }
                }
            }

            EnsureRunner();

            // Agent 删除降级检查
            if (!string.IsNullOrEmpty(session.AgentId) && FindAgentById(session.AgentId) == null)
            {
                Debug.LogWarning(
                    $"[UniAI Chat] 会话 \"{session.Title}\" 关联的 Agent \"{session.AgentId}\" 已被删除，" +
                    "该会话将以纯 Chat 模式运行。");
            }
        }

        // ─── Client Management ───

        private void EnsureRunner()
        {
            EnsureClient();
            if (_client == null)
            {
                _runner = null;
                return;
            }

            var agent = FindAgentById(_activeSession?.AgentId);
            _runner = agent != null
                ? new AIAgentRunner(_client, agent)
                : new ChatRunner(_client);
        }

        /// <summary>
        /// 根据 AgentId在已缓存列表中查找 AgentDefinition
        /// 返回 null 表示纯 Chat 模式，或该 Agent 已被删除
        /// </summary>
        private AgentDefinition FindAgentById(string agentId)
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

        private void EnsureClient()
        {
            if (_modelEntries == null || _modelEntries.Count == 0)
            {
                _client = null;
                return;
            }

            if (_selectedModelIndex >= _modelEntries.Count)
                _selectedModelIndex = 0;

            _currentModelId = _modelEntries[_selectedModelIndex].ModelId;

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
                _modelNames[i] = _modelEntries[i].ModelId;

            // 尝试保持之前选择的模型
            if (!string.IsNullOrEmpty(_currentModelId))
            {
                for (int i = 0; i < _modelEntries.Count; i++)
                {
                    if (_modelEntries[i].ModelId == _currentModelId)
                    {
                        _selectedModelIndex = i;
                        return;
                    }
                }
            }

            // 回退到第一个
            _selectedModelIndex = 0;
            _currentModelId = _modelEntries.Count > 0 ? _modelEntries[0].ModelId : null;
        }
    }
}
