using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace UniAI.Providers.OpenAI
{
    /// <summary>
    /// 将 UniAI 通用请求模型转换为 OpenAI Chat Completions wire model。
    /// 这里只负责协议形状转换；模型/厂商差异交给 IOpenAIChatDialect 处理。
    /// </summary>
    internal static class OpenAIRequestConverter
    {
        /// <summary>
        /// 检查请求中是否包含图片输入，用于在 provider 层提前验证模型是否具备 VisionInput 能力。
        /// </summary>
        public static bool ContainsImageInput(AIRequest request)
        {
            if (request?.Messages == null)
                return false;

            foreach (var msg in request.Messages)
            {
                if (msg?.Contents == null)
                    continue;

                if (msg.Contents.Any(c => c is AIImageContent))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 转换消息列表，包括 system、tool result、assistant tool_calls 和普通多模态消息。
        /// assistant 消息转换后会统一经过 dialect hook，以支持 provider-native 字段注入。
        /// </summary>
        public static List<OpenAIMessage> ConvertMessages(AIRequest request, IOpenAIChatDialect dialect)
        {
            var messages = new List<OpenAIMessage>();

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(new OpenAIMessage
                {
                    Role = "system",
                    Content = request.SystemPrompt
                });
            }

            foreach (var msg in request.Messages)
            {
                if (msg.Contents.Count == 1 && msg.Contents[0] is AIToolResultContent toolResult)
                {
                    messages.Add(new OpenAIMessage
                    {
                        Role = "tool",
                        Content = toolResult.Content,
                        ToolCallId = toolResult.ToolUseId
                    });
                    continue;
                }

                if (msg.Role == AIRole.Assistant && msg.Contents.Any(c => c is AIToolUseContent))
                {
                    var textPart = msg.Contents.OfType<AITextContent>().FirstOrDefault()?.Text;
                    var toolCalls = msg.Contents.OfType<AIToolUseContent>().Select(tu => new OpenAIToolCallMsg
                    {
                        Id = tu.Id,
                        Type = "function",
                        Function = new OpenAIFunctionCall { Name = tu.Name, Arguments = tu.Arguments ?? "{}" }
                    }).ToList();

                    var assistantMessage = new OpenAIMessage
                    {
                        Role = "assistant",
                        Content = textPart,
                        ToolCallsOut = toolCalls
                    };
                    dialect.ApplyAssistantMessageExtras(assistantMessage, msg, hasToolCalls: true);
                    messages.Add(assistantMessage);
                    continue;
                }

                var role = msg.Role == AIRole.User ? "user" : "assistant";
                var content = ConvertContent(msg);
                var openAIMessage = new OpenAIMessage { Role = role, Content = content };
                if (msg.Role == AIRole.Assistant)
                    dialect.ApplyAssistantMessageExtras(openAIMessage, msg, hasToolCalls: false);
                messages.Add(openAIMessage);
            }

            return messages;
        }

        /// <summary>
        /// 将 UniAI 的工具定义转换为 OpenAI tools/function schema。
        /// </summary>
        public static void BuildToolDefs(AIRequest request, OpenAIRequest openAIRequest)
        {
            openAIRequest.Tools = request.Tools.Select(t => new OpenAIToolDef
            {
                Function = new OpenAIFunctionDef
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = string.IsNullOrEmpty(t.ParametersSchema) ? new object()
                        : JsonConvert.DeserializeObject(t.ParametersSchema)
                }
            }).ToList();

            if (!string.IsNullOrEmpty(request.ToolChoice))
            {
                openAIRequest.ToolChoice = request.ToolChoice switch
                {
                    "auto" => "auto",
                    "any" => "required",
                    "none" => "none",
                    _ => new { type = "function", function = new { name = request.ToolChoice } }
                };
            }
        }

        /// <summary>
        /// 将 UniAI 的响应格式要求转换为 OpenAI response_format。
        /// 支持 json_object 与 json_schema。
        /// </summary>
        public static void BuildResponseFormat(AIRequest request, OpenAIRequest openAIRequest)
        {
            var format = request.ResponseFormat;
            if (format == null || format.Type == ResponseFormatType.Text)
                return;

            if (format.Type == ResponseFormatType.JsonObject)
            {
                openAIRequest.ResponseFormat = new { type = "json_object" };
            }
            else if (format.Type == ResponseFormatType.JsonSchema)
            {
                openAIRequest.ResponseFormat = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = format.Name,
                        schema = JsonConvert.DeserializeObject(format.Schema),
                        strict = format.Strict
                    }
                };
            }
        }

        /// <summary>
        /// 转换单条普通消息内容。
        /// 纯文本消息输出 string；包含图片或文件时输出 OpenAI content parts 数组。
        /// </summary>
        private static object ConvertContent(AIMessage msg)
        {
            var hasMultiContent = msg.Contents.Any(c => c is AIImageContent or AIFileContent);
            if (!hasMultiContent)
                return msg.Contents.FirstOrDefault() is AITextContent t ? t.Text : "";

            return msg.Contents.Select<AIContent, object>(c =>
            {
                if (c is AITextContent text)
                    return new OpenAITextPart { Text = text.Text };
                if (c is AIImageContent img)
                {
                    return new OpenAIImagePart
                    {
                        ImageUrl = new OpenAIImageUrl
                        {
                            Url = $"data:{img.MediaType};base64,{Convert.ToBase64String(img.Data)}"
                        }
                    };
                }
                if (c is AIFileContent file)
                    return new OpenAITextPart { Text = $"[File: {file.FileName}]\n{file.Text}" };
                return null;
            }).Where(x => x != null).ToList();
        }
    }
}
