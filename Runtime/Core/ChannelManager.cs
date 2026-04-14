using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using UniAI.Providers;

namespace UniAI
{
    /// <summary>
    /// 渠道管理器 — 全局静态，负责渠道查找、Provider 缓存和故障转移。
    /// 缓存独立于 AIClient 生命周期，所有调用者共享。
    /// </summary>
    public static class ChannelManager
    {
        /// <summary>
        /// Provider 池容量上限。超过时清除所有缓存的 Provider。
        /// 正常使用中 channelId:modelId 组合数远低于此值。
        /// </summary>
        private const int MAX_POOL_SIZE = 64;

        /// <summary>
        /// modelId → 上次成功使用的渠道 ID（路由缓存）
        /// </summary>
        private static readonly Dictionary<string, string> _routeCache = new();

        /// <summary>
        /// "channelId:modelId" → Provider 实例（Provider 池，同一渠道+模型复用同一个 Provider）
        /// </summary>
        private static readonly Dictionary<string, IAIProvider> _providerPool = new();

        /// <summary>
        /// 发送请求，内部处理渠道查找 + 缓存优先 + 故障转移
        /// </summary>
        public static async UniTask<AIResponse> SendAsync(
            AIConfig config, string modelId, AIRequest request, CancellationToken ct)
        {
            var candidates = BuildCandidateChannels(config, modelId);
            if (candidates.Count == 0)
                return AIResponse.Fail($"No channel found for model '{modelId}'.");

            AIResponse lastFailure = null;

            foreach (var channel in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var provider = GetOrCreateProvider(channel, modelId, config.General);

                try
                {
                    var response = await provider.SendAsync(request, ct);
                    if (response.IsSuccess)
                    {
                        _routeCache[modelId] = channel.Id;
                        return response;
                    }

                    lastFailure = response;
                    AILogger.Warning(
                        $"ChannelManager: channel '{channel.Name}' failed: {response.Error}, trying next...");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    lastFailure = AIResponse.Fail(e.Message);
                    AILogger.Warning(
                        $"ChannelManager: channel '{channel.Name}' exception: {e.Message}, trying next...");
                }
            }

            return lastFailure ?? AIResponse.Fail($"All channels failed for model '{modelId}'.");
        }

        /// <summary>
        /// 流式发送，支持缓存优先 + 故障转移（仅在 yield 数据前可切换）
        /// </summary>
        public static IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(
            AIConfig config, string modelId, AIRequest request, CancellationToken ct)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var candidates = BuildCandidateChannels(config, modelId);
                if (candidates.Count == 0)
                {
                    await writer.YieldAsync(new AIStreamChunk
                    {
                        IsComplete = true,
                        Error = $"No channel found for model '{modelId}'."
                    });
                    return;
                }

                string lastError = null;

                foreach (var channel in candidates)
                {
                    linkedToken.ThrowIfCancellationRequested();

                    var provider = GetOrCreateProvider(channel, modelId, config.General);
                    bool hasYielded = false;

                    try
                    {
                        await foreach (var chunk in provider.StreamAsync(request, linkedToken))
                        {
                            if (!hasYielded)
                            {
                                hasYielded = true;
                                _routeCache[modelId] = channel.Id;
                            }

                            await writer.YieldAsync(chunk);
                        }

                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        lastError = e.Message;

                        if (hasYielded)
                        {
                            AILogger.Error(
                                $"ChannelManager stream: channel '{channel.Name}' failed mid-stream: {e.Message}");
                            await writer.YieldAsync(new AIStreamChunk
                            {
                                IsComplete = true,
                                Error = $"Channel '{channel.Name}' failed mid-stream: {e.Message}"
                            });
                            return;
                        }

                        AILogger.Warning(
                            $"ChannelManager stream: channel '{channel.Name}' failed before streaming: {e.Message}, trying next...");
                    }
                }

                await writer.YieldAsync(new AIStreamChunk
                {
                    IsComplete = true,
                    Error = $"All channels failed. Last error: {lastError ?? "unknown"}"
                });
            });
        }

        /// <summary>
        /// 从 ChannelEntry 创建 IAIProvider
        /// </summary>
        internal static IAIProvider CreateProvider(ChannelEntry entry, string modelId, GeneralConfig general)
        {
            var providerConfig = new ProviderBase.ProviderConfig
            {
                ApiKey = entry.GetEffectiveApiKey(),
                BaseUrl = entry.BaseUrl,
                Model = modelId ?? entry.DefaultModel,
                TimeoutSeconds = general.TimeoutSeconds,
                ApiVersion = entry.ApiVersion ?? "2023-06-01"
            };

            return entry.Protocol switch
            {
                ProviderProtocol.Claude => new Providers.Claude.ClaudeProvider(providerConfig),
                ProviderProtocol.OpenAI => new Providers.OpenAI.OpenAIProvider(providerConfig),
                _ => throw new NotSupportedException(
                    $"Protocol '{entry.Protocol}' is not supported.")
            };
        }

        /// <summary>
        /// 清除指定模型的路由缓存
        /// </summary>
        public static void Invalidate(string modelId)
        {
            _routeCache.Remove(modelId);
        }

        /// <summary>
        /// 清除所有缓存（路由缓存 + Provider 池）
        /// </summary>
        public static void InvalidateAll()
        {
            _routeCache.Clear();
            _providerPool.Clear();
        }

        /// <summary>
        /// 创建一个立即返回错误的流式枚举
        /// </summary>
        internal static IUniTaskAsyncEnumerable<AIStreamChunk> ErrorStream(string error)
        {
            return UniTaskAsyncEnumerable.Create<AIStreamChunk>(async (writer, _) =>
            {
                await writer.YieldAsync(new AIStreamChunk { IsComplete = true, Error = error });
            });
        }

        /// <summary>
        /// 获取或创建 Provider（同一 channelId + modelId 复用同一实例）
        /// </summary>
        private static IAIProvider GetOrCreateProvider(ChannelEntry channel, string modelId, GeneralConfig general)
        {
            string poolKey = $"{channel.Id}:{modelId}";
            if (_providerPool.TryGetValue(poolKey, out var existing))
                return existing;

            // 超过容量上限时清空池，避免无限增长
            if (_providerPool.Count >= MAX_POOL_SIZE)
            {
                AILogger.Info($"ChannelManager: provider pool reached {MAX_POOL_SIZE}, clearing.");
                _providerPool.Clear();
            }

            general ??= new GeneralConfig();
            var provider = CreateProvider(channel, modelId, general);
            _providerPool[poolKey] = provider;
            return provider;
        }

        /// <summary>
        /// 在 config 中按 ID 查找渠道
        /// </summary>
        private static ChannelEntry FindChannel(AIConfig config, string channelId)
        {
            foreach (var channel in config.ChannelEntries)
            {
                if (channel.Id == channelId)
                    return channel;
            }
            return null;
        }

        /// <summary>
        /// 构建候选渠道列表：缓存的渠道优先，然后其余渠道（不创建 Provider）
        /// </summary>
        private static List<ChannelEntry> BuildCandidateChannels(AIConfig config, string modelId)
        {
            var result = new List<ChannelEntry>();

            // 缓存优先
            _routeCache.TryGetValue(modelId, out string cachedChannelId);
            var cachedChannel = FindChannel(config, cachedChannelId);
            if (cachedChannel != null && cachedChannel.IsValid(modelId))
                result.Add(cachedChannel);
            else
                _routeCache.Remove(modelId);

            // 其余渠道
            var channels = config.FindChannelsForModel(modelId);
            foreach (var channel in channels)
            {
                if (channel.Id == cachedChannelId) continue;
                if (string.IsNullOrEmpty(channel.GetEffectiveApiKey())) continue;
                result.Add(channel);
            }

            return result;
        }
    }
}
