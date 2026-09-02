using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// O 轮批 C C-1 设备管理四子页重构测试（2026-09）——树「设备管理」容器父节点 + 四叶子：
    /// 双击叶子打开设备管理 Tab 并切对应子页（DevicePanelPage）；父节点无自开面板；
    /// 子页切换/关闭后树叶子高亮同步。
    /// </summary>
    public class DeviceTreeFourPagesTests
    {
        private static EditWindowViewModel CreateVm()
        {
            var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
            p.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template, Width = 800, Height = 480 });
            p.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 800, Height = 480 });
            return new EditWindowViewModel(p);
        }

        private static DeviceRootNode FindDeviceRoot(EditWindowViewModel vm)
        {
            foreach (var root in vm.TreeRoots)
            {
                if (root is DeviceRootNode d) return d;
                foreach (var c in root.Children)
                    if (c is DeviceRootNode dd) return dd;
            }
            return null!;
        }

        [Fact]
        public void 设备管理_容器父节点含四子节点()
        {
            var vm = CreateVm();
            var root = FindDeviceRoot(vm);
            Assert.NotNull(root);
            Assert.Equal(4, root.Children.Count);
            Assert.IsType<DeviceConnectNode>(root.Children[0]);
            Assert.IsType<RemoteControlNode>(root.Children[1]);
            Assert.IsType<DeviceUpgradeNode>(root.Children[2]);
            Assert.IsType<ProjectDownloadNode>(root.Children[3]);
            Assert.Equal("设备连接", root.Children[0].Name);
            Assert.Equal("远程控制", root.Children[1].Name);
            Assert.Equal("设备升级", root.Children[2].Name);
            Assert.Equal("工程下载", root.Children[3].Name);
            vm.Dispose();
        }

        [Theory]
        [InlineData(0, "设备连接")]
        [InlineData(1, "远程控制")]
        [InlineData(2, "设备升级")]
        [InlineData(3, "工程下载")]
        public void 叶子双击_开设备Tab并切对应子页(int page, string expectedLeaf)
        {
            var vm = CreateVm();
            var root = FindDeviceRoot(vm);

            // 走叶子 DoubleClickCommand 全链（审查 🟡：不直接调 VM.Open*——验证树接线真实路径）
            var leaf = root.Children[page];
            Assert.NotNull(leaf.DoubleClickCommand);
            leaf.DoubleClickCommand.Execute(null);

            Assert.True(vm.DeviceTabOpen, "设备管理 Tab 应打开");
            Assert.True(vm.DeviceActive, "设备管理应激活（互斥其它工具面板）");
            Assert.Equal(page, vm.DevicePanelPage);

            // 树高亮：对应叶子 IsCurrent（其它叶子不高亮）
            for (int i = 0; i < root.Children.Count; i++)
                Assert.Equal(i == page, root.Children[i].IsCurrent);
            vm.Dispose();
        }

        [Fact]
        public void 设备管理父节点_无自开命令()
        {
            var vm = CreateVm();
            var root = FindDeviceRoot(vm);
            Assert.Null(root.DoubleClickCommand);   // 容器父节点双击不打开单面板（C-1 语义）
            Assert.Equal("设备管理", root.Name);
            vm.Dispose();
        }

        [Fact]
        public void 打开其它面板_设备管理子页高亮复位()
        {
            var vm = CreateVm();
            vm.OpenDeviceUpgrade();
            Assert.True(vm.DeviceActive);

            vm.OpenVariableManager();   // 互斥：设备管理关闭
            Assert.False(vm.DeviceActive);
            var root = FindDeviceRoot(vm);
            Assert.All(root.Children, n => Assert.False(n.IsCurrent));
            vm.Dispose();
        }
    }
}
