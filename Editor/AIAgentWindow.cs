using System.Collections.Generic;
using System.IO;
using UniAI.Editor.Chat;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Agent 管理窗口 — 双面板布局，左侧 Agent 列表，右侧配置详情
    /// </summary>
    public class AIAgentWindow : EditorWindow
    {
        private const float LeftPanelWidth = 200f;
        private const float LabelWidth = 100f;
        private const float Pad = 10f;
        private const float SystemPromptHeight = 400f;
        private const string DefaultAgentDir = "Assets/UniAI/Agents";

        // Colors
        private static readonly Color _builtinColor = new(0.55f, 0.55f, 0.55f);

        // State
        private List<AgentDefinition> _agents;
        private int _selectedIndex;
        private Vector2 _rightScroll;
        private SerializedObject _serializedAgent;

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _agentLabelStyle;
        private GUIStyle _builtinLabelStyle;
        private GUIStyle _addBtnStyle;
        private bool _stylesReady;

        [MenuItem("Window/UniAI/Agents")]
        [MenuItem("Tools/UniAI/Agents")]
        public static void Open()
        {
            var w = GetWindow<AIAgentWindow>("Agents");
            w.minSize = new Vector2(640, 400);
        }

        private void OnEnable()
        {
            RefreshAgentList();
        }

        private void RefreshAgentList()
        {
            _agents = AgentManager.GetAllAgents();
            if (_selectedIndex >= _agents.Count)
                _selectedIndex = Mathf.Max(0, _agents.Count - 1);
            _serializedAgent = null; // 强制重建
        }

        private bool IsDefaultAgent(AgentDefinition agent)
        {
            return agent == AgentManager.DefaultAgent;
        }

        private AgentDefinition SelectedAgent =>
            _agents != null && _agents.Count > 0 && _selectedIndex < _agents.Count
                ? _agents[_selectedIndex]
                : null;

        // ────────────────────────────── OnGUI ──────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();

            // Left panel bg + separator
            EditorGUI.DrawRect(new Rect(0, 0, LeftPanelWidth, position.height), EditorGUIHelper.LeftPanelBg);
            EditorGUI.DrawRect(new Rect(LeftPanelWidth, 0, 1, position.height), EditorGUIHelper.SeparatorColor);

            // Left panel
            GUILayout.BeginArea(new Rect(0, 0, LeftPanelWidth, position.height));
            DrawLeftPanel();
            GUILayout.EndArea();

            // Right panel
            float rx = LeftPanelWidth + 1;
            GUILayout.BeginArea(new Rect(rx, 0, position.width - rx, position.height));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawRightPanel();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ────────────────────────────── Left Panel ──────────────────────────────

        private void DrawLeftPanel()
        {
            GUILayout.Space(Pad);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label("UniAI Agents", _titleStyle);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            if (GUILayout.Button("+ 新建 Agent", _addBtnStyle, GUILayout.Height(26)))
                CreateNewAgent();
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            if (_agents == null) return;

            for (int i = 0; i < _agents.Count; i++)
            {
                DrawAgentItem(i);
                GUILayout.Space(2);
            }

            GUILayout.FlexibleSpace();
        }

        private void DrawAgentItem(int index)
        {
            var agent = _agents[index];
            bool isSelected = _selectedIndex == index;
            bool isDefault = IsDefaultAgent(agent);
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(32));

            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isSelected ? EditorGUIHelper.ItemSelectedBg : EditorGUIHelper.ItemBg);

            GUILayout.Space(Pad);

            // 图标
            if (agent.Icon != null)
            {
                var iconRect = GUILayoutUtility.GetRect(20, 32, GUILayout.Width(20), GUILayout.Height(32));
                float y = iconRect.y + (iconRect.height - 20) * 0.5f;
                GUI.DrawTexture(new Rect(iconRect.x, y, 20, 20), agent.Icon, ScaleMode.ScaleToFit);
                GUILayout.Space(4);
            }

            // Agent 名称
            string displayName = agent.AgentName ?? agent.name;
            if (isDefault)
                displayName += " (内置)";

            var style = isDefault ? _builtinLabelStyle : _agentLabelStyle;
            GUILayout.Label(displayName, style, GUILayout.Height(32));

            GUILayout.FlexibleSpace();
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            // 点击选中
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1 && !isDefault)
                    ShowAgentContextMenu(index);
                else
                {
                    _selectedIndex = index;
                    _serializedAgent = null; // 切换选中时重建
                }

                Event.current.Use();
                Repaint();
            }
        }

        // ────────────────────────────── Right Panel ──────────────────────────────

        private void DrawRightPanel()
        {
            GUILayout.Space(Pad);

            var agent = SelectedAgent;
            if (agent == null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(Pad);
                GUILayout.Label("点击左侧列表选择 Agent，或点击「+ 新建 Agent」创建新的。");
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (IsDefaultAgent(agent))
            {
                DrawDefaultAgentInfo(agent);
                return;
            }

            DrawAgentEditor(agent);
        }

        private void DrawDefaultAgentInfo(AgentDefinition agent)
        {
            DrawAgentDetail(agent, true);
        }

        private void DrawAgentEditor(AgentDefinition agent)
        {
            DrawAgentDetail(agent, false);
        }

        private void DrawAgentDetail(AgentDefinition agent, bool isReadonly)
        {
            // 确保 SerializedObject
            if (_serializedAgent == null || _serializedAgent.targetObject != agent)
                _serializedAgent = new SerializedObject(agent);

            _serializedAgent.Update();

            // Header
            string headerLabel = isReadonly ? "默认助手 (内置)" : $"{agent.AgentName ?? agent.name} 配置";
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            GUILayout.Label(headerLabel, _sectionTitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUI.BeginDisabledGroup(isReadonly);

            // 基本信息 Section
            DrawSection(() =>
            {
                GUILayout.Label("基本信息", _sectionTitleStyle);
                GUILayout.Space(6);

                DrawPropertyField("名称", "_agentName");
                DrawPropertyField("图标", "_icon");

                GUILayout.Space(4);
                GUILayout.Label("System Prompt:", EditorStyles.boldLabel);
                var promptProp = _serializedAgent.FindProperty("_systemPrompt");
                EditorGUILayout.PropertyField(promptProp, GUIContent.none, GUILayout.MinHeight(isReadonly ? 60 : SystemPromptHeight));
            });

            GUILayout.Space(8);

            // 参数 Section
            DrawSection(() =>
            {
                GUILayout.Label("参数设置", _sectionTitleStyle);
                GUILayout.Space(6);

                var tempProp = _serializedAgent.FindProperty("_temperature");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Temperature", GUILayout.Width(LabelWidth));
                tempProp.floatValue = EditorGUILayout.Slider(tempProp.floatValue, 0f, 1f);
                EditorGUILayout.EndHorizontal();

                var maxTokensProp = _serializedAgent.FindProperty("_maxTokens");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Max Tokens", GUILayout.Width(LabelWidth));
                maxTokensProp.intValue = EditorGUILayout.IntField(maxTokensProp.intValue);
                EditorGUILayout.EndHorizontal();

                var maxTurnsProp = _serializedAgent.FindProperty("_maxTurns");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Max Turns", GUILayout.Width(LabelWidth));
                maxTurnsProp.intValue = EditorGUILayout.IntSlider(maxTurnsProp.intValue, 1, 50);
                EditorGUILayout.EndHorizontal();
            });

            GUILayout.Space(8);

            // 工具列表 Section
            DrawSection(() =>
            {
                GUILayout.Label("工具列表", _sectionTitleStyle);
                GUILayout.Space(6);

                var toolsProp = _serializedAgent.FindProperty("_tools");
                EditorGUILayout.PropertyField(toolsProp, new GUIContent("Tools"), true);
            });

            EditorGUI.EndDisabledGroup();

            _serializedAgent.ApplyModifiedProperties();

            if (isReadonly)
            {
                GUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(Pad);
                EditorGUILayout.HelpBox("内置默认 Agent 不可编辑。它是无 Tool 的通用聊天助手，所有对话默认使用。", MessageType.Info);
                GUILayout.Space(Pad);
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(12);

            // 底部操作
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("开启对话", GUILayout.Height(28), GUILayout.Width(120)))
                OpenChatWithAgent(agent);

            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(Pad);
        }

        // ────────────────────────────── Helpers ──────────────────────────────

        private void DrawSection(System.Action drawContent)
        {
            EditorGUIHelper.DrawSection(Pad, drawContent);
        }

        private void DrawPropertyField(string label, string propertyName)
        {
            var prop = _serializedAgent.FindProperty(propertyName);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(LabelWidth));
            EditorGUILayout.PropertyField(prop, GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        // ────────────────────────────── Create / Delete ──────────────────────────────

        private void CreateNewAgent()
        {
            // 确保目录存在
            if (!AssetDatabase.IsValidFolder(DefaultAgentDir))
            {
                string parent = Path.GetDirectoryName(DefaultAgentDir)?.Replace('\\', '/');
                string folder = Path.GetFileName(DefaultAgentDir);
                if (!string.IsNullOrEmpty(parent))
                    AssetDatabase.CreateFolder(parent, folder);
            }

            // 生成不重复的文件名
            string baseName = "NewAgent";
            string path = $"{DefaultAgentDir}/{baseName}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var agent = CreateInstance<AgentDefinition>();
            AssetDatabase.CreateAsset(agent, path);
            AssetDatabase.SaveAssets();

            RefreshAgentList();

            // 选中新创建的 Agent
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i] == agent)
                {
                    _selectedIndex = i;
                    _serializedAgent = null;
                    break;
                }
            }

            Repaint();
        }

        private void DeleteAgent(AgentDefinition agent)
        {
            if (IsDefaultAgent(agent)) return;

            string agentName = agent.AgentName ?? agent.name;
            if (!EditorUtility.DisplayDialog(
                    "删除 Agent",
                    $"确定要删除 Agent「{agentName}」吗？\n此操作不可撤销。",
                    "删除", "取消"))
                return;

            string path = AssetDatabase.GetAssetPath(agent);
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
            }

            RefreshAgentList();
            _serializedAgent = null;
            Repaint();
        }

        private void OpenChatWithAgent(AgentDefinition agent)
        {
            AIChatWindow.OpenWithAgent(agent);
        }

        private void ShowAgentContextMenu(int index)
        {
            var agent = _agents[index];
            if (IsDefaultAgent(agent)) return;

            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
            {
                EditorGUIUtility.PingObject(agent);
                Selection.activeObject = agent;
            });

            menu.AddSeparator("");

            string agentName = agent.AgentName ?? agent.name;
            menu.AddItem(new GUIContent($"删除「{agentName}」"), false, () => DeleteAgent(agent));

            menu.ShowAsContext();
        }

        // ────────────────────────────── Styles ──────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _agentLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };

            _builtinLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _builtinLabelStyle.normal.textColor = _builtinColor;

            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
