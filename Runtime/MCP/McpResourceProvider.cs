using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 将 MCP Resource 适配为 IContextProvider，使其能被 ContextPipeline 自动注入
    /// </summary>
    public class McpResourceProvider : IContextProvider
    {
        private readonly McpClient _client;
        private readonly McpResourceDefinition _resource;

        /// <summary>
        /// 默认相关度（>0.3 才会被 ContextPipeline 注入）
        /// </summary>
        public float Relevance { get; set; } = 0.5f;

        public McpResourceProvider(McpClient client, McpResourceDefinition resource)
        {
            _client = client;
            _resource = resource;
        }

        public async UniTask<ContextResult> RetrieveAsync(string query, CancellationToken ct = default)
        {
            if (_client == null || _resource == null || string.IsNullOrEmpty(_resource.Uri))
                return null;

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
                    Relevance = Relevance
                };
            }
            catch (System.Exception e)
            {
                AILogger.Warning($"[MCP] Read resource '{_resource.Uri}' failed: {e.Message}");
                return null;
            }
        }
    }
}
