namespace UniAI.Tools
{
    /// <summary>
    /// 工具全局配置 — Editor 侧可在启动时覆盖默认值
    /// </summary>
    public static class ToolConfig
    {
        /// <summary>
        /// Tool 单次返回内容的最大字符数
        /// </summary>
        public static int MaxOutputChars = 50000;

        /// <summary>
        /// 搜索最大匹配数
        /// </summary>
        public static int SearchMaxMatches = 100;
    }
}
