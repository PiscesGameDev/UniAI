using System;

namespace UniAI
{
    /// <summary>
    /// Model capability flags. These describe what a model can do.
    /// </summary>
    [Flags]
    public enum ModelCapability
    {
        None = 0,
        Chat = 1 << 0,
        VisionInput = 1 << 1,
        ImageGen = 1 << 2,
        ImageEdit = 1 << 3,
        AudioGen = 1 << 4,
        VideoGen = 1 << 5,
        Embedding = 1 << 6,
        Rerank = 1 << 7,
    }

    /// <summary>
    /// Framework-known behavior flags for model/provider compatibility.
    /// User-defined behavior should use ModelEntry.BehaviorTags/BehaviorOptions.
    /// </summary>
    [Flags]
    public enum ModelBehavior
    {
        None = 0,

        /// <summary>Responses can include provider-native reasoning content.</summary>
        EmitsReasoningContent = 1 << 0,

        /// <summary>Assistant tool-call history must replay provider-native reasoning content.</summary>
        RequiresReasoningReplayForToolCalls = 1 << 1,

        /// <summary>Thinking/reasoning mode is enabled by default.</summary>
        ThinkingDefaultEnabled = 1 << 2,

        /// <summary>Sampling parameters such as temperature should be omitted in thinking mode.</summary>
        IgnoresTemperatureInThinking = 1 << 3,

        /// <summary>The model does not support streaming.</summary>
        NoStreaming = 1 << 4,

        /// <summary>The model does not support function/tool calling.</summary>
        NoFunctionCalling = 1 << 5,

        /// <summary>Image edit requests must be sent as multipart/form-data.</summary>
        RequiresMultipartForImageEdit = 1 << 6
    }

    /// <summary>
    /// Default API endpoint family for a model.
    /// </summary>
    public enum ModelEndpoint
    {
        ChatCompletions,
        Embeddings,
        ImageGenerations,
        ImageEdits,
        AudioGenerations,
        VideoGenerations,
        Rerank,
    }
}
