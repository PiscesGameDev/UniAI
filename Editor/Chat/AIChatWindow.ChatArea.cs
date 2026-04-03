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
            foreach (var msg in _activeSession.Messages)
                DrawMessage(msg, width);
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
            EditorGUI.DrawRect(new Rect(0, 0, width, height), _chatBg);

            float inputAreaH = CalcInputAreaHeight(width);
            float welcomeH = height - inputAreaH;

            GUILayout.BeginArea(new Rect(0, 0, width, welcomeH));
            GUILayout.FlexibleSpace();

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

            float cardWidth = Mathf.Min((width - PAD * 3) / 2f, 280f);
            float gridWidth = cardWidth * 2 + PAD;
            float gridStartX = (width - gridWidth) / 2f;

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
            bool isUser = msg.Role == AIRole.User;
            Color bgColor = isUser ? _userMsgBg : _assistantMsgBg;
            float maxContentWidth = areaWidth - MSG_HORIZONTAL_PAD * 2 - 32;

            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD);

            if (isUser)
            {
                GUILayout.Label("◉", _userRoleLabelStyle, GUILayout.Width(16), GUILayout.Height(18));
                GUILayout.Space(4);
                GUILayout.Label("你", _userRoleLabelStyle);
            }
            else
            {
                if (msg.IsStreaming)
                {
                    EnsureSpinnerIcons();
                    GUILayout.Label(_spinnerIcons[_spinnerFrame], GUILayout.Width(16), GUILayout.Height(18));
                }
                else
                {
                    GUILayout.Label("⭐", _assistantRoleLabelStyle, GUILayout.Width(16), GUILayout.Height(18));
                }
                GUILayout.Space(4);
                GUILayout.Label("助手", _assistantRoleLabelStyle);

                if (msg.IsStreaming)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("思考中...", EditorStyles.miniLabel);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD);

            var contentRect = EditorGUILayout.BeginVertical();
            if (contentRect.width > 1)
            {
                var bubbleRect = new Rect(
                    contentRect.x - 8,
                    contentRect.y - 4,
                    contentRect.width + 16,
                    contentRect.height + 8);
                DrawRoundedRect(bubbleRect, bgColor, MSG_BUBBLE_RADIUS);
            }

            GUILayout.Space(2);

            if (isUser)
            {
                GUILayout.Label(msg.Content, _userMsgStyle, GUILayout.MaxWidth(maxContentWidth));
            }
            else
            {
                MarkdownRenderer.Draw(msg.Content ?? "", maxContentWidth);
            }

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }
    }
}
