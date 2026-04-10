using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UniAI.Editor
{
    [CustomEditor(typeof(AgentDefinition))]
    internal class AgentDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty _id;
        private SerializedProperty _agentName;
        private SerializedProperty _description;
        private SerializedProperty _icon;
        private SerializedProperty _specifyModel;
        private SerializedProperty _temperature;
        private SerializedProperty _maxTokens;
        private SerializedProperty _maxTurns;
        private SerializedProperty _toolGroups;
        private SerializedProperty _mcpServers;
        private SerializedProperty _systemPrompt;

        private ReorderableList _mcpServerList;
        private ReorderableList _toolGroupList;

        // Styles
        private static readonly Color _promptBg = new(0.16f, 0.18f, 0.20f);
        private GUIStyle _promptLabelStyle;
        private GUIStyle _promptTextStyle;
        private GUIStyle _charCountStyle;
        private bool _stylesReady;

        private void OnEnable()
        {
            _id = serializedObject.FindProperty("_id");
            _agentName = serializedObject.FindProperty("_agentName");
            _description = serializedObject.FindProperty("_description");
            _icon = serializedObject.FindProperty("_icon");
            _specifyModel = serializedObject.FindProperty("_specifyModel");
            _temperature = serializedObject.FindProperty("_temperature");
            _maxTokens = serializedObject.FindProperty("_maxTokens");
            _maxTurns = serializedObject.FindProperty("_maxTurns");
            _toolGroups = serializedObject.FindProperty("_toolGroups");
            _mcpServers = serializedObject.FindProperty("_mcpServers");
            _systemPrompt = serializedObject.FindProperty("_systemPrompt");

            _mcpServerList = new ReorderableList(serializedObject, _mcpServers, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "MCP Servers"),
                drawElementCallback = DrawMcpServerElement,
                elementHeight = EditorGUIUtility.singleLineHeight + 4,
                drawNoneElementCallback = rect => EditorGUI.LabelField(rect, "无 MCP Server — 点击 + 添加", EditorStyles.centeredGreyMiniLabel)
            };

            _toolGroupList = new ReorderableList(serializedObject, _toolGroups, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "工具分组"),
                drawElementCallback = DrawToolGroupElement,
                elementHeight = EditorGUIUtility.singleLineHeight + 4,
                drawNoneElementCallback = rect => EditorGUI.LabelField(rect, "无工具分组 — 点击 + 添加", EditorStyles.centeredGreyMiniLabel),
                onAddDropdownCallback = OnToolGroupAddDropdown
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            DrawBasicInfo();
            EditorGUILayout.Space(8);
            DrawParameters();
            EditorGUILayout.Space(8);
            DrawTools();
            EditorGUILayout.Space(8);
            DrawMcpServers();
            EditorGUILayout.Space(8);
            DrawSystemPrompt();

            serializedObject.ApplyModifiedProperties();
        }

        // ─── 基本信息 ───

        private void DrawBasicInfo()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            // 左侧：图标
            EditorGUILayout.BeginVertical(GUILayout.Width(68));
            _icon.objectReferenceValue = EditorGUILayout.ObjectField(
                _icon.objectReferenceValue, typeof(Texture2D), false,
                GUILayout.Width(64), GUILayout.Height(64));
            EditorGUILayout.EndVertical();

            GUILayout.Space(8);

            // 右侧：名称 + 职责描述
            EditorGUILayout.BeginVertical();
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 60;
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(_id, new GUIContent("Id"));
            EditorGUI.EndDisabledGroup();
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(_agentName, new GUIContent("名称"));
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(_description, new GUIContent("职责描述"));
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ─── 参数设置 ───

        private void DrawParameters()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("模型参数", EditorStyles.boldLabel);
            GUILayout.Space(4);


            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_specifyModel, new GUIContent("Model"));
            var btnRect = GUILayoutUtility.GetRect(new GUIContent("选择"), EditorStyles.miniButton,
                GUILayout.Width(50));
            if (GUI.Button(btnRect, "选择", EditorStyles.miniButton))
                ShowModelPicker(btnRect);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Slider(_temperature, 0f, 1f, new GUIContent("Temperature", "控制回复的创造性，值越高越随机"));
            _maxTokens.intValue = EditorGUILayout.IntSlider(
                new GUIContent("Max Tokens", "单次回复最大 Token 数"),
                _maxTokens.intValue, 256, 32768);
            EditorGUILayout.IntSlider(_maxTurns, 1, 50, new GUIContent("Max Turns", "Tool 调用最大循环轮数"));

            EditorGUILayout.EndVertical();
        }

        private void ShowModelPicker(Rect activatorRect)
        {
            var menu = new GenericMenu();
            var settings = UniAISettings.Instance;
            string current = _specifyModel.stringValue;

            // 汇总所有启用渠道的模型，按厂商分组去重
            var byVendor = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.SortedSet<string>>();
            if (settings != null)
            {
                foreach (var provider in settings.Providers)
                {
                    if (provider == null || !provider.Enabled || provider.Models == null) continue;
                    foreach (var modelId in provider.Models)
                    {
                        if (string.IsNullOrEmpty(modelId)) continue;
                        var entry = ModelRegistry.Get(modelId);
                        string vendor = !string.IsNullOrEmpty(entry?.Vendor) ? entry.Vendor : "Unknown";
                        if (!byVendor.TryGetValue(vendor, out var set))
                            byVendor[vendor] = set = new System.Collections.Generic.SortedSet<string>();
                        set.Add(modelId);
                    }
                }
            }

            if (byVendor.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("无可用模型 — 请先在 UniAI Manager 中配置渠道"));
            }
            else
            {
                foreach (var kv in byVendor)
                {
                    foreach (var modelId in kv.Value)
                    {
                        var entry = ModelRegistry.Get(modelId);
                        var label = new GUIContent($"{kv.Key}/{modelId}");
                        string captured = modelId;
                        menu.AddItem(label, modelId == current, () =>
                        {
                            serializedObject.Update();
                            _specifyModel.stringValue = captured;
                            serializedObject.ApplyModifiedProperties();
                        });
                    }
                }
            }

            menu.DropDown(activatorRect);
        }

        // ─── 工具分组 ───

        private void DrawTools()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (UniAIToolRegistry.AllGroups.Count == 0)
            {
                EditorGUILayout.LabelField("工具分组", EditorStyles.boldLabel);
                GUILayout.Space(4);
                EditorGUILayout.HelpBox("未发现任何 [UniAITool] 工具。", MessageType.Info);
            }
            else
            {
                _toolGroupList.DoLayoutList();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawToolGroupElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = _toolGroups.GetArrayElementAtIndex(index);
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            string groupName = element.stringValue;
            bool isKnown = UniAIToolRegistry.AllGroups.Contains(groupName);
            int count = isKnown
                ? UniAIToolRegistry.GetHandlers(new[] { groupName }).Count
                : 0;

            string label = isKnown
                ? $"{groupName}  ({count})"
                : $"{groupName}  (未注册)";

            var oldColor = GUI.color;
            if (!isKnown) GUI.color = new Color(1f, 0.7f, 0.4f);
            EditorGUI.LabelField(rect, label);
            GUI.color = oldColor;
        }

        private void OnToolGroupAddDropdown(Rect buttonRect, ReorderableList list)
        {
            var menu = new GenericMenu();

            var enabled = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < _toolGroups.arraySize; i++)
                enabled.Add(_toolGroups.GetArrayElementAtIndex(i).stringValue);

            foreach (var group in UniAIToolRegistry.AllGroups)
            {
                int count = UniAIToolRegistry.GetHandlers(new[] { group }).Count;
                var content = new GUIContent($"{group}  ({count})");
                bool alreadyAdded = enabled.Contains(group);

                if (alreadyAdded)
                    menu.AddDisabledItem(content, true);
                else
                    menu.AddItem(content, false, () => AddToolGroup(group));
            }

            menu.DropDown(buttonRect);
        }

        private void AddToolGroup(string group)
        {
            serializedObject.Update();
            int idx = _toolGroups.arraySize;
            _toolGroups.InsertArrayElementAtIndex(idx);
            _toolGroups.GetArrayElementAtIndex(idx).stringValue = group;
            serializedObject.ApplyModifiedProperties();
        }

        // ─── MCP Servers ───

        private void DrawMcpServers()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _mcpServerList.DoLayoutList();
            EditorGUILayout.EndVertical();
        }

        private void DrawMcpServerElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = _mcpServers.GetArrayElementAtIndex(index);
            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.PropertyField(rect, element, GUIContent.none);
        }

        // ─── System Prompt ───

        private void DrawSystemPrompt()
        {
            var promptText = _systemPrompt.stringValue ?? "";
            var charCount = promptText.Length;

            // 标题栏：System Prompt + 字数统计
            var headerRect = EditorGUILayout.BeginHorizontal();
            if (headerRect.width > 1)
                EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, headerRect.width, EditorGUIUtility.singleLineHeight + 4), _promptBg);

            GUILayout.Label("System Prompt", _promptLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{charCount} chars", _charCountStyle);
            GUILayout.Space(4);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);

            // 文本编辑区 — 占满剩余空间
            var bgRect = EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            if (bgRect.width > 1)
                EditorGUI.DrawRect(bgRect, _promptBg);

            _systemPrompt.stringValue = EditorGUILayout.TextArea(
                _systemPrompt.stringValue, _promptTextStyle,
                GUILayout.ExpandHeight(true));

            EditorGUILayout.EndVertical();
        }

        // ─── Styles ───

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _promptLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                padding = new RectOffset(6, 0, 2, 2)
            };

            _promptTextStyle = new GUIStyle(EditorStyles.textArea)
            {
                padding = new RectOffset(6, 6, 6, 6),
                wordWrap = true
            };
            _promptTextStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            _charCountStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 4, 2, 2)
            };
            _charCountStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
        }
    }
}
