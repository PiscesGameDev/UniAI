using System;
using UnityEditor;
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
            string providerId = _config.Providers.Count > _selectedProviderIndex
                ? _config.Providers[_selectedProviderIndex].Id
                : "";
            _activeSession = ChatSession.Create(providerId);
            _chatScroll = Vector2.zero;
            _inputText = "";
            GUI.FocusControl("ChatInput");
            Repaint();
        }

        private void SwitchToSession(ChatSession session)
        {
            _activeSession = session;
            _chatScroll.y = float.MaxValue;

            if (!string.IsNullOrEmpty(session.ProviderId))
            {
                for (int i = 0; i < _config.Providers.Count; i++)
                {
                    if (_config.Providers[i].Id == session.ProviderId)
                    {
                        _selectedProviderIndex = i;
                        EnsureClient();
                        break;
                    }
                }
            }
        }

        // ─── Client Management ───

        private void EnsureClient()
        {
            if (_config.Providers.Count == 0)
            {
                _client = null;
                return;
            }

            if (_selectedProviderIndex >= _config.Providers.Count)
                _selectedProviderIndex = 0;

            var entry = _config.Providers[_selectedProviderIndex];

            if (!string.IsNullOrEmpty(entry.EnvVarName))
            {
                var envKey = Environment.GetEnvironmentVariable(entry.EnvVarName);
                if (!string.IsNullOrEmpty(envKey))
                    entry.ApiKey = envKey;
            }

            if (string.IsNullOrEmpty(entry.ApiKey))
            {
                _client = null;
                return;
            }

            try
            {
                _client = AIClient.Create(entry, _config.General);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI Chat] Failed to create client: {e.Message}");
                _client = null;
            }
        }

        private void RebuildProviderCache()
        {
            _providerNames = new string[_config.Providers.Count];
            for (int i = 0; i < _config.Providers.Count; i++)
                _providerNames[i] = _config.Providers[i].Name ?? _config.Providers[i].Id;

            if (!string.IsNullOrEmpty(_config.ActiveProviderId))
            {
                for (int i = 0; i < _config.Providers.Count; i++)
                {
                    if (_config.Providers[i].Id == _config.ActiveProviderId)
                    {
                        _selectedProviderIndex = i;
                        break;
                    }
                }
            }
        }
    }
}
