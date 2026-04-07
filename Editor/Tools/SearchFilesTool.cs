using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 按正则表达式搜索文件内容的 Tool（类似 grep）。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Search Files", fileName = "SearchFilesTool")]
    public class SearchFilesTool : AIToolAsset
    {
        private static int MaxMatches => EditorPreferences.instance.SearchMaxMatches;
        private const int MAX_FILES = 5000;

        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            var args = JsonConvert.DeserializeObject<SearchFilesArgs>(arguments);
            if (args == null || string.IsNullOrEmpty(args.Pattern))
                return UniTask.FromResult("Error: Missing required parameter 'pattern'.");

            string basePath = string.IsNullOrEmpty(args.Path) ? "." : args.Path;
            string fullBase = Path.GetFullPath(basePath);
            string projectRoot = Path.GetFullPath(".");

            if (!fullBase.StartsWith(projectRoot))
                return UniTask.FromResult("Error: Path is outside the project directory.");

            if (!Directory.Exists(fullBase))
                return UniTask.FromResult($"Error: Directory not found: {basePath}");

            Regex regex;
            try
            {
                var options = RegexOptions.Compiled;
                if (args.IgnoreCase) options |= RegexOptions.IgnoreCase;
                regex = new Regex(args.Pattern, options);
            }
            catch (ArgumentException e)
            {
                return UniTask.FromResult($"Error: Invalid regex pattern: {e.Message}");
            }

            string fileGlob = string.IsNullOrEmpty(args.FilePattern) ? "*" : args.FilePattern;

            var sb = new StringBuilder();
            int totalMatches = 0;
            int filesSearched = 0;
            int filesMatched = 0;

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(fullBase, fileGlob, SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();

                    string relative = Path.GetRelativePath(projectRoot, filePath).Replace('\\', '/');

                    // 跳过噪音目录和二进制文件
                    if (relative.Contains("/.") || relative.StartsWith(".")) continue;
                    if (relative.Contains("/Library/") || relative.StartsWith("Library/")) continue;
                    if (relative.Contains("/Temp/") || relative.StartsWith("Temp/")) continue;
                    if (IsBinaryExtension(filePath)) continue;

                    filesSearched++;
                    if (filesSearched > MAX_FILES)
                    {
                        sb.AppendLine($"\n[Stopped: searched {MAX_FILES} files limit]");
                        break;
                    }

                    string[] lines;
                    try
                    {
                        lines = File.ReadAllLines(filePath);
                    }
                    catch
                    {
                        continue;
                    }

                    bool fileHeaderWritten = false;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (!regex.IsMatch(lines[i])) continue;

                        if (!fileHeaderWritten)
                        {
                            sb.AppendLine($"\n--- {relative} ---");
                            fileHeaderWritten = true;
                            filesMatched++;
                        }

                        sb.AppendLine($"  {i + 1}: {TruncateLine(lines[i])}");
                        totalMatches++;

                        if (totalMatches >= MaxMatches)
                        {
                            sb.AppendLine($"\n[Truncated: showing first {MaxMatches} matches]");
                            goto done;
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                return UniTask.FromResult($"Error: Directory not found: {basePath}");
            }

            done:
            if (totalMatches == 0)
                return UniTask.FromResult($"No matches found for '{args.Pattern}' in {basePath}");

            sb.Insert(0, $"Found {totalMatches} matches in {filesMatched} files (searched {filesSearched} files):\n");
            return UniTask.FromResult(sb.ToString());
        }

        private static string TruncateLine(string line)
        {
            const int maxLen = 200;
            line = line.TrimEnd();
            return line.Length > maxLen ? line.Substring(0, maxLen) + "..." : line;
        }

        private static bool IsBinaryExtension(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tga" or ".psd"
                or ".tif" or ".tiff" or ".exr" or ".hdr"
                or ".wav" or ".mp3" or ".ogg" or ".aif" or ".aiff"
                or ".fbx" or ".obj" or ".blend" or ".dae" or ".3ds" or ".max"
                or ".dll" or ".exe" or ".so" or ".dylib" or ".pdb" or ".mdb"
                or ".zip" or ".rar" or ".7z" or ".gz" or ".tar"
                or ".asset" or ".prefab" or ".unity" or ".lighting"
                or ".ttf" or ".otf" or ".woff"
                or ".mp4" or ".avi" or ".mov" or ".webm";
        }

        private class SearchFilesArgs
        {
            [JsonProperty("pattern")] public string Pattern;
            [JsonProperty("path")] public string Path;
            [JsonProperty("file_pattern")] public string FilePattern;
            [JsonProperty("ignore_case")] public bool IgnoreCase;
        }
    }
}
