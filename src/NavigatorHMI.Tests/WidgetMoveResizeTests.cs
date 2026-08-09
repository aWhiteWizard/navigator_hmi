using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>批 2（#3）回归：move_widget / resize_widget 单参数调用时未传的轴/尺寸保持现值（AI 单轴移动/单边调整不得归零另一侧）。</summary>
public class WidgetMoveResizeTests
{
    private static (HMIProject project, CommandService svc, ButtonWidget w) Setup()
    {
        var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
        p.Screens.Add(new Screen { Name = "测试", Width = 800, Height = 480 });
        var svc = new CommandService(p);
        var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "测试", ["widget_type"] = "button", ["x"] = "100", ["y"] = "200" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        return (p, svc, (ButtonWidget)p.Screens[0].Widgets[0]);
    }

    [Fact]
    public void moveWidget只传x_y保持现值()
    {
        var (_, svc, w) = Setup();
        var r = svc.Execute("move_widget", new Dictionary<string, object?> { ["screen_name"] = "测试", ["widget_name"] = w.ObjectName, ["x"] = "300" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(300, w.X, 0.5);
        Assert.Equal(200, w.Y, 0.5);   // 未传 y 保持现值（不归 0）
    }

    [Fact]
    public void resizeWidget只传width_height保持现值()
    {
        var (_, svc, w) = Setup();
        var r = svc.Execute("resize_widget", new Dictionary<string, object?> { ["screen_name"] = "测试", ["widget_name"] = w.ObjectName, ["width"] = "250" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(250, w.Width, 0.5);
        Assert.Equal(40, w.Height, 0.5);   // 未传 height 保持现值（不归 0）
    }
}
