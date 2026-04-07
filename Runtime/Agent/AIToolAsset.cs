using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// Tool 的 ScriptableObject 基类
    /// 子类实现具体的工具执行逻辑
    /// </summary>
    public abstract class AIToolAsset : ScriptableObject
    {
        [SerializeField] private string _toolName;
        [SerializeField, TextArea(2, 4)] private string _description;
        [SerializeField, TextArea(3, 10)] private string _parametersSchema;

        /// <summary>
        /// 工具名称（唯一标识）
        /// </summary>
        public string ToolName => _toolName;

        /// <summary>
        /// 工具描述（告诉 AI 何时使用该工具）
        /// </summary>
        public string Description => _description;

        /// <summary>
        /// 参数的 JSON Schema 字符串
        /// </summary>
        public string ParametersSchema => _parametersSchema;

        /// <summary>
        /// 文件被修改时触发，外部（如 EditorAgentGuard）可订阅
        /// </summary>
        public event Action OnFileModified;

        /// <summary>
        /// 子类修改了文件后调用，触发 OnFileModified 事件
        /// </summary>
        protected void NotifyFileModified() => OnFileModified?.Invoke();

        /// <summary>
        /// 转换为 AITool 定义（传给 Provider）
        /// </summary>
        public AITool ToDefinition() => new()
        {
            Name = _toolName,
            Description = _description,
            ParametersSchema = _parametersSchema
        };

        /// <summary>
        /// 执行工具，返回结果字符串
        /// </summary>
        /// <param name="arguments">AI 传入的参数 JSON 字符串</param>
        /// <param name="ct">取消令牌</param>
        public abstract UniTask<string> ExecuteAsync(string arguments, CancellationToken ct);

        // ────────────────────────── 路径安全工具 ──────────────────────────

        private static readonly string _projectRoot = Path.GetFullPath(".");

        /// <summary>
        /// 项目根目录绝对路径（供子类使用）
        /// </summary>
        protected static string ProjectRoot => _projectRoot;

        /// <summary>
        /// 验证路径在项目目录内，返回完整路径。失败时返回 null 并输出错误信息。
        /// </summary>
        protected static bool ValidateProjectPath(string path, out string fullPath, out string error)
        {
            if (string.IsNullOrEmpty(path))
            {
                fullPath = null;
                error = "Error: Missing required parameter 'path'.";
                return false;
            }

            fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(_projectRoot))
            {
                error = "Error: Path is outside the project directory.";
                fullPath = null;
                return false;
            }

            error = null;
            return true;
        }
    }
}
