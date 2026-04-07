using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Newtonsoft.Json;

namespace UniAI.Providers
{
    /// <summary>
    /// Provider 抽象基类 — 提取 SendAsync / StreamAsync 模板方法和通用工具
    /// </summary>
    public abstract class ProviderBase : IAIProvider
    {
        public abstract string Name { get; }

        private readonly int _timeoutSeconds;

        protected static readonly JsonSerializerSettings SerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        protected ProviderBase(int timeoutSeconds)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        // ────────────────────────── SendAsync 模板方法 ──────────────────────────

        public async UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            var url = BuildUrl();
            var body = BuildRequestBody(request, stream: false);
            var json = JsonConvert.SerializeObject(body, Formatting.None, SerializerSettings);
            var headers = BuildHeaders();

            AILogger.Verbose($"{Name} SendAsync model={GetModelFromBody(body)}");

            var result = await AIHttpClient.PostJsonAsync(url, json, headers, _timeoutSeconds, ct);

            if (!result.IsSuccess)
                return ParseError(result);

            return ParseResponse(result.Body);
        }

        // ────────────────────────── StreamAsync 模板方法 ──────────────────────────

        public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var url = BuildUrl();
                var body = BuildRequestBody(request, stream: true);
                var json = JsonConvert.SerializeObject(body, Formatting.None, SerializerSettings);
                var headers = BuildHeaders();

                AILogger.Verbose($"{Name} StreamAsync model={GetModelFromBody(body)}");

                var parser = new SSEParser();
                var streamState = CreateStreamState();

                await foreach (var line in AIHttpClient.PostStreamAsync(url, json, headers, linkedToken))
                {
                    var evt = parser.ParseLine(line);
                    if (evt == null) continue;
                    if (evt.Data == null || evt.Data == "[DONE]")
                    {
                        if (OnStreamDone(streamState, chunk => writer.YieldAsync(chunk)))
                            break;
                        continue;
                    }

                    try
                    {
                        await ProcessStreamEvent(evt, streamState, chunk => writer.YieldAsync(chunk));
                    }
                    catch (Exception e)
                    {
                        AILogger.Warning($"Failed to parse stream event: {e.Message}");
                    }
                }
            });
        }

        /// <summary>
        /// 输出流式 chunk 的委托
        /// </summary>
        protected delegate UniTask EmitChunk(AIStreamChunk chunk);

        // ────────────────────────── 子类实现 ──────────────────────────

        protected abstract string BuildUrl();

        protected abstract object BuildRequestBody(AIRequest request, bool stream);

        protected abstract Dictionary<string, string> BuildHeaders();

        protected abstract AIResponse ParseResponse(string json);

        protected abstract string GetModelFromBody(object body);

        protected abstract string TryParseErrorBody(string body);

        /// <summary>
        /// 创建流式解析状态对象（用于跨事件累积 Tool 调用等）。默认返回 null。
        /// </summary>
        protected virtual object CreateStreamState() => null;

        /// <summary>
        /// 处理单个 SSE 事件，将解析结果通过 emit 输出。子类必须实现协议特定的解析逻辑。
        /// </summary>
        protected abstract UniTask ProcessStreamEvent(SSEEvent evt, object streamState, EmitChunk emit);

        /// <summary>
        /// 收到 [DONE] 时调用，返回 true 表示终止流。默认不终止。
        /// </summary>
        protected virtual bool OnStreamDone(object streamState, EmitChunk emit) => false;

        // ────────────────────────── 通用方法 ──────────────────────────

        private AIResponse ParseError(HttpResult result)
        {
            string errorMsg = result.Error;
            if (!string.IsNullOrEmpty(result.Body))
            {
                var parsed = TryParseErrorBody(result.Body);
                if (parsed != null)
                    errorMsg = parsed;
            }
            return AIResponse.Fail($"HTTP {result.StatusCode}: {errorMsg}", result.Body);
        }
    }
}
