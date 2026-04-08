#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace UniAI
{
    /// <summary>
    /// Stdio 传输实现 — 启动 MCP Server 子进程，通过 stdin/stdout 交换 JSON-RPC 消息
    /// 仅在 Editor 和 Standalone 平台可用（移动/WebGL 不支持 System.Diagnostics.Process）
    /// </summary>
    internal class StdioMcpTransport : IMcpTransport
    {
        private readonly string _command;
        private readonly string _arguments;
        private readonly Dictionary<string, string> _env;

        private Process _process;
        private int _nextId;
        private readonly object _sendLock = new();
        private readonly Dictionary<int, UniTaskCompletionSource<JsonRpcResponse>> _pending = new();
        private CancellationTokenSource _readCts;

        public bool IsConnected => _process is { HasExited: false };

        public StdioMcpTransport(string command, string arguments, Dictionary<string, string> env = null)
        {
            _command = command;
            _arguments = arguments;
            _env = env;
        }

        public UniTask ConnectAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_command))
                throw new InvalidOperationException("StdioMcpTransport: Command is empty");

            var psi = new ProcessStartInfo
            {
                FileName = _command,
                Arguments = _arguments ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (_env != null)
            {
                foreach (var kv in _env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;
            }

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            try
            {
                if (!_process.Start())
                    throw new InvalidOperationException($"Failed to start MCP process: {_command}");
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"StdioMcpTransport: failed to start '{_command} {_arguments}': {e.Message}", e);
            }

            _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ReadLoopAsync(_readCts.Token).Forget();
            ReadStderrLoopAsync(_readCts.Token).Forget();

            AILogger.Info($"[MCP] Stdio transport started: {_command} {_arguments}");
            return UniTask.CompletedTask;
        }

        public async UniTask<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("StdioMcpTransport: not connected");

            int id = Interlocked.Increment(ref _nextId);
            request.Id = id;

            var tcs = new UniTaskCompletionSource<JsonRpcResponse>();
            lock (_pending) _pending[id] = tcs;

            try
            {
                WriteLine(JsonConvert.SerializeObject(request));
            }
            catch (Exception e)
            {
                lock (_pending) _pending.Remove(id);
                throw new InvalidOperationException($"StdioMcpTransport: send failed: {e.Message}", e);
            }

            using (ct.Register(() =>
            {
                lock (_pending) _pending.Remove(id);
                tcs.TrySetCanceled();
            }))
            {
                return await tcs.Task;
            }
        }

        public UniTask SendNotificationAsync(string method, object param, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("StdioMcpTransport: not connected");

            var notification = new JsonRpcRequest { Method = method, Params = param, Id = null };
            WriteLine(JsonConvert.SerializeObject(notification));
            return UniTask.CompletedTask;
        }

        private void WriteLine(string line)
        {
            lock (_sendLock)
            {
                var stdin = _process.StandardInput;
                stdin.Write(line);
                stdin.Write('\n');
                stdin.Flush();
            }
            AILogger.Verbose($"[MCP →] {line}");
        }

        private async UniTaskVoid ReadLoopAsync(CancellationToken ct)
        {
            var stdout = _process.StandardOutput;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(stdout, ct);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    AILogger.Verbose($"[MCP ←] {line}");

                    JsonRpcResponse response;
                    try
                    {
                        response = JsonConvert.DeserializeObject<JsonRpcResponse>(line);
                    }
                    catch (Exception e)
                    {
                        AILogger.Warning($"[MCP] Failed to parse response: {e.Message}");
                        continue;
                    }

                    if (response?.Id == null) continue;  // 通知，忽略

                    UniTaskCompletionSource<JsonRpcResponse> tcs = null;
                    lock (_pending)
                    {
                        if (_pending.TryGetValue(response.Id.Value, out tcs))
                            _pending.Remove(response.Id.Value);
                    }
                    tcs?.TrySetResult(response);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                AILogger.Warning($"[MCP] Read loop error: {e.Message}");
            }
            finally
            {
                FailAllPending("Transport closed");
            }
        }

        private async UniTaskVoid ReadStderrLoopAsync(CancellationToken ct)
        {
            var stderr = _process.StandardError;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(stderr, ct);
                    if (line == null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                        AILogger.Verbose($"[MCP stderr] {line}");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        private static async UniTask<string> ReadLineAsync(StreamReader reader, CancellationToken ct)
        {
            // StreamReader.ReadLineAsync 不支持 CancellationToken，使用 Task.Run 包裹
            var task = System.Threading.Tasks.Task.Run(reader.ReadLineAsync, ct);
            return await task.AsUniTask();
        }

        private void FailAllPending(string reason)
        {
            lock (_pending)
            {
                foreach (var tcs in _pending.Values)
                    tcs.TrySetException(new InvalidOperationException(reason));
                _pending.Clear();
            }
        }

        public void Dispose()
        {
            try { _readCts?.Cancel(); } catch { }
            FailAllPending("Transport disposed");

            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        try { _process.StandardInput.Close(); } catch { }
                        if (!_process.WaitForExit(500))
                            _process.Kill();
                    }
                }
                catch { }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }

            _readCts?.Dispose();
            _readCts = null;
        }
    }
}
#endif
