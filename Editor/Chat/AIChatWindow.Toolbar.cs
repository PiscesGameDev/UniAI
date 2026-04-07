using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void DrawToolbar()
        {
            var toolbarRect = new Rect(0, 5, position.width, TOOLBAR_HEIGHT);
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

            // ─── Split Button: 新对话 + Agent 下拉 ───
            DrawNewSessionSplitButton();

            GUILayout.Space(12);

            // ─── 模型选择 ───
            if (_modelNames is { Length: > 0 })
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

            // ─── 会话身份标签 ───
            DrawSessionIdentityLabel();

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

        /// <summary>
        /// Split Button：左侧 "+" 创建纯 Chat 会话，右侧 "▾" 弹出 Agent 菜单
        /// </summary>
        private void DrawNewSessionSplitButton()
        {
            // 左半：新 Chat 会话
            if (GUILayout.Button("+ 新对话", EditorStyles.miniButtonLeft, GUILayout.Width(60), GUILayout.Height(22)))
                CreateNewSession();

            // 右半：Agent 选择下拉
            if (GUILayout.Button("▾", EditorStyles.miniButtonRight, GUILayout.Width(20), GUILayout.Height(22)))
            {
                var menu = new GenericMenu();
                
                if (_availableAgents != null)
                {
                    foreach (var agent in _availableAgents)
                    {
                        var captured = agent;
                        string label = agent.AgentName ?? agent.name;
                        menu.AddItem(new GUIContent(label), false, () => CreateNewSession(captured));
                    }
                }

                if (_availableAgents == null || _availableAgents.Count == 0)
                    menu.AddDisabledItem(new GUIContent("暂无 Agent（可在 Agent 管理器中创建）"));

                menu.AddSeparator("");
                menu.AddItem(new GUIContent("管理 Agent..."), false, () => AIAgentWindow.Open());
                menu.ShowAsContext();
            }
        }

        /// <summary>
        /// 显示当前会话身份：Agent 名 + Icon 或 "Chat"
        /// </summary>
        private void DrawSessionIdentityLabel()
        {
            string agentId = _activeSession?.AgentId;
            var agent = FindAgentById(agentId);

            if (agent != null)
            {
                // Agent 模式：显示 Icon + 名称
                if (agent.Icon != null)
                {
                    var iconRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                    iconRect.y += 4;
                    GUI.DrawTexture(iconRect, agent.Icon, ScaleMode.ScaleToFit);
                    GUILayout.Space(2);
                }
                GUILayout.Label(agent.AgentName ?? agent.name, EditorStyles.miniLabel);
            }
            else if (_activeSession != null && !string.IsNullOrEmpty(agentId))
            {
                // Agent 已被删除：显示降级标记
                GUILayout.Label($"⚠ {agentId}（已删除）", EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("Chat", EditorStyles.miniLabel);
            }
        }
    }
}
