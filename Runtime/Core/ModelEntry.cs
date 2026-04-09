using System;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// 模型定义 — 描述一个 AI 模型的元信息与能力。
    /// 模型决定能力（聊天/生图/生音频等），渠道只负责路由。
    /// </summary>
    [Serializable]
    public class ModelEntry
    {
        /// <summary>模型唯一标识（如 "dall-e-3"、"gpt-4o"）</summary>
        public string Id;

        /// <summary>展示名称（如 "DALL·E 3"）</summary>
        public string DisplayName;

        /// <summary>所属厂商（如 "OpenAI"、"Google"、"Anthropic"）</summary>
        public string Vendor;

        /// <summary>模型能力 — 决定走哪个 API 端点</summary>
        public ModelCapability Capability;

        /// <summary>模型图标（可选，用于 UI 展示）</summary>
        public Texture2D Icon;

        public ModelEntry() { }

        public ModelEntry(string id, string displayName, string vendor, ModelCapability capability)
        {
            Id = id;
            DisplayName = displayName;
            Vendor = vendor;
            Capability = capability;
        }
    }
}
