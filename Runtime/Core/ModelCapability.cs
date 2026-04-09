namespace UniAI
{
    /// <summary>
    /// 模型能力类型 — 决定调用哪个 API 端点
    /// </summary>
    public enum ModelCapability
    {
        /// <summary>聊天/对话 → /chat/completions</summary>
        Chat,

        /// <summary>图片生成 → /images/generations</summary>
        ImageGen,

        /// <summary>音频生成 → /audio/generations（预留）</summary>
        AudioGen,

        /// <summary>视频生成 → /video/generations（预留）</summary>
        VideoGen
    }
}
