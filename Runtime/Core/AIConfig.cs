using System;
using System.Collections.Generic;

namespace UniAI
{
    
    public enum AILogLevel
    {
        Verbose,
        Info,
        Warning,
        Error,
        None
    }

    /// <summary>
    /// AI 框架配置
    /// </summary>
    [Serializable]
    public sealed class AIConfig
    {
        /// <summary>
        /// 渠道列表（支持动态增删）
        /// </summary>
        public List<ChannelEntry> ChannelEntries = new();

        /// <summary>
        /// 当前激活的渠道 Id
        /// </summary>
        public string ActiveChannelId;

        /// <summary>
        /// 通用设置
        /// </summary>
        public GeneralConfig General = new();

        /// <summary>
        /// 获取当前激活的渠道，找不到则返回第一个启用的
        /// </summary>
        public ChannelEntry GetActiveChannel()
        {
            if (ChannelEntries.Count == 0) return null;
            var active = ChannelEntries.Find(p => p.Id == ActiveChannelId && p.Enabled);
            return active ?? ChannelEntries.Find(p => p.Enabled) ?? ChannelEntries[0];
        }

        /// <summary>
        /// 获取所有可用模型（扁平化、去重）
        /// </summary>
        public List<string> GetAllModels()
        {
            var result = new List<string>();
            var seen = new HashSet<string>();

            foreach (var channel in ChannelEntries)
            {
                if (!channel.Enabled) continue;
                if (channel.Models == null) continue;
                foreach (var modelId in channel.Models)
                {
                    if (string.IsNullOrEmpty(modelId)) continue;
                    if (seen.Add(modelId))
                        result.Add(modelId);
                }
            }

            return result;
        }

        /// <summary>
        /// 查找所有支持指定模型的渠道（按列表顺序 = 优先级）
        /// </summary>
        public List<ChannelEntry> FindChannelsForModel(string modelId)
        {
            var result = new List<ChannelEntry>();
            if (string.IsNullOrEmpty(modelId)) return result;

            foreach (var channel in ChannelEntries)
            {
                if (!channel.Enabled) continue;
                if (channel.Models != null && channel.Models.Contains(modelId))
                    result.Add(channel);
            }
            return result;
        }
    }
    

    [Serializable]
    public class GeneralConfig
    {
        public int TimeoutSeconds = 60;
        public AILogLevel LogLevel = AILogLevel.Info;

        /// <summary>
        /// 上下文窗口管理配置
        /// </summary>
        public ContextWindowConfig ContextWindow = new();

        /// <summary>
        /// MCP 相关配置
        /// </summary>
        public McpRuntimeConfig Mcp = new();
    }

    /// <summary>
    /// MCP 运行时配置（初始化 + 调用超时等，区别于 McpServerConfig 资产）
    /// </summary>
    [Serializable]
    public class McpRuntimeConfig
    {
        /// <summary>
        /// MCP Server 初始化超时（connect + initialize + tools/list 全流程，秒）
        /// </summary>
        public int InitTimeoutSeconds = 30;

        /// <summary>
        /// 单次 MCP Tool 调用超时（秒），0 = 不限制
        /// </summary>
        public int ToolCallTimeoutSeconds = 60;
    }
}
