using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// UniAI 对话窗口 — 侧边栏 + 主对话区经典布局
    /// </summary>
    public class AIChatWindow : EditorWindow
    {
        // ─── Layout Constants ───

        private const float SIDEBAR_WIDTH = 200f;
        private const float TOOLBAR_HEIGHT = 32f;
        private const float INPUT_MIN_HEIGHT = 32f;
        private const float INPUT_MAX_HEIGHT = 150f;
        private const float PAD = 8f;
        private const float MSG_HORIZONTAL_PAD = 16f;
        private const float MSG_BUBBLE_RADIUS = 6f;
        private const string ICONS_DIR = "Assets/UniAI/Editor/Icons";

        // ─── Colors ───

        private static readonly Color _sidebarBg = new(0.16f, 0.16f, 0.16f);
        private static readonly Color _toolbarBg = new(0.2f, 0.2f, 0.2f);
        private static readonly Color _chatBg = new(0.17f, 0.17f, 0.17f);
        private static readonly Color _userMsgBg = new(0.18f, 0.22f, 0.32f);
        private static readonly Color _assistantMsgBg = new(0.22f, 0.22f, 0.24f);
        private static readonly Color _inputBg = new(0.20f, 0.20f, 0.20f);
        private static readonly Color _separatorColor = new(0.12f, 0.12f, 0.12f);
        private static readonly Color _selectedItemBg = new(0.28f, 0.28f, 0.32f);
        private static readonly Color _groupLabelColor = new(0.55f, 0.55f, 0.55f);
        private static readonly Color _userRoleColor = new(0.55f, 0.70f, 0.95f);
        private static readonly Color _assistantRoleColor = new(0.60f, 0.85f, 0.65f);
        private static readonly Color _guideCardBg = new(0.22f, 0.24f, 0.28f);
        private static readonly Color _guideCardHoverBg = new(0.26f, 0.28f, 0.33f);
        private static readonly Color _contextBarBg = new(0.19f, 0.19f, 0.21f);

        // ─── State ───

        private AIConfig _config;
        private AIClient _client;
        private ChatHistory _history;
        private ChatSession _activeSession;
        private string _inputText = "";
        private Vector2 _chatScroll;
        private Vector2 _sidebarScroll;
        private bool _showSidebar = true;
        private bool _isStreaming;
        private CancellationTokenSource _streamCts;
        private ContextCollector.ContextSlot _contextSlots = ContextCollector.ContextSlot.Selection;
        private bool _scrollToBottom;
        private int _selectedProviderIndex;
        private bool _showActionBar;

        // Spinner state
        private double _spinnerStartTime;
        private int _spinnerFrame;
        private static readonly string[] _spinnerChars = { "\u28f7", "\u28ef", "\u28df", "\u287f", "\u28bf", "\u28fb", "\u28fd", "\u28fe" };

        // ─── Styles ───

        private GUIStyle _titleStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _userMsgStyle;
        private GUIStyle _userRoleLabelStyle;
        private GUIStyle _assistantRoleLabelStyle;
        private GUIStyle _groupStyle;
        private GUIStyle _sessionItemStyle;
        private GUIStyle _quickActionStyle;
        private GUIStyle _searchFieldStyle;
        private GUIStyle _costLabelStyle;
        private GUIStyle _spinnerStyle;
        private GUIStyle _guideCardStyle;
        private GUIStyle _guideCardDescStyle;
        private GUIStyle _welcomeTitleStyle;
        private GUIStyle _welcomeSubStyle;
        private GUIStyle _contextToggleOnStyle;
        private GUIStyle _contextToggleOffStyle;
        private bool _stylesReady;

        // ─── Provider Cache ───

        private string[] _providerNames;
        // ─── Quick Actions ───

        private static readonly (string Label, string Icon, ContextCollector.ContextSlot Slot, string Message)[] _quickActions =
        {
            ("解释", "\u26a1", ContextCollector.ContextSlot.Selection, "Explain the selected object's scripts in detail."),
            ("优化", "\u2692", ContextCollector.ContextSlot.Selection, "Suggest optimizations for the selected hierarchy."),
            ("注释", "\u270d", ContextCollector.ContextSlot.Selection, "Generate XML doc comments for the selected code."),
            ("修错", "\u2699", ContextCollector.ContextSlot.Console, "Analyze and fix the console errors.")
        };

        // ─── Guide Cards (Empty State) ───

        private static readonly (string Title, string Desc, ContextCollector.ContextSlot Slot, string Message)[] _guideCards =
        {
            ("编写单例脚本", "生成线程安全的 MonoBehaviour 单例模板", ContextCollector.ContextSlot.None, "Write a thread-safe MonoBehaviour singleton base class with lazy initialization."),
            ("分析选中对象", "解读选中 GameObject 的组件结构", ContextCollector.ContextSlot.Selection, "Explain the selected object's scripts and component setup in detail."),
            ("修复控制台报错", "读取并修复最近的控制台错误", ContextCollector.ContextSlot.Console, "Analyze and fix the console errors."),
            ("优化层级结构", "为选中的层级结构提供性能优化建议", ContextCollector.ContextSlot.Selection, "Suggest optimizations for the selected hierarchy, including draw call batching and component efficiency.")
        };

        // ─── Menu ───

        [MenuItem("Window/UniAI/Chat")]
        [MenuItem("Tools/UniAI Chat")]
        public static void Open()
        {
            var w = GetWindow<AIChatWindow>("UniAI 对话");
            w.minSize = new Vector2(640, 400);
        }

        // ─── Lifecycle ───

        private void OnEnable()
        {
            _config = AIConfigManager.LoadConfig();
            _history = new ChatHistory();
            _history.Load();
            RebuildProviderCache();
            EnsureClient();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            CancelStream();
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (_isStreaming)
            {
                int frame = (int)((EditorApplication.timeSinceStartup - _spinnerStartTime) * 8) % _spinnerChars.Length;
                if (frame != _spinnerFrame)
                {
                    _spinnerFrame = frame;
                    Repaint();
                }
            }
        }

        // ─── OnGUI Entry ───

        private void OnGUI()
        {
            if (_config == null) _config = AIConfigManager.LoadConfig();
            EnsureStyles();

            DrawToolbar();

            float contentY = TOOLBAR_HEIGHT;
            float contentH = position.height - TOOLBAR_HEIGHT;

            if (_showSidebar)
            {
                EditorGUI.DrawRect(new Rect(0, contentY, SIDEBAR_WIDTH, contentH), _sidebarBg);
                EditorGUI.DrawRect(new Rect(SIDEBAR_WIDTH, contentY, 1, contentH), _separatorColor);
                GUILayout.BeginArea(new Rect(0, contentY, SIDEBAR_WIDTH, contentH));
                DrawSidebar();
                GUILayout.EndArea();
            }

            float chatX = _showSidebar ? SIDEBAR_WIDTH + 1 : 0;
            float chatW = position.width - chatX;
            GUILayout.BeginArea(new Rect(chatX, contentY, chatW, contentH));
            DrawChatArea(chatW, contentH);
            GUILayout.EndArea();

            HandleInputShortcuts();
        }

        // ─── Toolbar ───

        private void DrawToolbar()
        {
            var toolbarRect = new Rect(0, 0, position.width, TOOLBAR_HEIGHT);
            EditorGUI.DrawRect(toolbarRect, _toolbarBg);
            EditorGUI.DrawRect(new Rect(0, TOOLBAR_HEIGHT - 1, position.width, 1), _separatorColor);

            GUILayout.BeginArea(toolbarRect);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(TOOLBAR_HEIGHT));
            GUILayout.Space(PAD);

            string toggleIcon = _showSidebar ? "\u2630" : "\u25b6";
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

            if (GUILayout.Button("\u2699", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(22)))
                AISettingsWindow.Open();

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ─── Sidebar ───

        private void DrawSidebar()
        {
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            string newFilter = EditorGUILayout.TextField(_history.SearchFilter,
                _searchFieldStyle, GUILayout.Height(20));
            if (newFilter != _history.SearchFilter)
                _history.SearchFilter = newFilter;
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            _sidebarScroll = EditorGUILayout.BeginScrollView(_sidebarScroll);

            var groups = _history.GetGroupedSessions();
            foreach (var (group, items) in groups)
            {
                GUILayout.Label(group, _groupStyle);
                foreach (var session in items)
                {
                    bool isActive = _activeSession != null && _activeSession.Id == session.Id;
                    DrawSessionItem(session, isActive);
                }
                GUILayout.Space(4);
            }

            if (groups.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label("还没有对话记录", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            if (GUILayout.Button("\u25c0 收起", EditorStyles.miniButton, GUILayout.Height(20)))
                _showSidebar = false;
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(PAD);
        }

        private void DrawSessionItem(ChatSession session, bool isActive)
        {
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(26));
            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isActive ? _selectedItemBg : Color.clear);

            GUILayout.Space(PAD + 4);
            GUILayout.Label(TruncateTitle(session.Title, 20), _sessionItemStyle, GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowSessionContextMenu(session);
                else
                    SwitchToSession(session);
                Event.current.Use();
                Repaint();
            }
        }

        private void ShowSessionContextMenu(ChatSession session)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("删除"), false, () =>
            {
                if (_activeSession != null && _activeSession.Id == session.Id)
                    _activeSession = null;
                _history.Delete(session.Id);
                Repaint();
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除全部"), false, () =>
            {
                _activeSession = null;
                _history.DeleteAll();
                Repaint();
            });
            menu.ShowAsContext();
        }

        // ─── Chat Area ───

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
            GUILayout.Label("\u2728 UniAI 对话", _welcomeTitleStyle);
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
                GUILayout.Label("\u25c9", _userRoleLabelStyle, GUILayout.Width(16), GUILayout.Height(18));
                GUILayout.Space(4);
                GUILayout.Label("你", _userRoleLabelStyle);
            }
            else
            {
                if (msg.IsStreaming)
                {
                    GUILayout.Label(_spinnerChars[_spinnerFrame], _spinnerStyle, GUILayout.Width(16), GUILayout.Height(18));
                }
                else
                {
                    GUILayout.Label("\u2b50", _assistantRoleLabelStyle, GUILayout.Width(16), GUILayout.Height(18));
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

        // ─── Input Area ───

        private float CalcInputAreaHeight(float width)
        {
            if (!_stylesReady) return 80f;
            float actionBarH = _showActionBar ? 24f : 0f;
            float textH = INPUT_MIN_HEIGHT;
            if (!string.IsNullOrEmpty(_inputText))
            {
                float calcH = _inputStyle.CalcHeight(new GUIContent(_inputText), width - PAD * 2 - 68);
                textH = Mathf.Clamp(calcH, INPUT_MIN_HEIGHT, INPUT_MAX_HEIGHT);
            }
            return 6 + actionBarH + 2 + textH + 8 + 6;
        }

        private void DrawInputArea(float width)
        {
            EditorGUI.DrawRect(new Rect(0, 0, width, CalcInputAreaHeight(width)), _inputBg);

            GUILayout.Space(6);

            if (_showActionBar)
            {
                var barRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(22));
                if (barRect.width > 1)
                    EditorGUI.DrawRect(barRect, _contextBarBg);

                GUILayout.Space(PAD);

                DrawContextToggle("选中对象", ContextCollector.ContextSlot.Selection);
                DrawContextToggle("控制台", ContextCollector.ContextSlot.Console);
                DrawContextToggle("工程资源", ContextCollector.ContextSlot.Project);

                GUILayout.Space(8);
                GUILayout.Label("|", EditorStyles.miniLabel, GUILayout.Width(6));
                GUILayout.Space(4);

                foreach (var (label, icon, slot, message) in _quickActions)
                {
                    if (GUILayout.Button(icon + " " + label, _quickActionStyle, GUILayout.Height(18)))
                        ExecuteQuickAction(slot, message);
                    GUILayout.Space(2);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Space(PAD);
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            string plusIcon = _showActionBar ? "\u25bc" : "+";
            if (GUILayout.Button(plusIcon, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(INPUT_MIN_HEIGHT)))
                _showActionBar = !_showActionBar;

            GUILayout.Space(4);

            GUI.SetNextControlName("ChatInput");
            float inputH = INPUT_MIN_HEIGHT;
            if (!string.IsNullOrEmpty(_inputText))
            {
                float calcH = _inputStyle.CalcHeight(new GUIContent(_inputText), width - PAD * 2 - 68 - 30);
                inputH = Mathf.Clamp(calcH, INPUT_MIN_HEIGHT, INPUT_MAX_HEIGHT);
            }
            _inputText = EditorGUILayout.TextArea(_inputText, _inputStyle,
                GUILayout.Height(inputH), GUILayout.ExpandWidth(true));

            GUILayout.Space(4);

            if (_isStreaming)
            {
                if (GUILayout.Button("\u25a0 停止", GUILayout.Width(60), GUILayout.Height(inputH)))
                    CancelStream();
            }
            else
            {
                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_inputText));
                if (GUILayout.Button("发送", GUILayout.Width(60), GUILayout.Height(inputH)))
                    SendMessage();
                EditorGUI.EndDisabledGroup();
            }

            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
        }

        private void DrawContextToggle(string label, ContextCollector.ContextSlot slot)
        {
            bool isOn = _contextSlots.HasFlag(slot);
            var style = isOn ? _contextToggleOnStyle : _contextToggleOffStyle;
            if (GUILayout.Button(label, style, GUILayout.Height(18)))
            {
                if (isOn) _contextSlots &= ~slot;
                else _contextSlots |= slot;
            }
        }

        // ─── Input Shortcuts ───

        private void HandleInputShortcuts()
        {
            if (Event.current.type != EventType.KeyDown) return;
            if (Event.current.keyCode != KeyCode.Return && Event.current.keyCode != KeyCode.KeypadEnter) return;
            if (Event.current.shift) return;
            if (GUI.GetNameOfFocusedControl() != "ChatInput") return;

            if (!_isStreaming && !string.IsNullOrWhiteSpace(_inputText))
            {
                Event.current.Use();
                SendMessage();
            }
        }

        // ─── Send / Stream ───

        private void SendMessage()
        {
            if (_activeSession == null)
                CreateNewSession();
            if (_activeSession == null) return;

            string text = _inputText.Trim();
            _inputText = "";

            _activeSession.Messages.Add(new ChatMessage
            {
                Role = AIRole.User,
                Content = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            _scrollToBottom = true;

            StreamResponseAsync().Forget();
        }

        private async UniTaskVoid StreamResponseAsync()
        {
            if (_client == null)
            {
                AddErrorMessage("未配置 AI 提供商，请打开设置进行配置。");
                return;
            }

            _isStreaming = true;
            _spinnerStartTime = EditorApplication.timeSinceStartup;
            _spinnerFrame = 0;
            _streamCts = new CancellationTokenSource();

            var assistantMsg = new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = "",
                IsStreaming = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            _activeSession.Messages.Add(assistantMsg);

            try
            {
                var request = BuildRequest();

                string context = ContextCollector.Collect(_contextSlots);
                if (!string.IsNullOrEmpty(context))
                {
                    request.SystemPrompt = string.IsNullOrEmpty(request.SystemPrompt)
                        ? context
                        : request.SystemPrompt + "\n\n" + context;
                }

                var ct = _streamCts.Token;
                await foreach (var chunk in _client.StreamAsync(request, ct))
                {
                    if (ct.IsCancellationRequested) break;

                    if (!string.IsNullOrEmpty(chunk.DeltaText))
                    {
                        assistantMsg.Content += chunk.DeltaText;
                        _scrollToBottom = true;
                        Repaint();
                    }

                    if (chunk.IsComplete && chunk.Usage != null)
                    {
                        assistantMsg.InputTokens = chunk.Usage.InputTokens;
                        assistantMsg.OutputTokens = chunk.Usage.OutputTokens;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                assistantMsg.Content += "\n\n[已停止]";
            }
            catch (Exception e)
            {
                assistantMsg.Content += $"\n\n[错误: {e.Message}]";
                Debug.LogWarning($"[UniAI Chat] Stream error: {e}");
            }
            finally
            {
                assistantMsg.IsStreaming = false;
                _isStreaming = false;
                _streamCts?.Dispose();
                _streamCts = null;

                _history.Save(_activeSession);
                Repaint();

                if (_activeSession.Messages.Count == 2 && _activeSession.Title == "新对话")
                    GenerateTitleAsync().Forget();
            }
        }

        private AIRequest BuildRequest()
        {
            var request = new AIRequest
            {
                SystemPrompt = "You are a helpful Unity game development assistant. " +
                    "Provide clear, concise answers. When showing code, use C# and Unity best practices.",
                Messages = new List<AIMessage>()
            };

            foreach (var msg in _activeSession.Messages)
            {
                if (msg.IsStreaming && string.IsNullOrEmpty(msg.Content))
                    continue;

                var aiMsg = msg.Role == AIRole.User
                    ? AIMessage.User(msg.Content)
                    : AIMessage.Assistant(msg.Content);
                request.Messages.Add(aiMsg);
            }

            return request;
        }

        private void CancelStream()
        {
            _streamCts?.Cancel();
        }

        private void AddErrorMessage(string error)
        {
            _activeSession?.Messages.Add(new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = $"[错误] {error}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            _scrollToBottom = true;
            Repaint();
        }

        // ─── Auto Title ───

        private async UniTaskVoid GenerateTitleAsync()
        {
            if (_client == null || _activeSession == null) return;
            if (_activeSession.Messages.Count < 2) return;

            try
            {
                var titleRequest = new AIRequest
                {
                    SystemPrompt = "Generate a short title (max 8 Chinese characters or 4 English words) for this conversation. Reply with ONLY the title, nothing else.",
                    Messages = new List<AIMessage>
                    {
                        AIMessage.User(_activeSession.Messages[0].Content),
                        AIMessage.Assistant(_activeSession.Messages[1].Content)
                    },
                    MaxTokens = 32,
                    Temperature = 0.3f
                };

                var response = await _client.SendAsync(titleRequest);
                if (response.IsSuccess && !string.IsNullOrEmpty(response.Text))
                {
                    _activeSession.Title = response.Text.Trim().Trim('"', '\'', '\n', '\r');
                    if (_activeSession.Title.Length > 20)
                        _activeSession.Title = _activeSession.Title.Substring(0, 20);
                    _history.Save(_activeSession);
                    Repaint();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI Chat] Auto-title failed: {e.Message}");
            }
        }

        // ─── Quick Actions ───

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

        // ─── Drawing Helpers ───

        private static void DrawRoundedRect(Rect rect, Color color, float radius)
        {
            var inner = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            EditorGUI.DrawRect(inner, color);
            EditorGUI.DrawRect(new Rect(rect.x + radius, rect.y, rect.width - radius * 2, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x + radius, rect.yMax - 1, rect.width - radius * 2, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + radius, 1, rect.height - radius * 2), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y + radius, 1, rect.height - radius * 2), color);
        }

        private static void DrawRectBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        private static string TruncateTitle(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "未命名";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen - 1) + "\u2026";
        }

        // ─── Styles ───

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };

            _inputStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                fontSize = 13,
                padding = new RectOffset(8, 8, 6, 6)
            };

            _userMsgStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = true,
                fontSize = 13,
                padding = new RectOffset(6, 6, 4, 4)
            };

            _userRoleLabelStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            _userRoleLabelStyle.normal.textColor = _userRoleColor;

            _assistantRoleLabelStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            _assistantRoleLabelStyle.normal.textColor = _assistantRoleColor;

            _groupStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                padding = new RectOffset(12, 4, 4, 2)
            };
            _groupStyle.normal.textColor = _groupLabelColor;

            _sessionItemStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };

            _quickActionStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                padding = new RectOffset(6, 6, 2, 2)
            };

            _searchFieldStyle = new GUIStyle(EditorStyles.toolbarSearchField);

            _costLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            _costLabelStyle.normal.textColor = new Color(0.6f, 0.8f, 0.6f);

            _spinnerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter
            };
            _spinnerStyle.normal.textColor = new Color(0.4f, 0.72f, 1f);

            _welcomeTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };

            _welcomeSubStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _welcomeSubStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

            _guideCardStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };

            _guideCardDescStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            _guideCardDescStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f);

            _contextToggleOnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 1, 1)
            };
            _contextToggleOnStyle.normal.textColor = new Color(0.5f, 0.8f, 1f);

            _contextToggleOffStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 10,
                padding = new RectOffset(4, 4, 1, 1)
            };
            _contextToggleOffStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
        }
    }
}
