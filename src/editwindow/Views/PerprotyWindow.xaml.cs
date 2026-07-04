using NavigatorHMI.ViewModels;
using System.Windows;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 属性窗口，显示并编辑选中 Widget 的属性。
    /// </summary>
    public partial class PerprotyWindow : Window
    {
        public PerprotyWindow()
        {
            InitializeComponent();
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
