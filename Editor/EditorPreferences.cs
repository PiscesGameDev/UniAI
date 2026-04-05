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
