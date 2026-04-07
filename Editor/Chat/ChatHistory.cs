using System.Collections.Generic;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 会话历史持久化管理（编辑器版本）
    /// 存储路径: Library/UniAI/History/{sessionId}.json
    /// </summary>
    public class ChatHistory
    {
        private readonly ChatHistoryManager _manager;

        public ChatHistory()
        {
            _manager = new ChatHistoryManager(new EditorChatHistoryStorage());
        }

        public IReadOnlyList<ChatSession> Sessions => _manager.Sessions;

        public string SearchFilter
        {
            get => _manager.SearchFilter;
            set => _manager.SearchFilter = value;
        }

        public void Load() => _manager.Load();

        public void Save(ChatSession session) => _manager.Save(session);

        public void Delete(string sessionId) => _manager.Delete(sessionId);

        public void DeleteAll() => _manager.DeleteAll();

        public List<(string Group, List<ChatSession> Items)> GetGroupedSessions()
            => _manager.GetGroupedSessions();
    }
}
