using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 写入或创建项目内文件的 Tool。
    /// 支持全量写入和按行插入/替换。
    /// 修改文件后自动通知 EditorAgentGuard 标记 dirty。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Write File", fileName = "WriteFileTool")]
    public class WriteFileTool : AIToolAsset
    {
        public override async UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            var args = JsonConvert.DeserializeObject<WriteFileArgs>(arguments);
            if (args == null || string.IsNullOrEmpty(args.Path))
                return "Error: Missing required parameter 'path'.";

            if (string.IsNullOrEmpty(args.Content) && string.IsNullOrEmpty(args.OldString))
                return "Error: Must provide 'content' (full write) or 'old_string'+'new_string' (replace).";

            string fullPath = Path.GetFullPath(args.Path);
            string projectRoot = Path.GetFullPath(".");

            if (!fullPath.StartsWith(projectRoot))
                return "Error: Path is outside the project directory.";

            // 替换模式：old_string → new_string
            if (!string.IsNullOrEmpty(args.OldString))
                return await ReplaceInFileAsync(fullPath, args, ct);

            // 全量写入模式
            return await WriteFullFileAsync(fullPath, args, ct);
        }

        private async UniTask<string> WriteFullFileAsync(string fullPath, WriteFileArgs args, CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            bool isNew = !File.Exists(fullPath);
            await File.WriteAllTextAsync(fullPath, args.Content, ct);
            NotifyFileModified();

            return isNew
                ? $"Created file: {args.Path} ({args.Content.Length} characters)"
                : $"Wrote file: {args.Path} ({args.Content.Length} characters)";
        }

        private async UniTask<string> ReplaceInFileAsync(string fullPath, WriteFileArgs args, CancellationToken ct)
        {
            if (!File.Exists(fullPath))
                return $"Error: File not found: {args.Path}";

            string content = await File.ReadAllTextAsync(fullPath, ct);

            int index = content.IndexOf(args.OldString);
            if (index < 0)
                return "Error: 'old_string' not found in file. Make sure it matches exactly (including whitespace and indentation).";

            // 检查唯一性
            int secondIndex = content.IndexOf(args.OldString, index + 1);
            if (secondIndex >= 0)
                return "Error: 'old_string' matches multiple locations. Provide more surrounding context to make it unique.";

            string newContent = content.Substring(0, index)
                                + (args.NewString ?? "")
                                + content.Substring(index + args.OldString.Length);

            await File.WriteAllTextAsync(fullPath, newContent, ct);
            NotifyFileModified();

            return $"Replaced in {args.Path}: {args.OldString.Length} chars → {(args.NewString?.Length ?? 0)} chars";
        }

        private class WriteFileArgs
        {
            [JsonProperty("path")] public string Path;
            [JsonProperty("content")] public string Content;
            [JsonProperty("old_string")] public string OldString;
            [JsonProperty("new_string")] public string NewString;
        }
    }
}
