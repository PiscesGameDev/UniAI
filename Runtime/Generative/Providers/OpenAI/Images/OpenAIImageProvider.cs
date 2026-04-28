using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI.Providers.OpenAI.Images
{
    /// <summary>
    /// OpenAI Images API 兼容图片生成 Provider。
    /// 具体模型的请求/响应差异由 dialect 处理。
    /// </summary>
    internal class OpenAIImageProvider : IGenerativeAssetProvider
    {
        public string ProviderId { get; }
        public string DisplayName { get; }
        public GenerativeAssetType AssetType => GenerativeAssetType.Image;

        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;
        private readonly int _timeoutSeconds;

        public OpenAIImageProvider(
            string apiKey,
            string baseUrl,
            string model,
            int timeoutSeconds,
            string providerId = "openai-image",
            string displayName = "OpenAI Image")
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');
            _model = model;
            _timeoutSeconds = timeoutSeconds;
            ProviderId = providerId;
            DisplayName = displayName;
        }

        public async UniTask<GenerateResult> GenerateAsync(GenerateRequest request, CancellationToken ct)
        {
            var modelEntry = ModelRegistry.Get(_model);
            var dialect = OpenAIImageDialectRegistry.Resolve(_model);
            var validationError = dialect.Validate(request, modelEntry);
            if (!string.IsNullOrEmpty(validationError))
                return GenerateResult.Fail(validationError);

            var endpointPath = dialect.GetEndpointPath(request);
            var url = $"{_baseUrl}{endpointPath}";
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {_apiKey}"
            };

            AILogger.Info($"[OpenAIImageProvider] Generating image: model={_model}, endpoint={endpointPath}, prompt={Preview(request?.Prompt)}...");

            HttpResult result;
            if (dialect.UseMultipart(request))
            {
                result = await AIHttpClient.PostMultipartAsync(
                    url,
                    dialect.BuildMultipartParts(_model, request),
                    headers,
                    _timeoutSeconds,
                    ct);
            }
            else
            {
                var body = dialect.BuildJsonBody(_model, request);
                result = await AIHttpClient.PostJsonAsync(url, body.ToString(), headers, _timeoutSeconds, ct);
            }

            if (!result.IsSuccess)
                return GenerateResult.Fail($"HTTP {result.StatusCode}: {result.Error}{FormatErrorBody(result.Body)}");

            return dialect.ParseResponse(result.Body, request);
        }

        public object GetCapabilities()
        {
            var modelEntry = ModelRegistry.Get(_model);
            return OpenAIImageDialectRegistry.Resolve(_model).GetCapabilities(modelEntry, _model);
        }

        private static string Preview(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            return text.Substring(0, Math.Min(text.Length, 50));
        }

        private static string FormatErrorBody(string body)
        {
            return string.IsNullOrEmpty(body) ? "" : $", body={body}";
        }
    }
}
