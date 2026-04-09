using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 捕获并缓存 Unity 运行时日志和编译错误。Editor 启动时自动订阅。
    /// </summary>
    [InitializeOnLoad]
    internal static class ConsoleLogBuffer
    {
        public enum LogLevel { Log, Warning, Error }

        public struct Entry
        {
            public LogLevel Level;
            public string Message;
            public string StackTrace;
            public DateTime Time;
            public bool IsCompileMessage;
        }

        private const int CAPACITY = 500;
        private static readonly Queue<Entry> _logs = new(CAPACITY);
        private static readonly object _lock = new();

        static ConsoleLogBuffer()
        {
            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        private static void OnLog(string condition, string stacktrace, LogType type)
        {
            var level = type switch
            {
                LogType.Error or LogType.Exception or LogType.Assert => LogLevel.Error,
                LogType.Warning => LogLevel.Warning,
                _ => LogLevel.Log
            };

            Push(new Entry
            {
                Level = level,
                Message = condition ?? "",
                StackTrace = stacktrace ?? "",
                Time = DateTime.Now,
                IsCompileMessage = false
            });
        }

        private static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            string assemblyName = System.IO.Path.GetFileNameWithoutExtension(assembly);
            foreach (var msg in messages)
            {
                var level = msg.type == CompilerMessageType.Error ? LogLevel.Error : LogLevel.Warning;
                Push(new Entry
                {
                    Level = level,
                    Message = $"[{assemblyName}] {msg.message}",
                    StackTrace = $"{msg.file}:{msg.line}",
                    Time = DateTime.Now,
                    IsCompileMessage = true
                });
            }
        }

        private static void Push(Entry entry)
        {
            lock (_lock)
            {
                if (_logs.Count >= CAPACITY) _logs.Dequeue();
                _logs.Enqueue(entry);
            }
        }

        public static List<Entry> GetAll()
        {
            lock (_lock) { return new List<Entry>(_logs); }
        }

        public static void Clear()
        {
            lock (_lock) { _logs.Clear(); }
        }
    }

    /// <summary>
    /// Unity Console 聚合工具：get_recent / get_errors / get_warnings / get_compile_errors / count / clear。
    /// </summary>
    [UniAITool(
        Name = "manage_console",
        Group = ToolGroups.Editor,
        Description =
            "Read Unity Console (runtime logs + compile errors/warnings). Actions: " +
            "'get_recent', 'get_errors', 'get_warnings', 'get_compile_errors', 'count', 'clear'.",
        Actions = new[] { "get_recent", "get_errors", "get_warnings", "get_compile_errors", "count", "clear" })]
    internal static class ManageConsole
    {
        private const int DEFAULT_LIMIT = 30;
        private const int MAX_MESSAGE_LEN = 1000;

        public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return UniTask.FromResult(ToolResponse.Error("Missing 'action'."));

            int limit = (int?)args["limit"] ?? DEFAULT_LIMIT;
            if (limit <= 0) limit = DEFAULT_LIMIT;

            object result;
            try
            {
                result = action switch
                {
                    "get_recent" => GetEntries(limit, null),
                    "get_errors" => GetEntries(limit, ConsoleLogBuffer.LogLevel.Error),
                    "get_warnings" => GetEntries(limit, ConsoleLogBuffer.LogLevel.Warning),
                    "get_compile_errors" => GetCompileErrors(limit),
                    "count" => GetCount(),
                    "clear" => Clear(),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            return UniTask.FromResult(result);
        }

        public class GetRecentArgs
        {
            [ToolParam(Description = "Max entries to return (default 30).", Required = false)]
            public int Limit;
        }

        public class GetErrorsArgs : GetRecentArgs { }
        public class GetWarningsArgs : GetRecentArgs { }
        public class GetCompileErrorsArgs : GetRecentArgs { }

        // ─── 实现 ───

        private static object GetEntries(int limit, ConsoleLogBuffer.LogLevel? filter)
        {
            var all = ConsoleLogBuffer.GetAll();
            var filtered = new List<object>();
            for (int i = all.Count - 1; i >= 0 && filtered.Count < limit; i--)
            {
                if (filter != null && all[i].Level != filter.Value) continue;
                var e = all[i];
                filtered.Add(new
                {
                    time = e.Time.ToString("HH:mm:ss"),
                    level = e.Level.ToString(),
                    compile = e.IsCompileMessage,
                    message = Truncate(e.Message),
                    stackTrace = e.Level == ConsoleLogBuffer.LogLevel.Error
                        ? Truncate(e.StackTrace, 500)
                        : null
                });
            }

            return ToolResponse.Success(new { count = filtered.Count, entries = filtered });
        }

        private static object GetCompileErrors(int limit)
        {
            var all = ConsoleLogBuffer.GetAll();
            var list = new List<object>();
            for (int i = all.Count - 1; i >= 0 && list.Count < limit; i--)
            {
                var e = all[i];
                if (!e.IsCompileMessage || e.Level != ConsoleLogBuffer.LogLevel.Error) continue;
                list.Add(new { message = e.Message, location = e.StackTrace });
            }
            return ToolResponse.Success(new { count = list.Count, errors = list });
        }

        private static object GetCount()
        {
            var all = ConsoleLogBuffer.GetAll();
            int errors = 0, warnings = 0, logs = 0, compileErrors = 0;
            foreach (var e in all)
            {
                if (e.Level == ConsoleLogBuffer.LogLevel.Error)
                {
                    errors++;
                    if (e.IsCompileMessage) compileErrors++;
                }
                else if (e.Level == ConsoleLogBuffer.LogLevel.Warning) warnings++;
                else logs++;
            }
            return ToolResponse.Success(new { total = all.Count, errors, compileErrors, warnings, logs });
        }

        private static object Clear()
        {
            ConsoleLogBuffer.Clear();
            return ToolResponse.Success(new { cleared = true }, "Console log buffer cleared.");
        }

        private static string Truncate(string s, int max = MAX_MESSAGE_LEN)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "... (truncated)";
        }
    }
}
