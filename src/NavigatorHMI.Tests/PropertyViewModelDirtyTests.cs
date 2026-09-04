using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Q-2（2026-09-04 Check 审查修复回归）：属性面板写回标脏——
    /// RobotList 机器人绑定槽位改变量（RobotSlotBinding POCO 无订阅）必须显式 DirtyRequested（此前静默丢改动）；
    /// B1 标题星号顺序缺陷由 MarkProjectDirty 无条件刷标题根修（GUI 行为，此处覆盖 VM 层 DirtyRequested 契约）。
    /// </summary>
    public class PropertyViewModelDirtyTests
    {
        [Fact]
        public void 机器人绑定槽改变量_触发标脏()
        {
            var project = new HMIProject { Name = "标脏测试", ProjectFilePath = @"C:\dirty-test.hmiproj" };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var w = new WindowWidget { ObjectName = "robotList1", Type = WindowType.RobotList };
            project.Screens[0].Widgets.Add(w);
            project.Tags.Add(new Tag { Name = "R01_id", DataType = TagDataType.STRING });

            var vm = new PropertyViewModel { Project = project };
            int dirtyCalls = 0;
            vm.DirtyRequested = () => { dirtyCalls++; project.MarkDirty(); };   // 模拟 GUI MarkProjectDirty
            vm.SelectWidgets(new[] { w });

            project.ClearDirty();
            dirtyCalls = 0;
            vm.SetRobotSlotTag(0, "R01_id");

            // Q-2 回归：此前仅 BeforeModify（撤销快照），无 DirtyRequested → IsDirty 不置 → 关闭无提示丢改动
            Assert.True(dirtyCalls > 0, "SetRobotSlotTag 应触发 DirtyRequested");
            Assert.True(project.IsDirty);
            Assert.Equal("R01_id", w.RobotSlots[0].IdTag);
        }

        [Fact]
        public void 补槽位后赋值_也标脏()
        {
            // 首次配置（RobotSlots 空）→ 补槽位 + 赋值 —— 同样必须标脏
            var project = new HMIProject { Name = "标脏测试", ProjectFilePath = @"C:\dirty-test2.hmiproj" };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var w = new WindowWidget { ObjectName = "robotList1", Type = WindowType.RobotList };
            project.Screens[0].Widgets.Add(w);

            var vm = new PropertyViewModel { Project = project };
            int dirtyCalls = 0;
            vm.DirtyRequested = () => { dirtyCalls++; project.MarkDirty(); };
            vm.SelectWidgets(new[] { w });

            project.ClearDirty();
            dirtyCalls = 0;
            vm.SetRobotSlotTag(4, "R01_oper");   // 索引 4 → OperTag + 补槽（0-4）
            Assert.True(dirtyCalls > 0);
            Assert.True(project.IsDirty);
            Assert.Equal(5, w.RobotSlots.Count);
            Assert.Equal("R01_oper", w.RobotSlots[4].OperTag);
        }
    }
}
