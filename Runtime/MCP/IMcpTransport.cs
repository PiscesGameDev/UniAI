using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// MCP 传输层抽象 — 负责将 JSON-RPC 请求发送到 MCP Server 并接收响应
    /// 支持 Stdio（本地子进程）和 Streamable HTTP（远程服务）两种模式
    /// </summary>
    internal interface IMcpTransport : IDisposable
    {
        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 建立连接（启动子进程 / 打开 HTTP 会话）
        /// </summary>
        UniTask ConnectAsync(CancellationToken ct = default);

        /// <summary>
        /// 发送 JSON-RPC 请求并等待响应
        /// </summary>
        UniTask<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default);

        /// <summary>
        /// 发送 JSON-RPC 通知（无响应）
        /// </summary>
        UniTask SendNotificationAsync(string method, object param, CancellationToken ct = default);
    }
}
