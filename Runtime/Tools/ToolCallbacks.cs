namespace UniAI.Tools
{
    /// <summary>
    /// 工具回调 — Editor 侧注入平台相关行为（如 AssetDatabase 刷新）
    /// </summary>
    public static class ToolCallbacks
    {
        /// <summary>
        /// 工具修改了项目资产后的回调（Editor 注入 AssetDatabase 刷新逻辑）
        /// </summary>
        public static System.Action OnAssetsModified;
    }
}
