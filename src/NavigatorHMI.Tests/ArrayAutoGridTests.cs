using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>任务 6 array_layout 自动计算 cols/rows（未显式提供时按控件数：cols=ceil(sqrt(count))，rows=ceil(count/cols)）。</summary>
public class ArrayAutoGridTests
{
    private static (HMIProject project, CommandService svc, Screen screen) Setup(int count)
    {
        var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
        p.Screens.Add(new Screen { Name = "测试", Width = 800, Height = 480 });
        var svc = new CommandService(p);
        var s = p.Screens[0];
        for (int i = 0; i < count; i++)
            s.Widgets.Add(new ButtonWidget { ObjectName = $"b{i + 1}", Width = 100, Height = 40 });
        return (p, svc, s);
    }

    private static CommandResult RunAuto(CommandService svc, string widgets)
        => svc.Execute("array_layout", new Dictionary<string, object?>
        {
            ["screen_name"] = "测试",
            ["widgets"] = widgets,
            ["mode"] = "rect",
            ["spacing_x"] = "120",
            ["spacing_y"] = "80",
        });

    [Fact]
    public void 四个控件_自动算2x2()
    {
        var (_, svc, s) = Setup(4);
        var r = RunAuto(svc, "b1,b2,b3,b4");
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        // cols=2 rows=2：b3 在第 2 行第 1 列（中心点 (0, 1*80)，减半宽后 X 钳 0、Y=60）
        var b3 = (ButtonWidget)s.Widgets[2];
        Assert.Equal(0, b3.X, 0.5);
        Assert.Equal(60, b3.Y, 0.5);
        // b4 在第 2 行第 2 列（中心点 (1*120, 1*80) → (70, 60)）
        var b4 = (ButtonWidget)s.Widgets[3];
        Assert.Equal(70, b4.X, 0.5);
        Assert.Equal(60, b4.Y, 0.5);
    }

    [Fact]
    public void 两个控件_自动算2x1()
    {
        var (_, svc, s) = Setup(2);
        var r = RunAuto(svc, "b1,b2");
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        // cols=ceil(sqrt(2))=2, rows=ceil(2/2)=1：b2 在第 1 行第 2 列（中心点 (1*120, 0) → (70, 0)）
        var b2 = (ButtonWidget)s.Widgets[1];
        Assert.Equal(70, b2.X, 0.5);
        Assert.Equal(0, b2.Y, 0.5);
    }

    [Fact]
    public void 显式传cols_不自动算()
    {
        var (_, svc, s) = Setup(4);
        var r = svc.Execute("array_layout", new Dictionary<string, object?>
        {
            ["screen_name"] = "测试",
            ["widgets"] = "b1,b2,b3,b4",
            ["mode"] = "rect",
            ["cols"] = "1",
            ["rows"] = "4",
            ["spacing_y"] = "80",
        });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        // 显式 1×4：b3 在第 3 行（中心点 (0, 2*80) → (0, 140)）
        var b3 = (ButtonWidget)s.Widgets[2];
        Assert.Equal(0, b3.X, 0.5);
        Assert.Equal(140, b3.Y, 0.5);
    }
}
