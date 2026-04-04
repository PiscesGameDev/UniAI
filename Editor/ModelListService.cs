using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniAI.Editor
{
    /// <summary>
    /// 从 Provider API 获取可用模型列表（Editor 专用）
    /// </summary>
    internal static class ModelListService
    {
        /// <summary>
        /// 获取指定渠道支持的模型 ID 列表
        /// </summary>
        internal static async UniTask<ModelListResult> FetchModelsAsync(
            ProviderEntry entry, GeneralConfig general, CancellationToken ct = default)
        {
            try
            {
                var apiKey = entry.GetEffectiveApiKey();
                if (string.IsNullOrEmpty(apiKey))
                    return ModelListResult.Fail("No API Key configured.");

                var timeout = general?.TimeoutSeconds ?? 30;

                return entry.Protocol switch
                {
                    ProviderProtocol.OpenAI => await FetchOpenAIModels(entry.BaseUrl, apiKey, timeout, ct),
                    ProviderProtocol.Claude => await FetchClaudeModels(entry.BaseUrl, apiKey, entry.ApiVersion, timeout, ct),
                    _ => ModelListResult.Fail($"Unsupported protocol: {entry.Protocol}")
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                return ModelListResult.Fail(e.Message);
            }
        }

        private static async UniTask<ModelListResult> FetchOpenAIModels(
            string baseUrl, string apiKey, int timeout, CancellationToken ct)
        {
            var url = $"{baseUrl.TrimEnd('/')}/models";
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {apiKey}" }
            };

            var result = await AIHttpClient.GetAsync(url, headers, timeout, ct);
            if (!result.IsSuccess)
                return ModelListResult.Fail(result.Error ?? $"HTTP {result.StatusCode}");

            var models = new List<ModelInfo>();
            var json = JObject.Parse(result.Body);
            var data = json["data"] as JArray;

            if (data != null)
            {
                foreach (var item in data)
                {
                    var id = item["id"]?.ToString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        models.Add(new ModelInfo
                        {
                            Id = id,
                            OwnedBy = item["owned_by"]?.ToString()
                        });
                    }
                }
            }

            models.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
            return ModelListResult.Success(models);
        }

        private static async UniTask<ModelListResult> FetchClaudeModels(
            string baseUrl, string apiKey, string apiVersion, int timeout, CancellationToken ct)
        {
            var url = $"{baseUrl.TrimEnd('/')}/v1/models";
            var headers = new Dictionary<string, string>
            {
                { "x-api-key", apiKey },
                { "anthropic-version", apiVersion ?? "2023-06-01" }
            };

            var models = new List<ModelInfo>();
            bool hasMore = true;
            string afterId = null;

            // Claude API 支持分页
            while (hasMore)
            {
                ct.ThrowIfCancellationRequested();

                var pageUrl = afterId != null ? $"{url}?after_id={afterId}&limit=100" : $"{url}?limit=100";
                var result = await AIHttpClient.GetAsync(pageUrl, headers, timeout, ct);

                if (!result.IsSuccess)
                    return ModelListResult.Fail(result.Error ?? $"HTTP {result.StatusCode}");

                var json = JObject.Parse(result.Body);
                var data = json["data"] as JArray;

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        var id = item["id"]?.ToString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            models.Add(new ModelInfo
                            {
                                Id = id,
                                DisplayName = item["display_name"]?.ToString()
                            });
                        }
                    }
                }

                hasMore = json["has_more"]?.Value<bool>() ?? false;
                if (hasMore)
                    afterId = json["last_id"]?.ToString();
            }

            models.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.Ordinal));
            return ModelListResult.Success(models);
        }
    }

    internal class ModelInfo
    {
        public string Id;
        public string DisplayName;
        public string OwnedBy;

        public string Label => !string.IsNullOrEmpty(DisplayName) ? $"{DisplayName} ({Id})" : Id;
    }

    internal class ModelListResult
    {
        public bool IsSuccess;
        public List<ModelInfo> Models;
        public string Error;

        internal static ModelListResult Success(List<ModelInfo> models) => new()
        {
            IsSuccess = true,
            Models = models
        };

        internal static ModelListResult Fail(string error) => new()
        {
            IsSuccess = false,
            Error = error,
            Models = new List<ModelInfo>()
        };
    }
}
