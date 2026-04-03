using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using UnityEngine.Networking;

namespace UniAI
{
    /// <summary>
    /// AI HTTP 客户端，封装 UnityWebRequest 处理 AI API 的 POST JSON 和 SSE 流式请求
    /// </summary>
    internal static class AIHttpClient
    {
        /// <summary>
        /// 发送 POST JSON 请求并获取完整响应
        /// </summary>
        internal static async UniTask<HttpResult> PostJsonAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            int timeoutSeconds,
            CancellationToken ct)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            if (headers != null)
            {
                foreach (var kv in headers)
                    request.SetRequestHeader(kv.Key, kv.Value);
            }

            AILogger.Verbose($"POST {url} body={jsonBody.Length} chars");

            try
            {
                await request.SendWebRequest().WithCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return HttpResult.Fail(0, $"Request failed: {e.Message}");
            }

            var responseBody = request.downloadHandler.text;

            if (request.result != UnityWebRequest.Result.Success)
            {
                AILogger.Error($"HTTP {request.responseCode}: {request.error}");
                return HttpResult.Fail(request.responseCode, request.error, responseBody);
            }

            AILogger.Verbose($"HTTP {request.responseCode} response={responseBody.Length} chars");
            return HttpResult.Success(request.responseCode, responseBody);
        }

        /// <summary>
        /// 发送 POST JSON 请求并流式读取 SSE 响应，逐行 yield return
        /// </summary>
        internal static IUniTaskAsyncEnumerable<string> PostStreamAsync(
            string url,
            string jsonBody,
            Dictionary<string, string> headers,
            CancellationToken ct)
        {
            return UniTaskAsyncEnumerable.Create<string>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var channel = Channel.CreateSingleConsumerUnbounded<string>();
                var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

                using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new SSEDownloadHandler(channel);
                request.SetRequestHeader("Content-Type", "application/json");

                if (headers != null)
                {
                    foreach (var kv in headers)
                        request.SetRequestHeader(kv.Key, kv.Value);
                }

                AILogger.Verbose($"POST (stream) {url}");

                // 启动请求但不 await，让 SSEDownloadHandler 异步回调
                var op = request.SendWebRequest();

                // 消费 channel 中的行
                var reader = channel.Reader;
                while (await reader.WaitToReadAsync(linkedToken))
                {
                    while (reader.TryRead(out var line))
                    {
                        await writer.YieldAsync(line);
                    }
                }

                // 检查请求结果
                if (request.result != UnityWebRequest.Result.Success)
                {
                    AILogger.Error($"Stream HTTP {request.responseCode}: {request.error}");
                }
            });
        }
    }
}
