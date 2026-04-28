namespace UniAI
{
    /// <summary>
    /// The extension point an adapter plugs into.
    /// </summary>
    public enum AdapterTarget
    {
        OpenAIChatDialect,
        OpenAIImageDialect,
        EmbeddingProvider,
        RerankProvider,
        AudioGenerationProvider,
        VideoGenerationProvider
    }
}
