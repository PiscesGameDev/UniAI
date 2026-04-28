using System;

namespace UniAI
{
    public sealed class ChatOrchestratorDependencies
    {
        public IConversationContextProvider ContextProvider;
        public IChatSessionPersistence Persistence;
        public IChatTitlePolicy TitlePolicy;
        public Func<IDisposable> ToolExecutionGuardFactory;
    }

    public sealed class ChatStreamRequest
    {
        public ChatSession Session;
        public int ContextSlots;
        public AIConfig Config;
        public string ModelId;
    }
}
