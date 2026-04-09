using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void DrawChatArea(float width, float height)
        {
            var activeSession = _controller.ActiveSession;
            if (activeSession == null || activeSession.Messages.Count == 0)
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
            var messages = activeSession.Messages;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].IsToolCall)
                {
                    // 收集连续的 ToolCall 消息为一组
                    int groupStart = i;
                    while (i < messages.Count && messages[i].IsToolCall)
                        i++;
                    DrawToolCallGroup(messages, groupStart, i - 1, width);
                    i--; // for 循环会 i++，回退一步
                }
                else
                {
                    DrawMessage(messages[i], width);
                }
            }
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
            var agent = _controller.FindAgentById(_controller.ActiveSession?.AgentId);
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

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(agent.AgentName ?? agent.name, _welcomeTitleStyle);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

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

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD);
            GUILayout.FlexibleSpace();
            var avatarRect = GUILayoutUtility.GetRect(AVATAR_SIZE, AVATAR_SIZE, GUILayout.Width(AVATAR_SIZE), GUILayout.Height(AVATAR_SIZE));
            DrawAvatar(avatarRect, _userAvatar, new Color(0.3f, 0.45f, 0.7f), "U");
            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

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

            // 用户消息操作栏：复制、删除（右对齐）
            DrawMessageActions(msg, areaWidth, false);

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        private void DrawAssistantMessage(ChatMessage msg, float areaWidth)
        {
            float maxBubbleWidth = areaWidth * MSG_MAX_WIDTH_RATIO;
            float maxContentWidth = maxBubbleWidth - 16;

            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);

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

            // AI 消息操作栏：复制、重新生成、删除（仅非流式时显示）
            if (!msg.IsStreaming)
                DrawMessageActions(msg, areaWidth, true);

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
        }

        // ─── Message Action Bar ───

        private void DrawMessageActions(ChatMessage msg, float areaWidth, bool isAssistant)
        {
            string msgId = $"msg_{msg.Timestamp}_{msg.Content?.GetHashCode()}";
            bool showCopied = _copiedMsgId == msgId
                && (EditorApplication.timeSinceStartup - _copiedMsgTime) < 1.5;

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            if (isAssistant)
            {
                // AI 消息：左对齐，与气泡左边缘对齐
                GUILayout.Space(MSG_HORIZONTAL_PAD + 8);
            }
            else
            {
                // 用户消息：右对齐
                GUILayout.FlexibleSpace();
            }

            // 复制按钮
            if (showCopied)
            {
                var copiedStyle = new GUIStyle(_msgActionBtnStyle);
                copiedStyle.normal.textColor = new Color(0.3f, 0.85f, 0.4f);
                GUILayout.Label("已复制!", copiedStyle, GUILayout.Height(16));
            }
            else
            {
                if (GUILayout.Button("复制", _msgActionBtnStyle, GUILayout.Height(16)))
                {
                    EditorGUIUtility.systemCopyBuffer = msg.Content;
                    _copiedMsgId = msgId;
                    _copiedMsgTime = EditorApplication.timeSinceStartup;
                }
            }

            GUILayout.Space(8);

            // 重新生成按钮（仅 AI 消息）
            if (isAssistant)
            {
                if (GUILayout.Button("重新生成", _msgActionBtnStyle, GUILayout.Height(16)))
                {
                    _controller?.RegenerateFromMessage(msg, _contextSlots);
                }
                GUILayout.Space(8);
            }

            // 删除按钮
            if (GUILayout.Button("删除", _msgActionBtnStyle, GUILayout.Height(16)))
            {
                if (EditorUtility.DisplayDialog("删除消息", "确定要删除这条消息吗？", "删除", "取消"))
                {
                    _controller?.DeleteMessage(msg);
                }
            }

            if (isAssistant)
                GUILayout.FlexibleSpace();
            else
                GUILayout.Space(MSG_HORIZONTAL_PAD + 8);

            EditorGUILayout.EndHorizontal();
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

        // ─── Tool Call Group ───

        /// <summary>
        /// 将连续的 ToolCall 消息合并为一个可折叠组
        /// </summary>
        private void DrawToolCallGroup(List<ChatMessage> messages, int startIdx, int endIdx, float areaWidth)
        {
            int count = endIdx - startIdx + 1;
            bool isExpanded = _expandedToolGroups.Contains(startIdx);

            // 统计完成状态
            int doneCount = 0;
            bool hasError = false;
            for (int i = startIdx; i <= endIdx; i++)
            {
                if (!string.IsNullOrEmpty(messages[i].ToolResult))
                    doneCount++;
                if (messages[i].IsToolError)
                    hasError = true;
            }
            bool allDone = doneCount == count;
            string statusIcon = hasError ? "x" : (allDone ? "v" : "...");

            // 折叠头
            GUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD + 20);

            string arrow = isExpanded ? "▼" : "▶";
            string label = $"{arrow}  [{statusIcon}] 工具调用 ({count})";

            if (GUILayout.Button(label, _toolCallFoldoutStyle, GUILayout.Height(20)))
            {
                if (isExpanded)
                    _expandedToolGroups.Remove(startIdx);
                else
                    _expandedToolGroups.Add(startIdx);
            }

            // 让按钮区域显示手型光标
            var lastRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(lastRect, MouseCursor.Link);

            GUILayout.FlexibleSpace();
            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            // 展开时绘制每条 ToolCall
            if (isExpanded)
            {
                float maxContentWidth = areaWidth - MSG_HORIZONTAL_PAD * 2 - 32;
                for (int i = startIdx; i <= endIdx; i++)
                    DrawToolCallItem(messages[i], maxContentWidth);
            }

            GUILayout.Space(2);
        }

        private void DrawToolCallItem(ChatMessage msg, float maxContentWidth)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(MSG_HORIZONTAL_PAD + 28);

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

            string statusIcon = string.IsNullOrEmpty(msg.ToolResult)
                ? "..."
                : (msg.IsToolError ? "x" : "v");
            string header = $"[{statusIcon}] Tool: {msg.ToolName}";

            EnsureToolCallStyle();
            GUILayout.Label(header, _toolCallStyle, GUILayout.MaxWidth(maxContentWidth));

            if (!string.IsNullOrEmpty(msg.ToolArguments))
            {
                var argsDisplay = msg.ToolArguments.Length > 120
                    ? msg.ToolArguments.Substring(0, 120) + "..."
                    : msg.ToolArguments;
                GUILayout.Label($"  Args: {argsDisplay}", EditorStyles.miniLabel, GUILayout.MaxWidth(maxContentWidth));
            }

            if (!string.IsNullOrEmpty(msg.ToolResult))
            {
                var resultDisplay = msg.ToolResult.Length > 200
                    ? msg.ToolResult.Substring(0, 200) + "..."
                    : msg.ToolResult;
                var resultStyle = msg.IsToolError ? _toolCallErrorStyle : EditorStyles.miniLabel;
                GUILayout.Label($"  Result: {resultDisplay}", resultStyle, GUILayout.MaxWidth(maxContentWidth));

                // 内联渲染生成的图片
                TryDrawGeneratedAssets(msg.ToolResult, maxContentWidth);
            }

            GUILayout.Space(2);
            EditorGUILayout.EndVertical();

            GUILayout.Space(MSG_HORIZONTAL_PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
        }

        // ─── Generated Asset Inline Rendering ───

        private const float GENERATED_IMAGE_MAX_WIDTH = 256f;

        /// <summary>
        /// 检测 ToolResult JSON 中是否含 generatedAssets 数组，如有则内联渲染图片缩略图。
        /// </summary>
        private void TryDrawGeneratedAssets(string toolResult, float maxContentWidth)
        {
            if (string.IsNullOrEmpty(toolResult) || !toolResult.Contains("generatedAssets"))
                return;

            try
            {
                var json = JObject.Parse(toolResult);
                var data = json["data"] ?? json["Data"];
                if (data == null) return;

                var assets = data["generatedAssets"] ?? data["GeneratedAssets"];
                if (assets is not JArray assetArray || assetArray.Count == 0) return;

                GUILayout.Space(4);

                foreach (var asset in assetArray)
                {
                    var path = (string)(asset["path"] ?? asset["Path"]);
                    if (string.IsNullOrEmpty(path)) continue;

                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null) continue;

                    float displayWidth = Mathf.Min(texture.width, GENERATED_IMAGE_MAX_WIDTH, maxContentWidth - 16);
                    float displayHeight = displayWidth * texture.height / texture.width;

                    GUILayout.Space(2);
                    var rect = GUILayoutUtility.GetRect(displayWidth, displayHeight,
                        GUILayout.Width(displayWidth), GUILayout.Height(displayHeight));
                    GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                    GUILayout.Space(2);
                }
            }
            catch
            {
                // JSON 解析失败时静默忽略
            }
        }
    }
}
