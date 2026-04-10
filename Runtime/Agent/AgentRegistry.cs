using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// Agent 注册表 — 运行时静态注册表，按 Id 查找/注册/注销 AgentDefinition。
    /// Editor 环境下由 AgentManager 启动时自动扫描注册；
    /// Runtime 环境下可手动 Register/Unregister。
    /// </summary>
    public static class AgentRegistry
    {
        private static readonly Dictionary<string, AgentDefinition> _agents = new();
        private static readonly List<AgentDefinition> _agentList = new();

        /// <summary>
        /// Agent 注册表中的所有 AgentDefinition 列表
        /// </summary>
        public static IReadOnlyList<AgentDefinition> AgentList => _agentList;
        
        /// <summary>按 Id 查找 Agent，未找到返回 null</summary>
        public static bool TryGet(string id, out AgentDefinition agent)
        {
            agent = null;
            if (string.IsNullOrEmpty(id)) return false;
            return _agents.TryGetValue(id, out agent);
        }
        
        /// <summary>注册 Agent（Id 冲突时覆盖）</summary>
        public static void Register(AgentDefinition agent)
        {
            if (agent == null) return;
            _agents[agent.Id] = agent;
            _agentList.Add(agent);
        }

        /// <summary>批量注册</summary>
        public static void Register(IEnumerable<AgentDefinition> agents)
        {
            if (agents == null) return;
            foreach (var agent in agents)
                Register(agent);
        }

        /// <summary>注销指定 Agent</summary>
        public static void Unregister(string id)
        {
            if (!string.IsNullOrEmpty(id)  && TryGet(id, out var agent))
            {
                _agentList.Remove(agent);
                _agents.Remove(id);
            }
        }

        /// <summary>注销指定 Agent</summary>
        public static void Unregister(AgentDefinition agent)
        {
            if (agent != null)
                Unregister(agent.Id);
        }

        /// <summary>清空所有注册</summary>
        public static void Clear()
        {
            _agents.Clear();
            _agentList.Clear();
        }

        /// <summary>已注册数量</summary>
        public static int Count => _agents.Count;
    }
}
