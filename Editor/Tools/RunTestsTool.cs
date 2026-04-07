using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 运行 Unity Test Runner 测试的 Tool。
    /// EditMode：直接在当前编辑器中执行（快速）。
    /// PlayMode：启动独立 Unity 子进程执行，避免 domain reload 中断会话。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Run Tests", fileName = "RunTestsTool")]
    public class RunTestsTool : AIToolAsset
    {
        private const int PROCESS_POLL_INTERVAL_MS = 500;

        public override async UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            var args = JsonConvert.DeserializeObject<RunTestsArgs>(arguments);
            string mode = args?.Mode?.ToLowerInvariant() ?? "editmode";
            string testFilter = args?.TestFilter;

            return mode switch
            {
                "playmode" => await RunPlayModeViaProcessAsync(testFilter, ct),
                "both" => await RunBothAsync(testFilter, ct),
                _ => await RunEditModeInProcessAsync(testFilter, ct)
            };
        }

        // ─── EditMode：进程内直接执行 ───

        private static async UniTask<string> RunEditModeInProcessAsync(string testFilter, CancellationToken ct)
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

                return callbacks.BuildReport("EditMode");
            }
            finally
            {
                api.UnregisterCallbacks(callbacks);
                UnityEngine.Object.DestroyImmediate(api);
            }
        }

        // ─── PlayMode：命令行子进程执行 ───

        private static async UniTask<string> RunPlayModeViaProcessAsync(string testFilter, CancellationToken ct)
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
                argsBuilder.Append($" -runTests -testPlatform PlayMode");
                argsBuilder.Append($" -testResults \"{resultPath}\"");
                argsBuilder.Append($" -logFile \"{logPath}\"");

                if (!string.IsNullOrEmpty(testFilter))
                    argsBuilder.Append($" -testFilter \"{testFilter}\"");

                Debug.Log($"[UniAI RunTests] Starting PlayMode subprocess:\n{unityExe} {argsBuilder}");

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
                        return ParseNUnitXml(resultPath, "PlayMode");

                    // 没有结果文件，读取日志诊断
                    string logTail = ReadLogTail(logPath, 2000);
                    string hint = logTail.Contains("already open")
                                  || logTail.Contains("project is already open")
                                  || logTail.Contains("Lock file")
                        ? "\nHint: Unity cannot open the same project in two instances. "
                          + "PlayMode tests via subprocess require the project not to be locked. "
                          + "Consider closing other Unity instances or using EditMode tests instead."
                        : "";

                    return $"Error: PlayMode tests failed (exit code {process.ExitCode}). "
                           + $"No result file generated.{hint}\n\n--- Unity Log (tail) ---\n{logTail}";
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        Debug.Log("[UniAI RunTests] PlayMode subprocess killed due to cancellation.");
                    }
                    throw;
                }
                finally
                {
                    process.Dispose();
                }
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* 清理失败不影响主流程 */ }
            }
        }

        private static string ReadLogTail(string logPath, int maxChars)
        {
            if (!File.Exists(logPath))
                return "(no log file found)";

            try
            {
                string content = File.ReadAllText(logPath);
                return content.Length > maxChars
                    ? "..." + content.Substring(content.Length - maxChars)
                    : content;
            }
            catch (Exception e)
            {
                return $"(failed to read log: {e.Message})";
            }
        }

        // ─── Both：串行执行 ───

        private static async UniTask<string> RunBothAsync(string testFilter, CancellationToken ct)
        {
            string editResult = await RunEditModeInProcessAsync(testFilter, ct);
            string playResult = await RunPlayModeViaProcessAsync(testFilter, ct);

            return $"=== EditMode ===\n{editResult}\n\n=== PlayMode ===\n{playResult}";
        }

        // ─── NUnit XML 解析 ───

        private static string ParseNUnitXml(string xmlPath, string label)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(xmlPath);

                var root = doc.DocumentElement;
                if (root == null)
                    return "Error: Empty test result XML.";

                int passed = 0, failed = 0, skipped = 0;
                var failures = new StringBuilder();

                // NUnit3 格式：<test-run> → <test-suite> → <test-case>
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

                var report = new StringBuilder();
                report.AppendLine($"{label} Test Results: {passed} passed, {failed} failed, {skipped} skipped");

                if (failed > 0)
                {
                    report.AppendLine("\n--- Failures ---");
                    report.Append(failures);
                }
                else if (passed > 0)
                {
                    report.AppendLine("All tests passed.");
                }
                else
                {
                    report.AppendLine("No tests found matching the filter.");
                }

                return report.ToString();
            }
            catch (Exception e)
            {
                return $"Error parsing test results: {e.Message}";
            }
        }

        // ─── EditMode 进程内回调 ───

        private class TestCallbacks : ICallbacks
        {
            private readonly StringBuilder _sb = new();
            private int _passed;
            private int _failed;
            private int _skipped;
            private bool _isComplete;

            public bool IsComplete => _isComplete;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                _isComplete = true;
            }

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

            public string BuildReport(string label)
            {
                var report = new StringBuilder();
                report.AppendLine($"{label} Test Results: {_passed} passed, {_failed} failed, {_skipped} skipped");

                if (_failed > 0)
                {
                    report.AppendLine("\n--- Failures ---");
                    report.Append(_sb);
                }
                else if (_passed > 0)
                {
                    report.AppendLine("All tests passed.");
                }
                else
                {
                    report.AppendLine("No tests found matching the filter.");
                }

                return report.ToString();
            }
        }

        private static string Truncate(string text, int maxLen)
        {
            text = text.Trim();
            return text.Length > maxLen ? text.Substring(0, maxLen) + "..." : text;
        }

        private class RunTestsArgs
        {
            [JsonProperty("mode")] public string Mode;
            [JsonProperty("test_filter")] public string TestFilter;
        }
    }
}
