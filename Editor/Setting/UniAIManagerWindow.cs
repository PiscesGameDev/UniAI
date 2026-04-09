using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// UniAI 统一管理窗口 — 左侧窄图标栏 + 右侧 Tab 内容
    /// </summary>
    public class UniAIManagerWindow : EditorWindow
    {
        private const float NAV_WIDTH = 44f;
        private const float ICON_SIZE = 36f;
        private const float INDICATOR_WIDTH = 3f;
        private const float MARGIN = 4f;
        private const float GAP = 3f;

        internal AIConfig Config { get; private set; }

        private List<ManagerTab> _tabs;
        private int _currentTabIndex;

        // Styles
        private GUIStyle _iconStyle;
        private GUIStyle _iconSelectedStyle;
        private GUIStyle _saveIconStyle;
        private bool _stylesReady;

        [MenuItem("Window/UniAI/Manager", priority = 100)]
        [MenuItem("Tools/UniAI/Manager", priority = 100)]
        public static void Open()
        {
            var w = GetWindow<UniAIManagerWindow>("UniAI Manager");
            w.minSize = new Vector2(720, 460);
        }

        /// <summary>打开并跳转到渠道页</summary>
        public static void OpenChannel()
        {
            Open();
            var w = GetWindow<UniAIManagerWindow>();
            w.SwitchTo<ChannelTab>();
        }

        /// <summary>打开并跳转到 Agent 页</summary>
        public static void OpenAgent()
        {
            Open();
            var w = GetWindow<UniAIManagerWindow>();
            w.SwitchTo<AgentTab>();
        }

        /// <summary>打开并跳转到模型页</summary>
        public static void OpenModel()
        {
            Open();
            var w = GetWindow<UniAIManagerWindow>();
            w.SwitchTo<ModelTab>();
        }

        private void OnEnable()
        {
            Config = AIConfigManager.LoadConfig();
            _tabs = new List<ManagerTab>
            {
                new ChannelTab(),
                new ModelTab(),
                new AgentTab(),
                new McpTab(),
                new ToolsTab(),
                new SettingsTab()
            };
            _tabs.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (var tab in _tabs) tab.Initialize(this);
        }

        private void OnDisable()
        {
            if (_tabs == null) return;
            foreach (var tab in _tabs) tab.OnDestroy();
        }

        private void OnGUI()
        {
            if (Config == null) Config = AIConfigManager.LoadConfig();
            EnsureStyles();

            float w = position.width;
            float h = position.height;

            // Window dark background (visible as margin/gap)
            EditorGUI.DrawRect(new Rect(0, 0, w, h), EditorGUIHelper.WindowBg);

            // Left card rect
            float navCardW = NAV_WIDTH;
            var navCardRect = new Rect(MARGIN, MARGIN, navCardW, h - MARGIN * 2);
            EditorGUI.DrawRect(navCardRect, EditorGUIHelper.LeftPanelBg);

            // Right card rect
            float contentX = MARGIN + navCardW + GAP;
            float contentW = w - contentX - MARGIN;
            float contentH = h - MARGIN * 2;
            var contentCardRect = new Rect(contentX, MARGIN, contentW, contentH);
            EditorGUI.DrawRect(contentCardRect, EditorGUIHelper.CardBg);

            // Draw icon rail (positioned inside the nav card)
            DrawIconRail(navCardRect);

            // Content area (inside the content card)
            GUILayout.BeginArea(contentCardRect);
            DrawContent(contentW, contentH);
            GUILayout.EndArea();
        }

        private void DrawIconRail(Rect cardRect)
        {
            float y = cardRect.y + 10f;

            for (int i = 0; i < _tabs.Count; i++)
            {
                bool isSelected = i == _currentTabIndex;
                float iconX = cardRect.x + (cardRect.width - ICON_SIZE) * 0.5f;
                var iconRect = new Rect(iconX, y, ICON_SIZE, ICON_SIZE);

                bool isHover = iconRect.Contains(Event.current.mousePosition);

                if (isHover && !isSelected)
                    EditorGUI.DrawRect(iconRect, EditorGUIHelper.ItemBg);

                // Selected indicator bar (left edge)
                if (isSelected)
                    EditorGUI.DrawRect(new Rect(cardRect.x, y, INDICATOR_WIDTH, ICON_SIZE), EditorGUIHelper.AccentColor);

                var style = isSelected ? _iconSelectedStyle : _iconStyle;
                GUI.Label(iconRect, new GUIContent(_tabs[i].TabIcon, _tabs[i].TabName), style);

                if (Event.current.type == EventType.MouseDown && iconRect.Contains(Event.current.mousePosition))
                {
                    _currentTabIndex = i;
                    Event.current.Use();
                    Repaint();
                }

                if (isHover && Event.current.type == EventType.Repaint)
                    Repaint();

                y += ICON_SIZE + 4f;
            }

            // Save icon at bottom
            float saveY = cardRect.yMax - ICON_SIZE - 10f;
            float saveX = cardRect.x + (cardRect.width - ICON_SIZE) * 0.5f;
            var saveRect = new Rect(saveX, saveY, ICON_SIZE, ICON_SIZE);

            bool saveHover = saveRect.Contains(Event.current.mousePosition);
            if (saveHover)
                EditorGUI.DrawRect(saveRect, EditorGUIHelper.ItemBg);

            GUI.Label(saveRect, new GUIContent("💾", "保存所有配置"), _saveIconStyle);

            if (Event.current.type == EventType.MouseDown && saveRect.Contains(Event.current.mousePosition))
            {
                SaveAll();
                Event.current.Use();
            }

            if (saveHover && Event.current.type == EventType.Repaint)
                Repaint();
        }

        private void DrawContent(float width, float height)
        {
            if (_currentTabIndex >= 0 && _currentTabIndex < _tabs.Count)
            {
                var tab = _tabs[_currentTabIndex];
                tab.EnsureStyles();
                tab.OnGUI(width, height);
            }
        }

        private void SaveAll()
        {
            foreach (var tab in _tabs) tab.OnSave();
            AIConfigManager.SaveConfig(Config);
            AIConfigManager.SavePrefs();
            ShowNotification(new GUIContent("已保存"));
        }

        private void SwitchTo<T>() where T : ManagerTab
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i] is T)
                {
                    _currentTabIndex = i;
                    break;
                }
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _iconStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _iconStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);

            _iconSelectedStyle = new GUIStyle(_iconStyle);
            _iconSelectedStyle.normal.textColor = Color.white;

            _saveIconStyle = new GUIStyle(_iconStyle)
            {
                fontSize = 16
            };
            _saveIconStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
        }
    }
}
