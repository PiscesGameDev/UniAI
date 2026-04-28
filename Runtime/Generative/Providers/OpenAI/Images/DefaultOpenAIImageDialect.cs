using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    internal sealed class DefaultOpenAIImageDialect : OpenAIImageDialectBase
    {
        private const string AdapterId = "openai.images.default";

        public static readonly DefaultOpenAIImageDialect Instance = new();

        private DefaultOpenAIImageDialect() { }

        public override string GetEndpointPath(GenerateRequest request) => "/images/generations";

        public override bool UseMultipart(GenerateRequest request) => false;

        public override string Validate(GenerateRequest request, ModelEntry model)
        {
            if (string.IsNullOrEmpty(request?.Prompt))
                return "'prompt' is required.";

            if (IsEditRequest(request))
                return $"Model '{model?.Id ?? "unknown"}' uses the default image generation dialect and does not support image edits.";

            return null;
        }

        public override JObject BuildJsonBody(string model, GenerateRequest request)
        {
            var body = new JObject
            {
                ["model"] = model,
                ["prompt"] = request.Prompt,
                ["n"] = ResolveCount(request),
                ["size"] = ResolveLegacySize(request),
                ["response_format"] = "b64_json"
            };

            ApplyExtraParameters(body, request);
            return body;
        }

        public override IReadOnlyList<HttpMultipartFormPart> BuildMultipartParts(string model, GenerateRequest request)
        {
            return Array.Empty<HttpMultipartFormPart>();
        }

        public override object GetCapabilities(ModelEntry model, string modelId) => new
        {
            model = modelId,
            adapterId = AdapterId,
            sizes = new[] { "1024x1024", "1792x1024", "1024x1792" },
            maxCount = 1,
            supportsImageEdit = false,
            supportsNegativePrompt = false,
            note = "OpenAI-compatible default image generation dialect"
        };
    }
}
