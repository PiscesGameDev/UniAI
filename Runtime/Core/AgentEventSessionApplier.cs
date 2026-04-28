using System;

namespace UniAI
{
    public readonly struct AgentEventApplyResult
    {
        public bool StateChanged { get; }
        public bool ScrollToBottom { get; }

        public AgentEventApplyResult(bool stateChanged, bool scrollToBottom)
        {
            StateChanged = stateChanged;
            ScrollToBottom = scrollToBottom;
        }
    }

    /// <summary>
    /// 将 AgentEvent 应用到 ChatSession，不直接触发 UI 事件。
    /// </summary>
    public sealed class AgentEventSessionApplier
    {
        public ChatMessage AddStreamingAssistant(ChatSession session)
        {
            var assistant = new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = "",
                IsStreaming = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            session.Messages.Add(assistant);
            return assistant;
        }

        public void AddErrorMessage(ChatSession session, string error)
        {
            session?.Messages.Add(new ChatMessage
            {
                Role = AIRole.Assistant,
                Content = $"[错误] {error}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        public AgentEventApplyResult Apply(ChatSession session, ChatMessage assistant, AgentEvent evt)
        {
            switch (evt.Type)
            {
                case AgentEventType.TextDelta:
                    assistant.Content += evt.Text;
                    return new AgentEventApplyResult(true, true);

                case AgentEventType.ToolCallStart:
                    AddToolCallMessage(session, evt);
                    return new AgentEventApplyResult(true, true);

                case AgentEventType.ToolCallResult:
                    ApplyToolResult(session, evt);
                    return new AgentEventApplyResult(true, true);

                case AgentEventType.TurnComplete:
                    ApplyUsage(assistant, evt.Usage);
                    return new AgentEventApplyResult(false, false);

                case AgentEventType.Error:
                    assistant.Content += $"\n\n[错误: {evt.Text}]";
                    return new AgentEventApplyResult(true, false);

                default:
                    return new AgentEventApplyResult(false, false);
            }
        }

        public void MarkCanceled(ChatMessage assistant)
        {
            if (assistant != null)
                assistant.Content += "\n\n[已停止]";
        }

        public void MarkError(ChatMessage assistant, string error)
        {
            if (assistant != null)
                assistant.Content += $"\n\n[错误: {error}]";
        }

        public void Finish(ChatMessage assistant)
        {
            if (assistant != null)
                assistant.IsStreaming = false;
        }

        private static void AddToolCallMessage(ChatSession session, AgentEvent evt)
        {
            session.Messages.Add(new ChatMessage
            {
                Role = AIRole.Assistant,
                IsToolCall = true,
                ToolUseId = evt.ToolCall?.Id,
                ToolName = evt.ToolCall?.Name ?? "unknown",
                ToolArguments = evt.ToolCall?.Arguments ?? "",
                ReasoningContent = evt.ReasoningContent,
                Content = $"调用工具: {evt.ToolCall?.Name}",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        private static void ApplyToolResult(ChatSession session, AgentEvent evt)
        {
            var toolUseId = evt.ToolCall?.Id;
            for (var i = session.Messages.Count - 1; i >= 0; i--)
            {
                var message = session.Messages[i];
                if (!message.IsToolCall || !string.IsNullOrEmpty(message.ToolResult))
                    continue;

                var idMatch = !string.IsNullOrEmpty(toolUseId)
                              && message.ToolUseId == toolUseId;
                var nameMatch = string.IsNullOrEmpty(toolUseId)
                                && message.ToolName == evt.ToolName;

                if (!idMatch && !nameMatch)
                    continue;

                message.ToolResult = evt.ToolResult;
                message.IsToolError = evt.IsToolError;
                break;
            }
        }

        private static void ApplyUsage(ChatMessage assistant, TokenUsage usage)
        {
            if (assistant == null || usage == null)
                return;

            assistant.InputTokens += usage.InputTokens;
            assistant.OutputTokens += usage.OutputTokens;
        }
    }
}
