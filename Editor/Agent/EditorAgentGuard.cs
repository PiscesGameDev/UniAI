using System;
using UnityEditor;

namespace UniAI.Editor
{
    /// <summary>
    /// Editor Agent 执行守卫 — 锁定编译直到 Agent 完成。
    /// 双重保护：DisallowAutoRefresh 阻止文件变更检测，LockReloadAssemblies 阻止程序集重载。
    /// 使用 IDisposable 模式确保 Unlock 一定执行。
    /// </summary>
    public sealed class EditorAgentGuard : IDisposable
    {
        private bool _isLocked;
        private bool _isDirty;

        public void Lock()
        {
            if (_isLocked) return;
            AssetDatabase.DisallowAutoRefresh();
            EditorApplication.LockReloadAssemblies();
            _isLocked = true;
        }

        /// <summary>
        /// Tool 修改了文件时调用，标记需要在结束后刷新
        /// </summary>
        public void MarkDirty() => _isDirty = true;

        public void Dispose()
        {
            if (!_isLocked) return;
            _isLocked = false;
            EditorApplication.UnlockReloadAssemblies();
            AssetDatabase.AllowAutoRefresh();
            if (_isDirty)
                EditorApplication.delayCall += () => AssetDatabase.Refresh();
        }
    }
}
