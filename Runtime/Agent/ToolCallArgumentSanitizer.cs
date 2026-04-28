using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    internal static class ToolCallArgumentSanitizer
    {
        public static void Sanitize(List<AIToolCall> toolCalls)
        {
            foreach (var tc in toolCalls)
                Parse(tc);
        }

        public static JObject Parse(AIToolCall toolCall)
        {
            if (string.IsNullOrEmpty(toolCall.Arguments))
            {
                toolCall.Arguments = "{}";
                return new JObject();
            }

            try
            {
                return JObject.Parse(toolCall.Arguments);
            }
            catch (JsonReaderException)
            {
                var fixedJson = TryParseFirstJsonObject(toolCall.Arguments);
                if (fixedJson != null)
                {
                    toolCall.Arguments = fixedJson.ToString(Formatting.None);
                    AILogger.Warning($"Tool '{toolCall.Name}' had concatenated JSON arguments, sanitized");
                    return fixedJson;
                }

                AILogger.Error($"Tool '{toolCall.Name}' has unparseable arguments, replacing with empty object");
                toolCall.Arguments = "{}";
                return new JObject();
            }
        }

        private static JObject TryParseFirstJsonObject(string json)
        {
            try
            {
                using var reader = new JsonTextReader(new System.IO.StringReader(json))
                {
                    SupportMultipleContent = true
                };

                if (reader.Read())
                    return JObject.Load(reader);
            }
            catch
            {
                // 模型返回无法修复的工具参数时，回退为空对象。
            }

            return null;
        }
    }
}
