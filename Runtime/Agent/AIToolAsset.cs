using System;
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
    }
}
