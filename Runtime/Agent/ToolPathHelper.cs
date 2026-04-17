using System;
using System.IO;

namespace UniAI
{
    /// <summary>
    /// 工具实现的公共路径工具：将用户输入的相对/绝对路径解析为项目内安全路径。
    /// </summary>
    public static class ToolPathHelper
    {
        /// <summary>
        /// 项目根目录绝对路径。
        /// </summary>
        public static string ProjectRoot { get; } = Path.GetFullPath(".");

        /// <summary>
        /// 项目根目录 + 分隔符，用于前缀匹配，避免 "E:\Proj" 误匹配 "E:\Project2\..."。
        /// </summary>
        private static readonly string _projectRootWithSep =
            ProjectRoot.EndsWith(Path.DirectorySeparatorChar)
                ? ProjectRoot
                : ProjectRoot + Path.DirectorySeparatorChar;

        /// <summary>
        /// 路径比较策略。Windows / macOS 文件系统默认不区分大小写，Linux 区分。
        /// </summary>
        private static readonly StringComparison _pathComparison =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            StringComparison.OrdinalIgnoreCase;
#else
            StringComparison.Ordinal;
#endif

        /// <summary>
        /// 校验相对/绝对路径是否位于项目目录内，返回完整路径。
        /// </summary>
        public static bool TryResolveProjectPath(string path, out string fullPath, out string error)
        {
            if (string.IsNullOrEmpty(path))
            {
                fullPath = null;
                error = "Missing required parameter 'path'.";
                return false;
            }

            fullPath = Path.GetFullPath(path);

            // 允许 fullPath 等于 ProjectRoot 自身（例如列根目录），
            // 否则必须以 ProjectRoot + 分隔符 开头，防止兄弟目录前缀逃逸
            bool inProject =
                fullPath.Equals(ProjectRoot, _pathComparison) ||
                fullPath.StartsWith(_projectRootWithSep, _pathComparison);

            if (!inProject)
            {
                error = "Path is outside the project directory.";
                fullPath = null;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 转为项目相对路径（以 / 分隔）。
        /// </summary>
        public static string ToRelative(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return fullPath;
            return Path.GetRelativePath(ProjectRoot, fullPath).Replace('\\', '/');
        }
    }
}
