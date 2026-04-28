namespace UniAI
{
    /// <summary>
    /// Binds an adapter descriptor to the factory instance that creates runtime handlers.
    /// </summary>
    public sealed class AdapterRegistration<TFactory>
    {
        public AdapterDescriptor Descriptor { get; }
        public TFactory Factory { get; }

        public AdapterRegistration(AdapterDescriptor descriptor, TFactory factory)
        {
            Descriptor = descriptor;
            Factory = factory;
        }
    }
}
