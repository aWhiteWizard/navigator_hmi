using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Tests;

/// <summary>任务A 窗口控件拆三：add-widget 三类型 → WindowWidget 单类 + Type 判别（protobuf-net 不支持多级继承，方案 2）。</summary>
public class WindowSplitTests
{
    private static (HMIProject project, CommandService svc) Setup()
    {
        var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
        p.Screens.Add(new Screen { Name = "测试", Width = 800, Height = 480 });
        return (p, new CommandService(p));
    }

    [Fact]
    public void AddWidget_UsrView_建WindowWidgetUserView()
    {
        var (p, svc) = Setup();
        var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["widget_type"] = "userview", ["screen_name"] = "测试" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(WindowType.UserView, ((WindowWidget)p.Screens[0].Widgets[0]).Type);
    }

    [Fact]
    public void AddWidget_AlarmView_建WindowWidgetAlarmView()
    {
        var (p, svc) = Setup();
        var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["widget_type"] = "alarmview", ["screen_name"] = "测试" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(WindowType.AlarmView, ((WindowWidget)p.Screens[0].Widgets[0]).Type);
    }

    [Fact]
    public void AddWidget_RobotList_建WindowWidgetRobotList()
    {
        var (p, svc) = Setup();
        var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["widget_type"] = "robotlist", ["screen_name"] = "测试" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(WindowType.RobotList, ((WindowWidget)p.Screens[0].Widgets[0]).Type);
    }

    [Fact]
    public void AddWidget_旧Window_建WindowWidget兼容()
    {
        var (p, svc) = Setup();
        var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["widget_type"] = "window", ["window_type"] = "alarmview", ["screen_name"] = "测试" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.IsType<WindowWidget>(p.Screens[0].Widgets[0]);
        Assert.Equal(WindowType.AlarmView, ((WindowWidget)p.Screens[0].Widgets[0]).Type);
    }

    [Fact]
    public void WindowWidget_序列化Type保持()
    {
        var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
        p.Screens.Add(new Screen { Name = "测试", Width = 800, Height = 480 });
        var svc = new CommandService(p);
        svc.Execute("add_widget", new Dictionary<string, object?> { ["widget_type"] = "robotlist", ["screen_name"] = "测试" });
        // 序列化→反序列化：WindowWidget 单类 + Type 枚举保持（方案 2）
        using var ms = new System.IO.MemoryStream();
        ProtoBuf.Serializer.Serialize(ms, p);
        ms.Position = 0;
        var p2 = ProtoBuf.Serializer.Deserialize<HMIProject>(ms);
        Assert.Equal(WindowType.RobotList, ((WindowWidget)p2.Screens[0].Widgets[0]).Type);
    }

    // W2：属性面板不再提供类型切换（用户 2026-08-09 拍板——类型由创建时决定，三个按钮分别放置三种窗口控件）——
    // 原「属性面板切Type_默认标题联动 / 自定义标题不联动」两测试随 WindowTypeName 联动逻辑一并移除；
    // Type 保持创建时值由上方序列化测试覆盖。
}
