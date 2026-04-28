namespace UniAI
{
    /// <summary>
    /// The extension point an adapter plugs into.
    /// </summary>
    public enum AdapterTarget
    {
        ConversationProvider,
        OpenAIChatDialect,
        ImageGenerationProvider,
        OpenAIImageDialect,
        EmbeddingProvider,
        RerankProvider,
        AudioGenerationProvider,
        VideoGenerationProvider
    }
}
