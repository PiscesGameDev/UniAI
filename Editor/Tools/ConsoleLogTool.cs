using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
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
            lock (_lock)
            {
                return new List<Entry>(_logs);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }
    }

    /// <summary>
    /// 读取 Unity Console 日志（包括运行时 Debug.Log 和编译错误）。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Console Log", fileName = "ConsoleLogTool")]
    public class ConsoleLogTool : AIToolAsset
    {
        private const int DEFAULT_LIMIT = 30;
        private const int MAX_MESSAGE_LEN = 1000;

        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            ConsoleLogArgs args;
            try { args = JsonConvert.DeserializeObject<ConsoleLogArgs>(arguments) ?? new ConsoleLogArgs(); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            string action = string.IsNullOrEmpty(args.Action) ? "get_recent" : args.Action.ToLowerInvariant();
            int limit = args.Limit > 0 ? args.Limit : DEFAULT_LIMIT;

            string result = action switch
            {
                "get_recent" => FormatEntries(ConsoleLogBuffer.GetAll(), limit, null),
                "get_errors" => FormatEntries(ConsoleLogBuffer.GetAll(), limit, ConsoleLogBuffer.LogLevel.Error),
                "get_warnings" => FormatEntries(ConsoleLogBuffer.GetAll(), limit, ConsoleLogBuffer.LogLevel.Warning),
                "get_compile_errors" => FormatCompileErrors(ConsoleLogBuffer.GetAll(), limit),
                "count" => FormatCount(ConsoleLogBuffer.GetAll()),
                "clear" => ClearBuffer(),
                _ => $"Error: Unknown action '{args.Action}'."
            };

            return UniTask.FromResult(result);
        }

        private static string FormatEntries(List<ConsoleLogBuffer.Entry> all, int limit, ConsoleLogBuffer.LogLevel? filter)
        {
            var filtered = new List<ConsoleLogBuffer.Entry>();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (filter == null || all[i].Level == filter.Value)
                {
                    filtered.Add(all[i]);
                    if (filtered.Count >= limit) break;
                }
            }

            if (filtered.Count == 0)
                return filter == null ? "No logs captured." : $"No {filter} logs found.";

            var sb = new StringBuilder($"=== {filtered.Count} log(s) (newest first) ===\n");
            // newest first already
            foreach (var e in filtered)
            {
                string tag = e.IsCompileMessage ? $"{e.Level}|COMPILE" : e.Level.ToString();
                string msg = Truncate(e.Message);
                sb.AppendLine($"[{e.Time:HH:mm:ss}] [{tag}] {msg}");
                if (e.Level == ConsoleLogBuffer.LogLevel.Error && !string.IsNullOrEmpty(e.StackTrace))
                    sb.AppendLine($"  ↳ {Truncate(e.StackTrace, 500)}");
            }
            return sb.ToString();
        }

        private static string FormatCompileErrors(List<ConsoleLogBuffer.Entry> all, int limit)
        {
            var compile = new List<ConsoleLogBuffer.Entry>();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                if (all[i].IsCompileMessage && all[i].Level == ConsoleLogBuffer.LogLevel.Error)
                {
                    compile.Add(all[i]);
                    if (compile.Count >= limit) break;
                }
            }

            if (compile.Count == 0) return "No compile errors.";

            var sb = new StringBuilder($"=== {compile.Count} compile error(s) ===\n");
            foreach (var e in compile)
                sb.AppendLine($"{e.Message}\n  at {e.StackTrace}");
            return sb.ToString();
        }

        private static string FormatCount(List<ConsoleLogBuffer.Entry> all)
        {
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
            return $"Buffered: {all.Count} total | Errors: {errors} (compile: {compileErrors}) | Warnings: {warnings} | Logs: {logs}";
        }

        private static string ClearBuffer()
        {
            ConsoleLogBuffer.Clear();
            return "Console log buffer cleared.";
        }

        private static string Truncate(string s, int max = MAX_MESSAGE_LEN)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "... (truncated)";
        }

        private class ConsoleLogArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("limit")] public int Limit;
        }
    }
}
