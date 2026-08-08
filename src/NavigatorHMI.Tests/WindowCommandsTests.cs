using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>W2 命令层测试：add_widget window / 用户命令 / 报警 W1 字段。</summary>
    public class WindowCommandsTests
    {
        private static (CommandService svc, HMIProject project) Create()
        {
            var project = new HMIProject();
            project.Screens.Add(new Screen { Name = "主", Width = 800, Height = 480, Type = ScreenType.Custom });
            project.Groups.Add(new UserGroup { Name = "管理员", Permissions = { UserPermission.UserManage, UserPermission.SystemSettings } });
            project.Groups.Add(new UserGroup { Name = "操作员", Permissions = { UserPermission.AlarmAck } });
            project.Groups.Add(new UserGroup { Name = "访客" });
            var svc = new CommandService(project);
            return (svc, project);
        }

        [Theory]
        [InlineData("userview", WindowType.UserView)]
        [InlineData("alarmview", WindowType.AlarmView)]
        [InlineData("robotlist", WindowType.RobotList)]
        public void add_widget_window_类型映射(string wt, WindowType expected)
        {
            var (svc, p) = Create();
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "window", ["window_type"] = wt });
            Assert.True(r.Success);
            var ww = Assert.IsType<WindowWidget>(p.Screens[0].Widgets[0]);
            Assert.Equal(expected, ww.Type);
        }

        [Fact]
        public void add_widget_window_未知类型拒绝()
        {
            var (svc, p) = Create();
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "window", ["window_type"] = "foo" });
            Assert.False(r.Success);
        }

        [Fact]
        public void create_user_哈希存储与组校验()
        {
            var (svc, p) = Create();
            var r = svc.Execute("create_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["password"] = "123456", ["group_name"] = "管理员" });
            Assert.True(r.Success);
            var u = Assert.Single(p.Users);
            Assert.Equal("张三", u.UserName);
            Assert.Equal(64, u.PasswordHash.Length);   // SHA256 Hex 长度
            Assert.NotEqual("123456", u.PasswordHash);   // 不存明文
        }

        [Fact]
        public void create_user_组不存在拒绝()
        {
            var (svc, p) = Create();
            var r = svc.Execute("create_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["password"] = "123", ["group_name"] = "不存在组" });
            Assert.False(r.Success);
            Assert.Equal("NOT_FOUND", r.ErrorCode);
        }

        [Fact]
        public void update_user_改名改密改组()
        {
            var (svc, p) = Create();
            svc.Execute("create_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["password"] = "123", ["group_name"] = "操作员" });
            var r = svc.Execute("update_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["new_user_name"] = "李四", ["new_password"] = "456", ["new_group_name"] = "管理员" });
            Assert.True(r.Success);
            var u = Assert.Single(p.Users);
            Assert.Equal("李四", u.UserName);
            Assert.Equal(64, u.PasswordHash.Length);
            Assert.NotEqual("456", u.PasswordHash);
            Assert.Equal("管理员", u.GroupName);
        }

        [Fact]
        public void delete_user_最后管理员保护()
        {
            var (svc, p) = Create();
            svc.Execute("create_user", new Dictionary<string, object?> { ["user_name"] = "admin", ["password"] = "1", ["group_name"] = "管理员" });
            var r = svc.Execute("delete_user", new Dictionary<string, object?> { ["user_name"] = "admin" });
            Assert.False(r.Success);
            Assert.Equal("BLOCKED", r.ErrorCode);
            Assert.Single(p.Users);
        }

        [Fact]
        public void create_alarm_W1字段落库()
        {
            var (svc, p) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            var r = svc.Execute("create_alarm", new Dictionary<string, object?>
            {
                ["name"] = "温度过高", ["tag_name"] = "温度", ["type"] = "High", ["threshold"] = "80",
                ["priority"] = "5", ["ack_group"] = "产线A", ["ack_required"] = "false",
            });
            Assert.True(r.Success);
            var a = Assert.Single(p.Alarms);
            Assert.Equal(5, a.Priority);
            Assert.Equal("产线A", a.AckGroup);
            Assert.False(a.AckRequired);   // 字符串 "false" 正确解析
            Assert.Equal(AlarmTriggerMode.Threshold, a.TriggerMode);
            Assert.Equal(AlarmCategory.User, a.Category);
        }

        [Fact]
        public void create_alarm_缺省ack_required为true()
        {
            var (svc, p) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            svc.Execute("create_alarm", new Dictionary<string, object?> { ["name"] = "A", ["tag_name"] = "温度", ["type"] = "High", ["threshold"] = "80" });
            var a = Assert.Single(p.Alarms);
            Assert.True(a.AckRequired);   // 缺省需确认（Definition DefaultValue=true 一致）
        }

        [Fact]
        public void update_user_组不存在时状态不变()
        {
            var (svc, p) = Create();
            svc.Execute("create_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["password"] = "123", ["group_name"] = "操作员" });
            var r = svc.Execute("update_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["new_user_name"] = "李四", ["new_group_name"] = "不存在组" });
            Assert.False(r.Success);
            var u = Assert.Single(p.Users);
            Assert.Equal("张三", u.UserName);   // 原子性：组校验失败不改名
            Assert.Equal("操作员", u.GroupName);
        }

        [Theory]
        [InlineData("severity", "Bogus")]
        [InlineData("priority", "abc")]
        [InlineData("trigger_mode", "Bogus")]
        public void update_alarm_非法字段不带delay_ms拒绝(string key, string bad)
        {
            var (svc, p) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            svc.Execute("create_alarm", new Dictionary<string, object?> { ["name"] = "A", ["tag_name"] = "温度", ["type"] = "High", ["threshold"] = "80" });
            var r = svc.Execute("update_alarm", new Dictionary<string, object?> { ["name"] = "A", [key] = bad });   // 不带 delay_ms
            Assert.False(r.Success);
            Assert.Equal("A", Assert.Single(p.Alarms).Name);   // 状态不变
        }

        [Fact]
        public void create_alarm_非法priority拒绝()
        {
            var (svc, p) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            var r = svc.Execute("create_alarm", new Dictionary<string, object?> { ["name"] = "A", ["tag_name"] = "温度", ["type"] = "High", ["threshold"] = "80", ["priority"] = "abc" });
            Assert.False(r.Success);
        }

        [Fact]
        public void update_alarm_W1字段更新()
        {
            var (svc, p) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            svc.Execute("create_alarm", new Dictionary<string, object?> { ["name"] = "A", ["tag_name"] = "温度", ["type"] = "High", ["threshold"] = "80" });
            var r = svc.Execute("update_alarm", new Dictionary<string, object?> { ["name"] = "A", ["trigger_mode"] = "OnRising", ["priority"] = "9" });
            Assert.True(r.Success);
            var a = Assert.Single(p.Alarms);
            Assert.Equal(AlarmTriggerMode.OnRising, a.TriggerMode);
            Assert.Equal(9, a.Priority);
        }
    }
}
