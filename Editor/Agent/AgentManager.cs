using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UniAI.Editor
{
    /// <summary>
    /// Agent 管理器（Editor-only）— 资产扫描、创建、删除。
    /// 扫描结果自动同步到 <see cref="AgentRegistry"/>。
    /// </summary>
    public static class AgentManager
    {
        /// <summary>
        /// 扫描项目中所有 AgentDefinition 资产并同步到 AgentRegistry
        /// </summary>
        public static List<AgentDefinition> GetAllAgents()
        {
            var agents = ScanAssets();
            AgentRegistry.Clear();
            AgentRegistry.Register(agents);
            return agents;
        }

        /// <summary>
        /// 创建新的 AgentDefinition 资产
        /// </summary>
        public static AgentDefinition CreateNewAgent(string dir, string name)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
                string folder = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(parent))
                    AssetDatabase.CreateFolder(parent, folder);
            }

            string path = $"{dir}/{name}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            var agent = ScriptableObject.CreateInstance<AgentDefinition>();
            AssetDatabase.CreateAsset(agent, path);
            AssetDatabase.SaveAssets();

            AgentRegistry.Register(agent);
            return agent;
        }

        /// <summary>
        /// 删除 AgentDefinition 资产
        /// </summary>
        public static void DeleteAgent(AgentDefinition agent)
        {
            string path = AssetDatabase.GetAssetPath(agent);
            if (!string.IsNullOrEmpty(path))
            {
                AgentRegistry.Unregister(agent);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.SaveAssets();
            }
        }

        private static List<AgentDefinition> ScanAssets()
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
    }
}
