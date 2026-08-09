using System.Windows;
using System.Windows.Input;

namespace NavigatorHMI.Views;

/// <summary>P3-6 AI 悬浮球独立窗：无边框 Topmost（严格置顶，不被对话框盖住）；点击回调打开主窗口侧边栏；拖动移动。</summary>
public partial class AiFloatBallWindow : Window
{
    /// <summary>点击回调（打开主窗口 AI 侧边栏；主窗口关闭时置空）。</summary>
    public Action? OnBallClick { get; set; }

    private bool _dragging;
    private Point _downPos;
    private double _downLeft, _downTop;

    public AiFloatBallWindow()
    {
        InitializeComponent();
        // 默认右下角（跟随主窗口初始位置由主窗口设置 Left/Top）
    }

    private void Fab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _downPos = e.GetPosition(this);
        _downLeft = Left;
        _downTop = Top;
        Fab.CaptureMouse();
        e.Handled = true;
    }

    private void Fab_MouseMove(object sender, MouseEventArgs e)
    {
        if (!Fab.IsMouseCaptured) return;
        var pos = e.GetPosition(this);
        var dx = pos.X - _downPos.X;
        var dy = pos.Y - _downPos.Y;
        if (!_dragging && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5)) _dragging = true;
        if (_dragging)
        {
            Left = _downLeft + dx;
            Top = _downTop + dy;
        }
        e.Handled = true;
    }

    private void Fab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        var moved = Math.Abs(pos.X - _downPos.X) + Math.Abs(pos.Y - _downPos.Y);
        var wasDragging = _dragging;
        _dragging = false;
        Fab.ReleaseMouseCapture();
        // 位移小于阈值视为点击：回调打开主窗口侧边栏
        if (!wasDragging && moved < 10) OnBallClick?.Invoke();
        e.Handled = true;
    }
}
