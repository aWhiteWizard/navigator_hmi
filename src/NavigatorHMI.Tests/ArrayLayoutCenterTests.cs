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
        // 默认 start 0 → 中心点 0 → 左上角 (0-50) 钳制 0
        Assert.Equal(0, b1.X, 0.5);
    }
}
