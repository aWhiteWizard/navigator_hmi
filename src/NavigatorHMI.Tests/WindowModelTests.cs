using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>W5/W6 窗口控件序列化 round-trip + 模型/过滤测试。</summary>
    public class WindowModelTests
    {
        [Fact]
        public void WindowWidget_序列化round_trip字段完整()
        {
            var w = new WindowWidget
            {
                ObjectName = "机器人列表1",
                X = 10, Y = 20, Width = 240, Height = 180,
                Type = WindowType.RobotList,
                Title = "我的机器人",
                ShowTitleBar = false,
                FillColor = "#FFFFFF",
                BorderColor = "#000000",
                ShowUserName = false, ShowRole = false, ShowMode = false,
                ShowHistory = true,
                CardWidth = 130, CardHeight = 120,
                CardShowNumber = false, CardShowStatus = false, CardShowLocation = false,
                SelectedTag = "选中变量",
                BoundDevice = "PLC1",
            };
            w.RobotSlots.Add(new RobotSlotBinding { IdTag = "R01_id", StatusTag = "R01_status", LocationTag = "R01_loc", DetailTag = "R01_detail", OperTag = "R01_oper" });

            using var ms1 = new System.IO.MemoryStream(); ProtoBuf.Serializer.Serialize(ms1, w); var bytes = ms1.ToArray();
            var back = ProtoBuf.Serializer.Deserialize<WindowWidget>(new System.IO.MemoryStream(bytes));

            Assert.Equal(WindowType.RobotList, back.Type);
            Assert.Equal("我的机器人", back.Title);
            Assert.False(back.ShowTitleBar);
            Assert.Equal("#FFFFFF", back.FillColor);
            Assert.Equal("#000000", back.BorderColor);
            Assert.False(back.ShowUserName);
            Assert.False(back.ShowRole);
            Assert.False(back.ShowMode);
            Assert.True(back.ShowHistory);
            Assert.Equal(130, back.CardWidth);
            Assert.Equal(120, back.CardHeight);
            Assert.False(back.CardShowNumber);
            Assert.False(back.CardShowStatus);
            Assert.False(back.CardShowLocation);
            Assert.Equal("选中变量", back.SelectedTag);
            Assert.Equal("PLC1", back.BoundDevice);
            var slot = Assert.Single(back.RobotSlots);
            Assert.Equal("R01_id", slot.IdTag);
            Assert.Equal("R01_status", slot.StatusTag);
            Assert.Equal("R01_loc", slot.LocationTag);
            Assert.Equal("R01_detail", slot.DetailTag);
            Assert.Equal("R01_oper", slot.OperTag);
        }

        [Fact]
        public void WindowWidget_继承序列化_ProtoInclude116()
        {
            var project = new HMIProject();
            project.Screens.Add(new Screen { Name = "主", Width = 800, Height = 480, Type = ScreenType.Custom });
            project.Screens[0].Widgets.Add(new WindowWidget { Type = WindowType.AlarmView, Title = "报警" });
            using var ms2 = new System.IO.MemoryStream(); ProtoBuf.Serializer.Serialize(ms2, project); var bytes = ms2.ToArray();
            var back = ProtoBuf.Serializer.Deserialize<HMIProject>(new System.IO.MemoryStream(bytes));
            var w = Assert.IsType<WindowWidget>(back.Screens[0].Widgets[0]);
            Assert.Equal(WindowType.AlarmView, w.Type);
        }

        [Fact]
        public void 用户系统_序列化round_trip()
        {
            var p = new HMIProject();
            p.Users.Add(new UserAccount { UserName = "张三", PasswordHash = new string('A', 64), GroupName = "管理员" });
            p.Groups.Add(new UserGroup { Name = "管理员", Permissions = { UserPermission.UserManage, UserPermission.SystemSettings } });
            p.Security.MinPasswordLength = 8;
            p.Security.RequireSpecial = true;
            using var ms3 = new System.IO.MemoryStream(); ProtoBuf.Serializer.Serialize(ms3, p); var bytes = ms3.ToArray();
            var back = ProtoBuf.Serializer.Deserialize<HMIProject>(new System.IO.MemoryStream(bytes));
            var u = Assert.Single(back.Users);
            Assert.Equal("张三", u.UserName);
            Assert.Equal("管理员", u.GroupName);
            var g = Assert.Single(back.Groups);
            Assert.Contains(UserPermission.UserManage, g.Permissions);
            Assert.Equal(8, back.Security.MinPasswordLength);
            Assert.True(back.Security.RequireSpecial);
        }

        [Fact]
        public void WindowWidget_绑定兼容性Any()
        {
            // 窗口控件基类 BoundTag 可绑任意类型（显示由内部绑定组驱动——不限制）
            var w = new WindowWidget();
            Assert.Equal(TagRequirement.Any, TagCompatibility.GetRequirement(w));
        }
    }
}
