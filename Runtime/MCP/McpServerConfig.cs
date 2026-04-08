using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// MCP Server 传输类型
    /// </summary>
    public enum McpTransportType
    {
        /// <summary>本地子进程，通过 stdin/stdout 通信</summary>
        Stdio,
        /// <summary>远程 HTTP MCP Server（Streamable HTTP）</summary>
        Http
    }

    /// <summary>
    /// MCP Server 连接配置 — ScriptableObject，在 Agent 中绑定
    /// 创建: Create > UniAI > MCP Server Config
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/MCP Server Config", fileName = "NewMcpServer")]
    public class McpServerConfig : ScriptableObject
    {
        [Serializable]
        public class KeyValueEntry
        {
            public string Key;
            public string Value;
        }

        [SerializeField] private string _id;
        [SerializeField] private string _serverName = "New MCP Server";
        [SerializeField] private string _description;
        [SerializeField] private Texture2D _icon;
        [SerializeField] private McpTransportType _transportType = McpTransportType.Stdio;
        [SerializeField] private bool _enabled = true;

        [Header("Stdio")]
        [SerializeField] private string _command = "npx";
        [SerializeField, TextArea(1, 3)] private string _arguments;
        [SerializeField] private List<KeyValueEntry> _environmentVariables = new();

        [Header("HTTP")]
        [SerializeField] private string _baseUrl;
        [SerializeField] private List<KeyValueEntry> _headers = new();
        [SerializeField] private int _httpTimeoutSeconds = 60;

        /// <summary>唯一标识符（GUID）</summary>
        public string Id
        {
            get
            {
                if (!string.IsNullOrEmpty(_id)) return _id;
                _id = Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
                return _id;
            }
        }

        public string ServerName => _serverName;
        public string Description => _description;
        public Texture2D Icon => _icon;
        public McpTransportType TransportType => _transportType;
        public bool Enabled => _enabled;

        public string Command => _command;
        public string Arguments => _arguments;
        public IReadOnlyList<KeyValueEntry> EnvironmentVariables => _environmentVariables;

        public string BaseUrl => _baseUrl;
        public IReadOnlyList<KeyValueEntry> Headers => _headers;
        public int HttpTimeoutSeconds => _httpTimeoutSeconds;

        /// <summary>
        /// 根据配置创建传输层实例
        /// </summary>
        internal IMcpTransport CreateTransport()
        {
            switch (_transportType)
            {
                case McpTransportType.Stdio:
#if UNITY_EDITOR || UNITY_STANDALONE
                    return new StdioMcpTransport(_command, _arguments, ToDict(_environmentVariables));
#else
                    throw new PlatformNotSupportedException("Stdio MCP transport is only supported on Editor/Standalone platforms");
#endif
                case McpTransportType.Http:
                    return new HttpMcpTransport(_baseUrl, ToDict(_headers), _httpTimeoutSeconds);
                default:
                    throw new NotSupportedException($"Unknown MCP transport type: {_transportType}");
            }
        }

        private static Dictionary<string, string> ToDict(IReadOnlyList<KeyValueEntry> entries)
        {
            if (entries == null || entries.Count == 0) return null;
            var dict = new Dictionary<string, string>(entries.Count);
            foreach (var e in entries)
            {
                if (!string.IsNullOrEmpty(e?.Key))
                    dict[e.Key] = e.Value ?? string.Empty;
            }
            return dict;
        }
    }
}
