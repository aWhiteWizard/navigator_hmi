using NavigatorHMI.Common;
using NavigatorHMI.Views.Helpers.Creators;
using System.Windows;

namespace NavigatorHMI.Tests;

/// <summary>P1-8 窗口控件创建器测试：固定 UserView + 命名编号。</summary>
public class WindowCreatorTests
{
    [Fact]
    public void Create_默认返回UserView()
    {
        var screen = new Screen { Name = "测试", Width = 800, Height = 480 };
        var creator = new WindowWidgetCreator();
        var w = creator.Create(new Point(200, 200), screen);
        Assert.IsType<WindowWidget>(w);
        var ww = (WindowWidget)w;
        Assert.Equal(WindowType.UserView, ww.Type);
        Assert.StartsWith("用户视图", ww.ObjectName);
        Assert.Equal(240, ww.Width);
        Assert.Equal(180, ww.Height);
    }

    [Fact]
    public void Create_已有窗口控件时编号递增()
    {
        var screen = new Screen { Name = "测试", Width = 800, Height = 480 };
        screen.Widgets.Add(new WindowWidget { ObjectName = "用户视图1", Type = WindowType.UserView });
        screen.Widgets.Add(new WindowWidget { ObjectName = "用户视图2", Type = WindowType.UserView });
        // 已有报警视图不参与用户视图编号（前缀互斥）
        screen.Widgets.Add(new WindowWidget { ObjectName = "报警视图1", Type = WindowType.AlarmView });

        var w = (WindowWidget)new WindowWidgetCreator().Create(new Point(200, 200), screen);
        Assert.Equal("用户视图3", w.ObjectName);
    }
}
