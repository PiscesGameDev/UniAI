using System;
using UnityEditor;

namespace UniAI.Editor
{
    /// <summary>
    /// Editor Agent 执行守卫 — 锁定程序集重载直到 Agent 完成。
    /// 仅使用 LockReloadAssemblies 阻止重载，不干扰 Auto Refresh 的文件变更检测。
    /// 使用 IDisposable 模式确保 Unlock 一定执行。
    /// </summary>
    public sealed class EditorAgentGuard : IDisposable
    {
        private bool _isLocked;
        private bool _isDirty;

        public void Lock()
        {
            if (_isLocked) return;
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
            if (_isDirty)
                EditorApplication.delayCall += () => AssetDatabase.Refresh();
        }
    }
}
