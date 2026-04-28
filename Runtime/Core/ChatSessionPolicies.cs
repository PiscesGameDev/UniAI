using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    public interface IChatSessionPersistence
    {
        void Save(ChatSession session);
    }

    public sealed class NullChatSessionPersistence : IChatSessionPersistence
    {
        public static readonly NullChatSessionPersistence Instance = new();

        private NullChatSessionPersistence() { }

        public void Save(ChatSession session) { }
    }

    public sealed class ChatHistorySessionPersistence : IChatSessionPersistence
    {
        private readonly ChatHistoryManager _history;

        public ChatHistorySessionPersistence(ChatHistoryManager history)
        {
            _history = history;
        }

        public void Save(ChatSession session)
        {
            _history?.Save(session);
        }
    }

    public interface IChatTitlePolicy
    {
        bool ShouldGenerateTitle(ChatSession session);
        UniTask GenerateTitleAsync(ChatSession session, CancellationToken ct = default);
    }

    public sealed class NullChatTitlePolicy : IChatTitlePolicy
    {
        public static readonly NullChatTitlePolicy Instance = new();

        private NullChatTitlePolicy() { }

        public bool ShouldGenerateTitle(ChatSession session) => false;

        public UniTask GenerateTitleAsync(ChatSession session, CancellationToken ct = default)
        {
            return UniTask.CompletedTask;
        }
    }

    public sealed class FirstUserMessageTitlePolicy : IChatTitlePolicy
    {
        private readonly int _maxLength;
        private readonly string _defaultTitle;

        public FirstUserMessageTitlePolicy(int maxLength = 15, string defaultTitle = "新对话")
        {
            _maxLength = maxLength;
            _defaultTitle = defaultTitle;
        }

        public bool ShouldGenerateTitle(ChatSession session)
        {
            return session != null
                   && session.Messages.Count > 0
                   && (string.IsNullOrEmpty(session.Title) || session.Title == _defaultTitle);
        }

        public UniTask GenerateTitleAsync(ChatSession session, CancellationToken ct = default)
        {
            if (session == null)
                return UniTask.CompletedTask;

            ct.ThrowIfCancellationRequested();

            var userText = FindFirstUserMessage(session);
            if (string.IsNullOrEmpty(userText))
                return UniTask.CompletedTask;

            session.Title = userText.Length <= _maxLength
                ? userText
                : userText.Substring(0, _maxLength) + "…";

            return UniTask.CompletedTask;
        }

        private static string FindFirstUserMessage(ChatSession session)
        {
            foreach (var msg in session.Messages)
            {
                if (!msg.IsToolCall && msg.Role == AIRole.User)
                    return msg.Content;
            }

            return null;
        }
    }
}
