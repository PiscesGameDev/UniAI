using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    /// <summary>
    /// 单个 MCP Server 连接客户端
    /// 协议流程: connect → initialize → notifications/initialized → tools/list / resources/list
    /// </summary>
    public class McpClient : IDisposable
    {
        private const string ProtocolVersion = "2024-11-05";

        private readonly IMcpTransport _transport;
        private readonly string _serverId;
        private readonly string _serverName;
        private readonly List<McpToolDefinition> _tools = new();
        private readonly List<McpResourceDefinition> _resources = new();

        /// <summary>McpServerConfig.Id，稳定唯一标识</summary>
        public string ServerId => _serverId;
        public string ServerName => _serverName;
        public McpServerInfo ServerInfo { get; private set; }
        public IReadOnlyList<McpToolDefinition> Tools => _tools;
        public IReadOnlyList<McpResourceDefinition> Resources => _resources;
        public bool IsInitialized { get; private set; }

        internal McpClient(string serverId, string serverName, IMcpTransport transport)
        {
            _serverId = serverId;
            _serverName = serverName;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>
        /// 初始化连接：建立传输 → MCP initialize 握手 → 拉取 tools/resources 列表
        /// 失败时会自动释放底层 transport，避免资源泄漏
        /// </summary>
        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            try
            {
                await _transport.ConnectAsync(ct);

                // 1. initialize
                var initResult = await SendAsync(McpMethods.Initialize, new JObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JObject
                    {
                        ["tools"] = new JObject(),
                        ["resources"] = new JObject()
                    },
                    ["clientInfo"] = new JObject
                    {
                        ["name"] = "UniAI",
                        ["version"] = "0.0.1"
                    }
                }, ct);

                ServerInfo = ParseServerInfo(initResult);

                // 2. initialized 通知
                await _transport.SendNotificationAsync(McpMethods.Initialized, new JObject(), ct);

                IsInitialized = true;

                // 3. 拉取 tools 和 resources（静默失败：Server 可能不支持）
                try { await LoadToolsAsync(ct); }
                catch (Exception e) { AILogger.Warning($"[MCP] {_serverName}: tools/list failed: {e.Message}"); }

                try { await LoadResourcesAsync(ct); }
                catch (Exception e) { AILogger.Warning($"[MCP] {_serverName}: resources/list failed: {e.Message}"); }

                AILogger.Info($"[MCP] {_serverName} initialized: {_tools.Count} tools, {_resources.Count} resources");
            }
            catch
            {
                // 握手或连接失败时释放底层 transport，避免子进程/HTTP session 泄漏
                try { _transport.Dispose(); }
                catch (Exception disposeEx) { AILogger.Warning($"[MCP] {_serverName}: dispose transport after init failure: {disposeEx.Message}"); }
                throw;
            }
        }

        /// <summary>
        /// 调用 MCP Tool
        /// </summary>
        public async UniTask<McpToolResult> CallToolAsync(string name, string argumentsJson, CancellationToken ct = default)
        {
            JToken args;
            if (string.IsNullOrWhiteSpace(argumentsJson))
                args = new JObject();
            else
            {
                try { args = JToken.Parse(argumentsJson); }
                catch { args = new JObject(); }
            }

            var result = await SendAsync(McpMethods.ToolsCall, new JObject
            {
                ["name"] = name,
                ["arguments"] = args
            }, ct);

            return ParseToolResult(result);
        }

        /// <summary>
        /// 读取 MCP Resource
        /// </summary>
        public async UniTask<McpResourceContent> ReadResourceAsync(string uri, CancellationToken ct = default)
        {
            var result = await SendAsync(McpMethods.ResourcesRead, new JObject { ["uri"] = uri }, ct);
            return ParseResourceContent(result, uri);
        }

        private async UniTask LoadToolsAsync(CancellationToken ct)
        {
            var result = await SendAsync(McpMethods.ToolsList, new JObject(), ct);
            _tools.Clear();
            if (result?["tools"] is JArray arr)
            {
                foreach (var item in arr)
                {
                    _tools.Add(new McpToolDefinition
                    {
                        Name = (string)item["name"],
                        Description = (string)item["description"],
                        InputSchemaJson = item["inputSchema"]?.ToString(Newtonsoft.Json.Formatting.None)
                    });
                }
            }
        }

        private async UniTask LoadResourcesAsync(CancellationToken ct)
        {
            var result = await SendAsync(McpMethods.ResourcesList, new JObject(), ct);
            _resources.Clear();
            if (result?["resources"] is JArray arr)
            {
                foreach (var item in arr)
                {
                    _resources.Add(new McpResourceDefinition
                    {
                        Uri = (string)item["uri"],
                        Name = (string)item["name"],
                        Description = (string)item["description"],
                        MimeType = (string)item["mimeType"]
                    });
                }
            }
        }

        private async UniTask<JToken> SendAsync(string method, object param, CancellationToken ct)
        {
            var request = new JsonRpcRequest { Method = method, Params = param };
            var response = await _transport.SendRequestAsync(request, ct);

            if (response?.Error != null)
                throw new InvalidOperationException($"MCP error {response.Error.Code}: {response.Error.Message}");

            return response?.Result;
        }

        private static McpServerInfo ParseServerInfo(JToken token)
        {
            if (token == null) return null;
            return new McpServerInfo
            {
                ProtocolVersion = (string)token["protocolVersion"],
                Name = (string)token["serverInfo"]?["name"],
                Version = (string)token["serverInfo"]?["version"]
            };
        }

        private static McpToolResult ParseToolResult(JToken token)
        {
            var result = new McpToolResult
            {
                IsError = token?["isError"]?.Value<bool>() ?? false
            };

            if (token?["content"] is JArray arr)
            {
                foreach (var item in arr)
                {
                    result.Content.Add(new McpContent
                    {
                        Type = (string)item["type"],
                        Text = (string)item["text"],
                        Data = (string)item["data"],
                        MimeType = (string)item["mimeType"]
                    });
                }
            }

            return result;
        }

        private static McpResourceContent ParseResourceContent(JToken token, string uri)
        {
            // resources/read 返回 { contents: [{ uri, mimeType, text|blob }] }
            var contents = token?["contents"] as JArray;
            if (contents == null || contents.Count == 0)
                return new McpResourceContent { Uri = uri };

            var first = contents[0];
            return new McpResourceContent
            {
                Uri = (string)first["uri"] ?? uri,
                MimeType = (string)first["mimeType"],
                Text = (string)first["text"],
                Blob = (string)first["blob"]
            };
        }

        public void Dispose()
        {
            IsInitialized = false;
            _transport?.Dispose();
        }
    }
}
