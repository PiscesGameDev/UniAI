using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// MCP 客户端管理器 — 聚合多个 McpClient 连接，提供统一的 Tool/Resource 访问接口
    /// </summary>
    public class McpClientManager : IDisposable
    {
        private readonly List<McpClient> _clients = new();
        private readonly Dictionary<string, McpClient> _toolToClient = new();

        /// <summary>
        /// 是否有任一已初始化的 Client
        /// </summary>
        public bool HasClients => _clients.Count > 0;

        /// <summary>
        /// 所有已连接的 Client
        /// </summary>
        public IReadOnlyList<McpClient> Clients => _clients;

        /// <summary>
        /// 连接所有启用的 MCP Server，拉取 Tools/Resources。单个 Server 失败不影响其他。
        /// </summary>
        public async UniTask ConnectAllAsync(IReadOnlyList<McpServerConfig> configs, CancellationToken ct = default)
        {
            if (configs == null) return;

            foreach (var config in configs)
            {
                if (config == null || !config.Enabled) continue;

                try
                {
                    var transport = config.CreateTransport();
                    var client = new McpClient(config.ServerName, transport);
                    await client.InitializeAsync(ct);

                    _clients.Add(client);

                    foreach (var tool in client.Tools)
                    {
                        if (string.IsNullOrEmpty(tool.Name)) continue;
                        if (_toolToClient.ContainsKey(tool.Name))
                        {
                            AILogger.Warning($"[MCP] Tool name conflict: '{tool.Name}' (server '{config.ServerName}' shadows previous)");
                        }
                        _toolToClient[tool.Name] = client;
                    }
                }
                catch (Exception e)
                {
                    AILogger.Error($"[MCP] Failed to connect '{config.ServerName}': {e.Message}");
                }
            }
        }

        /// <summary>
        /// 获取所有 MCP Server 提供的 Tools，转换为 AITool 定义
        /// </summary>
        public List<AITool> GetAllTools()
        {
            var list = new List<AITool>();
            foreach (var client in _clients)
            {
                foreach (var tool in client.Tools)
                {
                    list.Add(new AITool
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        ParametersSchema = tool.InputSchemaJson
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// 判断是否存在名为 toolName 的 MCP Tool
        /// </summary>
        public bool HasTool(string toolName) => !string.IsNullOrEmpty(toolName) && _toolToClient.ContainsKey(toolName);

        /// <summary>
        /// 调用 MCP Tool，返回文本结果
        /// </summary>
        public async UniTask<(string result, bool isError)> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
        {
            if (!_toolToClient.TryGetValue(toolName, out var client))
                return ($"Unknown MCP tool: {toolName}", true);

            try
            {
                var result = await client.CallToolAsync(toolName, argumentsJson, ct);
                return (FlattenContent(result.Content), result.IsError);
            }
            catch (Exception e)
            {
                return ($"MCP tool '{toolName}' failed: {e.Message}", true);
            }
        }

        /// <summary>
        /// 获取所有 MCP Resource 对应的 IContextProvider
        /// </summary>
        public List<IContextProvider> GetResourceProviders()
        {
            var providers = new List<IContextProvider>();
            foreach (var client in _clients)
            {
                foreach (var resource in client.Resources)
                    providers.Add(new McpResourceProvider(client, resource));
            }
            return providers;
        }

        private static string FlattenContent(List<McpContent> contents)
        {
            if (contents == null || contents.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var c in contents)
            {
                if (c == null) continue;
                if (c.Type == "text" && !string.IsNullOrEmpty(c.Text))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(c.Text);
                }
                else if (c.Type == "image" && !string.IsNullOrEmpty(c.MimeType))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[image: {c.MimeType}]");
                }
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            foreach (var client in _clients)
            {
                try { client.Dispose(); }
                catch (Exception e) { AILogger.Warning($"[MCP] Dispose client failed: {e.Message}"); }
            }
            _clients.Clear();
            _toolToClient.Clear();
        }
    }
}
