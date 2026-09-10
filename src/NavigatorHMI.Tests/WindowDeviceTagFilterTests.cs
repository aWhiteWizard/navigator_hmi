using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AB-4（2026-09-11）Check B3 回归：RobotList「绑定设备」两态过滤 —— `Tag.DeviceName` 在 PC 侧的
    /// 第一个消费方（另一消费方是 FW 采集路由）。
    /// 契约：未绑定设备 → 只列内部变量（DeviceName 空）；绑定设备 D → 列出 D 的变量 + 内部变量；
    /// 选择同时写回 `WindowWidget.BoundDevice` 并触发标脏；「（仅内部变量）」占位项等价于未绑定。
    /// 说明：GUI 侧「点六个区域」仍需人工冒烟，本测试锁定的是过滤语义本身。
    /// </summary>
    public class WindowDeviceTagFilterTests
    {
        private static (HMIProject project, WindowWidget w, PropertyViewModel vm) Build()
        {
            var project = new HMIProject { Name = "设备过滤测试", ProjectFilePath = @"C:\devfilter-test.hmiproj" };
            project.Devices.Add(new DeviceConfig { Name = "PT100", Protocol = ProtocolType.ModbusRTU });
            project.Devices.Add(new DeviceConfig { Name = "TCP-A", Protocol = ProtocolType.ModbusTCP });
            project.Tags.Add(new Tag { Name = "内部1" });
            project.Tags.Add(new Tag { Name = "PT100温度", DeviceName = "PT100" });
            project.Tags.Add(new Tag { Name = "TCPA温度", DeviceName = "TCP-A" });
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var w = new WindowWidget { ObjectName = "robotList1", Type = WindowType.RobotList };
            project.Screens[0].Widgets.Add(w);

            var vm = new PropertyViewModel { Project = project };
            vm.SelectWidgets(new[] { w });
            vm.RefreshWindowDeviceTags();
            return (project, w, vm);
        }

        [Fact]
        public void 未绑定设备_只列内部变量()
        {
            var (_, _, vm) = Build();
            Assert.Equal(new[] { "内部1" }, vm.WindowDeviceTags.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void 绑定设备_列出该设备变量与内部变量()
        {
            var (_, _, vm) = Build();
            vm.WindowBoundDevice = "PT100";
            Assert.Equal(new[] { "内部1", "PT100温度" }, vm.WindowDeviceTags.Select(t => t.Name).OrderBy(n => n).ToArray());

            vm.WindowBoundDevice = "TCP-A";
            Assert.Equal(new[] { "内部1", "TCPA温度" }, vm.WindowDeviceTags.Select(t => t.Name).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void 绑定设备_写回模型并通知变更()
        {
            var (project, w, vm) = Build();
            int undoSnapshots = 0;
            vm.BeforeModify = () => undoSnapshots++;
            var changed = new List<string?>();
            // 复刻 EditWindow.SubscribeModelDirty：View 层订阅 Widget.PropertyChanged 才落到 MarkProjectDirty
            // （模型层 POCO 自身不标脏——测试若不建这条订阅会误判为"静默丢改动"）
            w.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            project.ClearDirty();

            vm.WindowBoundDevice = "PT100";

            Assert.Equal("PT100", w.BoundDevice);                         // 模型写回
            Assert.Contains(nameof(WindowWidget.BoundDevice), changed);   // → View 层据此 MarkProjectDirty
            Assert.Equal(1, undoSnapshots);                              // 撤销快照
            Assert.False(project.IsDirty, "VM 层单独调用不假标脏（真实标脏由 View 订阅承担）");
        }

        [Fact]
        public void 占位项文本_等价于未绑定()
        {
            var (_, w, vm) = Build();
            vm.WindowBoundDevice = "PT100";
            vm.WindowBoundDevice = "（仅内部变量）";
            Assert.Equal("", w.BoundDevice);
            Assert.Equal(new[] { "内部1" }, vm.WindowDeviceTags.Select(t => t.Name).ToArray());
        }

        [Fact]
        public void 设备列表含占位项与全部设备名()
        {
            var (_, _, vm) = Build();
            Assert.Equal(new[] { "（仅内部变量）", "PT100", "TCP-A" }, vm.WindowDevices.ToArray());
        }
    }
}
