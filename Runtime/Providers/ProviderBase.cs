using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace UniAI.Providers
{
    /// <summary>
    /// Provider 抽象基类 — 提取 SendAsync 模板方法和通用工具
    /// </summary>
    public abstract class ProviderBase : IAIProvider
    {
        public abstract string Name { get; }

        protected readonly int _timeoutSeconds;

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

        public abstract IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default);

        // ────────────────────────── 子类实现 ──────────────────────────

        /// <summary>
        /// 构建 API 请求 URL
        /// </summary>
        protected abstract string BuildUrl();

        /// <summary>
        /// 构建协议特定的请求体对象（将被 JSON 序列化）
        /// </summary>
        protected abstract object BuildRequestBody(AIRequest request, bool stream);

        /// <summary>
        /// 构建请求头
        /// </summary>
        protected abstract Dictionary<string, string> BuildHeaders();

        /// <summary>
        /// 解析成功响应
        /// </summary>
        protected abstract AIResponse ParseResponse(string json);

        /// <summary>
        /// 从请求体对象中提取 Model 名称（用于日志）
        /// </summary>
        protected abstract string GetModelFromBody(object body);

        /// <summary>
        /// 从错误响应体中提取错误信息。返回 null 表示无法解析。
        /// </summary>
        protected abstract string TryParseErrorBody(string body);

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
