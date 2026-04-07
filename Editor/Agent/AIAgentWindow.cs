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
        private const float Pad = 10f;
        private const string DefaultAgentDir = "Assets/UniAI/Agents";

        // State
        private List<AgentDefinition> _agents;
        private int _selectedIndex;
        private Vector2 _rightScroll;
        private SerializedObject _serializedAgent;
        private UnityEditor.Editor _cachedEditor;

        // Styles
        private GUIStyle _titleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _agentLabelStyle;
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

        private void OnDisable()
        {
            if (_cachedEditor != null) DestroyImmediate(_cachedEditor);
        }

        private void RefreshAgentList()
        {
            _agents = AgentManager.GetAllAgents();
            if (_selectedIndex >= _agents.Count)
                _selectedIndex = Mathf.Max(0, _agents.Count - 1);
            _serializedAgent = null;
            if (_cachedEditor != null) DestroyImmediate(_cachedEditor);
            _cachedEditor = null;
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
            GUILayout.Label(displayName, _agentLabelStyle, GUILayout.Height(32));

            GUILayout.FlexibleSpace();
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            // 点击选中
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowAgentContextMenu(index);
                else
                {
                    _selectedIndex = index;
                    _serializedAgent = null;
                    if (_cachedEditor != null) DestroyImmediate(_cachedEditor);
                    _cachedEditor = null;
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

            DrawAgentEditor(agent);
        }

        private void DrawAgentEditor(AgentDefinition agent)
        {
            // Header: 标题 + 开始对话按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(Pad);
            string headerLabel = $"{agent.AgentName ?? agent.name} 配置";
            GUILayout.Label(headerLabel, _sectionTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("开启对话", GUILayout.Height(24), GUILayout.Width(100)))
                OpenChatWithAgent(agent);
            GUILayout.Space(Pad);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // 使用 CustomEditor 绘制
            if (_serializedAgent == null || _serializedAgent.targetObject != agent)
            {
                _serializedAgent = new SerializedObject(agent);
                if (_cachedEditor != null) DestroyImmediate(_cachedEditor);
                _cachedEditor = UnityEditor.Editor.CreateEditor(agent);
            }

            if (_cachedEditor != null)
                _cachedEditor.OnInspectorGUI();
        }

        // ────────────────────────────── Create / Delete ──────────────────────────────

        private void CreateNewAgent()
        {
            var agent = AgentManager.CreateNewAgent(DefaultAgentDir, "New Agent");
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
            string agentName = agent.AgentName ?? agent.name;
            if (!EditorUtility.DisplayDialog(
                    "删除 Agent",
                    $"确定要删除 Agent「{agentName}」吗？\n此操作不可撤销。",
                    "删除", "取消"))
                return;

            AgentManager.DeleteAgent(agent);

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

            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
        }
    }
}
