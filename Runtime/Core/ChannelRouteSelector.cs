using System.Collections.Concurrent;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 为模型选择候选渠道，并记录上一次成功的路由。
    /// </summary>
    internal sealed class ChannelRouteSelector
    {
        private readonly ConcurrentDictionary<string, string> _routeCache = new();

        public List<ChannelEntry> BuildCandidates(AIConfig config, string modelId)
        {
            var result = new List<ChannelEntry>();
            if (config == null)
                return result;

            _routeCache.TryGetValue(modelId, out var cachedChannelId);
            var cachedChannel = FindChannel(config, cachedChannelId);
            if (cachedChannel != null && cachedChannel.IsValid(modelId))
                result.Add(cachedChannel);
            else if (cachedChannelId != null)
                _routeCache.TryRemove(modelId, out _);

            foreach (var channel in config.FindChannelsForModel(modelId))
            {
                if (channel.Id == cachedChannelId)
                    continue;

                result.Add(channel);
            }

            return result;
        }

        public void MarkSuccess(string modelId, ChannelEntry channel)
        {
            if (!string.IsNullOrEmpty(modelId) && channel != null)
                _routeCache[modelId] = channel.Id;
        }

        public void Invalidate(string modelId)
        {
            if (!string.IsNullOrEmpty(modelId))
                _routeCache.TryRemove(modelId, out _);
        }

        public void Clear()
        {
            _routeCache.Clear();
        }

        private static ChannelEntry FindChannel(AIConfig config, string channelId)
        {
            if (string.IsNullOrEmpty(channelId) || config?.ChannelEntries == null)
                return null;

            foreach (var channel in config.ChannelEntries)
            {
                if (channel.Id == channelId)
                    return channel;
            }

            return null;
        }
    }
}
