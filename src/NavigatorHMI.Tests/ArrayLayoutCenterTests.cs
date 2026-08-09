using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>P3-3 array_layout center_start（阵列以画面中心为起点）测试。</summary>
public class ArrayLayoutCenterTests
{
    private static (HMIProject project, CommandService svc, Screen screen) Setup()
    {
        var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
        p.Screens.Add(new Screen { Name = "测试", Width = 800, Height = 480 });
        var svc = new CommandService(p);
        var s = p.Screens[0];
        s.Widgets.Add(new ButtonWidget { ObjectName = "b1", Width = 100, Height = 40 });
        s.Widgets.Add(new ButtonWidget { ObjectName = "b2", Width = 100, Height = 40 });
        return (p, svc, s);
    }

    [Fact]
    public void CenterStart_第一控件中心在画面中心()
    {
        var (_, svc, s) = Setup();
        var r = svc.Execute("array_layout", new Dictionary<string, object?>
        {
            ["screen_name"] = "测试",
            ["widgets"] = "b1,b2",
            ["mode"] = "rect",
            ["cols"] = "1",
            ["rows"] = "2",
            ["spacing_y"] = "80",
            ["center_start"] = "true",
        });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        var b1 = (ButtonWidget)s.Widgets[0];
        // 第一控件中心 ≈ 画面中心（(800-100)/2=350, (480-40)/2=220）
        Assert.Equal(350, b1.X, 0.5);
        Assert.Equal(220, b1.Y, 0.5);
        // 第二控件沿 y 下移 80
        var b2 = (ButtonWidget)s.Widgets[1];
        Assert.Equal(300, b2.Y, 0.5);
    }

    [Fact]
    public void 不传CenterStart_保持默认起点()
    {
        var (_, svc, s) = Setup();
        svc.Execute("array_layout", new Dictionary<string, object?>
        {
            ["screen_name"] = "测试",
            ["widgets"] = "b1,b2",
            ["mode"] = "rect",
            ["cols"] = "1",
            ["rows"] = "2",
            ["spacing_y"] = "80",
        });
        var b1 = (ButtonWidget)s.Widgets[0];
        // 默认 start 0 → 起点 0 → 左上角 (0,0) 钳制 0（W3：起点=左上角，不再减半）
        Assert.Equal(0, b1.X, 0.5);
        Assert.Equal(0, b1.Y, 0.5);
    }

    [Fact]
    public void 圆形阵列_中心恒在圆周()
    {
        var (_, svc, s) = Setup();
        var r = svc.Execute("array_layout", new Dictionary<string, object?>
        {
            ["screen_name"] = "测试",
            ["widgets"] = "b1,b2",
            ["mode"] = "circle",
            ["center_x"] = "400",
            ["center_y"] = "240",
            ["radius"] = "150",
        });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        // W3 回归：圆形阵列恒为中心语义（圆周点=控件中心）——b1 圆心 0° 圆周点 (550,240)，左上角 = 圆周点-宽高/2
        var b1 = (ButtonWidget)s.Widgets[0];
        Assert.Equal(500, b1.X, 0.5);   // 550 - 100/2
        Assert.Equal(220, b1.Y, 0.5);   // 240 - 40/2
        // b2 在 180° 圆周点 (250,240) → 左上角 (200,220)
        var b2 = (ButtonWidget)s.Widgets[1];
        Assert.Equal(200, b2.X, 0.5);
        Assert.Equal(220, b2.Y, 0.5);
    }

    [Fact]
    public void 显式起点_第一控件左上角在起点()
    {
        var (_, svc, s) = Setup();
        var r = svc.Execute("array_layout", new Dictionary<string, object?>
        {
            ["screen_name"] = "测试",
            ["widgets"] = "b1,b2",
            ["mode"] = "rect",
            ["cols"] = "1",
            ["rows"] = "2",
            ["start_x"] = "100",
            ["start_y"] = "50",
            ["spacing_y"] = "80",
        });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        // W3：start_x/start_y = 第一个控件左上角（落 X=startX/Y=startY 不减半）
        var b1 = (ButtonWidget)s.Widgets[0];
        Assert.Equal(100, b1.X, 0.5);
        Assert.Equal(50, b1.Y, 0.5);
        // 第二控件沿 y 下移 80（左上角 (100, 130)）
        var b2 = (ButtonWidget)s.Widgets[1];
        Assert.Equal(130, b2.Y, 0.5);
    }
}
