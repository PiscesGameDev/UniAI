using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Agent 管理器 — 扫描项目中所有 AgentDefinition 资产 + 提供内置默认 Agent
    /// </summary>
    public static class AgentManager
    {
        private static AgentDefinition _defaultAgent;
        private const string DefaultAgentIconPath = "Assets/UniAI/Editor/Icons/agent-default.png";

        /// <summary>
        /// 内置默认 Agent（无 Tool，通用聊天助手）
        /// </summary>
        public static AgentDefinition DefaultAgent
        {
            get
            {
                if (_defaultAgent == null)
                {
                    _defaultAgent = ScriptableObject.CreateInstance<AgentDefinition>();
                    _defaultAgent.name = "DefaultAgent";
                    // 通过 SerializedObject 设置私有字段
                    var so = new SerializedObject(_defaultAgent);
                    so.FindProperty("_agentName").stringValue = "默认助手";
                    so.FindProperty("_systemPrompt").stringValue =
                        "You are a helpful Unity game development assistant. " +
                        "Provide clear, concise answers. When showing code, use C# and Unity best practices.";
                    so.FindProperty("_temperature").floatValue = 0.7f;
                    so.FindProperty("_maxTokens").intValue = 4096;
                    so.FindProperty("_maxTurns").intValue = 1;
                    // 加载默认图标（如果存在）
                    var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultAgentIconPath);
                    if (icon != null)
                        so.FindProperty("_icon").objectReferenceValue = icon;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                return _defaultAgent;
            }
        }

        /// <summary>
        /// 获取所有可用 Agent（默认 + 用户自定义）
        /// </summary>
        public static List<AgentDefinition> GetAllAgents()
        {
            var agents = new List<AgentDefinition> { DefaultAgent };

            // 扫描项目中所有 AgentDefinition 资产
            var guids = AssetDatabase.FindAssets("t:AgentDefinition");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var agent = AssetDatabase.LoadAssetAtPath<AgentDefinition>(path);
                if (agent != null)
                    agents.Add(agent);
            }

            return agents;
        }

        /// <summary>
        /// 获取 Agent 显示名称列表
        /// </summary>
        public static string[] GetAgentNames(List<AgentDefinition> agents)
        {
            return agents.Select(a => a.AgentName ?? a.name).ToArray();
        }
    }
}
