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

        private void CreateNewSession()
        {
            string modelId = _currentModelId ?? "";
            _activeSession = ChatSession.Create(modelId);
            _activeSession.AgentId = GetSelectedAgentId();
            _chatScroll = Vector2.zero;
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
                        EnsureRunner();
                        break;
                    }
                }
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

            var agent = GetSelectedAgent();
            _runner = new AIAgentRunner(_client, agent);
        }

        private AgentDefinition GetSelectedAgent()
        {
            if (_availableAgents == null || _availableAgents.Count == 0)
                return AgentManager.DefaultAgent;
            if (_selectedAgentIndex >= _availableAgents.Count)
                _selectedAgentIndex = 0;
            return _availableAgents[_selectedAgentIndex];
        }

        private string GetSelectedAgentId()
        {
            var agent = GetSelectedAgent();
            return agent == AgentManager.DefaultAgent ? "" : agent.name;
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
