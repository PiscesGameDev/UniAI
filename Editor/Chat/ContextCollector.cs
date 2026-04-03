using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// Unity 上下文采集器 — Selection / Console / Project
    /// </summary>
    public static class ContextCollector
    {
        [Flags]
        public enum ContextSlot
        {
            None = 0,
            Selection = 1 << 0,
            Console = 1 << 1,
            Project = 1 << 2
        }

        /// <summary>
        /// 采集指定槽位的上下文，拼接为 XML 块
        /// </summary>
        public static string Collect(ContextSlot slots)
        {
            if (slots == ContextSlot.None) return null;

            var sb = new StringBuilder();
            sb.AppendLine("<unity-context>");

            if (slots.HasFlag(ContextSlot.Selection))
                AppendSelection(sb);

            if (slots.HasFlag(ContextSlot.Console))
                AppendConsole(sb);

            if (slots.HasFlag(ContextSlot.Project))
                AppendProject(sb);

            sb.AppendLine("</unity-context>");
            return sb.ToString();
        }

        private static void AppendSelection(StringBuilder sb)
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                var obj = Selection.activeObject;
                if (obj != null)
                {
                    sb.AppendLine("[Selection]");
                    sb.AppendLine($"Asset: {AssetDatabase.GetAssetPath(obj)} ({obj.GetType().Name})");
                    return;
                }
                sb.AppendLine("[Selection] (none)");
                return;
            }

            sb.AppendLine("[Selection]");
            sb.AppendLine($"GameObject: {GetHierarchyPath(go)}");
            sb.AppendLine($"Active: {go.activeSelf}, Layer: {LayerMask.LayerToName(go.layer)}");

            var components = go.GetComponents<Component>();
            sb.AppendLine($"Components ({components.Length}):");
            foreach (var comp in components)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                sb.AppendLine($"  - {typeName}");

                // 对 MonoBehaviour 显示脚本路径
                if (comp is MonoBehaviour mb)
                {
                    var script = MonoScript.FromMonoBehaviour(mb);
                    if (script != null)
                        sb.AppendLine($"    Script: {AssetDatabase.GetAssetPath(script)}");
                }
            }
        }

        private static void AppendConsole(StringBuilder sb)
        {
            sb.AppendLine("[Console Errors]");
            var entries = GetConsoleErrors(10);
            if (entries.Count == 0)
            {
                sb.AppendLine("(no errors)");
                return;
            }
            foreach (var entry in entries)
                sb.AppendLine($"  [{entry.Type}] {entry.Message}");
        }

        private static void AppendProject(StringBuilder sb)
        {
            sb.AppendLine("[Project Selection]");

            var guids = Selection.assetGUIDs;
            if (guids == null || guids.Length == 0)
            {
                sb.AppendLine("(no assets selected)");
                return;
            }

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                sb.AppendLine($"  {path}");

                // 如果是脚本文件，读取前 50 行内容
                if (path.EndsWith(".cs"))
                {
                    try
                    {
                        string content = System.IO.File.ReadAllText(path);
                        string[] lines = content.Split('\n');
                        int count = Mathf.Min(lines.Length, 50);
                        sb.AppendLine("  ```csharp");
                        for (int i = 0; i < count; i++)
                            sb.AppendLine($"  {lines[i].TrimEnd()}");
                        if (lines.Length > 50)
                            sb.AppendLine($"  // ... ({lines.Length - 50} more lines)");
                        sb.AppendLine("  ```");
                    }
                    catch { /* ignore read errors */ }
                }
            }
        }

        private static string GetHierarchyPath(GameObject go)
        {
            var parts = new List<string>();
            var current = go.transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ─── Console Log Reflection ───

        private struct LogEntry
        {
            public string Message;
            public string Type;
        }

        private static List<LogEntry> GetConsoleErrors(int maxCount)
        {
            var entries = new List<LogEntry>();
            try
            {
                // 反射 UnityEditor.LogEntries
                var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
                if (logEntriesType == null) return entries;

                var getCount = logEntriesType.GetMethod("GetCount",
                    BindingFlags.Public | BindingFlags.Static);
                var startGetting = logEntriesType.GetMethod("StartGettingEntries",
                    BindingFlags.Public | BindingFlags.Static);
                var getEntry = logEntriesType.GetMethod("GetEntryInternal",
                    BindingFlags.Public | BindingFlags.Static);
                var endGetting = logEntriesType.GetMethod("EndGettingEntries",
                    BindingFlags.Public | BindingFlags.Static);

                if (getCount == null || startGetting == null || endGetting == null)
                    return entries;

                int count = (int)getCount.Invoke(null, null);
                if (count == 0) return entries;

                startGetting.Invoke(null, null);

                try
                {
                    // LogEntry struct
                    var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");
                    if (logEntryType == null) return entries;

                    var messageField = logEntryType.GetField("message",
                        BindingFlags.Public | BindingFlags.Instance);
                    var modeField = logEntryType.GetField("mode",
                        BindingFlags.Public | BindingFlags.Instance);

                    // 从最后读起（最新的条目）
                    int start = Mathf.Max(0, count - maxCount);
                    for (int i = start; i < count; i++)
                    {
                        var entryObj = Activator.CreateInstance(logEntryType);
                        if (getEntry != null)
                            getEntry.Invoke(null, new[] { i, entryObj });

                        string message = messageField?.GetValue(entryObj) as string ?? "";
                        int mode = modeField != null ? (int)modeField.GetValue(entryObj) : 0;

                        // mode flags: 1=Error, 2=Assert, 4=Log, 8=Fatal, 16=DontPreprocess,
                        //             32=LogLevelLog, 64=LogLevelWarning, 128=LogLevelError
                        bool isError = (mode & (1 | 2 | 8 | 128)) != 0;
                        bool isWarning = (mode & 64) != 0;

                        if (!isError && !isWarning) continue;

                        // 截断过长消息
                        if (message.Length > 300)
                            message = message.Substring(0, 300) + "...";

                        entries.Add(new LogEntry
                        {
                            Message = message.Replace("\n", " "),
                            Type = isError ? "Error" : "Warning"
                        });
                    }
                }
                finally
                {
                    endGetting.Invoke(null, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI] Failed to read console logs: {e.Message}");
            }

            return entries;
        }
    }
}
