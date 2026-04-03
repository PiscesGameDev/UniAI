using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Chat
{
    public partial class AIChatWindow
    {
        private void SendMessage()
        {
            if (_activeSession == null)
                CreateNewSession();
            if (_activeSession == null) return;

            string text = _inputText.Trim();
            _inputText = "";

            _activeSession.Messages.Add(new ChatMessage
            {
                Role = AIRole.User,
                Content = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            _scrollToBottom = true;

            StreamResponseAsync().Forget();
        }

        private async UniTaskVoid StreamResponseAsync()
        {
            if (_client == null)
            {
                AddErrorMessage("未配置 AI 提供商，请打开设置进行配置。");
                return;
            }

            _isStreaming = true;
            _spinnerStartTime = EditorApplication.timeSinceStartup;
            _spinnerFrame = 0;
            _streamCts = new System.Threading.CancellationTokenSource();

            var assistantMsg = new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = "",
                IsStreaming = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            _activeSession.Messages.Add(assistantMsg);

            try
            {
                var request = BuildRequest();

                string context = ContextCollector.Collect(_contextSlots);
                if (!string.IsNullOrEmpty(context))
                {
                    request.SystemPrompt = string.IsNullOrEmpty(request.SystemPrompt)
                        ? context
                        : request.SystemPrompt + "\n\n" + context;
                }

                var ct = _streamCts.Token;
                await foreach (var chunk in _client.StreamAsync(request, ct))
                {
                    if (ct.IsCancellationRequested) break;

                    if (!string.IsNullOrEmpty(chunk.DeltaText))
                    {
                        assistantMsg.Content += chunk.DeltaText;
                        _scrollToBottom = true;
                        Repaint();
                    }

                    if (chunk.IsComplete && chunk.Usage != null)
                    {
                        assistantMsg.InputTokens = chunk.Usage.InputTokens;
                        assistantMsg.OutputTokens = chunk.Usage.OutputTokens;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                assistantMsg.Content += "\n\n[已停止]";
            }
            catch (Exception e)
            {
                assistantMsg.Content += $"\n\n[错误: {e.Message}]";
                Debug.LogWarning($"[UniAI Chat] Stream error: {e}");
            }
            finally
            {
                assistantMsg.IsStreaming = false;
                _isStreaming = false;
                _streamCts?.Dispose();
                _streamCts = null;

                _history.Save(_activeSession);
                Repaint();

                if (_activeSession.Messages.Count == 2 && _activeSession.Title == "新对话")
                    GenerateTitleAsync().Forget();
            }
        }

        private AIRequest BuildRequest()
        {
            var request = new AIRequest
            {
                SystemPrompt = "You are a helpful Unity game development assistant. " +
                    "Provide clear, concise answers. When showing code, use C# and Unity best practices.",
                Messages = new List<AIMessage>()
            };

            foreach (var msg in _activeSession.Messages)
            {
                if (msg.IsStreaming && string.IsNullOrEmpty(msg.Content))
                    continue;

                var aiMsg = msg.Role == AIRole.User
                    ? AIMessage.User(msg.Content)
                    : AIMessage.Assistant(msg.Content);
                request.Messages.Add(aiMsg);
            }

            return request;
        }

        private void CancelStream()
        {
            _streamCts?.Cancel();
        }

        private void AddErrorMessage(string error)
        {
            _activeSession?.Messages.Add(new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = $"[错误] {error}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            _scrollToBottom = true;
            Repaint();
        }

        private async UniTaskVoid GenerateTitleAsync()
        {
            if (_client == null || _activeSession == null) return;
            if (_activeSession.Messages.Count < 2) return;

            try
            {
                var titleRequest = new AIRequest
                {
                    SystemPrompt = "Generate a short title (max 8 Chinese characters or 4 English words) for this conversation. Reply with ONLY the title, nothing else.",
                    Messages = new List<AIMessage>
                    {
                        AIMessage.User(_activeSession.Messages[0].Content),
                        AIMessage.Assistant(_activeSession.Messages[1].Content)
                    },
                    MaxTokens = 32,
                    Temperature = 0.3f
                };

                var response = await _client.SendAsync(titleRequest);
                if (response.IsSuccess && !string.IsNullOrEmpty(response.Text))
                {
                    _activeSession.Title = response.Text.Trim().Trim('"', '\'', '\n', '\r');
                    if (_activeSession.Title.Length > 20)
                        _activeSession.Title = _activeSession.Title.Substring(0, 20);
                    _history.Save(_activeSession);
                    Repaint();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UniAI Chat] Auto-title failed: {e.Message}");
            }
        }
    }
}
