using System.Windows.Controls;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 设备管理面板（O 轮批 C C-1 四子页重构）——DataContext 注入 <see cref="DevicePanelViewModel"/>（EditWindow 构造时设置）。
    /// 四子页：设备连接 / 远程控制 / 设备升级 / 工程下载（TabControl SelectedIndex 绑 EditWindowViewModel.DevicePanelPage）。
    /// </summary>
    public partial class DevicePanelView : UserControl
    {
        public DevicePanelView()
        {
            InitializeComponent();
        }

        /// <summary>「浏览…」手动选 .fw（O-C C-4：覆盖固件文件夹自动匹配——命令层 --file 语义；选中后写入固件列表并选中）。</summary>
        private void BrowseFirmware_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not DevicePanelViewModel vm) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择固件文件（.fw）",
                Filter = "固件包 (*.fw)|*.fw|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                vm.SetManualFirmware(dlg.FileName);
            }
        }

        /// <summary>「浏览…」选固件库根目录（Check 修复 2026-09：目录编辑用文件夹选择器而非纯手输）——OpenFolderDialog
        /// 写入 VM.FirmwareFolderText（setter 触发切换固件库根 + 持久化 + 自动刷新列表）。</summary>
        private void BrowseFirmwareFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not DevicePanelViewModel vm) return;
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择固件库根目录（直接存放 NavigatorHMI_7inch_vX.Y.Z.fw 等固件）",
                Multiselect = false
            };
            // 初始目录 = 当前固件库根（存在时）——OpenFolderDialog 需绝对路径
            var cur = vm.FirmwareFolderText;
            if (!string.IsNullOrWhiteSpace(cur) && System.IO.Directory.Exists(cur))
                dlg.InitialDirectory = cur;
            if (dlg.ShowDialog() == true)
            {
                vm.FirmwareFolderText = dlg.FolderName;
            }
        }
    }
}
