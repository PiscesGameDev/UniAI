using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// 基于文件系统的聊天历史存储实现
    /// </summary>
    public class FileChatHistoryStorage : IChatHistoryStorage
    {
        private readonly string _directory;
        private readonly int _maxSessions;

        public FileChatHistoryStorage(string directory, int maxSessions = 50)
        {
            _directory = directory;
            _maxSessions = maxSessions;
        }

        public List<ChatSession> LoadAll()
        {
            var sessions = new List<ChatSession>();

            if (!Directory.Exists(_directory))
                return sessions;

            foreach (var file in Directory.GetFiles(_directory, "*.json"))
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

            if (!Directory.Exists(_directory))
                Directory.CreateDirectory(_directory);

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
            if (!Directory.Exists(_directory))
                return;

            foreach (var file in Directory.GetFiles(_directory, "*.json"))
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
            if (!Directory.Exists(_directory))
                return;

            var files = Directory.GetFiles(_directory, "*.json");

            if (files.Length <= _maxSessions)
                return;

            // 按修改时间排序，删除最旧的
            var fileInfos = new List<FileInfo>();
            foreach (var file in files)
                fileInfos.Add(new FileInfo(file));

            fileInfos.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

            for (int i = _maxSessions; i < fileInfos.Count; i++)
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

        private string GetPath(string sessionId) => $"{_directory}/{sessionId}.json";
    }
}
