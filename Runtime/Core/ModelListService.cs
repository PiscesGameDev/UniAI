using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    /// <summary>
    /// 从 Provider API 获取可用模型列表
    /// </summary>
    public static class ModelListService
    {
        /// <summary>
        /// 获取指定渠道支持的模型 ID 列表
        /// </summary>
        /// <param name="entry">渠道配置</param>
        /// <param name="apiKey">有效的 API Key（调用方负责解析环境变量等）</param>
        /// <param name="timeoutSeconds">请求超时秒数</param>
        /// <param name="ct">取消令牌</param>
        public static async UniTask<ModelListResult> FetchModelsAsync(
            ChannelEntry entry, string apiKey, int timeoutSeconds = 30, CancellationToken ct = default)
        {
            try
            {
                if (string.IsNullOrEmpty(apiKey))
                    return ModelListResult.Fail("No API Key configured.");

                return entry.Protocol switch
                {
                    ProviderProtocol.OpenAI => await FetchOpenAIModels(entry.BaseUrl, apiKey, timeoutSeconds, ct),
                    ProviderProtocol.Claude => await FetchClaudeModels(entry.BaseUrl, apiKey, entry.ApiVersion, timeoutSeconds, ct),
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
            var headers = new Dictionary<string, string>
            {
                { "Authorization", $"Bearer {apiKey}" }
            };

            var trimmedBase = baseUrl.TrimEnd('/');
            var url = $"{trimmedBase}/models";

            var result = await AIHttpClient.GetAsync(url, headers, timeout, ct);

            // If failed and baseUrl ends with a version path (e.g. /v1), retry without it
            if (!result.IsSuccess && System.Text.RegularExpressions.Regex.IsMatch(trimmedBase, @"/v\d+$"))
            {
                var rootBase = System.Text.RegularExpressions.Regex.Replace(trimmedBase, @"/v\d+$", "");
                var fallbackUrl = $"{rootBase}/models";
                AILogger.Info($"[ModelList] Retrying without version path: {fallbackUrl}");
                result = await AIHttpClient.GetAsync(fallbackUrl, headers, timeout, ct);
            }

            if (!result.IsSuccess)
                return ModelListResult.Fail(result.Error ?? $"HTTP {result.StatusCode}");

            var models = new List<ModelInfo>();
            var json = JObject.Parse(result.Body);

            if (json["data"] is JArray data)
            {
                foreach (var item in data)
                {
                    var id = item["id"]?.ToString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        models.Add(new ModelInfo
                        {
                            Id = id
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

            while (hasMore)
            {
                ct.ThrowIfCancellationRequested();

                var pageUrl = afterId != null ? $"{url}?after_id={afterId}&limit=100" : $"{url}?limit=100";
                var result = await AIHttpClient.GetAsync(pageUrl, headers, timeout, ct);

                if (!result.IsSuccess)
                    return ModelListResult.Fail(result.Error ?? $"HTTP {result.StatusCode}");

                var json = JObject.Parse(result.Body);

                if (json["data"] is JArray data)
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

    /// <summary>
    /// 模型信息
    /// </summary>
    public class ModelInfo
    {
        public string Id;
        public string DisplayName;

        public string Label => !string.IsNullOrEmpty(DisplayName) ? $"{DisplayName} ({Id})" : Id;
    }

    /// <summary>
    /// 模型列表查询结果
    /// </summary>
    public class ModelListResult
    {
        public bool IsSuccess;
        public List<ModelInfo> Models;
        public string Error;

        public static ModelListResult Success(List<ModelInfo> models) => new()
        {
            IsSuccess = true,
            Models = models
        };

        public static ModelListResult Fail(string error) => new()
        {
            IsSuccess = false,
            Error = error,
            Models = new List<ModelInfo>()
        };
    }
}
