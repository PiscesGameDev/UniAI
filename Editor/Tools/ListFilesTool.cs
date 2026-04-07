using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 列出目录内容或按 glob 模式搜索文件的 Tool。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/List Files", fileName = "ListFilesTool")]
    public class ListFilesTool : AIToolAsset
    {
        private const int MAX_RESULTS = 200;

        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            var args = JsonConvert.DeserializeObject<ListFilesArgs>(arguments);
            string basePath = string.IsNullOrEmpty(args?.Path) ? "." : args.Path;

            string fullBase = Path.GetFullPath(basePath);
            string projectRoot = Path.GetFullPath(".");

            if (!fullBase.StartsWith(projectRoot))
                return UniTask.FromResult("Error: Path is outside the project directory.");

            if (!Directory.Exists(fullBase))
                return UniTask.FromResult($"Error: Directory not found: {basePath}");

            string pattern = string.IsNullOrEmpty(args.Pattern) ? "*" : args.Pattern;
            bool recursive = args.Recursive || pattern.Contains("**") || pattern.Contains("/");

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // 处理 glob 中的 **/ 前缀
            string searchPattern = pattern.Replace("**/", "").Replace("**", "*");

            var sb = new StringBuilder();
            int count = 0;

            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(fullBase, searchPattern, searchOption))
                {
                    ct.ThrowIfCancellationRequested();

                    string relative = Path.GetRelativePath(projectRoot, entry).Replace('\\', '/');

                    // 跳过隐藏目录和常见噪音
                    if (relative.Contains("/.") || relative.StartsWith("."))
                        continue;
                    if (relative.Contains("/Library/") || relative.StartsWith("Library/"))
                        continue;
                    if (relative.Contains("/Temp/") || relative.StartsWith("Temp/"))
                        continue;

                    bool isDir = Directory.Exists(entry);
                    sb.AppendLine(isDir ? $"{relative}/" : relative);
                    count++;

                    if (count >= MAX_RESULTS)
                    {
                        sb.AppendLine($"\n[Truncated: showing first {MAX_RESULTS} results]");
                        break;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                return UniTask.FromResult($"Error: Directory not found: {basePath}");
            }

            if (count == 0)
                return UniTask.FromResult($"No files found matching '{pattern}' in {basePath}");

            sb.Insert(0, $"Found {count} entries:\n");
            return UniTask.FromResult(sb.ToString());
        }

        private class ListFilesArgs
        {
            [JsonProperty("path")] public string Path;
            [JsonProperty("pattern")] public string Pattern;
            [JsonProperty("recursive")] public bool Recursive;
        }
    }
}
