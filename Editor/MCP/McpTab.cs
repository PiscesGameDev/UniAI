using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// MCP Server 管理 Tab — 双面板布局：左侧列出项目内所有 McpServerConfig，右侧编辑详情
    /// </summary>
    internal class McpTab : ManagerTab
    {
        public override string TabName => "MCP";
        public override string TabIcon => "⇄";
        public override int Order => 2;

        private const float LEFT_PANEL_WIDTH = 220f;
        private const float PAD = 10f;

        private List<McpServerConfig> _servers;
        private int _selectedIndex;
        private Vector2 _rightScroll;
        private UnityEditor.Editor _cachedEditor;

        private GUIStyle _sectionTitleStyle;
        private GUIStyle _itemLabelStyle;
        private GUIStyle _itemSubLabelStyle;
        private GUIStyle _addBtnStyle;
        private bool _stylesReady;

        private McpServerConfig SelectedServer =>
            _servers != null && _servers.Count > 0 && _selectedIndex < _servers.Count
                ? _servers[_selectedIndex]
                : null;

        protected override void OnInit()
        {
            RefreshList();
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
            _itemLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _itemSubLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            _addBtnStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
        }

        public override void OnGUI(float width, float height)
        {
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

        // ─── Left Panel ───

        private void DrawLeftPanel()
        {
            GUILayout.Space(PAD);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            if (GUILayout.Button("+ 新建 MCP Server", _addBtnStyle, GUILayout.Height(26)))
                CreateNewServer();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            if (GUILayout.Button("刷新列表", EditorStyles.miniButton, GUILayout.Height(20)))
                RefreshList();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            if (_servers == null || _servers.Count == 0)
            {
                GUILayout.Space(20);
                EditorGUILayout.LabelField("项目中无 MCP Server 配置", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (int i = 0; i < _servers.Count; i++)
            {
                DrawServerItem(i);
                GUILayout.Space(2);
            }
        }

        private void DrawServerItem(int index)
        {
            var server = _servers[index];
            bool isSelected = _selectedIndex == index;
            var rect = EditorGUILayout.BeginVertical(GUILayout.Height(40));

            if (rect.width > 1)
                EditorGUI.DrawRect(rect, isSelected ? EditorGUIHelper.ItemSelectedBg : EditorGUIHelper.ItemBg);

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);

            // 启用状态点
            var dotRect = GUILayoutUtility.GetRect(8, 8, GUILayout.Width(8), GUILayout.Height(8));
            dotRect.y += 4;
            EditorGUI.DrawRect(dotRect, server.Enabled ? new Color(0.4f, 0.85f, 0.4f) : new Color(0.5f, 0.5f, 0.5f));
            GUILayout.Space(6);

            string displayName = string.IsNullOrEmpty(server.ServerName) ? server.name : server.ServerName;
            GUILayout.Label(displayName, _itemLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(server.TransportType.ToString(), _itemSubLabelStyle);
            GUILayout.Space(6);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1)
                    ShowContextMenu(index);
                else
                {
                    _selectedIndex = index;
                    if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
                    _cachedEditor = null;
                }

                Event.current.Use();
                Window.Repaint();
            }
        }

        // ─── Right Panel ───

        private void DrawRightPanel()
        {
            GUILayout.Space(PAD);

            var server = SelectedServer;
            if (server == null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(PAD);
                GUILayout.Label("点击左侧选择 MCP Server，或点击「+ 新建」创建新的。");
                EditorGUILayout.EndHorizontal();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            string headerLabel = string.IsNullOrEmpty(server.ServerName) ? server.name : server.ServerName;
            GUILayout.Label($"{headerLabel} 配置", _sectionTitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(PAD);
            EditorGUILayout.BeginVertical();

            if (_cachedEditor == null || _cachedEditor.target != server)
            {
                if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
                _cachedEditor = UnityEditor.Editor.CreateEditor(server);
            }
            _cachedEditor.OnInspectorGUI();

            EditorGUILayout.EndVertical();
            GUILayout.Space(PAD);
            EditorGUILayout.EndHorizontal();
        }

        // ─── Create / Delete ───

        private void CreateNewServer()
        {
            string defaultDir = "Assets/UniAI/Resources/MCP";
            if (!Directory.Exists(defaultDir))
                Directory.CreateDirectory(defaultDir);

            var asset = ScriptableObject.CreateInstance<McpServerConfig>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{defaultDir}/NewMcpServer.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            RefreshList();
            for (int i = 0; i < _servers.Count; i++)
            {
                if (_servers[i] == asset)
                {
                    _selectedIndex = i;
                    break;
                }
            }
            Window.Repaint();
        }

        private void DeleteServer(McpServerConfig server)
        {
            string serverName = string.IsNullOrEmpty(server.ServerName) ? server.name : server.ServerName;
            if (!EditorUtility.DisplayDialog(
                    "删除 MCP Server",
                    $"确定要删除「{serverName}」吗？\n此操作不可撤销。",
                    "删除", "取消"))
                return;

            string path = AssetDatabase.GetAssetPath(server);
            AssetDatabase.DeleteAsset(path);
            RefreshList();
            Window.Repaint();
        }

        private void ShowContextMenu(int index)
        {
            var server = _servers[index];
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("在 Project 中定位"), false, () =>
            {
                EditorGUIUtility.PingObject(server);
                Selection.activeObject = server;
            });

            menu.AddSeparator("");

            string serverName = string.IsNullOrEmpty(server.ServerName) ? server.name : server.ServerName;
            menu.AddItem(new GUIContent($"删除「{serverName}」"), false, () => DeleteServer(server));

            menu.ShowAsContext();
        }

        private void RefreshList()
        {
            _servers = new List<McpServerConfig>();
            var guids = AssetDatabase.FindAssets($"t:{nameof(McpServerConfig)}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var server = AssetDatabase.LoadAssetAtPath<McpServerConfig>(path);
                if (server != null) _servers.Add(server);
            }

            if (_selectedIndex >= _servers.Count)
                _selectedIndex = Mathf.Max(0, _servers.Count - 1);

            if (_cachedEditor != null) Object.DestroyImmediate(_cachedEditor);
            _cachedEditor = null;
        }
    }
}
