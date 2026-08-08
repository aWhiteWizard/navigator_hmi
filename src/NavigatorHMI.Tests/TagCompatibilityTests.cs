using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>P2-2 绑定类型兼容测试：DateTime 控件限绑 DATETIME；数值/字符串控件不受影响。</summary>
    public class TagCompatibilityTests
    {
        private static (CommandService svc, HMIProject project, Screen screen) Create()
        {
            var project = new HMIProject();
            project.Screens.Add(new Screen { Name = "主", Width = 800, Height = 480, Type = ScreenType.Custom });
            var svc = new CommandService(project);
            return (svc, project, project.Screens[0]);
        }

        [Fact]
        public void DateTime控件_绑非DATETIME拒绝()
        {
            var (svc, p, s) = Create();
            svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "datetime" });
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            var r = svc.Execute("bind_tag", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_name"] = "datetime_1", ["tag_name"] = "温度" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_TYPE", r.ErrorCode);
        }

        [Fact]
        public void DateTime控件_绑DATETIME通过()
        {
            var (svc, p, s) = Create();
            svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "datetime" });
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "时间", ["data_type"] = "DATETIME" });
            var r = svc.Execute("bind_tag", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_name"] = "datetime_1", ["tag_name"] = "时间" });
            Assert.True(r.Success);
            Assert.Equal("时间", p.Screens[0].Widgets[0].BoundTag);
        }

        [Fact]
        public void 数值控件_绑DATETIME拒绝()
        {
            var (svc, p, s) = Create();
            svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "numeric" });
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "时间", ["data_type"] = "DATETIME" });
            var r = svc.Execute("bind_tag", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_name"] = "numeric_1", ["tag_name"] = "时间" });
            Assert.False(r.Success);   // 数值显示控件不接受 DATETIME
        }

        [Fact]
        public void DateTime控件_addWidget时bound_tag校验()
        {
            var (svc, p, s) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "datetime", ["bound_tag"] = "温度" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_TYPE", r.ErrorCode);
        }
    }
}
