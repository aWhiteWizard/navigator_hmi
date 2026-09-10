using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AB-4（2026-09-11）Check B3 回归：RobotList「绑定设备」两态过滤 —— `Tag.DeviceName` 在 PC 侧的
    /// 第一个消费方（另一消费方是 FW 采集路由）。
    /// 契约：未绑定设备 → 只列内部变量（DeviceName 空）；绑定设备 D → 列出 D 的变量 + 内部变量；
    /// 选择同时写回 `WindowWidget.BoundDevice`；「（仅内部变量）」占位项等价于未绑定。
    /// 说明：GUI 侧「点六个区域」仍需人工冒烟，本测试锁定的是过滤语义本身。
    /// <para>
    /// **脏标记口径（2026-09-11 审查确认，二分法）**：`Widget`/`Screen` **在 View 层订阅面上**
    /// （`EditWindow.SubscribeModelDirty`）⇒ 写这两个对象**不需要也不应**在 VM 里再补 `DirtyRequested`，
    /// 此类"只测 VM"的用例可以**复刻订阅**来验证标脏；而**不在订阅面上的嵌套 POCO / POCO 内 List**
    /// （如 `RobotSlotBinding`、顶点 `List`）**必须**显式 `DirtyRequested`，此类用例**必须直接断言
    /// `DirtyRequested`**（照搬"复刻订阅"会把缺标脏的真缺陷掩盖成通过）。
    /// </para>
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
        public void 绑定设备_写回模型并通知变更_标脏由View订阅承担()
        {
            var (project, w, vm) = Build();
            int undoSnapshots = 0;
            vm.BeforeModify = () => undoSnapshots++;
            var changed = new List<string?>();
            w.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            project.ClearDirty();

            vm.WindowBoundDevice = "PT100";

            // 第一段（现状锚定）：只观察、不标脏 ⇒ VM 层单独调用不假标脏
            Assert.Equal("PT100", w.BoundDevice);                         // 模型写回
            Assert.Contains(nameof(WindowWidget.BoundDevice), changed);   // PropertyChanged 已发
            Assert.Equal(1, undoSnapshots);                              // 撤销快照
            Assert.False(project.IsDirty, "VM 层单独调用不假标脏");

            // 第二段：复刻 EditWindow.SubscribeModelDirty（Canvas.cs:113-140）——含两个瞬态排除项
            project.ClearDirty();
            w.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Widget.IsSelected)) return;
                if (e.PropertyName == nameof(Widget.DisplayText)) return;
                project.MarkDirty();
            };
            vm.WindowBoundDevice = "TCP-A";
            Assert.Equal("TCP-A", w.BoundDevice);
            Assert.True(project.IsDirty, "有了 View 层订阅后，模型写回必须导致工程标脏（防关闭静默丢失）");
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
