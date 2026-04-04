using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// AI 配置管理器
    /// 读写 UniAISettings SO + EditorPreferences，支持从旧 JSON 格式迁移
    /// </summary>
    public static class AIConfigManager
    {
        private const string SettingsAssetPath = "Assets/Resources/UniAI/UniAISettings.asset";
        private const string OldSettingsPath = "UserSettings/UniAISettings.json";
        private const string EditorPrefsKey = "UniAI_Config";

        /// <summary>
        /// 编辑器偏好（ScriptableSingleton）
        /// </summary>
        internal static EditorPreferences Prefs => EditorPreferences.instance;

        /// <summary>
        /// 加载配置（从 SO 读取，环境变量覆盖 API Key）
        /// </summary>
        public static AIConfig LoadConfig()
        {
            var settings = LoadOrCreateSettings();
            var config = settings.ToConfig();

            // 环境变量覆盖 API Key
            foreach (var provider in config.Providers)
            {
                var effectiveKey = EditorPreferences.GetEffectiveApiKey(provider);
                if (effectiveKey != provider.ApiKey)
                    provider.ApiKey = effectiveKey;
            }

            return config;
        }

        /// <summary>
        /// 保存配置到 SO（环境变量来源的 Key 不写入资产）
        /// </summary>
        public static void SaveConfig(AIConfig config)
        {
            var settings = LoadOrCreateSettings();

            // 清除来自环境变量的 Key，避免写入资产
            foreach (var provider in config.Providers)
            {
                if (EditorPreferences.IsApiKeyFromEnv(provider))
                    provider.ApiKey = null;
            }

            // 同步数据到 SO（跳过同引用场景，避免 Clear 清空自身）
            if (settings.Providers != config.Providers)
            {
                settings.Providers.Clear();
                settings.Providers.AddRange(config.Providers);
            }
            if (settings.General != config.General)
            {
                settings.General.TimeoutSeconds = config.General.TimeoutSeconds;
                settings.General.LogLevel = config.General.LogLevel;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            // 恢复环境变量覆盖的 Key（内存中保持有效值）
            foreach (var provider in config.Providers)
            {
                var effectiveKey = EditorPreferences.GetEffectiveApiKey(provider);
                if (effectiveKey != provider.ApiKey)
                    provider.ApiKey = effectiveKey;
            }

            // 同步保存编辑器偏好
            Prefs.SaveToFile();
        }

        /// <summary>
        /// 保存编辑器偏好
        /// </summary>
        internal static void SavePrefs()
        {
            Prefs.SaveToFile();
        }

        /// <summary>
        /// 获取有效的 API Key（环境变量优先）
        /// </summary>
        public static string GetEffectiveApiKey(ChannelEntry entry)
        {
            return EditorPreferences.GetEffectiveApiKey(entry);
        }

        /// <summary>
        /// 当前 ApiKey 是否来自环境变量
        /// </summary>
        public static bool IsApiKeyFromEnv(ChannelEntry entry)
        {
            return EditorPreferences.IsApiKeyFromEnv(entry);
        }

        // ─── SO 管理 ───

        private static UniAISettings LoadOrCreateSettings()
        {
            // 优先从 AssetDatabase 加载
            var settings = AssetDatabase.LoadAssetAtPath<UniAISettings>(SettingsAssetPath);
            if (settings != null) return settings;

            // 尝试从旧 JSON 迁移
            settings = TryMigrateFromJson();
            if (settings != null) return settings;

            // 创建默认
            return CreateDefaultSettings();
        }

        private static UniAISettings CreateDefaultSettings()
        {
            var settings = ScriptableObject.CreateInstance<UniAISettings>();
            settings.Providers.AddRange(ChannelPresets.CreateDefaults());

            EnsureResourcesDir();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[UniAI] Created default UniAISettings asset.");
            return settings;
        }

        // ─── 旧 JSON 迁移 ───

        private static UniAISettings TryMigrateFromJson()
        {
            // 尝试从旧 JSON 文件读取
            AIConfig oldConfig = null;

            if (File.Exists(OldSettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(OldSettingsPath);
                    oldConfig = JsonConvert.DeserializeObject<AIConfig>(json);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UniAI] Failed to read old config: {e.Message}");
                }
            }

            // 也检查 EditorPrefs 备份
            if (oldConfig == null)
            {
                var prefsJson = EditorPrefs.GetString(EditorPrefsKey, "");
                if (!string.IsNullOrEmpty(prefsJson))
                {
                    try { oldConfig = JsonConvert.DeserializeObject<AIConfig>(prefsJson); } catch { }
                }
            }

            if (oldConfig == null || oldConfig.Providers.Count == 0)
                return null;

            // 迁移 Models（旧 Model → Models）
            MigrateModelsFromJson(oldConfig);

            // 创建 SO
            var settings = ScriptableObject.CreateInstance<UniAISettings>();
            settings.Providers.AddRange(oldConfig.Providers);
            settings.General.TimeoutSeconds = oldConfig.General.TimeoutSeconds;
            settings.General.LogLevel = oldConfig.General.LogLevel;

            EnsureResourcesDir();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[UniAI] Migrated config from JSON to ScriptableObject.");
            return settings;
        }

        private static void MigrateModelsFromJson(AIConfig config)
        {
            if (!File.Exists(OldSettingsPath)) return;

            try
            {
                var rawJson = File.ReadAllText(OldSettingsPath);
                var root = JObject.Parse(rawJson);
                var providersArray = root["Providers"] as JArray;
                if (providersArray == null) return;

                for (int i = 0; i < providersArray.Count && i < config.Providers.Count; i++)
                {
                    var providerJson = providersArray[i] as JObject;
                    var provider = config.Providers[i];

                    if (provider.Models.Count > 0) continue;

                    var oldModel = providerJson?["Model"]?.ToString();
                    if (!string.IsNullOrEmpty(oldModel))
                        provider.Models = new List<string> { oldModel };
                }
            }
            catch { /* ignore migration errors */ }
        }

        private static void EnsureResourcesDir()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/UniAI"))
                AssetDatabase.CreateFolder("Assets/Resources", "UniAI");
        }
    }
}
