using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Tools Tab — 只读展示 UniAIToolRegistry 中注册的所有内置工具，按 Group 分组。
    /// </summary>
    internal class ToolsTab : ManagerTab
    {
        public override string TabName => "工具";
        public override string TabIcon => "🧰";
        public override int Order => 3;

        private const float PAD = 16f;

        private Vector2 _scroll;
        private string _search = "";
        private readonly Dictionary<string, bool> _groupFoldouts = new();
        private readonly HashSet<string> _schemaExpanded = new();

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _toolNameStyle;
        private GUIStyle _descStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _schemaStyle;
        private GUIStyle _builtInBadgeStyle;
        private GUIStyle _customBadgeStyle;
        private bool _stylesReady;

        public override void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _toolNameStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _descStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 11 };
            _descStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
            _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2)
            };
            _badgeStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            _schemaStyle = new GUIStyle(EditorStyles.textArea)
            {
                font = EditorStyles.miniFont,
                fontSize = 10,
                wordWrap = true,
                richText = false
            };
            _builtInBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 1, 1)
            };
            _builtInBadgeStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
            _customBadgeStyle = new GUIStyle(_builtInBadgeStyle);
            _customBadgeStyle.normal.textColor = new Color(0.6f, 1f, 0.7f);
        }

        public override void OnGUI(float width, float height)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label("工具", _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("(只读)", EditorStyles.miniLabel);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Search bar
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            EditorGUILayout.LabelField("搜索", GUILayout.Width(40));
            _search = EditorGUILayout.TextField(_search ?? "");
            if (GUILayout.Button("刷新", GUILayout.Width(60)))
            {
                UniAIToolRegistry.Reset();
                _groupFoldouts.Clear();
                _schemaExpanded.Clear();
            }
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            var handlers = UniAIToolRegistry.AllHandlers;
            string lowerSearch = string.IsNullOrEmpty(_search) ? null : _search.ToLowerInvariant();

            var filtered = lowerSearch == null
                ? handlers
                : handlers.Where(h =>
                    h.Name.ToLowerInvariant().Contains(lowerSearch)
                    || (h.Definition.Description ?? "").ToLowerInvariant().Contains(lowerSearch)
                    || h.Group.ToLowerInvariant().Contains(lowerSearch)).ToList();

            var groups = filtered
                .GroupBy(h => h.Group)
                .OrderByDescending(g => g.Any(h => h.IsBuiltIn))
                .ThenBy(g => g.Key);

            int totalCount = 0;
            foreach (var group in groups)
            {
                totalCount += group.Count();
                DrawGroup(group.Key, group.OrderBy(h => h.Name).ToList());
                GUILayout.Space(8);
            }

            GUILayout.Space(PAD);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            GUILayout.Label($"共 {totalCount} 个工具，{handlers.Count} 总注册。", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(PAD);
            EditorGUILayout.EndScrollView();
        }

        private void DrawGroup(string groupName, List<ToolHandlerInfo> tools)
        {
            bool groupIsBuiltIn = tools.Any(t => t.IsBuiltIn);

            EditorGUIHelper.DrawBox(PAD, () =>
            {
                if (!_groupFoldouts.TryGetValue(groupName, out bool expanded))
                    expanded = true;

                EditorGUILayout.BeginHorizontal();
                string foldoutLabel = $"{groupName}  ({tools.Count})";
                float foldoutWidth = _sectionTitleStyle.CalcSize(new GUIContent(foldoutLabel)).x + 16f;
                var foldoutRect = GUILayoutUtility.GetRect(foldoutWidth, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(false));
                expanded = EditorGUI.Foldout(foldoutRect, expanded, foldoutLabel, true, _sectionTitleStyle);

                GUILayout.Space(6);
                string badgeText = groupIsBuiltIn ? "内置" : "自定义";
                var badgeGUIStyle = groupIsBuiltIn ? _builtInBadgeStyle : _customBadgeStyle;
                var badgeColor = groupIsBuiltIn
                    ? new Color(0.3f, 0.5f, 0.8f, 0.2f)
                    : new Color(0.3f, 0.7f, 0.4f, 0.2f);
                var badgeRect = GUILayoutUtility.GetRect(new GUIContent(badgeText), badgeGUIStyle,
                    GUILayout.ExpandWidth(false));
                EditorGUI.DrawRect(badgeRect, badgeColor);
                GUI.Label(badgeRect, badgeText, badgeGUIStyle);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                _groupFoldouts[groupName] = expanded;

                if (!expanded) return;

                GUILayout.Space(4);
                for (int i = 0; i < tools.Count; i++)
                {
                    DrawTool(tools[i]);
                    if (i < tools.Count - 1)
                    {
                        GUILayout.Space(4);
                        EditorGUI.DrawRect(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true)),
                            new Color(1f, 1f, 1f, 0.06f));
                        GUILayout.Space(4);
                    }
                }
            });
        }

        private void DrawTool(ToolHandlerInfo info)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(info.Name, _toolNameStyle, GUILayout.ExpandWidth(false));

            if (info.RequiresPolling)
            {
                GUILayout.Space(6);
                var badge = $"polling ≤{info.MaxPollSeconds}s";
                var rect = GUILayoutUtility.GetRect(new GUIContent(badge), _badgeStyle,
                    GUILayout.ExpandWidth(false));
                EditorGUI.DrawRect(rect, new Color(1f, 0.65f, 0.2f, 0.18f));
                GUI.Label(rect, badge, _badgeStyle);
            }

            GUILayout.FlexibleSpace();

            string schemaKey = info.Name;
            bool schemaShown = _schemaExpanded.Contains(schemaKey);
            if (GUILayout.Button(schemaShown ? "隐藏 Schema" : "查看 Schema",
                EditorStyles.miniButton, GUILayout.Width(100)))
            {
                if (schemaShown) _schemaExpanded.Remove(schemaKey);
                else _schemaExpanded.Add(schemaKey);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(info.Definition.Description))
            {
                GUILayout.Space(2);
                EditorGUILayout.LabelField(info.Definition.Description, _descStyle);
            }

            if (_schemaExpanded.Contains(schemaKey))
            {
                GUILayout.Space(4);
                string pretty = PrettyPrintJson(info.Definition.ParametersSchema);
                EditorGUILayout.SelectableLabel(pretty, _schemaStyle,
                    GUILayout.MinHeight(60),
                    GUILayout.MaxHeight(240),
                    GUILayout.ExpandWidth(true));
            }
        }

        private static string PrettyPrintJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return "(无 Schema)";
            try
            {
                var token = JToken.Parse(json);
                return token.ToString(Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }
    }
}
