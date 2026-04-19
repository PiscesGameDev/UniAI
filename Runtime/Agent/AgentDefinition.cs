using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UniAI
{
    /// <summary>
    /// Agent 定义 — ScriptableObject 配置
    /// 在编辑器中创建: Create > UniAI > Agent Definition
    /// </summary>
    [CreateAssetMenu(menuName = "UniAI/Agent Definition", fileName = "NewAgent")]
    public class AgentDefinition : ScriptableObject
    {
        [SerializeField] private string _id = Guid.NewGuid().ToString("N");
        [SerializeField] private string _agentName = "助手";
        [SerializeField] private string _description;
        [SerializeField, TextArea(3, 10)] private string _systemPrompt;
        [SerializeField, Tooltip("可选：从 Assets 下相对路径的文本文件加载 SystemPrompt。" +
                                  "启用 PreferFileOverInline 时优先级高于 _systemPrompt。" +
                                  "用于把 prompt 资产化到 Git 版本库。")]
        private string _systemPromptFilePath;
        [SerializeField, Tooltip("为 true 时，若 _systemPromptFilePath 指向的文件存在，则用它覆盖 _systemPrompt。")]
        private bool _preferFileOverInline;
        [SerializeField] private Texture2D _icon;
        [SerializeField] private string _specifyModel;
        [SerializeField, Range(0f, 1f)] private float _temperature = 0.7f;
        [SerializeField] private int _maxTokens = 4096;
        [SerializeField, Range(1, 50)] private int _maxTurns = 10;
        [SerializeField] private List<string> _toolGroups = new();
        [SerializeField] private List<McpServerConfig> _mcpServers = new();

        /// <summary>
        /// Agent 唯一标识符（GUID）
        /// </summary>
        public string Id
        {
            get
            {
                if (!string.IsNullOrEmpty(_id)) return _id;
                _id = Guid.NewGuid().ToString("N");
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
                return _id;
            }
        }

        /// <summary>
        /// Agent 显示名称
        /// </summary>
        public string AgentName => _agentName;

        /// <summary>
        /// 职责描述
        /// </summary>
        public string Description => _description;

        /// <summary>
        /// 系统提示词（角色设定）。优先级：
        /// <list type="number">
        ///   <item><see cref="SystemPromptFilePath"/> 指向的文件（当 <see cref="PreferFileOverInline"/>=true 且文件存在）</item>
        ///   <item>否则使用内联字段 <c>_systemPrompt</c></item>
        /// </list>
        /// 文件读取失败时自动回退到内联值。
        /// </summary>
        public string SystemPrompt
        {
            get
            {
                if (_preferFileOverInline && !string.IsNullOrEmpty(_systemPromptFilePath))
                {
                    var fileContent = TryReadPromptFile(_systemPromptFilePath);
                    if (!string.IsNullOrEmpty(fileContent))
                        return fileContent;
                }
                return _systemPrompt;
            }
        }

        /// <summary>可选：SystemPrompt 的外部文本文件路径（项目相对或绝对）。</summary>
        public string SystemPromptFilePath => _systemPromptFilePath;

        /// <summary>是否优先使用 <see cref="SystemPromptFilePath"/>。</summary>
        public bool PreferFileOverInline => _preferFileOverInline;

        /// <summary>
        /// Agent 图标
        /// </summary>
        public Texture2D Icon { get => _icon; set => _icon = value; }

        /// <summary>
        /// 指定模型名称（空则使用默认）
        /// </summary>
        public string SpecifyModel => _specifyModel;

        /// <summary>
        /// 温度参数
        /// </summary>
        public float Temperature => _temperature;

        /// <summary>
        /// 最大输出 Token 数
        /// </summary>
        public int MaxTokens => _maxTokens;

        /// <summary>
        /// 最大 Tool 调用循环轮数
        /// </summary>
        public int MaxTurns => _maxTurns;

        /// <summary>
        /// 启用的工具分组。通过 <see cref="UniAIToolRegistry"/> 按组加载 <see cref="UniAITool"/> 标记的工具。
        /// </summary>
        public IReadOnlyList<string> ToolGroups => _toolGroups;

        /// <summary>
        /// 是否启用了任何工具分组
        /// </summary>
        public bool HasTools => _toolGroups is { Count: > 0 };

        /// <summary>
        /// 绑定的 MCP Server 配置列表
        /// </summary>
        public IReadOnlyList<McpServerConfig> McpServers => _mcpServers;

        /// <summary>
        /// 是否配置了 MCP Server
        /// </summary>
        public bool HasMcpServers => _mcpServers is { Count: > 0 };

        private static string TryReadPromptFile(string path)
        {
            try
            {
                var resolved = Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, "..", path);
                if (File.Exists(resolved))
                    return File.ReadAllText(resolved);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AgentDefinition] Failed to read SystemPrompt file '{path}': {e.Message}");
            }
            return null;
        }
    }
}

