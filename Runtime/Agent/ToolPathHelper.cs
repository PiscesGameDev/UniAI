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
            if (!fullPath.StartsWith(ProjectRoot))
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
