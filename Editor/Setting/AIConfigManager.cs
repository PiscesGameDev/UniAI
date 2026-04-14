using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// AI 配置管理器
    /// 读写 UniAISettings SO
    /// </summary>
    public static class AIConfigManager
    {
        private const string SettingsAssetPath = "Assets/Resources/UniAI/UniAISettings.asset";

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

            // 应用日志级别
            AILogger.LogLevel = config.General.LogLevel;

            // 环境变量覆盖 API Key
            foreach (var provider in config.ChannelEntries)
            {
                var effectiveKey = provider.GetEffectiveApiKey();
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
            foreach (var provider in config.ChannelEntries)
            {
                if (provider.IsApiKeyFromEnv())
                    provider.ApiKey = null;
            }

            // 同步数据到 SO（跳过同引用场景，避免 Clear 清空自身）
            if (settings.ChannelEntries != config.ChannelEntries)
            {
                settings.ChannelEntries.Clear();
                settings.ChannelEntries.AddRange(config.ChannelEntries);
            }
            if (settings.General != config.General)
            {
                settings.General.TimeoutSeconds = config.General.TimeoutSeconds;
                settings.General.LogLevel = config.General.LogLevel;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            // 应用日志级别
            AILogger.LogLevel = config.General.LogLevel;

            // 恢复环境变量覆盖的 Key（内存中保持有效值）
            foreach (var provider in config.ChannelEntries)
            {
                var effectiveKey = provider.GetEffectiveApiKey();
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

        // ─── SO 管理 ───

        private static UniAISettings LoadOrCreateSettings()
        {
            // 从 AssetDatabase 加载
            var settings = AssetDatabase.LoadAssetAtPath<UniAISettings>(SettingsAssetPath);
            if (settings != null) return settings;

            // 创建默认
            return CreateDefaultSettings();
        }

        private static UniAISettings CreateDefaultSettings()
        {
            var settings = ScriptableObject.CreateInstance<UniAISettings>();
            settings.ChannelEntries.AddRange(ChannelEntry.CreateDefaults());

            EnsureResourcesDir();
            AssetDatabase.CreateAsset(settings, SettingsAssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[UniAI] Created default UniAISettings asset.");
            return settings;
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
