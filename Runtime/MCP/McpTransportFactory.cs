using System;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// MCP 传输层工厂 — 将 McpServerConfig 与具体 Transport 实现解耦
    /// </summary>
    internal static class McpTransportFactory
    {
        public static IMcpTransport Create(McpServerConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            switch (config.TransportType)
            {
                case McpTransportType.Stdio:
#if UNITY_EDITOR || UNITY_STANDALONE
                    return new StdioMcpTransport(config.Command, config.Arguments, ToDict(config.EnvironmentVariables));
#else
                    throw new PlatformNotSupportedException("Stdio MCP transport is only supported on Editor/Standalone platforms");
#endif
                case McpTransportType.Http:
                    return new HttpMcpTransport(config.BaseUrl, ToDict(config.Headers), GetHttpSocketTimeout());
                default:
                    throw new NotSupportedException($"Unknown MCP transport type: {config.TransportType}");
            }
        }

        private static Dictionary<string, string> ToDict(IReadOnlyList<McpServerConfig.KeyValueEntry> entries)
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

        /// <summary>
        /// HTTP 传输层的 socket 级兜底超时 — 取 UniAI 全局 HTTP 超时，
        /// 真正的连接/调用超时由 McpRuntimeConfig.InitTimeoutSeconds / ToolCallTimeoutSeconds 控制
        /// </summary>
        private static int GetHttpSocketTimeout()
        {
            return UniAISettings.Instance?.General?.TimeoutSeconds ?? 60;
        }
    }
}
