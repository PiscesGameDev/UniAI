using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 编辑器聊天历史存储实现 — 基于文件系统
    /// 存储路径: Library/UniAI/History/{sessionId}.json
    /// </summary>
    public class EditorChatHistoryStorage : IChatHistoryStorage
    {
        private const string HistoryDir = "Library/UniAI/History";

        public List<ChatSession> LoadAll()
        {
            var sessions = new List<ChatSession>();

            if (!Directory.Exists(HistoryDir))
                return sessions;

            foreach (var file in Directory.GetFiles(HistoryDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var session = JsonConvert.DeserializeObject<ChatSession>(json);
                    if (session != null)
                        sessions.Add(session);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UniAI] Failed to load session {file}: {e.Message}");
                }
            }

            return sessions;
        }

        public void Save(ChatSession session)
        {
            if (session == null) return;

            if (!Directory.Exists(HistoryDir))
                Directory.CreateDirectory(HistoryDir);

            string path = GetPath(session.Id);
            string json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(path, json);

            EnforceLimit();
        }

        public void Delete(string sessionId)
        {
            string path = GetPath(sessionId);
            if (File.Exists(path))
                File.Delete(path);
        }

        public void DeleteAll()
        {
            if (!Directory.Exists(HistoryDir))
                return;

            foreach (var file in Directory.GetFiles(HistoryDir, "*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UniAI] Failed to delete session {file}: {e.Message}");
                }
            }
        }

        private void EnforceLimit()
        {
            if (!Directory.Exists(HistoryDir))
                return;

            var files = Directory.GetFiles(HistoryDir, "*.json");
            int maxSessions = AIConfigManager.Prefs.MaxHistorySessions;

            if (files.Length <= maxSessions)
                return;

            // 按修改时间排序，删除最旧的
            var fileInfos = new List<FileInfo>();
            foreach (var file in files)
                fileInfos.Add(new FileInfo(file));

            fileInfos.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

            for (int i = maxSessions; i < fileInfos.Count; i++)
            {
                try
                {
                    fileInfos[i].Delete();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[UniAI] Failed to delete old session {fileInfos[i].Name}: {e.Message}");
                }
            }
        }

        private static string GetPath(string sessionId) => $"{HistoryDir}/{sessionId}.json";
    }
}
