using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 会话历史持久化管理
    /// 存储路径: Library/UniAI/History/{sessionId}.json
    /// </summary>
    public class ChatHistory
    {
        private const string HistoryDir = "Library/UniAI/History";

        private readonly List<ChatSession> _sessions = new();
        private string _searchFilter = "";

        public IReadOnlyList<ChatSession> Sessions => _sessions;
        public string SearchFilter
        {
            get => _searchFilter;
            set => _searchFilter = value ?? "";
        }

        public void Load()
        {
            _sessions.Clear();

            if (!Directory.Exists(HistoryDir)) return;

            foreach (var file in Directory.GetFiles(HistoryDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var session = JsonConvert.DeserializeObject<ChatSession>(json);
                    if (session != null)
                        _sessions.Add(session);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UniAI] Failed to load session {file}: {e.Message}");
                }
            }

            SortByUpdated();
        }

        public void Save(ChatSession session)
        {
            session.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!Directory.Exists(HistoryDir))
                Directory.CreateDirectory(HistoryDir);

            string path = GetPath(session.Id);
            string json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(path, json);

            // 确保在内存列表中
            int idx = _sessions.FindIndex(s => s.Id == session.Id);
            if (idx >= 0)
                _sessions[idx] = session;
            else
                _sessions.Add(session);

            SortByUpdated();
            EnforceLimit();
        }

        public void Delete(string sessionId)
        {
            _sessions.RemoveAll(s => s.Id == sessionId);

            string path = GetPath(sessionId);
            if (File.Exists(path))
                File.Delete(path);
        }

        public void DeleteAll()
        {
            foreach (var session in _sessions)
            {
                string path = GetPath(session.Id);
                if (File.Exists(path))
                    File.Delete(path);
            }
            _sessions.Clear();
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

        private void EnforceLimit()
        {
            int maxSessions = AIConfigManager.Prefs.MaxHistorySessions;
            while (_sessions.Count > maxSessions)
            {
                var oldest = _sessions[^1];
                string path = GetPath(oldest.Id);
                if (File.Exists(path))
                    File.Delete(path);
                _sessions.RemoveAt(_sessions.Count - 1);
            }
        }

        private static string GetPath(string sessionId) => $"{HistoryDir}/{sessionId}.json";
    }
}
