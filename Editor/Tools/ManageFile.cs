using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// 文件管理聚合工具：read / write / list / search。
    /// </summary>
    [UniAITool(
        Name = "manage_file",
        Group = ToolGroups.Core,
        Description =
            "Manage project files. Actions: 'read' (read a file, optional offset/limit); " +
            "'write' (full write via 'content' or exact replace via 'oldString'/'newString'); " +
            "'list' (enumerate a directory, supports glob 'pattern' and 'recursive'); " +
            "'search' (regex content search, filter via 'filePattern', 'ignoreCase').",
        Actions = new[] { "read", "write", "list", "search" })]
    internal static class ManageFile
    {
        private const int MAX_LIST_RESULTS = 200;
        private const int MAX_SEARCH_FILES = 5000;
        private const int MAX_LINE_LEN = 200;

        private static int MaxOutputChars => EditorPreferences.instance.ToolMaxOutputChars;
        private static int MaxSearchMatches => EditorPreferences.instance.SearchMaxMatches;

        public static async UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            return action switch
            {
                "read" => await ReadAsync(args, ct),
                "write" => await WriteAsync(args, ct),
                "list" => List(args, ct),
                "search" => Search(args, ct),
                _ => ToolResponse.Error($"Unknown action '{action}'.")
            };
        }

        // ─── read ───

        public class ReadArgs
        {
            [ToolParam(Description = "Project-relative file path (e.g. 'Assets/Scripts/Foo.cs').")]
            public string Path;
            [ToolParam(Description = "Optional 1-based start line index.", Required = false)]
            public int? Offset;
            [ToolParam(Description = "Optional maximum number of lines to read.", Required = false)]
            public int? Limit;
        }

        private static async UniTask<object> ReadAsync(JObject args, CancellationToken ct)
        {
            var path = (string)args["path"];
            if (!ToolPathHelper.TryResolveProjectPath(path, out var fullPath, out var error))
                return ToolResponse.Error(error);
            if (!File.Exists(fullPath))
                return ToolResponse.Error($"File not found: {path}");

            int? offset = (int?)args["offset"];
            int? limit = (int?)args["limit"];

            if (offset.HasValue || limit.HasValue)
                return await ReadLinesAsync(fullPath, offset ?? 0, limit ?? int.MaxValue, ct);

            string content = await File.ReadAllTextAsync(fullPath, ct);
            int max = MaxOutputChars;
            bool truncated = content.Length > max;
            if (truncated) content = content.Substring(0, max);

            return ToolResponse.Success(new
            {
                path,
                content,
                truncated,
                totalChars = content.Length
            });
        }

        private static async UniTask<object> ReadLinesAsync(string fullPath, int offset, int limit, CancellationToken ct)
        {
            var sb = new StringBuilder();
            int lineNumber = 0;
            int collected = 0;
            int max = MaxOutputChars;
            bool truncated = false;

            using var reader = new StreamReader(fullPath);
            while (await reader.ReadLineAsync() is { } line)
            {
                ct.ThrowIfCancellationRequested();
                lineNumber++;
                if (lineNumber <= offset) continue;

                sb.AppendLine($"{lineNumber}: {line}");
                collected++;
                if (collected >= limit) break;

                if (sb.Length > max)
                {
                    truncated = true;
                    break;
                }
            }

            if (collected == 0)
                return ToolResponse.Error($"No lines found at offset {offset} (file has {lineNumber} lines).");

            return ToolResponse.Success(new { content = sb.ToString(), truncated, linesRead = collected });
        }

        // ─── write ───

        public class WriteArgs
        {
            [ToolParam(Description = "Project-relative file path.")]
            public string Path;
            [ToolParam(Description = "Full content to write. Required for full-write mode.", Required = false)]
            public string Content;
            [ToolParam(Description = "Exact string to replace. Required for replace mode (must be unique in file).", Required = false)]
            public string OldString;
            [ToolParam(Description = "Replacement for 'oldString'. Optional (defaults to empty for deletion).", Required = false)]
            public string NewString;
        }

        private static async UniTask<object> WriteAsync(JObject args, CancellationToken ct)
        {
            var path = (string)args["path"];
            var content = (string)args["content"];
            var oldString = (string)args["oldString"];
            var newString = (string)args["newString"];

            if (string.IsNullOrEmpty(path))
                return ToolResponse.Error("Missing required parameter 'path'.");
            if (content == null && string.IsNullOrEmpty(oldString))
                return ToolResponse.Error("Must provide 'content' (full write) or 'oldString' (replace).");

            if (!ToolPathHelper.TryResolveProjectPath(path, out var fullPath, out var error))
                return ToolResponse.Error(error);

            object result;
            if (!string.IsNullOrEmpty(oldString))
                result = await ReplaceAsync(fullPath, path, oldString, newString ?? "", ct);
            else
                result = await WriteFullAsync(fullPath, path, content, ct);

            EditorAgentGuard.NotifyAssetsModified();
            return result;
        }

        private static async UniTask<object> WriteFullAsync(string fullPath, string relative, string content, CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            bool isNew = !File.Exists(fullPath);
            await File.WriteAllTextAsync(fullPath, content, ct);
            return ToolResponse.Success(
                new { path = relative, bytes = content.Length, created = isNew },
                isNew ? "File created." : "File overwritten.");
        }

        private static async UniTask<object> ReplaceAsync(string fullPath, string relative, string oldString, string newString, CancellationToken ct)
        {
            if (!File.Exists(fullPath))
                return ToolResponse.Error($"File not found: {relative}");

            string content = await File.ReadAllTextAsync(fullPath, ct);
            int index = content.IndexOf(oldString, StringComparison.Ordinal);
            if (index < 0)
                return ToolResponse.Error("'oldString' not found. Must match exactly (including whitespace).");
            if (content.IndexOf(oldString, index + 1, StringComparison.Ordinal) >= 0)
                return ToolResponse.Error("'oldString' matches multiple locations. Provide more surrounding context.");

            string updated = content.Substring(0, index) + newString + content.Substring(index + oldString.Length);
            await File.WriteAllTextAsync(fullPath, updated, ct);
            return ToolResponse.Success(
                new { path = relative, removedChars = oldString.Length, insertedChars = newString.Length },
                "Replaced.");
        }

        // ─── list ───

        public class ListArgs
        {
            [ToolParam(Description = "Directory to list (project-relative). Defaults to project root.", Required = false)]
            public string Path;
            [ToolParam(Description = "Glob pattern (e.g. '*.cs', '**/*.prefab'). Defaults to '*'.", Required = false)]
            public string Pattern;
            [ToolParam(Description = "Recurse into subdirectories.", Required = false)]
            public bool Recursive;
        }

        private static object List(JObject args, CancellationToken ct)
        {
            var basePath = (string)args["path"];
            if (string.IsNullOrEmpty(basePath)) basePath = ".";
            var pattern = (string)args["pattern"];
            if (string.IsNullOrEmpty(pattern)) pattern = "*";
            bool recursive = (bool?)args["recursive"] ?? false;

            if (!ToolPathHelper.TryResolveProjectPath(basePath, out var fullBase, out var error))
                return ToolResponse.Error(error);
            if (!Directory.Exists(fullBase))
                return ToolResponse.Error($"Directory not found: {basePath}");

            if (pattern.Contains("**") || pattern.Contains("/")) recursive = true;
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            string searchPattern = pattern.Replace("**/", "").Replace("**", "*");

            var entries = new System.Collections.Generic.List<object>();
            int count = 0;
            bool truncated = false;

            foreach (string entry in Directory.EnumerateFileSystemEntries(fullBase, searchPattern, searchOption))
            {
                ct.ThrowIfCancellationRequested();
                string relative = ToolPathHelper.ToRelative(entry);
                if (relative.Contains("/.") || relative.StartsWith(".")) continue;
                if (relative.Contains("/Library/") || relative.StartsWith("Library/")) continue;
                if (relative.Contains("/Temp/") || relative.StartsWith("Temp/")) continue;

                bool isDir = Directory.Exists(entry);
                entries.Add(new { path = relative, isDirectory = isDir });
                count++;
                if (count >= MAX_LIST_RESULTS) { truncated = true; break; }
            }

            return ToolResponse.Success(new { count, truncated, entries });
        }

        // ─── search ───

        public class SearchArgs
        {
            [ToolParam(Description = "Regex pattern to search for.")]
            public string Pattern;
            [ToolParam(Description = "Root directory (project-relative). Defaults to project root.", Required = false)]
            public string Path;
            [ToolParam(Description = "Glob for which files to inspect. Defaults to '*'.", Required = false)]
            public string FilePattern;
            [ToolParam(Description = "Case-insensitive match.", Required = false)]
            public bool IgnoreCase;
        }

        private static object Search(JObject args, CancellationToken ct)
        {
            var pattern = (string)args["pattern"];
            if (string.IsNullOrEmpty(pattern))
                return ToolResponse.Error("Missing required parameter 'pattern'.");
            var basePath = (string)args["path"];
            if (string.IsNullOrEmpty(basePath)) basePath = ".";
            var filePattern = (string)args["filePattern"];
            if (string.IsNullOrEmpty(filePattern)) filePattern = "*";
            bool ignoreCase = (bool?)args["ignoreCase"] ?? false;

            if (!ToolPathHelper.TryResolveProjectPath(basePath, out var fullBase, out var error))
                return ToolResponse.Error(error);
            if (!Directory.Exists(fullBase))
                return ToolResponse.Error($"Directory not found: {basePath}");

            Regex regex;
            try
            {
                var options = RegexOptions.Compiled;
                if (ignoreCase) options |= RegexOptions.IgnoreCase;
                regex = new Regex(pattern, options);
            }
            catch (ArgumentException ex)
            {
                return ToolResponse.Error($"Invalid regex: {ex.Message}");
            }

            var matches = new System.Collections.Generic.List<object>();
            int total = 0, filesSearched = 0, filesMatched = 0;
            int max = MaxSearchMatches;
            bool truncated = false;

            foreach (string filePath in Directory.EnumerateFiles(fullBase, filePattern, SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string relative = ToolPathHelper.ToRelative(filePath);
                if (relative.Contains("/.") || relative.StartsWith(".")) continue;
                if (relative.Contains("/Library/") || relative.StartsWith("Library/")) continue;
                if (relative.Contains("/Temp/") || relative.StartsWith("Temp/")) continue;
                if (IsBinary(filePath)) continue;

                filesSearched++;
                if (filesSearched > MAX_SEARCH_FILES) { truncated = true; break; }

                string[] lines;
                try { lines = File.ReadAllLines(filePath); }
                catch { continue; }

                bool matched = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!regex.IsMatch(lines[i])) continue;
                    matched = true;
                    matches.Add(new
                    {
                        file = relative,
                        line = i + 1,
                        text = lines[i].Length > MAX_LINE_LEN ? lines[i].Substring(0, MAX_LINE_LEN) + "..." : lines[i]
                    });
                    total++;
                    if (total >= max) { truncated = true; goto done; }
                }
                if (matched) filesMatched++;
            }

        done:
            return ToolResponse.Success(new { total, filesMatched, filesSearched, truncated, matches });
        }

        private static bool IsBinary(string path)
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
    }
}
