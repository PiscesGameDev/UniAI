using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Agent 管理器 — 扫描项目中所有 AgentDefinition 资产
    /// </summary>
    public static class AgentManager
    {
        private static AgentDefinition _defaultAgent;
        private const string DefaultAgentIconPath = "Assets/UniAI/Editor/Icons/agent-default.png";

        /// <summary>
        /// 内置默认 Agent（仅供 AIAgentWindow 展示只读预览用；
        /// 纯 Chat 场景请使用 ChatRunner，不要通过这个 Agent 路由）
        /// </summary>
        public static AgentDefinition DefaultAgent
        {
            get
            {
                if (_defaultAgent != null) return _defaultAgent;
                _defaultAgent = AgentDefinition.CreateDefault();
                var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultAgentIconPath);
                if (icon != null)
                {
                    _defaultAgent.Icon = icon;
                }
                return _defaultAgent;
            }
        }

        /// <summary>
        /// 扫描项目中所有 AgentDefinition 资产（不含内置默认 Agent）
        /// </summary>
        public static List<AgentDefinition> GetAllAgents()
        {
            var agents = new List<AgentDefinition>();

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
