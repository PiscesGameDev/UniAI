using UniAI.Editor.Chat;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// 设置 Tab — 滚动表单：运行时设置 + 编辑器偏好 + Tool 设置
    /// </summary>
    internal class SettingsTab : ManagerTab
    {
        public override string TabName => "设置";
        public override string TabIcon => "⚙";
        public override int Order => 3;

        private const float LABEL_WIDTH = 140f;
        private const float PAD = 16f;

        private Vector2 _scroll;

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private bool _stylesReady;

        public override void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }

        public override void OnGUI(float width, float height)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("UniAI 设置", _titleStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(16);

            EditorGUIHelper.DrawSection(PAD, DrawRuntimeSettings);

            GUILayout.Space(12);

            EditorGUIHelper.DrawSection(PAD, DrawEditorSettings);

            GUILayout.FlexibleSpace();
            GUILayout.Space(PAD);
            EditorGUILayout.EndScrollView();
        }

        private void DrawRuntimeSettings()
        {
            GUILayout.Label("运行时设置", _sectionTitleStyle);
            EditorGUILayout.LabelField("影响游戏运行时的 AI 调用参数，存储在 UniAISettings 资产中。", EditorStyles.miniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("请求超时 (秒)", GUILayout.Width(LABEL_WIDTH));
            Config.General.TimeoutSeconds = EditorGUILayout.IntSlider(Config.General.TimeoutSeconds, 10, 300);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("日志级别", GUILayout.Width(LABEL_WIDTH));
            Config.General.LogLevel = (AILogLevel)EditorGUILayout.EnumPopup(Config.General.LogLevel);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);
            DrawContextWindowSettings();
        }

        private void DrawContextWindowSettings()
        {
            var cw = Config.General.ContextWindow;

            GUILayout.Label("上下文窗口", _sectionTitleStyle);
            EditorGUILayout.LabelField("长对话自动截断与摘要压缩，防止超出模型 token 上限。", EditorStyles.miniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("启用", GUILayout.Width(LABEL_WIDTH));
            cw.Enabled = EditorGUILayout.Toggle(cw.Enabled);
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!cw.Enabled))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("最大上下文 Token", GUILayout.Width(LABEL_WIDTH));
                cw.MaxContextTokens = EditorGUILayout.IntField(cw.MaxContextTokens);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("    0 = 自动（模型上下文窗口的 80%）", EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("预留输出 Token", GUILayout.Width(LABEL_WIDTH));
                cw.ReservedOutputTokens = EditorGUILayout.IntSlider(cw.ReservedOutputTokens, 512, 16384);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("最少保留消息数", GUILayout.Width(LABEL_WIDTH));
                cw.MinRecentMessages = EditorGUILayout.IntSlider(cw.MinRecentMessages, 2, 20);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("启用摘要压缩", GUILayout.Width(LABEL_WIDTH));
                cw.EnableSummary = EditorGUILayout.Toggle(cw.EnableSummary);
                EditorGUILayout.EndHorizontal();

                using (new EditorGUI.DisabledScope(!cw.EnableSummary))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("摘要最大 Token", GUILayout.Width(LABEL_WIDTH));
                    cw.SummaryMaxTokens = EditorGUILayout.IntSlider(cw.SummaryMaxTokens, 128, 2048);
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawEditorSettings()
        {
            var prefs = AIConfigManager.Prefs;

            GUILayout.Label("编辑器设置", _sectionTitleStyle);
            EditorGUILayout.LabelField("仅影响编辑器中的 AI 对话窗口体验。", EditorStyles.miniLabel);
            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("侧边栏默认展开", GUILayout.Width(LABEL_WIDTH));
            var newSidebar = EditorGUILayout.Toggle(prefs.ShowSidebar);
            if (newSidebar != prefs.ShowSidebar)
                prefs.ShowSidebar = newSidebar;
            EditorGUILayout.EndHorizontal();

            // 默认上下文槽位
            var currentSlots = (ContextCollector.ContextSlot)prefs.DefaultContextSlots;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("默认上下文", GUILayout.Width(LABEL_WIDTH));
            DrawContextSlotToggle(ref currentSlots, ContextCollector.ContextSlot.Selection, "选中对象");
            DrawContextSlotToggle(ref currentSlots, ContextCollector.ContextSlot.Console, "控制台");
            DrawContextSlotToggle(ref currentSlots, ContextCollector.ContextSlot.Project, "工程资源");
            EditorGUILayout.EndHorizontal();
            if ((int)currentSlots != prefs.DefaultContextSlots)
                prefs.DefaultContextSlots = (int)currentSlots;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("历史会话上限", GUILayout.Width(LABEL_WIDTH));
            var newMax = EditorGUILayout.IntField(prefs.MaxHistorySessions);
            if (newMax != prefs.MaxHistorySessions)
                prefs.MaxHistorySessions = Mathf.Clamp(newMax, 5, 500);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Agent 创建目录", GUILayout.Width(LABEL_WIDTH));
            EditorGUILayout.TextField(prefs.AgentDirectory);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("选择 Agent 创建目录", "Assets", "");
                if (!string.IsNullOrEmpty(path))
                {
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
            EditorGUILayout.LabelField("用户头像", GUILayout.Width(LABEL_WIDTH));
            var newUserAvatar = (Texture2D)EditorGUILayout.ObjectField(prefs.UserAvatar, typeof(Texture2D), false);
            if (newUserAvatar != prefs.UserAvatar)
                prefs.UserAvatar = newUserAvatar;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("AI 默认头像", GUILayout.Width(LABEL_WIDTH));
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
            EditorGUILayout.LabelField("Tool 超时 (秒)", GUILayout.Width(LABEL_WIDTH));
            var newTimeout = EditorGUILayout.Slider(prefs.ToolTimeout, 5f, 120f);
            if (!Mathf.Approximately(newTimeout, prefs.ToolTimeout))
                prefs.ToolTimeout = newTimeout;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("最大输出字符数", GUILayout.Width(LABEL_WIDTH));
            var newMaxChars = EditorGUILayout.IntField(prefs.ToolMaxOutputChars);
            if (newMaxChars != prefs.ToolMaxOutputChars)
                prefs.ToolMaxOutputChars = Mathf.Clamp(newMaxChars, 5000, 200000);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索最大匹配数", GUILayout.Width(LABEL_WIDTH));
            var newMaxMatches = EditorGUILayout.IntField(prefs.SearchMaxMatches);
            if (newMaxMatches != prefs.SearchMaxMatches)
                prefs.SearchMaxMatches = Mathf.Clamp(newMaxMatches, 10, 1000);
            EditorGUILayout.EndHorizontal();
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
