using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// UniAI 设置窗口 — 集中管理运行时参数和编辑器偏好
    /// </summary>
    public class UniAISettingsWindow : EditorWindow
    {
        private const float LabelWidth = 140f;
        private const float Pad = 16f;

        private AIConfig _config;
        private Vector2 _scroll;

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private bool _stylesReady;

        [MenuItem("Window/UniAI/Settings")]
        [MenuItem("Tools/UniAI/Settings")]
        public static void Open()
        {
            var w = GetWindow<UniAISettingsWindow>("UniAI Settings");
            w.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            _config = AIConfigManager.LoadConfig();
        }

        private void OnGUI()
        {
            if (_config == null) _config = AIConfigManager.LoadConfig();
            EnsureStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(Pad);

            // Title
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label("UniAI 设置", _titleStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(16);

            // Runtime settings
            EditorGUIHelper.DrawSection(Pad, DrawRuntimeSettings);

            GUILayout.Space(12);

            // Editor settings
            EditorGUIHelper.DrawSection(Pad, DrawEditorSettings);

            GUILayout.FlexibleSpace();

            // Bottom bar
            DrawBottomBar();

            GUILayout.Space(Pad);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeSettings()
        {
            GUILayout.Label("运行时设置", _sectionTitleStyle);
            EditorGUILayout.LabelField("影响游戏运行时的 AI 调用参数，存储在 UniAISettings 资产中。", EditorStyles.miniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("请求超时 (秒)", GUILayout.Width(LabelWidth));
            _config.General.TimeoutSeconds = EditorGUILayout.IntSlider(_config.General.TimeoutSeconds, 10, 300);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("日志级别", GUILayout.Width(LabelWidth));
            _config.General.LogLevel = (AILogLevel)EditorGUILayout.EnumPopup(_config.General.LogLevel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEditorSettings()
        {
            var prefs = AIConfigManager.Prefs;

            GUILayout.Label("编辑器设置", _sectionTitleStyle);
            EditorGUILayout.LabelField("仅影响编辑器中的 AI 对话窗口体验。", EditorStyles.miniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("侧边栏默认展开", GUILayout.Width(LabelWidth));
            var newSidebar = EditorGUILayout.Toggle(prefs.ShowSidebar);
            if (newSidebar != prefs.ShowSidebar)
                prefs.ShowSidebar = newSidebar;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("历史会话上限", GUILayout.Width(LabelWidth));
            var newMax = EditorGUILayout.IntField(prefs.MaxHistorySessions);
            if (newMax != prefs.MaxHistorySessions)
                prefs.MaxHistorySessions = Mathf.Clamp(newMax, 5, 500);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("保存", GUILayout.Height(28), GUILayout.Width(80)))
            {
                AIConfigManager.SaveConfig(_config);
                AIConfigManager.SavePrefs();
                ShowNotification(new GUIContent("设置已保存"));
            }

            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }
    }
}
