using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace UniAI
{
    /// <summary>
    /// Streamable HTTP 传输实现 — 通过 POST {BaseUrl} 发送 JSON-RPC 请求
    /// 响应可能是 application/json 或 text/event-stream
    /// </summary>
    internal class HttpMcpTransport : IMcpTransport
    {
        private readonly string _baseUrl;
        private readonly Dictionary<string, string> _headers;
        private readonly int _timeoutSeconds;
        private int _nextId;
        private string _sessionId;

        public bool IsConnected { get; private set; }

        public HttpMcpTransport(string baseUrl, Dictionary<string, string> headers, int timeoutSeconds = 60)
        {
            _baseUrl = baseUrl?.TrimEnd('/');
            _headers = headers != null ? new Dictionary<string, string>(headers) : new Dictionary<string, string>();
            _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;
        }

        public UniTask ConnectAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_baseUrl))
                throw new InvalidOperationException("HttpMcpTransport: BaseUrl is empty");
            IsConnected = true;
            AILogger.Info($"[MCP] HTTP transport ready: {_baseUrl}");
            return UniTask.CompletedTask;
        }

        public async UniTask<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("HttpMcpTransport: not connected");

            request.Id = Interlocked.Increment(ref _nextId);
            string body = JsonConvert.SerializeObject(request);

            var headers = BuildHeaders();
            var result = await AIHttpClient.PostJsonAsync(_baseUrl, body, headers, _timeoutSeconds, ct);

            if (!result.IsSuccess)
                throw new InvalidOperationException($"HTTP {result.StatusCode}: {result.Error}");

            // TODO: 检查 Mcp-Session-Id 响应头（UnityWebRequest 响应头读取需额外处理，
            // 当前实现暂不自动提取，用户可在 Headers 中静态指定）

            return ParseResponseBody(result.Body);
        }

        public async UniTask SendNotificationAsync(string method, object param, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("HttpMcpTransport: not connected");

            var notification = new JsonRpcRequest { Method = method, Params = param, Id = null };
            string body = JsonConvert.SerializeObject(notification);
            var headers = BuildHeaders();
            await AIHttpClient.PostJsonAsync(_baseUrl, body, headers, _timeoutSeconds, ct);
        }

        private Dictionary<string, string> BuildHeaders()
        {
            var headers = new Dictionary<string, string>(_headers)
            {
                ["Accept"] = "application/json, text/event-stream"
            };
            if (!string.IsNullOrEmpty(_sessionId))
                headers["Mcp-Session-Id"] = _sessionId;
            return headers;
        }

        private static JsonRpcResponse ParseResponseBody(string body)
        {
            if (string.IsNullOrEmpty(body))
                throw new InvalidOperationException("Empty HTTP response body");

            string trimmed = body.TrimStart();

            // 直接 JSON
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                return JsonConvert.DeserializeObject<JsonRpcResponse>(body);

            // SSE 格式：逐行解析，取最后一个 data: 事件
            string lastData = null;
            foreach (var line in body.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.StartsWith("data:"))
                    lastData = l.Substring(5).TrimStart();
            }

            if (string.IsNullOrEmpty(lastData))
                throw new InvalidOperationException($"Cannot parse MCP HTTP response: {body.Substring(0, Math.Min(200, body.Length))}");

            return JsonConvert.DeserializeObject<JsonRpcResponse>(lastData);
        }

        public void Dispose()
        {
            IsConnected = false;
        }
    }
}
