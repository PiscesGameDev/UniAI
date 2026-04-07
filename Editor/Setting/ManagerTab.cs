namespace UniAI.Editor
{
    /// <summary>
    /// UniAIManagerWindow 的 Tab 页面基类。
    /// 子类实现具体页面的初始化、绘制、销毁逻辑。
    /// </summary>
    internal abstract class ManagerTab
    {
        /// <summary>导航栏显示名称（用于 Tooltip）</summary>
        public abstract string TabName { get; }

        /// <summary>导航栏图标字符（Unicode）</summary>
        public abstract string TabIcon { get; }

        /// <summary>Tab 排序权重（越小越靠上）</summary>
        public virtual int Order => 0;

        /// <summary>所属窗口引用（Initialize 时注入）</summary>
        protected UniAIManagerWindow Window { get; private set; }

        /// <summary>共享配置引用</summary>
        protected AIConfig Config => Window.Config;

        /// <summary>初始化（窗口 OnEnable 时调用）</summary>
        public void Initialize(UniAIManagerWindow window)
        {
            Window = window;
            OnInit();
        }

        /// <summary>子类初始化逻辑</summary>
        protected virtual void OnInit() { }

        /// <summary>绘制页面内容</summary>
        public abstract void OnGUI(float width, float height);

        /// <summary>确保样式初始化</summary>
        public virtual void EnsureStyles() { }

        /// <summary>销毁（窗口 OnDisable 时调用）</summary>
        public virtual void OnDestroy() { }

        /// <summary>页面需要保存时的回调</summary>
        public virtual void OnSave() { }
    }
}
