using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        // ─── Quick Actions Data ───

        private static readonly (string Label, string Icon, ContextCollector.ContextSlot Slot, string Message)[] _quickActions =
        {
            ("解释", "⚡", ContextCollector.ContextSlot.Selection, "Explain the selected object's scripts in detail."),
            ("优化", "⚒", ContextCollector.ContextSlot.Selection, "Suggest optimizations for the selected hierarchy."),
            ("注释", "✍", ContextCollector.ContextSlot.Selection, "Generate XML doc comments for the selected code."),
            ("修错", "⚙", ContextCollector.ContextSlot.Console, "Analyze and fix the console errors.")
        };

        // ─── Guide Cards Data ───

        private static readonly (string Title, string Desc, ContextCollector.ContextSlot Slot, string Message)[] _guideCards =
        {
            ("编写单例脚本", "生成线程安全的 MonoBehaviour 单例模板", ContextCollector.ContextSlot.None, "Write a thread-safe MonoBehaviour singleton base class with lazy initialization."),
            ("分析选中对象", "解读选中 GameObject 的组件结构", ContextCollector.ContextSlot.Selection, "Explain the selected object's scripts and component setup in detail."),
            ("修复控制台报错", "读取并修复最近的控制台错误", ContextCollector.ContextSlot.Console, "Analyze and fix the console errors."),
            ("优化层级结构", "为选中的层级结构提供性能优化建议", ContextCollector.ContextSlot.Selection, "Suggest optimizations for the selected hierarchy, including draw call batching and component efficiency.")
        };

        // ─── Styles ───

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

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

        private void EnsureToolCallStyle()
        {
            if (_toolCallStyle != null) return;
            _toolCallStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                wordWrap = true,
                richText = false,
                padding = new RectOffset(4, 4, 2, 2)
            };
            _toolCallStyle.normal.textColor = new Color(0.6f, 0.8f, 0.65f);
        }

        // ─── Drawing Helpers ───

        private static void EnsureSpinnerIcons()
        {
            if (_spinnerIcons != null) return;
            _spinnerIcons = new GUIContent[SPINNER_FRAME_COUNT];
            for (int i = 0; i < SPINNER_FRAME_COUNT; i++)
                _spinnerIcons[i] = EditorGUIUtility.IconContent($"WaitSpin{i:D2}");
        }

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
            return text.Length <= maxLen ? text : text.Substring(0, maxLen - 1) + "…";
        }
    }
}
