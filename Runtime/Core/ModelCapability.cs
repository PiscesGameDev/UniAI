using System;

namespace UniAI
{
    /// <summary>
    /// 模型能力标志 — 描述模型能做什么（可组合）。
    /// 用于 UI 筛选、能力校验，与 API 端点路由无关。
    /// </summary>
    [Flags]
    public enum ModelCapability
    {
        None      = 0,

        /// <summary>聊天/对话</summary>
        Chat      = 1 << 0,

        /// <summary>从文本生成图片（Text-to-Image）</summary>
        ImageGen  = 1 << 1,

        /// <summary>图片编辑（Inpainting / 扩图 / 局部重绘，需传入底图）</summary>
        ImageEdit = 1 << 2,

        /// <summary>音频生成（预留）</summary>
        AudioGen  = 1 << 3,

        /// <summary>视频生成（预留）</summary>
        VideoGen  = 1 << 4,
    }

    /// <summary>
    /// 模型 API 端点类型 — 每个模型唯一，决定调用哪个接口。
    /// </summary>
    public enum ModelEndpoint
    {
        /// <summary>/chat/completions</summary>
        ChatCompletions,

        /// <summary>/images/generations</summary>
        ImageGenerations,

        /// <summary>/images/edits（预留）</summary>
        ImageEdits,

        /// <summary>/audio/generations（预留）</summary>
        AudioGenerations,

        /// <summary>/video/generations（预留）</summary>
        VideoGenerations,
    }
}
