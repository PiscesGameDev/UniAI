using System;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// 模型定义 — 描述一个 AI 模型的元信息与能力。
    /// Capabilities 描述模型能做什么（可多选），Endpoint 决定走哪个 API 接口（唯一）。
    /// </summary>
    [Serializable]
    public class ModelEntry
    {
        /// <summary>模型唯一标识（如 "dall-e-3"、"gpt-4o"）</summary>
        public string Id;

        /// <summary>所属厂商（如 "OpenAI"、"Google"、"Anthropic"）</summary>
        public string Vendor;

        /// <summary>模型能力集 — 描述模型能做什么（Flags，可组合）</summary>
        public ModelCapability Capabilities;

        /// <summary>API 端点类型 — 决定走哪个接口（唯一）</summary>
        public ModelEndpoint Endpoint;

        /// <summary>模型简介（用于 UI 悬浮提示，如"多模态全能型"、"专用图片生成"）</summary>
        public string Description;

        /// <summary>模型图标（可选，用于 UI 展示）</summary>
        public Texture2D Icon;

        /// <summary>上下文窗口大小（tokens），0 表示由 ModelRegistry 前缀表兜底</summary>
        public int ContextWindow;

        /// <summary>Provider 内部使用的方言/适配器标识。为空时走协议默认适配。</summary>
        public string AdapterId;

        /// <summary>模型行为标志，用于选择 Provider 方言和参数兼容策略。</summary>
        public ModelBehavior Behavior;

        public ModelEntry() { }

        public ModelEntry(string id, string vendor,
            ModelCapability capabilities, ModelEndpoint endpoint,
            string description = null, int contextWindow = 0,
            string adapterId = null, ModelBehavior behavior = ModelBehavior.None)
        {
            Id = id;
            Vendor = vendor;
            Capabilities = capabilities;
            Endpoint = endpoint;
            Description = description;
            ContextWindow = contextWindow;
            AdapterId = adapterId;
            Behavior = behavior;
        }

        /// <summary>判断模型是否支持指定能力</summary>
        public bool HasCapability(ModelCapability cap) => (Capabilities & cap) != 0;
    }
}
