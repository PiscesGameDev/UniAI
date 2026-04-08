using System.Collections.Generic;
using UniAI.Editor.Chat;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Agent 管理 Tab — 双面板布局，左侧 Agent 列表，右侧配置详情
    /// </summary>
    internal class AgentTab : ManagerTab
    {
        public override string TabName => "Agent";
        public override string TabIcon => "♟";
        public override int Order => 1;

        private const float LEFT_PANEL_WIDTH = 200f;
        private const float PAD = 10f;

        // State
        private List<AgentDefinition> _agents;
        private int _selectedIndex;
        private Vector2 _rightScroll;
        private SerializedObject _serializedAgent;
        private UnityEditor.Editor _cachedEditor;

        // Styles
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _agentLabelStyle;
        private GUIStyle _addBtnStyle;
        private bool _stylesReady;

        private AgentDefinition SelectedAgent =>
            _agents is { Count: > 0 } && _selectedIndex < _agents.Count
                ? _agents[_selectedIndex]
                : null;

        protected override void OnInit()
        {
            RefreshAgentList();
        }

        public override void OnDestroy()
        {
            if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
        }

        public override void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            _agentLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
        }

        public override void OnGUI(float width, float height)
        {
            // Left panel bg + separator
            EditorGUI.DrawRect(new Rect(0, 0, LEFT_PANEL_WIDTH, height), EditorGUIHelper.LeftPanelBg);
            EditorGUI.DrawRect(new Rect(LEFT_PANEL_WIDTH, 0, 1, height), EditorGUIHelper.SeparatorColor);

            GUILayout.BeginArea(new Rect(0, 0, LEFT_PANEL_WIDTH, height));
            DrawLeftPanel();
            GUILayout.EndArea();

            float rx = LEFT_PANEL_WIDTH + 1;
            GUILayout.BeginArea(new Rect(rx, 0, width - rx, height));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawRightPanel();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ────────────────────────────── Left Panel ──────────────────────────────

        private void DrawLeftPanel()
        {
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            if (GUILayout.Button("+ 新建 Agent", _addBtnStyle, GUILayout.Height(26)))
                CreateNewAgent();
            GUILayout.Space(PAD);
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

            GUILayout.Space(PAD);

            if (agent.Icon != null)
            {
                var iconRect = GUILayoutUtility.GetRect(20, 32, GUILayout.Width(20), GUILayout.Height(32));
                float y = iconRect.y + (iconRect.height - 20) * 0.5f;
                GUI.DrawTexture(new Rect(iconRect.x, y, 20, 20), agent.Icon, ScaleMode.ScaleToFit);
                GUILayout.Space(4);
            }

            string displayName = agent.AgentName ?? agent.name;
            GUILayout.Label(displayName, _agentLabelStyle, GUILayout.Height(32));

            GUILayout.FlexibleSpace();
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowAgentContextMenu(index);
                else
                {
                    _selectedIndex = index;
                    _serializedAgent = null;
                    if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
                    _cachedEditor = null;
                }

                Event.current.Use();
                Window.Repaint();
            }
        }

        // ────────────────────────────── Right Panel ──────────────────────────────

        private void DrawRightPanel()
        {
            GUILayout.Space(PAD);

            var agent = SelectedAgent;
            if (agent == null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(PAD);
                GUILayout.Label("点击左侧列表选择 Agent，或点击「+ 新建 Agent」创建新的。");
                EditorGUILayout.EndHorizontal();
                return;
            }

            DrawAgentEditor(agent);
        }

        private void DrawAgentEditor(AgentDefinition agent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            string headerLabel = $"{agent.AgentName ?? agent.name} 配置";
            GUILayout.Label(headerLabel, _sectionTitleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("开启对话", GUILayout.Height(24), GUILayout.Width(100)))
                OpenChatWithAgent(agent);
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            if (_serializedAgent == null || _serializedAgent.targetObject != agent)
            {
                _serializedAgent = new SerializedObject(agent);
                if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
                _cachedEditor = UnityEditor.Editor.CreateEditor(agent);
            }

            if (_cachedEditor != null)
                _cachedEditor.OnInspectorGUI();
        }

        // ────────────────────────────── Create / Delete ──────────────────────────────

        private void CreateNewAgent()
        {
            string agentDir = AIConfigManager.Prefs.AgentDirectory;
            var agent = AgentManager.CreateNewAgent(agentDir, "New Agent");
            RefreshAgentList();

            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i] == agent)
                {
                    _selectedIndex = i;
                    _serializedAgent = null;
                    break;
                }
            }

            Window.Repaint();
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
            Window.Repaint();
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

        private void RefreshAgentList()
        {
            _agents = AgentManager.GetAllAgents();
            if (_selectedIndex >= _agents.Count)
                _selectedIndex = Mathf.Max(0, _agents.Count - 1);
            _serializedAgent = null;
            if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
            _cachedEditor = null;
        }
    }
}
