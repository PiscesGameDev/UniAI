using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// 编辑器偏好设置 — 基于 ScriptableSingleton，自动持久化到 Library/
    /// </summary>
    [FilePath("UniAI/EditorPreferences.asset", FilePathAttribute.Location.PreferencesFolder)]
    internal class EditorPreferences : ScriptableSingleton<EditorPreferences>
    {
        /// <summary>
        /// 上次选择的模型 ID
        /// </summary>
        [SerializeField] private string _lastSelectedModelId;

        /// <summary>
        /// 聊天窗口侧边栏是否展开
        /// </summary>
        [SerializeField] private bool _showSidebar = true;

        /// <summary>
        /// 历史会话上限
        /// </summary>
        [SerializeField] private int _maxHistorySessions = 50;

        /// <summary>
        /// 用户头像自定义
        /// </summary>
        [SerializeField] private Texture2D _userAvatar;

        /// <summary>
        /// AI 头像自定义
        /// </summary>
        [SerializeField] private Texture2D _aiAvatar;

        /// <summary>
        /// Agent 资产默认创建目录
        /// </summary>
        [SerializeField] private string _agentDirectory = "Assets/Agents";

        /// <summary>
        /// Tool 执行超时时间（秒）
        /// </summary>
        [SerializeField] private float _toolTimeout = 30f;

        /// <summary>
        /// Tool 单次返回内容的最大字符数（ReadFile 全文读取等场景）
        /// </summary>
        [SerializeField] private int _toolMaxOutputChars = 50000;

        /// <summary>
        /// SearchFiles 最大匹配数
        /// </summary>
        [SerializeField] private int _searchMaxMatches = 100;

        /// <summary>
        /// 对话窗口默认启用的上下文槽位（ContextSlot 标志位）
        /// </summary>
        [SerializeField] private int _defaultContextSlots = 1; // ContextSlot.Selection

        /// <summary>
        /// 切换 Agent 时是否自动连接 MCP Server
        /// </summary>
        [SerializeField] private bool _mcpAutoConnect = true;

        /// <summary>
        /// 是否将 MCP Resource 自动注入上下文
        /// </summary>
        [SerializeField] private bool _mcpResourceInjection = true;

        /// <summary>
        /// MCP Server 配置资产默认创建目录
        /// </summary>
        [SerializeField] private string _mcpServerDirectory = "Assets/Agents/MCP";

        // ─── 公开属性 ───

        internal string LastSelectedModelId
        {
            get => _lastSelectedModelId;
            set => _lastSelectedModelId = value;
        }

        internal bool ShowSidebar
        {
            get => _showSidebar;
            set => _showSidebar = value;
        }

        internal int MaxHistorySessions
        {
            get => _maxHistorySessions;
            set => _maxHistorySessions = value;
        }

        internal Texture2D UserAvatar
        {
            get => _userAvatar;
            set => _userAvatar = value;
        }

        internal Texture2D AiAvatar
        {
            get => _aiAvatar;
            set => _aiAvatar = value;
        }

        internal string AgentDirectory
        {
            get => string.IsNullOrEmpty(_agentDirectory) ? "Assets/Agents" : _agentDirectory;
            set => _agentDirectory = value;
        }

        internal float ToolTimeout
        {
            get => _toolTimeout > 0 ? _toolTimeout : 30f;
            set => _toolTimeout = value;
        }

        internal int ToolMaxOutputChars
        {
            get => _toolMaxOutputChars > 0 ? _toolMaxOutputChars : 50000;
            set => _toolMaxOutputChars = value;
        }

        internal int SearchMaxMatches
        {
            get => _searchMaxMatches > 0 ? _searchMaxMatches : 100;
            set => _searchMaxMatches = value;
        }

        internal int DefaultContextSlots
        {
            get => _defaultContextSlots;
            set => _defaultContextSlots = value;
        }

        internal bool McpAutoConnect
        {
            get => _mcpAutoConnect;
            set => _mcpAutoConnect = value;
        }

        internal bool McpResourceInjection
        {
            get => _mcpResourceInjection;
            set => _mcpResourceInjection = value;
        }

        internal string McpServerDirectory
        {
            get => string.IsNullOrEmpty(_mcpServerDirectory) ? "Assets/Agents/MCP" : _mcpServerDirectory;
            set => _mcpServerDirectory = value;
        }

        // ─── 环境变量映射（按预设 ID，跟随 AI 供应商） ───

        private static readonly Dictionary<string, string> _presetEnvVars = new()
        {
            { "claude", "ANTHROPIC_API_KEY" },
            { "openai", "OPENAI_API_KEY" },
            { "gemini", "GEMINI_API_KEY" },
            { "deepseek", "DEEPSEEK_API_KEY" }
        };

        /// <summary>
        /// 获取预设渠道对应的环境变量名（非预设渠道返回 null）
        /// </summary>
        internal static string GetEnvVarName(string channelId)
        {
            return _presetEnvVars.GetValueOrDefault(channelId);
        }

        /// <summary>
        /// 获取有效的 API Key（环境变量优先，其次使用配置值）
        /// </summary>
        internal static string GetEffectiveApiKey(ChannelEntry entry)
        {
            var envVarName = GetEnvVarName(entry.Id);
            if (!string.IsNullOrEmpty(envVarName))
            {
                var envKey = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrEmpty(envKey))
                    return envKey;
            }
            return entry.ApiKey;
        }

        /// <summary>
        /// 当前 ApiKey 是否来自环境变量
        /// </summary>
        internal static bool IsApiKeyFromEnv(ChannelEntry entry)
        {
            var envVarName = GetEnvVarName(entry.Id);
            if (string.IsNullOrEmpty(envVarName))
                return false;
            var envKey = Environment.GetEnvironmentVariable(envVarName);
            return !string.IsNullOrEmpty(envKey);
        }

        /// <summary>
        /// 保存到磁盘
        /// </summary>
        internal void SaveToFile()
        {
            Save(true);
        }
    }
}
