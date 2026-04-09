using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    /// <summary>
    /// OpenAI Images API 兼容的图片生成 Provider。
    /// 适用于所有 OpenAI 兼容端点（OpenAI、NewAPI/OneAPI 中转站、Gemini via relay 等）。
    /// 每个启用图片生成的渠道创建一个实例。
    /// </summary>
    internal class OpenAIImageProvider : IGenerativeAssetProvider
    {
        public string ProviderId { get; }
        public string DisplayName { get; }
        public GenerativeAssetType AssetType => GenerativeAssetType.Image;

        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        /// <param name="apiKey">API Key</param>
        /// <param name="baseUrl">基础 URL（含 /v1），如 https://api.openai.com/v1</param>
        /// <param name="model">图片生成模型名（如 "dall-e-3"、"gemini-imagen-3"）</param>
        /// <param name="providerId">唯一标识</param>
        /// <param name="displayName">展示名</param>
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
            var url = $"{_baseUrl}/images/generations";
            var size = AspectRatioToSize(request.AspectRatio);
            int count = Math.Max(1, request.Count);

            var body = new JObject
            {
                ["model"] = _model,
                ["prompt"] = request.Prompt,
                ["n"] = count,
                ["size"] = size,
                ["response_format"] = "b64_json"
            };

            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {_apiKey}"
            };

            AILogger.Info($"[OpenAIImageProvider] Generating image: model={_model}, size={size}, prompt={request.Prompt.Substring(0, Math.Min(request.Prompt.Length, 50))}...");

            var result = await AIHttpClient.PostJsonAsync(url, body.ToString(), headers, 120, ct);

            if (!result.IsSuccess)
                return GenerateResult.Fail($"HTTP {result.StatusCode}: {result.Error}");

            try
            {
                var json = JObject.Parse(result.Body);
                var dataArray = json["data"] as JArray;

                if (dataArray == null || dataArray.Count == 0)
                    return GenerateResult.Fail("No images returned from API.");

                var assets = new List<GeneratedAsset>();
                foreach (var item in dataArray)
                {
                    var b64 = (string)item["b64_json"];
                    if (string.IsNullOrEmpty(b64))
                        continue;

                    var metadata = new Dictionary<string, object>();
                    var revisedPrompt = (string)item["revised_prompt"];
                    if (!string.IsNullOrEmpty(revisedPrompt))
                        metadata["revised_prompt"] = revisedPrompt;

                    assets.Add(new GeneratedAsset
                    {
                        Data = Convert.FromBase64String(b64),
                        MediaType = "image/png",
                        SuggestedExtension = ".png",
                        Metadata = metadata
                    });
                }

                if (assets.Count == 0)
                    return GenerateResult.Fail("API returned data but no valid b64_json found.");

                return GenerateResult.Success(assets);
            }
            catch (Exception ex)
            {
                return GenerateResult.Fail($"Failed to parse response: {ex.Message}");
            }
        }

        public object GetCapabilities() => new
        {
            model = _model,
            sizes = new[] { "1024x1024", "1792x1024", "1024x1792" },
            maxCount = 1,
            supportsNegativePrompt = false,
            note = "OpenAI-compatible endpoint, works with NewAPI/OneAPI relays"
        };

        private static string AspectRatioToSize(string aspectRatio) => aspectRatio switch
        {
            "16:9" => "1792x1024",
            "9:16" => "1024x1792",
            _ => "1024x1024"
        };
    }
}
