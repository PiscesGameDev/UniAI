using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor.Tools
{
    /// <summary>
    /// AssetDatabase 元操作聚合工具：create_folder/copy/move/rename/delete/refresh/find/dependencies/guid_to_path/path_to_guid。
    /// </summary>
    [UniAITool(
        Name = "manage_asset",
        Group = ToolGroups.Asset,
        Description =
            "AssetDatabase meta operations. Actions: 'create_folder', 'copy', 'move', 'rename', 'delete', " +
            "'refresh', 'find' (filter via 'filter' + optional 'folders'), 'dependencies', 'guid_to_path', 'path_to_guid'.",
        Actions = new[]
        {
            "create_folder", "copy", "move", "rename", "delete", "refresh",
            "find", "dependencies", "guid_to_path", "path_to_guid"
        })]
    internal static class ManageAsset
    {
        private const int MAX_RESULTS = 50;

        public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
        {
            var action = (string)args["action"];
            if (string.IsNullOrEmpty(action))
                return UniTask.FromResult<object>(ToolResponse.Error("Missing 'action'."));

            object result;
            try
            {
                result = action switch
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
                    _ => ToolResponse.Error($"Unknown action '{action}'.")
                };
            }
            catch (Exception ex) { result = ToolResponse.Error(ex.Message); }

            if (IsMutation(action))
                EditorAgentGuard.NotifyAssetsModified();

            return UniTask.FromResult(result);
        }

        private static bool IsMutation(string action) => action switch
        {
            "create_folder" or "copy" or "move" or "rename" or "delete" or "refresh" => true,
            _ => false
        };

        public class CreateFolderArgs
        {
            [ToolParam(Description = "Folder path (e.g. 'Assets/MyFolder').")]
            public string Path;
        }

        public class CopyArgs
        {
            [ToolParam(Description = "Source asset path.")]
            public string From;
            [ToolParam(Description = "Destination asset path.")]
            public string To;
        }

        public class MoveArgs : CopyArgs { }

        public class RenameArgs
        {
            [ToolParam(Description = "Current asset path.")]
            public string Path;
            [ToolParam(Description = "New asset name (no path, no extension).")]
            public string NewName;
        }

        public class DeleteArgs
        {
            [ToolParam(Description = "Asset path to delete.")]
            public string Path;
        }

        public class FindArgs
        {
            [ToolParam(Description = "AssetDatabase search filter (e.g. 't:Prefab', 'MyName t:Material').")]
            public string Filter;
            [ToolParam(Description = "Optional folder roots.", Required = false)]
            public string[] Folders;
        }

        public class DependenciesArgs
        {
            [ToolParam(Description = "Asset path.")]
            public string Path;
            [ToolParam(Description = "Recursive lookup.", Required = false)]
            public bool Recursive;
        }

        public class GuidToPathArgs
        {
            [ToolParam(Description = "Asset GUID string.")]
            public string Guid;
        }

        public class PathToGuidArgs
        {
            [ToolParam(Description = "Asset path.")]
            public string Path;
        }

        // ─── 实现 ───

        private static object CreateFolder(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) return ToolResponse.Error("'path' required.");
            if (!path.StartsWith("Assets")) return ToolResponse.Error("path must start with 'Assets'.");
            if (AssetDatabase.IsValidFolder(path)) return ToolResponse.Success(new { path }, "Folder already exists.");

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string name = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent))
                return ToolResponse.Error($"Parent folder '{parent}' does not exist.");

            string guid = AssetDatabase.CreateFolder(parent, name);
            return string.IsNullOrEmpty(guid)
                ? ToolResponse.Error($"Failed to create folder '{path}'.")
                : ToolResponse.Success(new { path, guid });
        }

        private static object Copy(JObject args)
        {
            var from = (string)args["from"];
            var to = (string)args["to"];
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return ToolResponse.Error("'from' and 'to' required.");
            return AssetDatabase.CopyAsset(from, to)
                ? ToolResponse.Success(new { from, to })
                : ToolResponse.Error($"Failed to copy '{from}' to '{to}'.");
        }

        private static object Move(JObject args)
        {
            var from = (string)args["from"];
            var to = (string)args["to"];
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return ToolResponse.Error("'from' and 'to' required.");
            string err = AssetDatabase.MoveAsset(from, to);
            return string.IsNullOrEmpty(err)
                ? ToolResponse.Success(new { from, to })
                : ToolResponse.Error(err);
        }

        private static object Rename(JObject args)
        {
            var path = (string)args["path"];
            var newName = (string)args["newName"];
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
                return ToolResponse.Error("'path' and 'newName' required.");
            string err = AssetDatabase.RenameAsset(path, newName);
            return string.IsNullOrEmpty(err)
                ? ToolResponse.Success(new { path, newName })
                : ToolResponse.Error(err);
        }

        private static object Delete(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) return ToolResponse.Error("'path' required.");
            return AssetDatabase.DeleteAsset(path)
                ? ToolResponse.Success(new { path }, "Deleted.")
                : ToolResponse.Error($"Failed to delete '{path}'.");
        }

        private static object Refresh()
        {
            // Play Mode 下 AssetDatabase.Refresh 会触发脚本重编译并中断 Play Mode，禁止调用。
            if (Application.isPlaying)
                return ToolResponse.Error("'refresh' is not allowed during Play Mode (would trigger domain reload).");
            AssetDatabase.Refresh();
            return ToolResponse.Success(new { refreshed = true });
        }

        private static object Find(JObject args)
        {
            var filter = (string)args["filter"];
            if (string.IsNullOrEmpty(filter)) return ToolResponse.Error("'filter' required.");

            string[] folders = args["folders"] is JArray fa ? fa.ToObject<string[]>() : null;
            string[] guids = folders is { Length: > 0 }
                ? AssetDatabase.FindAssets(filter, folders)
                : AssetDatabase.FindAssets(filter);

            int shown = Math.Min(guids.Length, MAX_RESULTS);
            var paths = new string[shown];
            for (int i = 0; i < shown; i++)
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);

            return ToolResponse.Success(new
            {
                total = guids.Length,
                truncated = guids.Length > MAX_RESULTS,
                paths
            });
        }

        private static object Dependencies(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) return ToolResponse.Error("'path' required.");
            bool recursive = (bool?)args["recursive"] ?? false;

            string[] deps = AssetDatabase.GetDependencies(path, recursive);
            int shown = Math.Min(deps.Length, MAX_RESULTS);
            var slice = new string[shown];
            Array.Copy(deps, slice, shown);

            return ToolResponse.Success(new
            {
                path,
                total = deps.Length,
                truncated = deps.Length > MAX_RESULTS,
                dependencies = slice
            });
        }

        private static object GuidToPath(JObject args)
        {
            var guid = (string)args["guid"];
            if (string.IsNullOrEmpty(guid)) return ToolResponse.Error("'guid' required.");
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? ToolResponse.Error($"No asset for guid '{guid}'.")
                : ToolResponse.Success(new { guid, path });
        }

        private static object PathToGuid(JObject args)
        {
            var path = (string)args["path"];
            if (string.IsNullOrEmpty(path)) return ToolResponse.Error("'path' required.");
            string guid = AssetDatabase.AssetPathToGUID(path);
            return string.IsNullOrEmpty(guid)
                ? ToolResponse.Error($"No guid for path '{path}'.")
                : ToolResponse.Success(new { path, guid });
        }
    }
}
