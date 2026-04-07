using System;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void DrawChatArea(float width, float height)
        {
            if (_activeSession == null || _activeSession.Messages.Count == 0)
            {
                DrawWelcomeState(width, height);
                return;
            }

            float inputAreaH = CalcInputAreaHeight(width);
            float chatH = height - inputAreaH;

            EditorGUI.DrawRect(new Rect(0, 0, width, chatH), _chatBg);
            GUILayout.BeginArea(new Rect(0, 0, width, chatH));
            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll);

            GUILayout.Space(PAD);
            // 使用 for 循环替代 foreach，避免流式过程中 Messages 列表变化导致枚举异常
            var messages = _activeSession.Messages;
            for (int i = 0; i < messages.Count; i++)
                DrawMessage(messages[i], width);
            GUILayout.Space(PAD);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            if (_scrollToBottom)
            {
                _chatScroll.y = float.MaxValue;
                _scrollToBottom = false;
                Repaint();
            }

            EditorGUI.DrawRect(new Rect(0, chatH, width, 1), _separatorColor);

            GUILayout.BeginArea(new Rect(0, chatH + 1, width, inputAreaH - 1));
            DrawInputArea(width);
            GUILayout.EndArea();
        }

        // ─── Welcome / Empty State ───

        private void DrawWelcomeState(float width, float height)
        {
            // 根据会话身份选择展示 Agent 信息或通用欢迎页
            var agent = FindAgentById(_activeSession?.AgentId);
            if (agent != null)
                DrawAgentWelcomeState(agent, width, height);
            else
                DrawChatWelcomeState(width, height);
        }

        /// <summary>
        /// Agent 模式空状态：显示 Agent Icon + Name + Description
        /// </summary>
        private void DrawAgentWelcomeState(AgentDefinition agent, float width, float height)
        {
            DrawWelcomeLayout(width, height, _ =>
            {
                // Agent Icon
                if (agent.Icon != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    var iconRect = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                    GUI.DrawTexture(iconRect, agent.Icon, ScaleMode.ScaleToFit);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(8);
                }

                // Agent Name
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(agent.AgentName ?? agent.name, _welcomeTitleStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                // Agent Description
                if (!string.IsNullOrEmpty(agent.Description))
                {
                    GUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(agent.Description, _welcomeSubStyle, GUILayout.MaxWidth(width * 0.7f));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.Space(24);
            });
        }

        /// <summary>
        /// 纯 Chat 模式空状态：通用欢迎页 + Guide Cards
        /// </summary>
        private void DrawChatWelcomeState(float width, float height)
        {
            DrawWelcomeLayout(width, height, w =>
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("✨ UniAI 对话", _welcomeTitleStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("你的 Unity 工程助手。随便问，或试试下方的快捷操作。", _welcomeSubStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                GUILayout.Space(24);

                float cardWidth = Mathf.Min((w - PAD * 3) / 2f, 280f);
                float gridWidth = cardWidth * 2 + PAD;
                float gridStartX = (w - gridWidth) / 2f;

                for (int row = 0; row < 2; row++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(gridStartX);

                    for (int col = 0; col < 2; col++)
                    {
                        int idx = row * 2 + col;
                        if (idx >= _guideCards.Length) break;
                        var (cardTitle, desc, slot, message) = _guideCards[idx];

                        DrawGuideCard(cardTitle, desc, slot, message, cardWidth);

                        if (col == 0) GUILayout.Space(PAD);
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(PAD);
                }
            });
        }

        /// <summary>
        /// Welcome 布局骨架：背景 + 上部居中内容区 + 分隔线 + 输入区
        /// </summary>
        private void DrawWelcomeLayout(float width, float height, Action<float> drawContent)
        {
            EditorGUI.DrawRect(new Rect(0, 0, width, height), _chatBg);

            float inputAreaH = CalcInputAreaHeight(width);
            float welcomeH = height - inputAreaH;

            GUILayout.BeginArea(new Rect(0, 0, width, welcomeH));
            GUILayout.FlexibleSpace();

            drawContent(width);

            GUILayout.FlexibleSpace();
            GUILayout.EndArea();

            EditorGUI.DrawRect(new Rect(0, welcomeH, width, 1), _separatorColor);

            GUILayout.BeginArea(new Rect(0, welcomeH + 1, width, inputAreaH - 1));
            DrawInputArea(width);
            GUILayout.EndArea();
        }

        private void DrawGuideCard(string cardTitle, string desc, ContextCollector.ContextSlot slot, string message, float cardWidth)
        {
            var rect = GUILayoutUtility.GetRect(cardWidth, 56, GUILayout.Width(cardWidth));

            bool isHover = rect.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(rect, isHover ? _guideCardHoverBg : _guideCardBg);

            if (isHover)
                DrawRectBorder(rect, new Color(0.4f, 0.5f, 0.7f, 0.4f));

            var titleRect = new Rect(rect.x + 10, rect.y + 6, rect.width - 20, 18);
            GUI.Label(titleRect, cardTitle, _guideCardStyle);

            var descRect = new Rect(rect.x + 10, rect.y + 26, rect.width - 20, 24);
            GUI.Label(descRect, desc, _guideCardDescStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                ExecuteQuickAction(slot, message);
            }

            if (isHover)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }

        // ─── Message Drawing ───

        private void DrawMessage(ChatMessage msg, float areaWidth)
        {
            if (msg.IsToolCall)
            {
                DrawToolCallMessage(msg, areaWidth);
                return;
            }

            if (msg.Role == AIRole.User)
                DrawUserMessage(msg, areaWidth);
            else
                DrawAssistantMessage(msg, areaWidth);
        }

        private void DrawUserMessage(ChatMessage msg, float areaWidth)
        {
            float maxBubbleWidth = areaWidth * MSG_MAX_WIDTH_RATIO;
            float maxContentWidth = maxBubbleWidth - 16;

            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);

            // Header: right-aligned avatar
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD);
            GUILayout.FlexibleSpace();
            var avatarRect = GUILayoutUtility.GetRect(AVATAR_SIZE, AVATAR_SIZE, GUILayout.Width(AVATAR_SIZE), GUILayout.Height(AVATAR_SIZE));
            DrawAvatar(avatarRect, _userAvatar, new Color(0.3f, 0.45f, 0.7f), "U");
            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Bubble: right-aligned
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var contentRect = EditorGUILayout.BeginVertical(GUILayout.MaxWidth(maxBubbleWidth));
            DrawBubbleBackground(contentRect, _userMsgBg, true);
            GUILayout.Space(2);
            GUILayout.Label(msg.Content, _userMsgStyle, GUILayout.MaxWidth(maxContentWidth));
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        private void DrawAssistantMessage(ChatMessage msg, float areaWidth)
        {
            float maxBubbleWidth = areaWidth * MSG_MAX_WIDTH_RATIO;
            float maxContentWidth = maxBubbleWidth - 16;

            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);

            // Header: left-aligned avatar
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD);

            if (msg.IsStreaming)
            {
                EnsureSpinnerIcons();
                GUILayout.Label(_spinnerIcons[_spinnerFrame], GUILayout.Width(AVATAR_SIZE), GUILayout.Height(AVATAR_SIZE));
            }
            else
            {
                var avatarRect = GUILayoutUtility.GetRect(AVATAR_SIZE, AVATAR_SIZE, GUILayout.Width(AVATAR_SIZE), GUILayout.Height(AVATAR_SIZE));
                DrawAvatar(avatarRect, _aiAvatar, new Color(0.35f, 0.6f, 0.4f), "AI");
            }

            if (msg.IsStreaming)
            {
                GUILayout.Space(6);
                GUILayout.Label("思考中...", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Bubble: left-aligned
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD);

            var contentRect = EditorGUILayout.BeginVertical(GUILayout.MaxWidth(maxBubbleWidth));
            DrawBubbleBackground(contentRect, _assistantMsgBg, false);
            GUILayout.Space(2);
            MarkdownRenderer.Draw(msg.Content ?? "", maxContentWidth);
            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        private static void DrawBubbleBackground(Rect contentRect, Color bgColor, bool isUser)
        {
            if (contentRect.width <= 1) return;
            var bubbleRect = new Rect(
                contentRect.x - 8,
                contentRect.y - 4,
                contentRect.width + 16,
                contentRect.height + 8);
            DrawAsymmetricBubble(bubbleRect, bgColor, MSG_BUBBLE_RADIUS, 2f, isUser);
        }

        // ─── Tool Call Message ───

        private void DrawToolCallMessage(ChatMessage msg, float areaWidth)
        {
            float maxContentWidth = areaWidth - MSG_HORIZONTAL_PAD * 2 - 32;

            EditorGUILayout.BeginVertical();
            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD + 20); // 缩进，表示是 assistant 的子操作

            var contentRect = EditorGUILayout.BeginVertical();
            if (contentRect.width > 1)
            {
                var bubbleRect = new Rect(
                    contentRect.x - 6,
                    contentRect.y - 3,
                    contentRect.width + 12,
                    contentRect.height + 6);
                DrawRoundedRect(bubbleRect, _toolCallBg, MSG_BUBBLE_RADIUS);
            }

            GUILayout.Space(2);

            // 工具名称行
            string statusIcon = string.IsNullOrEmpty(msg.ToolResult)
                ? "..."
                : (msg.IsToolError ? "x" : "v");
            string header = $"[{statusIcon}] Tool: {msg.ToolName}";

            EnsureToolCallStyle();
            GUILayout.Label(header, _toolCallStyle, GUILayout.MaxWidth(maxContentWidth));

            // 参数（折叠显示）
            if (!string.IsNullOrEmpty(msg.ToolArguments))
            {
                var argsDisplay = msg.ToolArguments.Length > 120
                    ? msg.ToolArguments.Substring(0, 120) + "..."
                    : msg.ToolArguments;
                GUILayout.Label($"  Args: {argsDisplay}", EditorStyles.miniLabel, GUILayout.MaxWidth(maxContentWidth));
            }

            // 结果
            if (!string.IsNullOrEmpty(msg.ToolResult))
            {
                var resultDisplay = msg.ToolResult.Length > 200
                    ? msg.ToolResult.Substring(0, 200) + "..."
                    : msg.ToolResult;
                var resultStyle = msg.IsToolError ? _toolCallErrorStyle : EditorStyles.miniLabel;
                GUILayout.Label($"  Result: {resultDisplay}", resultStyle, GUILayout.MaxWidth(maxContentWidth));
            }

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }
    }
}
