using System;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 聊天历史管理器 — 业务逻辑层，与存储实现解耦
    /// </summary>
    public class ChatHistoryManager
    {
        private readonly IChatHistoryStorage _storage;
        private readonly List<ChatSession> _sessions = new();
        private string _searchFilter = "";

        public ChatHistoryManager(IChatHistoryStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public IReadOnlyList<ChatSession> Sessions => _sessions;

        public string SearchFilter
        {
            get => _searchFilter;
            set => _searchFilter = value ?? "";
        }

        /// <summary>
        /// 从存储加载所有会话
        /// </summary>
        public void Load()
        {
            _sessions.Clear();
            var loaded = _storage.LoadAll();
            if (loaded != null)
                _sessions.AddRange(loaded);
            SortByUpdated();
        }

        /// <summary>
        /// 保存会话到存储
        /// </summary>
        public void Save(ChatSession session)
        {
            if (session == null) return;

            session.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _storage.Save(session);

            // 确保在内存列表中
            int idx = _sessions.FindIndex(s => s.Id == session.Id);
            if (idx >= 0)
                _sessions[idx] = session;
            else
                _sessions.Add(session);

            SortByUpdated();
        }

        /// <summary>
        /// 删除指定会话
        /// </summary>
        public void Delete(string sessionId)
        {
            _sessions.RemoveAll(s => s.Id == sessionId);
            _storage.Delete(sessionId);
        }

        /// <summary>
        /// 删除所有会话
        /// </summary>
        public void DeleteAll()
        {
            _sessions.Clear();
            _storage.DeleteAll();
        }

        /// <summary>
        /// 获取按日期分组、经搜索过滤的会话列表
        /// </summary>
        public List<(string Group, List<ChatSession> Items)> GetGroupedSessions()
        {
            var result = new List<(string, List<ChatSession>)>();
            var today = new List<ChatSession>();
            var yesterday = new List<ChatSession>();
            var earlier = new List<ChatSession>();

            var nowDate = DateTime.Now.Date;
            var yesterdayDate = nowDate.AddDays(-1);
            bool hasFilter = !string.IsNullOrEmpty(_searchFilter);

            foreach (var session in _sessions)
            {
                if (hasFilter && session.Title != null
                    && session.Title.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var date = DateTimeOffset.FromUnixTimeSeconds(session.UpdatedAt).LocalDateTime.Date;
                if (date == nowDate)
                    today.Add(session);
                else if (date == yesterdayDate)
                    yesterday.Add(session);
                else
                    earlier.Add(session);
            }

            if (today.Count > 0) result.Add(("今天", today));
            if (yesterday.Count > 0) result.Add(("昨天", yesterday));
            if (earlier.Count > 0) result.Add(("更早", earlier));

            return result;
        }

        private void SortByUpdated()
        {
            _sessions.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        }
    }
}
