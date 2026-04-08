using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// McpServerConfig 自定义 Inspector — Icon+Name 头部 + 根据 TransportType 条件显示对应字段，
    /// 提供「测试连接」按钮验证配置可用性
    /// </summary>
    [CustomEditor(typeof(McpServerConfig))]
    internal class McpServerConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _id;
        private SerializedProperty _serverName;
        private SerializedProperty _description;
        private SerializedProperty _icon;
        private SerializedProperty _transportType;
        private SerializedProperty _enabled;
        private SerializedProperty _command;
        private SerializedProperty _arguments;
        private SerializedProperty _environmentVariables;
        private SerializedProperty _baseUrl;
        private SerializedProperty _headers;
        private SerializedProperty _httpTimeoutSeconds;

        private ReorderableList _envList;
        private ReorderableList _headerList;

        private string _testStatus;
        private bool _testRunning;
        private MessageType _testStatusType;

        private void OnEnable()
        {
            _id = serializedObject.FindProperty("_id");
            _serverName = serializedObject.FindProperty("_serverName");
            _description = serializedObject.FindProperty("_description");
            _icon = serializedObject.FindProperty("_icon");
            _transportType = serializedObject.FindProperty("_transportType");
            _enabled = serializedObject.FindProperty("_enabled");
            _command = serializedObject.FindProperty("_command");
            _arguments = serializedObject.FindProperty("_arguments");
            _environmentVariables = serializedObject.FindProperty("_environmentVariables");
            _baseUrl = serializedObject.FindProperty("_baseUrl");
            _headers = serializedObject.FindProperty("_headers");
            _httpTimeoutSeconds = serializedObject.FindProperty("_httpTimeoutSeconds");

            _envList = BuildKeyValueList(_environmentVariables, "环境变量");
            _headerList = BuildKeyValueList(_headers, "请求头");

            // 确保 Id 已生成
            var config = (McpServerConfig)target;
            _ = config.Id;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBasicInfo();
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(_transportType, new GUIContent("传输类型"));
            EditorGUILayout.PropertyField(_enabled, new GUIContent("启用"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            var transport = (McpTransportType)_transportType.enumValueIndex;
            switch (transport)
            {
                case McpTransportType.Stdio: DrawStdio(); break;
                case McpTransportType.Http: DrawHttp(); break;
            }

            EditorGUILayout.Space(8);
            DrawTestSection();

            serializedObject.ApplyModifiedProperties();
        }

        // ─── 基本信息（Icon + Name 布局，与 AgentDefinitionEditor 一致） ───

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

            // 右侧：Id + 名称 + 描述
            EditorGUILayout.BeginVertical();
            var oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 60;
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(_id, new GUIContent("Id"));
            EditorGUI.EndDisabledGroup();
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(_serverName, new GUIContent("名称"));
            GUILayout.Space(2);
            EditorGUILayout.PropertyField(_description, new GUIContent("描述"));
            EditorGUIUtility.labelWidth = oldLabelWidth;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawStdio()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Stdio 子进程", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUILayout.PropertyField(_command, new GUIContent("Command", "可执行文件名，例如 npx / python / node"));
            EditorGUILayout.PropertyField(_arguments, new GUIContent("Arguments", "命令行参数"));

            GUILayout.Space(4);
            _envList.DoLayoutList();

#if !(UNITY_EDITOR || UNITY_STANDALONE)
            EditorGUILayout.HelpBox("Stdio 传输仅在 Editor / Standalone 平台可用", MessageType.Warning);
#endif

            EditorGUILayout.EndVertical();
        }

        private void DrawHttp()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Streamable HTTP", EditorStyles.boldLabel);
            GUILayout.Space(4);

            EditorGUILayout.PropertyField(_baseUrl, new GUIContent("Base URL", "MCP Server JSON-RPC 端点"));
            EditorGUILayout.PropertyField(_httpTimeoutSeconds, new GUIContent("超时 (秒)"));

            GUILayout.Space(4);
            _headerList.DoLayoutList();
            EditorGUILayout.EndVertical();
        }

        private void DrawTestSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("连接测试", EditorStyles.boldLabel);
            GUILayout.Space(4);

            using (new EditorGUI.DisabledScope(_testRunning))
            {
                if (GUILayout.Button(_testRunning ? "测试中..." : "测试连接", GUILayout.Height(24)))
                    TestConnection();
            }

            if (!string.IsNullOrEmpty(_testStatus))
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(_testStatus, _testStatusType);
            }

            EditorGUILayout.EndVertical();
        }

        private void TestConnection()
        {
            _testRunning = true;
            _testStatus = null;
            var config = (McpServerConfig)target;
            RunTestAsync(config).Forget();
        }

        private async UniTaskVoid RunTestAsync(McpServerConfig config)
        {
            McpClient client = null;
            try
            {
                int timeout = AIConfigManager.LoadConfig()?.General?.Mcp?.InitTimeoutSeconds ?? 30;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
                var transport = config.CreateTransport();
                client = new McpClient(config.Id, config.ServerName, transport);
                await client.InitializeAsync(cts.Token);

                _testStatus = $"连接成功 — Server: {client.ServerInfo?.Name ?? "(unknown)"} v{client.ServerInfo?.Version}\n" +
                              $"Tools: {client.Tools.Count}, Resources: {client.Resources.Count}";
                _testStatusType = MessageType.Info;
            }
            catch (Exception e)
            {
                _testStatus = $"连接失败: {e.Message}";
                _testStatusType = MessageType.Error;
            }
            finally
            {
                client?.Dispose();
                _testRunning = false;
                Repaint();
            }
        }

        // ─── KeyValueEntry ReorderableList ───

        private static ReorderableList BuildKeyValueList(SerializedProperty prop, string title)
        {
            return new ReorderableList(prop.serializedObject, prop, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, title),
                drawElementCallback = (rect, index, _, _) =>
                {
                    var element = prop.GetArrayElementAtIndex(index);
                    var key = element.FindPropertyRelative("Key");
                    var value = element.FindPropertyRelative("Value");

                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;

                    float keyWidth = rect.width * 0.35f;
                    float gap = 4f;
                    var keyRect = new Rect(rect.x, rect.y, keyWidth, rect.height);
                    var valueRect = new Rect(rect.x + keyWidth + gap, rect.y, rect.width - keyWidth - gap, rect.height);

                    EditorGUI.PropertyField(keyRect, key, GUIContent.none);
                    EditorGUI.PropertyField(valueRect, value, GUIContent.none);
                },
                drawNoneElementCallback = rect => EditorGUI.LabelField(rect, "（空）", EditorStyles.centeredGreyMiniLabel),
                elementHeight = EditorGUIUtility.singleLineHeight + 4
            };
        }
    }
}
