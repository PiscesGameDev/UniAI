using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// MCP 客户端管理器 — 聚合多个 McpClient 连接，提供统一的 Tool/Resource 访问接口
    ///
    /// 线程模型：实例方法不是线程安全的，调用方需在单一主线程（如 Unity Main Thread / UniTask 调度上下文）
    /// 内串行调用 ConnectAllAsync / CallToolAsync / GetAllTools / Dispose 等。内部集合无锁保护。
    /// </summary>
    public class McpClientManager : IDisposable
    {
        private readonly List<McpClient> _clients = new();
        private readonly Dictionary<string, McpClient> _toolToClient = new();
        private readonly Dictionary<string, McpClient> _serverIdToClient = new();

        /// <summary>
        /// 单次 MCP Tool 调用超时（秒），0 = 不限制
        /// </summary>
        public int ToolCallTimeoutSeconds { get; set; }

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
        /// <param name="configs">Server 配置列表</param>
        /// <param name="initTimeoutSeconds">每个 Server 的初始化超时（秒），0 = 不限制</param>
        /// <param name="ct">外部取消令牌</param>
        public async UniTask ConnectAllAsync(IReadOnlyList<McpServerConfig> configs, int initTimeoutSeconds = 0, CancellationToken ct = default)
        {
            if (configs == null) return;

            foreach (var config in configs)
            {
                if (config == null || !config.Enabled) continue;

                try
                {
                    var client = await ConnectOneAsync(config, initTimeoutSeconds, ct);
                    AddClient(config, client);
                }
                catch (Exception e)
                {
                    AILogger.Error($"[MCP] Failed to connect '{config.ServerName}': {e.Message}");
                }
            }
        }

        /// <summary>
        /// 测试单个 Server 配置是否可用。不会加入管理器，调用后即释放连接。
        /// 供编辑器「测试连接」按钮使用。
        /// </summary>
        public static async UniTask<TestConnectionResult> TestConnectionAsync(
            McpServerConfig config,
            int initTimeoutSeconds,
            CancellationToken ct = default)
        {
            if (config == null)
                return TestConnectionResult.Fail("config is null");

            McpClient client = null;
            try
            {
                client = await ConnectOneAsync(config, initTimeoutSeconds, ct);
                return new TestConnectionResult
                {
                    Success = true,
                    ServerInfo = client.ServerInfo,
                    ToolCount = client.Tools.Count,
                    ResourceCount = client.Resources.Count
                };
            }
            catch (Exception e)
            {
                return TestConnectionResult.Fail(e.Message);
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// 生成所有已连接 Server 的状态概要（供 UI 展示）
        /// </summary>
        public string GetConnectionSummary()
        {
            if (_clients.Count == 0) return "未连接任何 MCP Server";

            int toolTotal = 0;
            int resTotal = 0;
            foreach (var c in _clients)
            {
                toolTotal += c.Tools.Count;
                resTotal += c.Resources.Count;
            }
            return $"已连接 {_clients.Count} 个 MCP Server — Tools: {toolTotal}, Resources: {resTotal}";
        }

        private static async UniTask<McpClient> ConnectOneAsync(McpServerConfig config, int initTimeoutSeconds, CancellationToken ct)
        {
            var transport = McpTransportFactory.Create(config);
            var client = new McpClient(config.Id, config.ServerName, transport);

            await TimeoutHelper.WithTimeout(token => client.InitializeAsync(token), initTimeoutSeconds, ct);

            return client;
        }

        private void AddClient(McpServerConfig config, McpClient client)
        {
            _clients.Add(client);
            _serverIdToClient[config.Id] = client;

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
        /// 通过 McpServerConfig.Id 查找已连接的 Client（未连接返回 null）
        /// </summary>
        public McpClient GetClientByServerId(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            _serverIdToClient.TryGetValue(serverId, out var client);
            return client;
        }

        /// <summary>
        /// 调用 MCP Tool，返回文本结果
        /// </summary>
        public async UniTask<(string result, bool isError)> CallToolAsync(string toolName, string argumentsJson, CancellationToken ct = default)
        {
            if (!_toolToClient.TryGetValue(toolName, out var client))
                return ($"Unknown MCP tool: {toolName}", true);

            try
            {
                var result = await TimeoutHelper.WithTimeout(
                    token => client.CallToolAsync(toolName, argumentsJson, token),
                    ToolCallTimeoutSeconds, ct);
                return (FlattenContent(result.Content), result.IsError);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ($"MCP tool '{toolName}' timed out after {ToolCallTimeoutSeconds}s", true);
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
                if (c.Type == McpContentTypes.Text && !string.IsNullOrEmpty(c.Text))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(c.Text);
                }
                else if (c.Type == McpContentTypes.Image && !string.IsNullOrEmpty(c.MimeType))
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
            _serverIdToClient.Clear();
        }
    }

    /// <summary>
    /// MCP Server 连接测试结果
    /// </summary>
    public class TestConnectionResult
    {
        public bool Success;
        public string Error;
        public McpServerInfo ServerInfo;
        public int ToolCount;
        public int ResourceCount;

        public static TestConnectionResult Fail(string error) =>
            new() { Success = false, Error = error };
    }
}
