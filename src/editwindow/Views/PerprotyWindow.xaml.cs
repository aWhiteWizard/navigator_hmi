using NavigatorHMI.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 属性窗口，显示并编辑选中 Widget 的属性。
    /// </summary>
    public partial class PerprotyWindow : Window
    {
        /// <summary>ESC 键按下时的回调，由 EditWindow 注入清除选中逻辑</summary>
        public Action? OnEscapePressed { get; set; }
        public PerprotyWindow()
        {
            InitializeComponent();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    OnEscapePressed?.Invoke();
                    e.Handled = true;
                }
            };
        }

        /// <summary>
        /// 设置属性窗口的 ViewModel 并绑定数据上下文。
        /// </summary>
        public void SetViewModel(PropertyViewModel viewModel)
        {
            this.DataContext = viewModel;
        }
    }
}
