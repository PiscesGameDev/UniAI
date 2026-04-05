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
        [SerializeField] private string _agentName = "助手";
        [SerializeField] private string _description;
        [SerializeField, TextArea(3, 10)] private string _systemPrompt;
        [SerializeField] private Texture2D _icon;
        [SerializeField, Range(0f, 1f)] private float _temperature = 0.7f;
        [SerializeField] private int _maxTokens = 4096;
        [SerializeField, Range(1, 50)] private int _maxTurns = 10;
        [SerializeField] private List<AIToolAsset> _tools = new();

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
        /// 注册的工具列表
        /// </summary>
        public IReadOnlyList<AIToolAsset> Tools => _tools;

        /// <summary>
        /// 是否包含工具
        /// </summary>
        public bool HasTools => _tools is { Count: > 0 };

        /// <summary>
        /// 创建内置默认 Agent（无 Tool，通用聊天助手，单轮对话）
        /// </summary>
        public static AgentDefinition CreateDefault()
        {
            var agent = CreateInstance<AgentDefinition>();
            agent.name = "DefaultAgent";
            agent._agentName = "Uni";
            agent._systemPrompt =
                "你是一位乐于助人的 Unity 游戏开发助手. " +
                "请提供简洁明了的答案. 当展示代码时，请使用 C# 和 Unity 最佳实践.";
            agent._temperature = 0.7f;
            agent._maxTokens = 4096;
            agent._maxTurns = 1;
            return agent;
        }
    }
}
