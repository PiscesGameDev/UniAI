using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// AI 配置管理器
    /// 优先级: 环境变量 > UserSettings JSON > EditorPrefs
    /// </summary>
    public static class AIConfigManager
    {
        private const string SettingsPath = "UserSettings/UniAISettings.json";
        private const string EditorPrefsKey = "UniAI_Config";

        /// <summary>
        /// 加载配置（按优先级合并）
        /// </summary>
        public static AIConfig LoadConfig()
        {
            var config = LoadFromFile() ?? LoadFromEditorPrefs() ?? new AIConfig();

            // 首次使用：填充默认 Providers
            if (config.Providers.Count == 0)
            {
                config.Providers = ProviderPresets.CreateDefaults();
                config.ActiveProviderId = "claude";
            }

            // 环境变量覆盖 API Key
            foreach (var provider in config.Providers)
            {
                var effectiveKey = provider.GetEffectiveApiKey();
                if (effectiveKey != provider.ApiKey)
                    provider.ApiKey = effectiveKey;
            }

            return config;
        }

        /// <summary>
        /// 保存配置到 UserSettings 文件（环境变量来源的 Key 不写入文件）
        /// </summary>
        public static void SaveConfig(AIConfig config)
        {
            // 保存前清除来自环境变量的 Key，避免写入文件
            var clone = JsonConvert.DeserializeObject<AIConfig>(JsonConvert.SerializeObject(config));
            foreach (var provider in clone.Providers)
            {
                if (provider.IsApiKeyFromEnv())
                    provider.ApiKey = null;
            }

            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(clone, Formatting.Indented);
            File.WriteAllText(SettingsPath, json);

            EditorPrefs.SetString(EditorPrefsKey, json);
        }

        private static AIConfig LoadFromFile()
        {
            if (!File.Exists(SettingsPath)) return null;

            try
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<AIConfig>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI] Failed to load config from {SettingsPath}: {e.Message}");
                return null;
            }
        }

        private static AIConfig LoadFromEditorPrefs()
        {
            var json = EditorPrefs.GetString(EditorPrefsKey, "");
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonConvert.DeserializeObject<AIConfig>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
