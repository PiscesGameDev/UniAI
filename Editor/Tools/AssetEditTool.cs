using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// AssetDatabase 元操作 Tool：创建/复制/移动/删除/查找/依赖。
    /// 仅 Edit Mode 下生效。
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Tools/Asset Edit", fileName = "AssetEditTool")]
    public class AssetEditTool : AIToolAsset
    {
        private const int MAX_RESULTS = 50;

        public override UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
        {
            if (Application.isPlaying)
                return UniTask.FromResult("Error: AssetEditTool is only available in Edit Mode.");

            AssetEditArgs args;
            try { args = JsonConvert.DeserializeObject<AssetEditArgs>(arguments); }
            catch (Exception ex) { return UniTask.FromResult($"Error: Invalid arguments JSON: {ex.Message}"); }

            if (args == null || string.IsNullOrEmpty(args.Action))
                return UniTask.FromResult("Error: Missing required parameter 'action'.");

            string result;
            try
            {
                result = args.Action.ToLowerInvariant() switch
                {
                    "create_folder" => CreateFolder(args),
                    "copy" => Copy(args),
                    "move" => Move(args),
                    "rename" => Rename(args),
                    "delete" => Delete(args),
                    "refresh" => Refresh(),
                    "find" => Find(args),
                    "dependencies" => Dependencies(args),
                    "guid_to_path" => GuidToPath(args),
                    "path_to_guid" => PathToGuid(args),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex)
            {
                result = $"Error: {ex.Message}";
            }

            if (IsMutation(args.Action))
                NotifyFileModified();

            return UniTask.FromResult(result);
        }

        private static bool IsMutation(string action) => action switch
        {
            "create_folder" or "copy" or "move" or "rename" or "delete" or "refresh" => true,
            _ => false
        };

        private static string CreateFolder(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path)) return "Error: 'path' required (e.g. Assets/MyFolder).";
            if (!args.Path.StartsWith("Assets")) return "Error: path must start with 'Assets'.";

            if (AssetDatabase.IsValidFolder(args.Path))
                return $"Folder already exists: {args.Path}";

            string parent = System.IO.Path.GetDirectoryName(args.Path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(args.Path);

            if (!AssetDatabase.IsValidFolder(parent))
                return $"Error: Parent folder '{parent}' does not exist.";

            string guid = AssetDatabase.CreateFolder(parent, name);
            return string.IsNullOrEmpty(guid)
                ? $"Error: Failed to create folder '{args.Path}'."
                : $"Created folder: {args.Path} (guid: {guid})";
        }

        private static string Copy(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.From) || string.IsNullOrEmpty(args.To))
                return "Error: 'from' and 'to' parameters required.";
            bool ok = AssetDatabase.CopyAsset(args.From, args.To);
            return ok ? $"Copied: {args.From} → {args.To}" : $"Error: Failed to copy '{args.From}' to '{args.To}'.";
        }

        private static string Move(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.From) || string.IsNullOrEmpty(args.To))
                return "Error: 'from' and 'to' parameters required.";
            string err = AssetDatabase.MoveAsset(args.From, args.To);
            return string.IsNullOrEmpty(err) ? $"Moved: {args.From} → {args.To}" : $"Error: {err}";
        }

        private static string Rename(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path) || string.IsNullOrEmpty(args.NewName))
                return "Error: 'path' and 'new_name' required.";
            string err = AssetDatabase.RenameAsset(args.Path, args.NewName);
            return string.IsNullOrEmpty(err) ? $"Renamed: {args.Path} → {args.NewName}" : $"Error: {err}";
        }

        private static string Delete(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path)) return "Error: 'path' required.";
            bool ok = AssetDatabase.DeleteAsset(args.Path);
            return ok ? $"Deleted: {args.Path}" : $"Error: Failed to delete '{args.Path}'.";
        }

        private static string Refresh()
        {
            AssetDatabase.Refresh();
            return "AssetDatabase refreshed.";
        }

        private static string Find(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Filter))
                return "Error: 'filter' required (e.g. 't:Prefab', 'MyName t:Material').";

            string[] guids = args.Folders != null && args.Folders.Length > 0
                ? AssetDatabase.FindAssets(args.Filter, args.Folders)
                : AssetDatabase.FindAssets(args.Filter);

            if (guids.Length == 0) return $"No assets found for filter '{args.Filter}'.";

            var sb = new StringBuilder($"Found {guids.Length} assets (showing up to {MAX_RESULTS}):\n");
            int shown = Math.Min(guids.Length, MAX_RESULTS);
            for (int i = 0; i < shown; i++)
                sb.AppendLine($"  - {AssetDatabase.GUIDToAssetPath(guids[i])}");
            if (guids.Length > MAX_RESULTS)
                sb.AppendLine($"  ... ({guids.Length - MAX_RESULTS} more)");
            return sb.ToString();
        }

        private static string Dependencies(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path)) return "Error: 'path' required.";
            string[] deps = AssetDatabase.GetDependencies(args.Path, args.Recursive);
            if (deps.Length == 0) return $"No dependencies for {args.Path}.";

            var sb = new StringBuilder($"Dependencies of {args.Path} ({deps.Length}):\n");
            int shown = Math.Min(deps.Length, MAX_RESULTS);
            for (int i = 0; i < shown; i++)
                sb.AppendLine($"  - {deps[i]}");
            if (deps.Length > MAX_RESULTS)
                sb.AppendLine($"  ... ({deps.Length - MAX_RESULTS} more)");
            return sb.ToString();
        }

        private static string GuidToPath(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Guid)) return "Error: 'guid' required.";
            string path = AssetDatabase.GUIDToAssetPath(args.Guid);
            return string.IsNullOrEmpty(path) ? $"No asset found for guid '{args.Guid}'." : path;
        }

        private static string PathToGuid(AssetEditArgs args)
        {
            if (string.IsNullOrEmpty(args.Path)) return "Error: 'path' required.";
            string guid = AssetDatabase.AssetPathToGUID(args.Path);
            return string.IsNullOrEmpty(guid) ? $"No guid found for path '{args.Path}'." : guid;
        }

        private class AssetEditArgs
        {
            [JsonProperty("action")] public string Action;
            [JsonProperty("path")] public string Path;
            [JsonProperty("from")] public string From;
            [JsonProperty("to")] public string To;
            [JsonProperty("new_name")] public string NewName;
            [JsonProperty("filter")] public string Filter;
            [JsonProperty("folders")] public string[] Folders;
            [JsonProperty("recursive")] public bool Recursive;
            [JsonProperty("guid")] public string Guid;
        }
    }
}
