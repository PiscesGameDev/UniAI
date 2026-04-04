using System.Collections.Generic;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// UniAI 运行时配置 — ScriptableObject
    /// 在 Project 中创建: Create > UniAI > Settings
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Settings", fileName = "UniAISettings")]
    public class UniAISettings : ScriptableObject
    {
        [SerializeField] private List<ChannelEntry> _providers = new();
        [SerializeField] private GeneralConfig _general = new();

        /// <summary>
        /// 渠道列表
        /// </summary>
        public List<ChannelEntry> Providers => _providers;

        /// <summary>
        /// 通用设置
        /// </summary>
        public GeneralConfig General => _general;

        /// <summary>
        /// 转换为 AIConfig（供 AIClient.Create 使用）
        /// </summary>
        public AIConfig ToConfig() => new()
        {
            Providers = _providers,
            General = _general
        };

        // ─── 单例访问 ───

        private static UniAISettings _instance;

        /// <summary>
        /// 全局单例（从 Resources/UniAI/ 加载）
        /// </summary>
        public static UniAISettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<UniAISettings>("UniAI/UniAISettings");
                return _instance;
            }
        }

        /// <summary>
        /// 手动设置单例（用于非 Resources 场景，如 Addressables）
        /// </summary>
        public static void SetInstance(UniAISettings settings)
        {
            _instance = settings;
        }
    }
}
