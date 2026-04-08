using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// UniAI 对话窗口 — 侧边栏 + 主对话区经典布局
    /// 纯 UI 层，业务逻辑委托给 ChatWindowController
    /// </summary>
    public partial class AIChatWindow : EditorWindow
    {
        // ─── Layout Constants ───

        private const float SIDEBAR_WIDTH = 200f;
        private const float TOOLBAR_HEIGHT = 32f;
        private const float INPUT_MIN_HEIGHT = 32f;
        private const float INPUT_MAX_HEIGHT = 150f;
        private const float PAD = 8f;
        private const float MSG_HORIZONTAL_PAD = 16f;
        private const float MSG_BUBBLE_RADIUS = 6f;
        private const float AVATAR_SIZE = 24f;
        private const float MSG_MAX_WIDTH_RATIO = 0.82f;

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
        private static readonly Color _toolCallBg = new(0.20f, 0.25f, 0.22f);

        // ─── Controller ───

        private ChatWindowController _controller;

        // ─── UI State ───

        private string _inputText = "";
        private Vector2 _chatScroll;
        private Vector2 _sidebarScroll;
        private bool _showSidebar = true;
        private ContextCollector.ContextSlot _contextSlots;
        private bool _scrollToBottom;
        private bool _showActionBar;

        // Spinner state
        private double _spinnerStartTime;
        private int _spinnerFrame;
        private const int SpinnerFrameCount = 12;
        private static GUIContent[] _spinnerIcons;

        // ─── Avatar ───

        private Texture2D _userAvatar;
        private Texture2D _aiAvatar;

        // ─── Styles ───

        private GUIStyle _inputStyle;
        private GUIStyle _userMsgStyle;
        private GUIStyle _userRoleLabelStyle;
        private GUIStyle _assistantRoleLabelStyle;
        private GUIStyle _groupStyle;
        private GUIStyle _sessionItemStyle;
        private GUIStyle _quickActionStyle;
        private GUIStyle _searchFieldStyle;
        private GUIStyle _costLabelStyle;
        private GUIStyle _guideCardStyle;
        private GUIStyle _guideCardDescStyle;
        private GUIStyle _welcomeTitleStyle;
        private GUIStyle _welcomeSubStyle;
        private GUIStyle _contextToggleOnStyle;
        private GUIStyle _contextToggleOffStyle;
        private GUIStyle _toolCallStyle;
        private GUIStyle _toolCallErrorStyle;
        private GUIStyle _msgActionBtnStyle;
        private GUIStyle _toolCallFoldoutStyle;
        private bool _stylesReady;

        // ─── Message Action Bar State ───

        private string _copiedMsgId;
        private double _copiedMsgTime;

        // ─── Tool Call Foldout State ───

        private readonly System.Collections.Generic.HashSet<int> _expandedToolGroups = new();

        // ─── Menu ───

        [MenuItem("Window/UniAI/Chat")]
        [MenuItem("Tools/UniAI/Chat")]
        public static void Open()
        {
            var w = GetWindow<AIChatWindow>("UniAI 对话");
            w.minSize = new Vector2(640, 400);
        }

        public static void OpenWithAgent(AgentDefinition agent)
        {
            var w = GetWindow<AIChatWindow>("UniAI 对话");
            w.minSize = new Vector2(640, 400);

            if (agent == null)
            {
                w._controller?.CreateNewSession();
                return;
            }

            if (w._controller?.AvailableAgents != null)
            {
                foreach (var a in w._controller.AvailableAgents)
                {
                    if (a == agent)
                    {
                        w._controller.CreateNewSession(agent);
                        return;
                    }
                }
            }
        }

        // ─── Lifecycle ───

        private void OnEnable()
        {
            _controller = new ChatWindowController();
            _controller.Initialize();

            // 订阅 Controller 事件
            _controller.OnStateChanged += Repaint;
            _controller.OnScrollToBottom += () => _scrollToBottom = true;
            _controller.OnStreamingChanged += OnStreamingChanged;
            _controller.OnAIAvatarChanged += avatar => _aiAvatar = avatar;

            // 恢复编辑器偏好
            var prefs = AIConfigManager.Prefs;
            _showSidebar = prefs.ShowSidebar;
            _contextSlots = (ContextCollector.ContextSlot)prefs.DefaultContextSlots;

            LoadAvatars();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.OnStateChanged -= Repaint;
                _controller.OnStreamingChanged -= OnStreamingChanged;
                _controller.Dispose();
            }
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnStreamingChanged(bool isStreaming)
        {
            if (isStreaming)
            {
                _spinnerStartTime = EditorApplication.timeSinceStartup;
                _spinnerFrame = 0;
            }
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (_controller == null || !_controller.IsStreaming) return;
            int frame = (int)((EditorApplication.timeSinceStartup - _spinnerStartTime) * 8) % SpinnerFrameCount;
            if (frame != _spinnerFrame)
            {
                _spinnerFrame = frame;
                Repaint();
            }
        }

        // ─── OnGUI Entry ───

        private void OnGUI()
        {
            if (_controller == null) return;
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

        // ─── Avatar Loading ───

        private void LoadAvatars()
        {
            var prefs = AIConfigManager.Prefs;
            _userAvatar = prefs.UserAvatar;
            RefreshAIAvatar();
        }

        private void RefreshAIAvatar()
        {
            var agent = _controller?.FindAgentById(_controller.ActiveSession?.AgentId);
            if (agent != null)
            {
                _aiAvatar = agent.Icon;
                return;
            }

            var prefs = AIConfigManager.Prefs;
            _aiAvatar = prefs.AiAvatar;
        }
    }
}
