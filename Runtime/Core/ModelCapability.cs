using System;

namespace UniAI
{
    /// <summary>
    /// 模型能力标志，用于描述模型具备的功能。
    /// </summary>
    [Flags]
    public enum ModelCapability
    {
        None = 0,

        /// <summary>聊天与文本生成。</summary>
        Chat = 1 << 0,

        /// <summary>支持图片输入，可用于视觉理解或截图分析。</summary>
        VisionInput = 1 << 1,

        /// <summary>根据文本生成图片。</summary>
        ImageGen = 1 << 2,

        /// <summary>编辑图片或执行局部重绘。</summary>
        ImageEdit = 1 << 3,

        /// <summary>生成音频。</summary>
        AudioGen = 1 << 4,

        /// <summary>生成视频。</summary>
        VideoGen = 1 << 5,
        Embedding = 1 << 6,
        Rerank = 1 << 7,
    }

    /// <summary>
    /// 模型行为标志，用于描述同一协议外壳下的模型/厂商方言差异。
    /// </summary>
    [Flags]
    public enum ModelBehavior
    {
        None = 0,

        /// <summary>响应中可能返回 reasoning_content。</summary>
        EmitsReasoningContent = 1 << 0,

        /// <summary>当 assistant 消息包含 tool_calls 时，后续请求必须回放 reasoning_content。</summary>
        RequiresReasoningReplayForToolCalls = 1 << 1,

        /// <summary>模型默认开启 thinking/reasoning 模式。</summary>
        ThinkingDefaultEnabled = 1 << 2,

        /// <summary>thinking 模式下不应发送 temperature 等采样参数。</summary>
        IgnoresTemperatureInThinking = 1 << 3
    }

    /// <summary>
    /// 模型对应的 API 端点类型。
    /// </summary>
    public enum ModelEndpoint
    {
        /// <summary>/chat/completions</summary>
        ChatCompletions,
        Embeddings,

        /// <summary>/images/generations</summary>
        ImageGenerations,

        /// <summary>/images/edits</summary>
        ImageEdits,

        /// <summary>/audio/generations</summary>
        AudioGenerations,

        /// <summary>/video/generations</summary>
        VideoGenerations,
        Rerank,
    }
}
