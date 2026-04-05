using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// UniAI 对话窗口 — 侧边栏 + 主对话区经典布局
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

        // ─── State ───

        private AIConfig _config;
        private AIClient _client;
        private AIAgentRunner _runner;
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
        private int _selectedModelIndex;
        private string _currentModelId;
        private bool _showActionBar;

        // ─── Agent State ───

        private List<AgentDefinition> _availableAgents;
        private string[] _agentNames;
        private int _selectedAgentIndex;

        // Spinner state
        private double _spinnerStartTime;
        private int _spinnerFrame;
        private const int SpinnerFrameCount = 12;
        private static GUIContent[] _spinnerIcons;

        // ─── Avatar ───

        private Texture2D _userAvatar;
        private Texture2D _aiAvatar;
        private const string IconsDir = "Assets/UniAI/Editor/Icons";
        private const string DefaultUserAvatarPath = IconsDir + "/avatar-user.png";
        private const string DefaultAIAvatarPath = IconsDir + "/avatar-ai.png";

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
        private bool _stylesReady;

        // ─── Model Cache ───

        private string[] _modelNames;
        private List<ModelRoute> _modelEntries;

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
            if (agent == null || w._availableAgents == null) return;
            for (int i = 0; i < w._availableAgents.Count; i++)
            {
                if (w._availableAgents[i] == agent)
                {
                    w._selectedAgentIndex = i;
                    w.CreateNewSession();
                    w.EnsureRunner();
                    break;
                }
            }
        }

        // ─── Lifecycle ───

        private void OnEnable()
        {
            _config = AIConfigManager.LoadConfig();
            _history = new ChatHistory();
            _history.Load();

            // 恢复编辑器偏好
            var prefs = AIConfigManager.Prefs;
            _showSidebar = prefs.ShowSidebar;
            _currentModelId = prefs.LastSelectedModelId;

            RebuildModelCache();
            RebuildAgentCache();
            LoadAvatars();
            EnsureRunner();
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
                int frame = (int)((EditorApplication.timeSinceStartup - _spinnerStartTime) * 8) % SpinnerFrameCount;
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

        // ─── Avatar Loading ───

        private void LoadAvatars()
        {
            var prefs = AIConfigManager.Prefs;
            _userAvatar = LoadAvatarTexture(prefs.UserAvatar, DefaultUserAvatarPath);
            _aiAvatar = LoadAvatarTexture(null, DefaultAIAvatarPath);
        }

        private static Texture2D LoadAvatarTexture(Texture2D customTex, string defaultPath)
        {
            // 1. Try custom texture from preferences
            if (customTex != null) return customTex;

            // 2. Try default icon in Icons folder
            var defaultTex = AssetDatabase.LoadAssetAtPath<Texture2D>(defaultPath);
            if (defaultTex != null) return defaultTex;

            // 3. Fallback: generate a simple placeholder
            return null;
        }

        // ─── Agent Cache ───

        private void RebuildAgentCache()
        {
            _availableAgents = AgentManager.GetAllAgents();
            _agentNames = AgentManager.GetAgentNames(_availableAgents);
        }
    }
}
