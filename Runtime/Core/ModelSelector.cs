using System;
using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 维护可用模型列表和当前选择状态。
    /// </summary>
    public class ModelSelector
    {
        private readonly Func<string, bool> _modelFilter;
        private int _selectedModelIndex;
        private string _currentModelId;
        private string[] _modelNames;
        private List<string> _modelEntries;

        public string CurrentModelId => _currentModelId;
        public string[] ModelNames => _modelNames;
        public int SelectedModelIndex => _selectedModelIndex;

        public ModelSelector(string lastModelId, Func<string, bool> modelFilter = null)
        {
            _currentModelId = lastModelId;
            _modelFilter = modelFilter;
        }

        /// <summary>
        /// 从配置重建模型缓存。
        /// </summary>
        public void RebuildCache(AIConfig config)
        {
            _modelEntries = config.GetAllModels();
            if (_modelFilter != null)
                _modelEntries = _modelEntries.FindAll(modelId => _modelFilter(modelId));

            _modelNames = new string[_modelEntries.Count];
            for (int i = 0; i < _modelEntries.Count; i++)
            {
                var entry = ModelRegistry.Get(_modelEntries[i]);
                string vendor = !string.IsNullOrEmpty(entry?.Vendor) ? entry.Vendor : "Unknown";
                _modelNames[i] = $"{vendor}/{_modelEntries[i]}";
            }

            if (!string.IsNullOrEmpty(_currentModelId))
            {
                for (int i = 0; i < _modelEntries.Count; i++)
                {
                    if (_modelEntries[i] == _currentModelId)
                    {
                        _selectedModelIndex = i;
                        return;
                    }
                }
            }

            _selectedModelIndex = 0;
            _currentModelId = _modelEntries.Count > 0 ? _modelEntries[0] : null;
        }

        /// <summary>
        /// 按索引选择模型。
        /// </summary>
        public bool Select(int index)
        {
            if (_modelEntries == null || index < 0 || index >= _modelEntries.Count) return false;
            if (index == _selectedModelIndex) return false;

            _selectedModelIndex = index;
            _currentModelId = _modelEntries[index];
            return true;
        }

        /// <summary>
        /// 解析 Agent 实际应使用的模型。
        /// </summary>
        public string ResolveForAgent(AgentDefinition agent)
        {
            if (agent != null && !string.IsNullOrEmpty(agent.SpecifyModel))
                return agent.SpecifyModel;

            return _currentModelId ?? "";
        }

        /// <summary>
        /// 从会话中恢复模型选择。
        /// </summary>
        public void RestoreFromSession(ChatSession session)
        {
            if (string.IsNullOrEmpty(session.ModelId) || _modelEntries == null) return;

            for (int i = 0; i < _modelEntries.Count; i++)
            {
                if (_modelEntries[i] == session.ModelId)
                {
                    _selectedModelIndex = i;
                    _currentModelId = session.ModelId;
                    break;
                }
            }
        }

        /// <summary>
        /// 确保当前选中的模型有效。
        /// </summary>
        public string EnsureValid()
        {
            if (_modelEntries == null || _modelEntries.Count == 0)
                return null;

            if (_selectedModelIndex >= _modelEntries.Count)
                _selectedModelIndex = 0;

            _currentModelId = _modelEntries[_selectedModelIndex];
            return _currentModelId;
        }
    }
}
