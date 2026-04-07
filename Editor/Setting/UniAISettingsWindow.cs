using UniAI.Editor.Chat;
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

            // 默认上下文槽位
            var currentSlots = (ContextCollector.ContextSlot)prefs.DefaultContextSlots;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("默认上下文", GUILayout.Width(LabelWidth));
            DrawContextSlotToggle(ref currentSlots, ContextCollector.ContextSlot.Selection, "选中对象");
            DrawContextSlotToggle(ref currentSlots, ContextCollector.ContextSlot.Console, "控制台");
            DrawContextSlotToggle(ref currentSlots, ContextCollector.ContextSlot.Project, "工程资源");
            EditorGUILayout.EndHorizontal();
            if ((int)currentSlots != prefs.DefaultContextSlots)
                prefs.DefaultContextSlots = (int)currentSlots;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("历史会话上限", GUILayout.Width(LabelWidth));
            var newMax = EditorGUILayout.IntField(prefs.MaxHistorySessions);
            if (newMax != prefs.MaxHistorySessions)
                prefs.MaxHistorySessions = Mathf.Clamp(newMax, 5, 500);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Agent 创建目录", GUILayout.Width(LabelWidth));
            EditorGUILayout.TextField(prefs.AgentDirectory);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("选择 Agent 创建目录", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
                    // 转换为相对路径
                    if (path.StartsWith(Application.dataPath))
                    {
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                        prefs.AgentDirectory = path;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("路径错误", "请选择 Assets 目录下的文件夹", "确定");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("用户头像", GUILayout.Width(LabelWidth));
            var newUserAvatar = (Texture2D)EditorGUILayout.ObjectField(prefs.UserAvatar, typeof(Texture2D), false);
            if (newUserAvatar != prefs.UserAvatar)
                prefs.UserAvatar = newUserAvatar;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("AI 默认头像", GUILayout.Width(LabelWidth));
            var newAiAvatar = (Texture2D)EditorGUILayout.ObjectField(prefs.AiAvatar, typeof(Texture2D), false);
            if (newAiAvatar != prefs.AiAvatar)
                prefs.AiAvatar = newAiAvatar;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("    Agent 自带 Icon 时优先使用 Agent Icon", EditorStyles.miniLabel);

            GUILayout.Space(12);
            GUILayout.Label("Tool 设置", _sectionTitleStyle);
            EditorGUILayout.LabelField("Agent Tool 执行的相关参数。", EditorStyles.miniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Tool 超时 (秒)", GUILayout.Width(LabelWidth));
            var newTimeout = EditorGUILayout.Slider(prefs.ToolTimeout, 5f, 120f);
            if (!Mathf.Approximately(newTimeout, prefs.ToolTimeout))
                prefs.ToolTimeout = newTimeout;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("最大输出字符数", GUILayout.Width(LabelWidth));
            var newMaxChars = EditorGUILayout.IntField(prefs.ToolMaxOutputChars);
            if (newMaxChars != prefs.ToolMaxOutputChars)
                prefs.ToolMaxOutputChars = Mathf.Clamp(newMaxChars, 5000, 200000);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索最大匹配数", GUILayout.Width(LabelWidth));
            var newMaxMatches = EditorGUILayout.IntField(prefs.SearchMaxMatches);
            if (newMaxMatches != prefs.SearchMaxMatches)
                prefs.SearchMaxMatches = Mathf.Clamp(newMaxMatches, 10, 1000);
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

        private static void DrawContextSlotToggle(ref ContextCollector.ContextSlot slots, ContextCollector.ContextSlot flag, string label)
        {
            bool isOn = slots.HasFlag(flag);
            bool newOn = EditorGUILayout.ToggleLeft(label, isOn, GUILayout.Width(70));
            if (newOn != isOn)
                slots = newOn ? slots | flag : slots & ~flag;
        }
    }
}
