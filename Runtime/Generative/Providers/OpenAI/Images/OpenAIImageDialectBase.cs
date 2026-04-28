using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    public abstract class OpenAIImageDialectBase : IOpenAIImageDialect
    {
        public abstract string GetEndpointPath(GenerateRequest request);
        public abstract bool UseMultipart(GenerateRequest request);
        public abstract string Validate(GenerateRequest request, ModelEntry model);
        public abstract JObject BuildJsonBody(string model, GenerateRequest request);
        public abstract IReadOnlyList<HttpMultipartFormPart> BuildMultipartParts(string model, GenerateRequest request);
        public abstract object GetCapabilities(ModelEntry model, string modelId);

        public virtual GenerateResult ParseResponse(string json, GenerateRequest request)
        {
            try
            {
                var root = JObject.Parse(json);
                var dataArray = root["data"] as JArray;

                if (dataArray == null || dataArray.Count == 0)
                    return GenerateResult.Fail("No images returned from API.");

                var assets = new List<GeneratedAsset>();
                foreach (var item in dataArray)
                {
                    var b64 = (string)item["b64_json"];
                    if (string.IsNullOrEmpty(b64))
                        continue;

                    var format = ResolveOutputFormat(request, root, "png");
                    var metadata = BuildMetadata(root, item, request, format);

                    assets.Add(new GeneratedAsset
                    {
                        Data = Convert.FromBase64String(b64),
                        MediaType = ToMediaType(format),
                        SuggestedExtension = ToExtension(format),
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

        protected static bool IsEditRequest(GenerateRequest request)
        {
            if (request == null)
                return false;

            if (request.ImageOperation == GenerateImageOperation.Edit)
                return true;

            if (request.ImageOperation == GenerateImageOperation.Generate)
                return false;

            return HasInputImages(request) || request.MaskImage?.Data != null;
        }

        protected static bool HasInputImages(GenerateRequest request)
        {
            return request?.InputImages != null && request.InputImages.Any(i => i?.Data != null && i.Data.Length > 0);
        }

        protected static int ResolveCount(GenerateRequest request)
        {
            return Math.Max(1, request?.Count ?? 1);
        }

        protected static string ResolveParameterString(GenerateRequest request, string key, string fallback = null)
        {
            if (request?.Parameters != null && request.Parameters.TryGetValue(key, out var value) && value != null)
                return value.ToString();

            return fallback;
        }

        protected static int? ResolveParameterInt(GenerateRequest request, string key, int? fallback = null)
        {
            if (request?.Parameters != null && request.Parameters.TryGetValue(key, out var value) && value != null)
            {
                if (value is int intValue)
                    return intValue;

                if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return fallback;
        }

        protected static void AddString(JObject body, string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
                body[name] = value;
        }

        protected static void AddInt(JObject body, string name, int? value)
        {
            if (value.HasValue)
                body[name] = value.Value;
        }

        protected static void ApplyExtraParameters(JObject body, GenerateRequest request)
        {
            if (request?.Parameters == null)
                return;

            foreach (var kv in request.Parameters)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                    continue;

                body[kv.Key] = kv.Value is JToken token ? token : JToken.FromObject(kv.Value);
            }
        }

        protected static string ResolveLegacySize(GenerateRequest request)
        {
            if (!string.IsNullOrEmpty(request?.Size))
                return request.Size;

            return request?.AspectRatio switch
            {
                "16:9" => "1792x1024",
                "9:16" => "1024x1792",
                _ => "1024x1024"
            };
        }

        protected static string ResolveGptImageSize(GenerateRequest request)
        {
            if (!string.IsNullOrEmpty(request?.Size))
                return request.Size;

            return request?.AspectRatio switch
            {
                "1:1" => "1024x1024",
                "16:9" => "1536x1024",
                "9:16" => "1024x1536",
                _ => "auto"
            };
        }

        protected static string ResolveOutputFormat(GenerateRequest request, JObject root, string fallback)
        {
            var format = ResolveParameterString(request, "output_format", request?.OutputFormat);
            if (string.IsNullOrEmpty(format))
                format = (string)root?["output_format"];
            if (string.IsNullOrEmpty(format))
                format = fallback;

            return format.ToLowerInvariant();
        }

        protected static string ToMediaType(string format)
        {
            return format switch
            {
                "jpeg" or "jpg" => "image/jpeg",
                "webp" => "image/webp",
                _ => "image/png"
            };
        }

        protected static string ToExtension(string format)
        {
            return format switch
            {
                "jpeg" or "jpg" => ".jpg",
                "webp" => ".webp",
                _ => ".png"
            };
        }

        private static Dictionary<string, object> BuildMetadata(
            JObject root,
            JToken item,
            GenerateRequest request,
            string outputFormat)
        {
            var metadata = new Dictionary<string, object>
            {
                ["output_format"] = outputFormat
            };

            var revisedPrompt = (string)item["revised_prompt"];
            if (!string.IsNullOrEmpty(revisedPrompt))
                metadata["revised_prompt"] = revisedPrompt;

            if (root?["usage"] != null)
                metadata["usage"] = root["usage"].ToString();

            AddMetadata(metadata, "size", ResolveParameterString(request, "size", request?.Size) ?? (string)root?["size"]);
            AddMetadata(metadata, "quality", ResolveParameterString(request, "quality", request?.Quality) ?? (string)root?["quality"]);
            AddMetadata(metadata, "background", ResolveParameterString(request, "background", request?.Background) ?? (string)root?["background"]);

            return metadata;
        }

        private static void AddMetadata(Dictionary<string, object> metadata, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
                metadata[key] = value;
        }
    }
}
