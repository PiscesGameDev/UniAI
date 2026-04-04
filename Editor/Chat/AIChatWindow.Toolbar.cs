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
            {
                _showSidebar = !_showSidebar;
                AIConfigManager.Prefs.ShowSidebar = _showSidebar;
                AIConfigManager.SavePrefs();
            }

            GUILayout.Space(4);

            if (GUILayout.Button("+ 新对话", EditorStyles.miniButton, GUILayout.Width(76), GUILayout.Height(22)))
                CreateNewSession();

            GUILayout.Space(12);

            // ─── 模型选择 ───
            if (_modelNames != null && _modelNames.Length > 0)
            {
                GUILayout.Label("模型:", EditorStyles.miniLabel, GUILayout.Width(36));
                int newIdx = EditorGUILayout.Popup(_selectedModelIndex, _modelNames,
                    GUILayout.Width(180), GUILayout.Height(22));
                if (newIdx != _selectedModelIndex)
                {
                    _selectedModelIndex = newIdx;
                    _currentModelId = _modelEntries[newIdx].ModelId;
                    EnsureRunner();
                    if (_activeSession != null)
                        _activeSession.ModelId = _currentModelId;

                    // 持久化模型选择
                    AIConfigManager.Prefs.LastSelectedModelId = _currentModelId;
                    AIConfigManager.SavePrefs();
                }
            }

            GUILayout.Space(12);

            // ─── Agent 选择 ───
            if (_agentNames != null && _agentNames.Length > 0)
            {
                GUILayout.Label("Agent:", EditorStyles.miniLabel, GUILayout.Width(40));
                int newAgentIdx = EditorGUILayout.Popup(_selectedAgentIndex, _agentNames,
                    GUILayout.Width(100), GUILayout.Height(22));
                if (newAgentIdx != _selectedAgentIndex)
                {
                    _selectedAgentIndex = newAgentIdx;
                    EnsureRunner();
                    if (_activeSession != null)
                        _activeSession.AgentId = GetSelectedAgentId();
                }
            }

            if (GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(22)))
            {
                AIAgentWindow.Open();
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
                AIChannelWindow.Open();

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
