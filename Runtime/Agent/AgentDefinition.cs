using System;
using System.Collections.Generic;
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
        /// 系统提示词（角色设定）
        /// </summary>
        public string SystemPrompt => _systemPrompt;

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
    }
}
