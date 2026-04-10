using System;
using UnityEditor;

namespace UniAI.Editor
{
    /// <summary>
    /// Editor Agent 执行守卫 — 锁定程序集重载直到 Agent 完成。
    /// 仅使用 LockReloadAssemblies 阻止重载，不干扰 Auto Refresh 的文件变更检测。
    /// 使用 IDisposable 模式确保 Unlock 一定执行。
    ///
    /// 工具通过静态方法 <see cref="NotifyAssetsModified"/> 通知守卫"有文件被修改"，
    /// 守卫在 Dispose 时统一触发一次 AssetDatabase.Refresh，避免中途刷新引发重载。
    /// </summary>
    public sealed class EditorAgentGuard : IDisposable
    {
        [ThreadStatic] private static EditorAgentGuard _current;

        private bool _isLocked;
        private bool _isDirty;

        /// <summary>
        /// 当前线程上激活的守卫（仅 Unity 主线程有效）。
        /// </summary>
        public static EditorAgentGuard Current => _current;

        /// <summary>
        /// 工具修改了项目文件后调用，通知当前激活的守卫标记为脏。
        /// 无激活守卫时为 no-op（例如单元测试或工具被直接调用的场景）。
        /// </summary>
        public static void NotifyAssetsModified() => _current?.MarkDirty();

        public void Lock()
        {
            if (_isLocked) return;
            EditorApplication.LockReloadAssemblies();
            _isLocked = true;
            _current = this;
        }

        /// <summary>
        /// Tool 修改了文件时调用，标记需要在结束后刷新
        /// </summary>
        public void MarkDirty() => _isDirty = true;

        public void Dispose()
        {
            if (!_isLocked) return;
            _isLocked = false;
            if (ReferenceEquals(_current, this)) _current = null;
            EditorApplication.UnlockReloadAssemblies();
            if (_isDirty)
                EditorApplication.delayCall += AssetDatabase.Refresh;
        }
    }
}
