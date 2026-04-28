using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UniAI.Providers.OpenAI.Images
{
    [Adapter(
        "openai.images.gpt-image-2",
        AdapterTarget.OpenAIImageDialect,
        "GPT Image 2",
        "OpenAI image generation/edit dialect for gpt-image-2.",
        priority: 100,
        protocolId: "OpenAI",
        capabilities: ModelCapability.ImageGen | ModelCapability.ImageEdit)]
    internal sealed class GptImage2DialectFactory : IOpenAIImageDialectFactory
    {
        public bool CanHandle(ModelEntry model)
        {
            if (model == null)
                return false;

            return model.HasBehaviorTag("openai.images.gpt_image")
                   || model.HasBehaviorTag("openai.images.no_response_format");
        }

        public IOpenAIImageDialect Create(ModelEntry model) => new GptImage2ImageDialect(model);
    }

    internal sealed class GptImage2ImageDialect : OpenAIImageDialectBase
    {
        private const string AdapterId = "openai.images.gpt-image-2";

        private readonly ModelEntry _model;

        public GptImage2ImageDialect(ModelEntry model)
        {
            _model = model;
        }

        public override string GetEndpointPath(GenerateRequest request)
        {
            return IsEditRequest(request) ? "/images/edits" : "/images/generations";
        }

        public override bool UseMultipart(GenerateRequest request)
        {
            return IsEditRequest(request);
        }

        public override string Validate(GenerateRequest request, ModelEntry model)
        {
            if (string.IsNullOrEmpty(request?.Prompt))
                return "'prompt' is required.";

            if (request.ImageOperation == GenerateImageOperation.Generate && (HasInputImages(request) || request.MaskImage?.Data != null))
                return "Image inputs were provided but ImageOperation is Generate. Use Auto or Edit.";

            if (IsEditRequest(request) && !HasInputImages(request))
                return "Image edit requests require at least one input image.";

            var count = ResolveCount(request);
            if (count < 1 || count > 10)
                return "gpt-image-2 supports Count values from 1 to 10.";

            var outputFormat = ResolveOutputFormat(request, null, "png");
            if (!IsAllowedOutputFormat(outputFormat))
                return $"OutputFormat '{outputFormat}' is not supported. Allowed values: {GetAllowedOutputFormatsCsv()}.";

            var background = ResolveParameterString(request, "background", request.Background);
            if (string.Equals(background, "transparent", StringComparison.OrdinalIgnoreCase)
                && !(model?.GetBehaviorOptionBool("image.supports_transparent_background", true) ?? true))
            {
                return "This model configuration does not support transparent background.";
            }

            var sizeError = ValidateGptImageSize(ResolveGptImageSize(request), model);
            if (!string.IsNullOrEmpty(sizeError))
                return sizeError;

            return null;
        }

        public override JObject BuildJsonBody(string model, GenerateRequest request)
        {
            var body = new JObject
            {
                ["model"] = model,
                ["prompt"] = request.Prompt,
                ["n"] = ResolveCount(request)
            };

            AddString(body, "size", ResolveGptImageSize(request));
            AddString(body, "quality", ResolveParameterString(request, "quality", request.Quality ?? "auto"));
            AddString(body, "output_format", ResolveOutputFormat(request, null, "png"));
            AddInt(body, "output_compression", ResolveParameterInt(request, "output_compression", request.OutputCompression));
            AddString(body, "background", ResolveParameterString(request, "background", request.Background ?? "auto"));

            ApplyExtraParameters(body, request);
            return body;
        }

        public override IReadOnlyList<HttpMultipartFormPart> BuildMultipartParts(string model, GenerateRequest request)
        {
            var parts = new List<HttpMultipartFormPart>
            {
                HttpMultipartFormPart.Field("model", model),
                HttpMultipartFormPart.Field("prompt", request.Prompt),
                HttpMultipartFormPart.Field("n", ResolveCount(request).ToString(CultureInfo.InvariantCulture)),
                HttpMultipartFormPart.Field("size", ResolveGptImageSize(request)),
                HttpMultipartFormPart.Field("quality", ResolveParameterString(request, "quality", request.Quality ?? "auto")),
                HttpMultipartFormPart.Field("output_format", ResolveOutputFormat(request, null, "png"))
            };

            var outputCompression = ResolveParameterInt(request, "output_compression", request.OutputCompression);
            if (outputCompression.HasValue)
                parts.Add(HttpMultipartFormPart.Field("output_compression", outputCompression.Value.ToString(CultureInfo.InvariantCulture)));

            var background = ResolveParameterString(request, "background", request.Background ?? "auto");
            if (!string.IsNullOrEmpty(background))
                parts.Add(HttpMultipartFormPart.Field("background", background));

            foreach (var image in request.InputImages.Where(i => i?.Data != null && i.Data.Length > 0))
            {
                parts.Add(HttpMultipartFormPart.File(
                    "image[]",
                    image.Data,
                    string.IsNullOrEmpty(image.FileName) ? "image.png" : image.FileName,
                    string.IsNullOrEmpty(image.MediaType) ? "image/png" : image.MediaType));
            }

            if (request.MaskImage?.Data != null)
            {
                parts.Add(HttpMultipartFormPart.File(
                    "mask",
                    request.MaskImage.Data,
                    string.IsNullOrEmpty(request.MaskImage.FileName) ? "mask.png" : request.MaskImage.FileName,
                    string.IsNullOrEmpty(request.MaskImage.MediaType) ? "image/png" : request.MaskImage.MediaType));
            }

            return parts;
        }

        public override object GetCapabilities(ModelEntry model, string modelId) => new
        {
            model = modelId,
            adapterId = AdapterId,
            endpoints = new[] { "/images/generations", "/images/edits" },
            sizes = new[] { "auto", "1024x1024", "1536x1024", "1024x1536", "custom multiple-of-16 up to configured max side" },
            qualities = new[] { "auto", "low", "medium", "high" },
            outputFormats = GetAllowedOutputFormats(),
            supportsImageEdit = true,
            supportsMultipleInputImages = true,
            supportsTransparentBackground = model?.GetBehaviorOptionBool("image.supports_transparent_background", true) ?? true,
            supportsStreaming = false,
            supportsFunctionCalling = false
        };

        private bool IsAllowedOutputFormat(string format)
        {
            return GetAllowedOutputFormats().Any(f => string.Equals(f, format, StringComparison.OrdinalIgnoreCase));
        }

        private string[] GetAllowedOutputFormats()
        {
            var csv = _model?.GetBehaviorOption("image.allowed_output_formats", "png,jpeg,webp") ?? "png,jpeg,webp";
            return csv.Split(',')
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct()
                .ToArray();
        }

        private string GetAllowedOutputFormatsCsv()
        {
            return string.Join(",", GetAllowedOutputFormats());
        }

        private static string ValidateGptImageSize(string size, ModelEntry model)
        {
            if (string.IsNullOrEmpty(size) || string.Equals(size, "auto", StringComparison.OrdinalIgnoreCase))
                return null;

            var parts = size.Split('x');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                return $"Image size '{size}' is invalid. Use 'auto' or '<width>x<height>'.";
            }

            var maxSide = model?.GetBehaviorOptionInt("image.max_side", 3840) ?? 3840;
            if (width <= 0 || height <= 0 || width > maxSide || height > maxSide)
                return $"Image size '{size}' exceeds the configured max side {maxSide}.";

            if (width % 16 != 0 || height % 16 != 0)
                return $"Image size '{size}' is invalid. Width and height must be multiples of 16.";

            var longSide = Math.Max(width, height);
            var shortSide = Math.Min(width, height);
            if (longSide > shortSide * 3)
                return $"Image size '{size}' is invalid. Aspect ratio must be 3:1 or less.";

            return null;
        }
    }
}
