using System.Collections.Generic;

namespace UniAI
{
    internal static class AgentMessageFactory
    {
        public static AIMessage BuildAssistantMessage(
            string text,
            List<AIToolCall> toolCalls,
            string reasoningContent)
        {
            var msg = new AIMessage
            {
                Role = AIRole.Assistant,
                Contents = new List<AIContent>(),
                ReasoningContent = string.IsNullOrEmpty(reasoningContent) ? null : reasoningContent
            };

            if (!string.IsNullOrEmpty(text))
                msg.Contents.Add(new AITextContent(text));

            foreach (var tc in toolCalls)
            {
                msg.Contents.Add(new AIToolUseContent
                {
                    Id = tc.Id,
                    Name = tc.Name,
                    Arguments = tc.Arguments
                });
            }

            return msg;
        }
    }
}
