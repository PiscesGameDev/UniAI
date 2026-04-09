using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// Unity Test Runner 聚合工具：run_editmode（进程内）/ run_playmode（子进程）/ run_both。
    /// </summary>
    [UniAITool(
        Name = "manage_test",
        Group = ToolGroups.Testing,
        Description =
            "Run Unity tests. Actions: 'run_editmode' (in-process), " +
            "'run_playmode' (subprocess, avoids domain reload), 'run_both' (sequential).",
        Actions = new[] { "run_editmode", "run_playmode", "run_both" },
        RequiresPolling = true,
        MaxPollSeconds = 600)]
    internal static class ManageTest
    {
        private const int PROCESS_POLL_INTERVAL_MS = 500;

        public static async UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return ToolResponse.Error("Missing 'action'.");

            string testFilter = (string)args["testFilter"];

            try
            {
                return action switch
                {
                    "run_editmode" => await RunEditModeInProcessAsync(testFilter, ct),
                    "run_playmode" => await RunPlayModeViaProcessAsync(testFilter, ct),
                    "run_both" => await RunBothAsync(testFilter, ct),
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return ToolResponse.Error(ex.Message); }
        }

        public class RunEditmodeArgs
        {
            [ToolParam(Description = "Optional test name filter.", Required = false)]
            public string TestFilter;
        }

        public class RunPlaymodeArgs : RunEditmodeArgs { }
        public class RunBothArgs : RunEditmodeArgs { }

        // ─── EditMode：进程内 ───

        private static async UniTask<object> RunEditModeInProcessAsync(string testFilter, CancellationToken ct)
        {
            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(testFilter))
                filter.testNames = new[] { testFilter };

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var callbacks = new TestCallbacks();

            try
            {
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(filter));

                while (!callbacks.IsComplete)
                {
                    ct.ThrowIfCancellationRequested();
                    await UniTask.Delay(200, cancellationToken: ct);
                }

                return ToolResponse.Success(new { report = callbacks.BuildReport("EditMode") });
            }
            finally
            {
                api.UnregisterCallbacks(callbacks);
                UnityEngine.Object.DestroyImmediate(api);
            }
        }

        // ─── PlayMode：子进程 ───

        private static async UniTask<object> RunPlayModeViaProcessAsync(string testFilter, CancellationToken ct)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), $"UniAI_Test_{Guid.NewGuid():N}");
            string resultPath = Path.Combine(tempDir, "results.xml");
            string logPath = Path.Combine(tempDir, "unity.log");
            Directory.CreateDirectory(tempDir);

            try
            {
                string unityExe = EditorApplication.applicationPath;
                string projectPath = Path.GetDirectoryName(Application.dataPath);

                var argsBuilder = new StringBuilder();
                argsBuilder.Append($"-batchmode -nographics -projectPath \"{projectPath}\"");
                argsBuilder.Append(" -runTests -testPlatform PlayMode");
                argsBuilder.Append($" -testResults \"{resultPath}\"");
                argsBuilder.Append($" -logFile \"{logPath}\"");

                if (!string.IsNullOrEmpty(testFilter))
                    argsBuilder.Append($" -testFilter \"{testFilter}\"");

                Debug.Log($"[UniAI manage_test] Starting PlayMode subprocess:\n{unityExe} {argsBuilder}");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = unityExe,
                        Arguments = argsBuilder.ToString(),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                process.Start();

                try
                {
                    while (!process.HasExited)
                    {
                        ct.ThrowIfCancellationRequested();
                        await UniTask.Delay(PROCESS_POLL_INTERVAL_MS, cancellationToken: ct);
                    }

                    if (File.Exists(resultPath))
                        return ToolResponse.Success(new { report = ParseNUnitXml(resultPath, "PlayMode") });

                    string logTail = ReadLogTail(logPath, 2000);
                    string hint = logTail.Contains("already open")
                                  || logTail.Contains("project is already open")
                                  || logTail.Contains("Lock file")
                        ? " Hint: Unity cannot open the same project twice. Close other instances or use EditMode."
                        : "";

                    return ToolResponse.Error(
                        $"PlayMode tests failed (exit code {process.ExitCode}). No result file generated.{hint}\n\n--- Unity Log (tail) ---\n{logTail}");
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        Debug.Log("[UniAI manage_test] PlayMode subprocess killed due to cancellation.");
                    }
                    throw;
                }
                finally { process.Dispose(); }
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { }
            }
        }

        private static async UniTask<object> RunBothAsync(string testFilter, CancellationToken ct)
        {
            var editResult = await RunEditModeInProcessAsync(testFilter, ct);
            var playResult = await RunPlayModeViaProcessAsync(testFilter, ct);
            return ToolResponse.Success(new { editMode = editResult, playMode = playResult });
        }

        // ─── NUnit XML 解析 ───

        private static string ParseNUnitXml(string xmlPath, string label)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(xmlPath);

                var root = doc.DocumentElement;
                if (root == null) return "Empty test result XML.";

                int passed = 0, failed = 0, skipped = 0;
                var failures = new StringBuilder();

                var testCases = root.SelectNodes("//test-case");
                if (testCases != null)
                {
                    foreach (XmlNode tc in testCases)
                    {
                        string result = tc.Attributes?["result"]?.Value ?? "";
                        string fullName = tc.Attributes?["fullname"]?.Value ?? tc.Attributes?["name"]?.Value ?? "?";

                        switch (result.ToLowerInvariant())
                        {
                            case "passed":
                                passed++;
                                break;
                            case "failed":
                                failed++;
                                failures.AppendLine($"FAIL: {fullName}");
                                var message = tc.SelectSingleNode("failure/message")?.InnerText;
                                if (!string.IsNullOrEmpty(message))
                                    failures.AppendLine($"  Message: {Truncate(message, 300)}");
                                var stack = tc.SelectSingleNode("failure/stack-trace")?.InnerText;
                                if (!string.IsNullOrEmpty(stack))
                                    failures.AppendLine($"  Stack: {Truncate(stack, 200)}");
                                break;
                            default:
                                skipped++;
                                break;
                        }
                    }
                }
                return FormatReport(label, passed, failed, skipped, failures);
            }
            catch (Exception e) { return $"Error parsing test results: {e.Message}"; }
        }

        private static string ReadLogTail(string logPath, int maxChars)
        {
            if (!File.Exists(logPath)) return "(no log file found)";
            try
            {
                string content = File.ReadAllText(logPath);
                return content.Length > maxChars
                    ? "..." + content.Substring(content.Length - maxChars)
                    : content;
            }
            catch (Exception e) { return $"(failed to read log: {e.Message})"; }
        }

        // ─── EditMode 回调 ───

        private class TestCallbacks : ICallbacks
        {
            private readonly StringBuilder _sb = new();
            private int _passed;
            private int _failed;
            private int _skipped;
            private bool _isComplete;

            public bool IsComplete => _isComplete;

            public void RunStarted(ITestAdaptor testsToRun) { }
            public void RunFinished(ITestResultAdaptor result) { _isComplete = true; }
            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.Test.IsSuite) return;

                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        _passed++;
                        break;
                    case TestStatus.Failed:
                        _failed++;
                        _sb.AppendLine($"FAIL: {result.Test.FullName}");
                        if (!string.IsNullOrEmpty(result.Message))
                            _sb.AppendLine($"  Message: {Truncate(result.Message, 300)}");
                        if (!string.IsNullOrEmpty(result.StackTrace))
                            _sb.AppendLine($"  Stack: {Truncate(result.StackTrace, 200)}");
                        break;
                    default:
                        _skipped++;
                        break;
                }
            }

            public string BuildReport(string label) => FormatReport(label, _passed, _failed, _skipped, _sb);
        }

        private static string FormatReport(string label, int passed, int failed, int skipped, StringBuilder failures)
        {
            var report = new StringBuilder();
            report.AppendLine($"{label} Test Results: {passed} passed, {failed} failed, {skipped} skipped");

            if (failed > 0)
            {
                report.AppendLine("\n--- Failures ---");
                report.Append(failures);
            }
            else if (passed > 0)
                report.AppendLine("All tests passed.");
            else
                report.AppendLine("No tests found matching the filter.");

            return report.ToString();
        }

        private static string Truncate(string text, int maxLen)
        {
            text = text.Trim();
            return text.Length > maxLen ? text.Substring(0, maxLen) + "..." : text;
        }
    }
}
