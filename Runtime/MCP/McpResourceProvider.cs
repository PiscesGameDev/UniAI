using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 将 MCP Resource 适配为 IContextProvider，使其能被 ContextPipeline 自动注入。
    /// 使用 query 与 Resource 元数据的关键词匹配调整相关度。
    /// </summary>
    internal class McpResourceProvider : IContextProvider
    {
        private readonly McpClient _client;
        private readonly McpResourceDefinition _resource;

        /// <summary>基础相关度：query 为空或无匹配时返回该值</summary>
        public float BaseRelevance { get; set; } = 0.4f;

        /// <summary>命中关键词时的相关度加成</summary>
        public float MatchBonus { get; set; } = 0.4f;

        public McpResourceProvider(McpClient client, McpResourceDefinition resource)
        {
            _client = client;
            _resource = resource;
        }

        public async UniTask<ContextResult> RetrieveAsync(string query, CancellationToken ct = default)
        {
            if (_client == null || _resource == null || string.IsNullOrEmpty(_resource.Uri))
                return null;

            float relevance = ComputeRelevance(query);
            if (relevance <= 0.3f)
                return null; // 低相关度直接跳过，避免无谓的 resources/read

            try
            {
                var content = await _client.ReadResourceAsync(_resource.Uri, ct);
                string text = content?.Text;
                if (string.IsNullOrEmpty(text))
                    return null;

                string header = !string.IsNullOrEmpty(_resource.Name)
                    ? $"[{_resource.Name}] ({_resource.Uri})"
                    : $"[{_resource.Uri}]";

                string body = $"{header}\n{text}";

                return new ContextResult
                {
                    Content = body,
                    EstimatedTokens = TokenEstimator.EstimateTokens(body),
                    Relevance = relevance
                };
            }
            catch (System.Exception e)
            {
                AILogger.Warning($"[MCP] Read resource '{_resource.Uri}' failed: {e.Message}");
                return null;
            }
        }

        private float ComputeRelevance(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return BaseRelevance;

            string q = query.ToLowerInvariant();
            bool hit = ContainsToken(q, _resource.Name) ||
                       ContainsToken(q, _resource.Uri) ||
                       ContainsToken(q, _resource.Description);

            return hit ? UnityEngine.Mathf.Min(1f, BaseRelevance + MatchBonus) : BaseRelevance;
        }

        private static bool ContainsToken(string query, string field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            return query.Contains(field.ToLowerInvariant());
        }
    }
}
