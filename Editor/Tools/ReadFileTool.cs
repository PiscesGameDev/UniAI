using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UniAI.Editor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 读取项目内文件内容的 Tool。
    /// 支持全文读取和按行范围读取（offset/limit）。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Read File", fileName = "ReadFileTool")]
    public class ReadFileTool : AIToolAsset
    {
        private static int MaxChars => EditorPreferences.instance.ToolMaxOutputChars;

        public override async UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            var args = JsonConvert.DeserializeObject<ReadFileArgs>(arguments);
            if (!ValidateProjectPath(args?.Path, out var fullPath, out var error))
                return error;

            if (!File.Exists(fullPath))
                return $"Error: File not found: {args.Path}";

            // 按行范围读取
            if (args.Offset.HasValue || args.Limit.HasValue)
                return await ReadLinesAsync(fullPath, args.Offset ?? 0, args.Limit ?? int.MaxValue, ct);

            // 全文读取
            string content = await File.ReadAllTextAsync(fullPath, ct);

            int maxChars = MaxChars;
            if (content.Length > maxChars)
                content = content.Substring(0, maxChars)
                          + $"\n\n[Truncated: file has {content.Length} characters, showing first {maxChars}]";

            return content;
        }

        private static async UniTask<string> ReadLinesAsync(string fullPath, int offset, int limit, CancellationToken ct)
        {
            int maxChars = MaxChars;
            var sb = new StringBuilder();
            int lineNumber = 0;
            int collected = 0;

            using var reader = new StreamReader(fullPath);
            while (await reader.ReadLineAsync() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                lineNumber++;

                if (lineNumber <= offset) continue;

                sb.AppendLine($"{lineNumber}: {line}");
                collected++;

                if (collected >= limit) break;

                if (sb.Length > maxChars)
                {
                    sb.AppendLine($"\n[Truncated at line {lineNumber}]");
                    break;
                }
            }

            if (collected == 0)
                return $"Error: No lines found at offset {offset} (file has {lineNumber} lines).";

            return sb.ToString();
        }

        private class ReadFileArgs
        {
            [JsonProperty("path")] public string Path;
            [JsonProperty("offset")] public int? Offset;
            [JsonProperty("limit")] public int? Limit;
        }
    }
}
