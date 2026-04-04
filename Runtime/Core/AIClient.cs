using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniAI.Providers.Claude;
using UniAI.Providers.OpenAI;

namespace UniAI
{
    /// <summary>
    /// AI 客户端 — 框架的唯一入口
    /// </summary>
    public class AIClient
    {
        private readonly IAIProvider _provider;

        /// <summary>
        /// 当前 Provider 名称
        /// </summary>
        public string ProviderName => _provider.Name;

        /// <summary>
        /// 使用指定 Provider 创建客户端
        /// </summary>
        public AIClient(IAIProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 从配置创建客户端（使用 ActiveProvider）
        /// </summary>
        public static AIClient Create(AIConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var entry = config.GetActiveProvider()
                ?? throw new InvalidOperationException("No provider configured in AIConfig.");

            return Create(entry, config.General);
        }

        /// <summary>
        /// 从单个 ProviderEntry 创建客户端
        /// </summary>
        public static AIClient Create(ProviderEntry entry, GeneralConfig general = null)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            general ??= new GeneralConfig();

            IAIProvider provider = entry.Protocol switch
            {
                ProviderProtocol.Claude => new ClaudeProvider(
                    new ClaudeConfig
                    {
                        ApiKey = entry.ApiKey,
                        BaseUrl = entry.BaseUrl,
                        Model = entry.Model,
                        ApiVersion = entry.ApiVersion ?? "2023-06-01"
                    },
                    general.TimeoutSeconds),
                ProviderProtocol.OpenAI => new OpenAIProvider(
                    new OpenAIConfig
                    {
                        ApiKey = entry.ApiKey,
                        BaseUrl = entry.BaseUrl,
                        Model = entry.Model
                    },
                    general.TimeoutSeconds),
                _ => throw new NotSupportedException(
                    $"Protocol '{entry.Protocol}' is not supported. Use AIClient(IAIProvider) for custom protocols.")
            };

            AILogger.LogLevel = general.LogLevel;
            AILogger.Info($"AIClient created with provider: {entry.Name} ({entry.Protocol})");

            return new AIClient(provider);
        }

        /// <summary>
        /// 发送请求获取完整响应
        /// </summary>
        public UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default)
        {
            return _provider.SendAsync(request, ct);
        }

        /// <summary>
        /// 流式发送请求
        /// </summary>
        public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default)
        {
            return _provider.StreamAsync(request, ct);
        }
    }
}
