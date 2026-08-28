using System.Windows.Controls;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 设备管理面板（K 循环 K-4）——DataContext 注入 <see cref="DevicePanelViewModel"/>（EditWindow 构造时设置）。
    /// </summary>
    public partial class DevicePanelView : UserControl
    {
        public DevicePanelView()
        {
            InitializeComponent();
        }
    }
}
