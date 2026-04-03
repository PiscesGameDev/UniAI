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
            if (_runner == null)
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
                var aiMessages = BuildAIMessages();

                // 注入 Unity 上下文到消息列表
                string context = ContextCollector.Collect(_contextSlots);
                if (!string.IsNullOrEmpty(context))
                {
                    // 在用户消息前插入上下文作为系统提示补充
                    aiMessages.Insert(0, AIMessage.User($"[Unity Context]\n{context}"));
                    aiMessages.Insert(1, AIMessage.Assistant("收到上下文信息，我会结合这些信息回答你的问题。"));
                }

                var ct = _streamCts.Token;
                await foreach (var evt in _runner.RunStreamAsync(aiMessages, ct))
                {
                    if (ct.IsCancellationRequested) break;

                    switch (evt.Type)
                    {
                        case AgentEventType.TextDelta:
                            assistantMsg.Content += evt.Text;
                            _scrollToBottom = true;
                            Repaint();
                            break;

                        case AgentEventType.ToolCallStart:
                            _activeSession.Messages.Add(new ChatMessage
                            {
                                Role = AIRole.Assistant,
                                IsToolCall = true,
                                ToolName = evt.ToolCall?.Name ?? "unknown",
                                ToolArguments = evt.ToolCall?.Arguments ?? "",
                                Content = $"调用工具: {evt.ToolCall?.Name}",
                                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            });
                            _scrollToBottom = true;
                            Repaint();
                            break;

                        case AgentEventType.ToolCallResult:
                        {
                            // 找到最近的 ToolCall 消息并更新结果
                            for (int i = _activeSession.Messages.Count - 1; i >= 0; i--)
                            {
                                var m = _activeSession.Messages[i];
                                if (m.IsToolCall && m.ToolName == evt.ToolName && string.IsNullOrEmpty(m.ToolResult))
                                {
                                    m.ToolResult = evt.ToolResult;
                                    m.IsToolError = evt.IsToolError;
                                    break;
                                }
                            }
                            _scrollToBottom = true;
                            Repaint();
                            break;
                        }

                        case AgentEventType.TurnComplete:
                            if (evt.Usage != null)
                            {
                                assistantMsg.InputTokens += evt.Usage.InputTokens;
                                assistantMsg.OutputTokens += evt.Usage.OutputTokens;
                            }
                            break;

                        case AgentEventType.Error:
                            assistantMsg.Content += $"\n\n[错误: {evt.Text}]";
                            Repaint();
                            break;
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

                if (_activeSession.Messages.Count >= 2 && _activeSession.Title == "新对话")
                    GenerateTitleAsync().Forget();
            }
        }

        private List<AIMessage> BuildAIMessages()
        {
            var messages = new List<AIMessage>();

            foreach (var msg in _activeSession.Messages)
            {
                if (msg.IsStreaming && string.IsNullOrEmpty(msg.Content))
                    continue;
                if (msg.IsToolCall)
                    continue; // Tool 调用消息是 UI 展示用，不作为对话消息发送

                var aiMsg = msg.Role == AIRole.User
                    ? AIMessage.User(msg.Content)
                    : AIMessage.Assistant(msg.Content);
                messages.Add(aiMsg);
            }

            return messages;
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
                // 找到第一条用户消息和第一条 assistant 文本消息
                string userText = null, assistantText = null;
                foreach (var msg in _activeSession.Messages)
                {
                    if (msg.IsToolCall) continue;
                    if (msg.Role == AIRole.User && userText == null)
                        userText = msg.Content;
                    else if (msg.Role == AIRole.Assistant && assistantText == null && !string.IsNullOrEmpty(msg.Content))
                        assistantText = msg.Content;
                    if (userText != null && assistantText != null) break;
                }

                if (userText == null || assistantText == null) return;

                var titleRequest = new AIRequest
                {
                    SystemPrompt = "Generate a short title (max 8 Chinese characters or 4 English words) for this conversation. Reply with ONLY the title, nothing else.",
                    Messages = new List<AIMessage>
                    {
                        AIMessage.User(userText),
                        AIMessage.Assistant(assistantText)
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
