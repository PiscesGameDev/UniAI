using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// OpenAI Images API compatible image generation provider.
    /// Dialects handle model-specific request and response differences.
    /// </summary>
    internal class OpenAIImageProvider : IGenerativeAssetProvider
    {
        public string ProviderId { get; }
        public string DisplayName { get; }
        public GenerativeAssetType AssetType => GenerativeAssetType.Image;

        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public OpenAIImageProvider(
            string apiKey,
            string baseUrl,
            string model,
            string providerId = "openai-image",
            string displayName = "OpenAI Image")
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');
            _model = model;
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
                    120,
                    ct);
            }
            else
            {
                var body = dialect.BuildJsonBody(_model, request);
                result = await AIHttpClient.PostJsonAsync(url, body.ToString(), headers, 120, ct);
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
