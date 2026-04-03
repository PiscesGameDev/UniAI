using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void DrawToolbar()
        {
            var toolbarRect = new Rect(0, 0, position.width, TOOLBAR_HEIGHT);
            EditorGUI.DrawRect(toolbarRect, _toolbarBg);
            EditorGUI.DrawRect(new Rect(0, TOOLBAR_HEIGHT - 1, position.width, 1), _separatorColor);

            GUILayout.BeginArea(toolbarRect);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(TOOLBAR_HEIGHT));
            GUILayout.Space(PAD);

            string toggleIcon = _showSidebar ? "☰" : "▶";
            if (GUILayout.Button(toggleIcon, EditorStyles.miniButton, GUILayout.Width(28), GUILayout.Height(22)))
                _showSidebar = !_showSidebar;

            GUILayout.Space(4);

            if (GUILayout.Button("+ 新对话", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(22)))
                CreateNewSession();

            GUILayout.Space(12);

            if (_providerNames != null && _providerNames.Length > 0)
            {
                GUILayout.Label("提供商:", EditorStyles.miniLabel, GUILayout.Width(52));
                int newIdx = EditorGUILayout.Popup(_selectedProviderIndex, _providerNames,
                    GUILayout.Width(120), GUILayout.Height(22));
                if (newIdx != _selectedProviderIndex)
                {
                    _selectedProviderIndex = newIdx;
                    EnsureClient();
                    if (_activeSession != null)
                        _activeSession.ProviderId = _config.Providers[_selectedProviderIndex].Id;
                }
            }

            if (_selectedProviderIndex < _config.Providers.Count)
            {
                GUILayout.Space(8);
                GUILayout.Label(_config.Providers[_selectedProviderIndex].Model, EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();

            if (_activeSession != null)
            {
                int totalTokens = _activeSession.TotalInputTokens + _activeSession.TotalOutputTokens;
                if (totalTokens > 0)
                    GUILayout.Label($"用量: {totalTokens:N0}", _costLabelStyle);
            }

            GUILayout.Space(8);

            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(22)))
                AISettingsWindow.Open();

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
