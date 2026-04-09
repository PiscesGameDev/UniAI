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
            var modelNames = _controller.ModelNames;
            if (modelNames is { Length: > 0 })
            {
                GUILayout.Label("模型:", EditorStyles.miniLabel, GUILayout.Width(36));
                int selectedIndex = _controller.SelectedModelIndex;
                int newIdx = EditorGUILayout.Popup(selectedIndex, modelNames,
                    GUILayout.Width(220), GUILayout.Height(22));
                if (newIdx != selectedIndex)
                    _controller.SelectModel(newIdx);
            }

            GUILayout.Space(12);

            // ─── 会话身份标签 ───
            DrawSessionIdentityLabel();

            GUILayout.FlexibleSpace();

            // ─── MCP 状态 ───
            if (!string.IsNullOrEmpty(_controller.McpStatus))
            {
                GUILayout.Label(_controller.McpStatus, _costLabelStyle);
                GUILayout.Space(8);
            }

            var activeSession = _controller.ActiveSession;
            if (activeSession != null)
            {
                int totalTokens = activeSession.TotalInputTokens + activeSession.TotalOutputTokens;
                if (totalTokens > 0)
                    GUILayout.Label($"用量: {totalTokens:N0}", _costLabelStyle);
            }

            GUILayout.Space(8);

            if (GUILayout.Button("⚙", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(22)))
                UniAIManagerWindow.OpenChannel();

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
                var agents = _controller.AvailableAgents;

                if (agents != null)
                {
                    foreach (var agent in agents)
                    {
                        var captured = agent;
                        string label = agent.AgentName ?? agent.name;
                        menu.AddItem(new GUIContent(label), false, () => CreateNewSession(captured));
                    }
                }

                if (agents == null || agents.Count == 0)
                    menu.AddDisabledItem(new GUIContent("暂无 Agent（可在 Agent 管理器中创建）"));

                menu.AddSeparator("");
                menu.AddItem(new GUIContent("管理 Agent..."), false, () => UniAIManagerWindow.OpenAgent());
                menu.ShowAsContext();
            }
        }

        /// <summary>
        /// 显示当前会话身份：Agent 名 + Icon 或 "Chat"
        /// </summary>
        private void DrawSessionIdentityLabel()
        {
            string agentId = _controller.ActiveSession?.AgentId;
            var agent = _controller.FindAgentById(agentId);

            if (agent != null)
            {
                if (agent.Icon != null)
                {
                    var iconRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                    iconRect.y += 4;
                    GUI.DrawTexture(iconRect, agent.Icon, ScaleMode.ScaleToFit);
                    GUILayout.Space(2);
                }
                GUILayout.Label(agent.AgentName ?? agent.name, EditorStyles.miniLabel);
            }
            else if (_controller.ActiveSession != null && !string.IsNullOrEmpty(agentId))
            {
                GUILayout.Label($"⚠ {agentId}（已删除）", EditorStyles.miniLabel);
            }
            else
            {
                GUILayout.Label("Chat", EditorStyles.miniLabel);
            }
        }
    }
}
