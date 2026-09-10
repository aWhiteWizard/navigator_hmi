using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Behaviors;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using ProtoBuf;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;   // P10：XmlLayoutSerializer（Dirkster.AvalonDock）

namespace NavigatorHMI.Views
{
    /// <summary>
    /// EditWindow 的交互逻辑。作为 WPF 窗口的 code-behind，
    /// 负责响应 UI 事件并将业务逻辑委托给拆分后的专职类。
    /// </summary>
    public partial class EditWindow : Window
    {

        // AB-6 复审 🟡-1（2026-09-11）：画布鼠标/绘制预览/键盘快捷键 自 EditWindow.Canvas.cs 纯搬家（内容零改动）
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // 世界地图画面：画布编辑短路（框选/两点式预览不启动），事件到达 MapControl 平移
            if (_viewModel?.IsWorldMapActive == true) return;
            // 两点式绘制预览更新
            if (_isDrawingPreview)
            {
                UpdateDrawPreview(e.GetPosition(DrawingCanvas));
                return;
            }

            if (!_isMarquee) return;
            var pos = e.GetPosition(DrawingCanvas);
            var x = Math.Min(_marqueeStart.X, pos.X);
            var y = Math.Min(_marqueeStart.Y, pos.Y);
            var w = Math.Abs(pos.X - _marqueeStart.X);
            var h = Math.Abs(pos.Y - _marqueeStart.Y);
            Canvas.SetLeft(MarqueeRect, x);
            Canvas.SetTop(MarqueeRect, y);
            MarqueeRect.Width = w;
            MarqueeRect.Height = h;
            // 边框灰色细线(实线=左→右, 虚线=右→左)，填充蓝/绿
            bool leftToRight = pos.X >= _marqueeStart.X;
            MarqueeRect.Stroke = Brushes.Gray;
            MarqueeRect.StrokeThickness = 1;
            MarqueeRect.StrokeDashArray = leftToRight ? null : new DoubleCollection { 4, 4 };
            MarqueeRect.Fill = leftToRight
                ? new SolidColorBrush(Color.FromArgb(0x20, 0x33, 0x99, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x20, 0x33, 0xCC, 0x33));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 世界地图画面：地图交互优先——短路但保留泄漏状态兜底清理
            if (_viewModel?.IsWorldMapActive == true)
            {
                _dragBehavior.ClearLeakState();
                return;
            }
            // 容器层无条件清理泄漏候选状态（pending + 粘性标志）
            // 注意：不清 _isDragging——widget 冒泡 up 负责拖拽收尾（光标恢复 + 捕获释放），
            // capture 保证 up 必路由到捕获元素，此处兜底不失效。
            _dragBehavior.ClearLeakState();

            if (!_isMarquee) return;
            _isMarquee = false;
            MarqueeRect.Visibility = Visibility.Collapsed;

            var screen = _viewModel.CurrentScreen;
            if (screen == null) return;

            var rect = new Rect(Canvas.GetLeft(MarqueeRect), Canvas.GetTop(MarqueeRect), MarqueeRect.Width, MarqueeRect.Height);
            bool leftToRight = e.GetPosition(DrawingCanvas).X >= _marqueeStart.X;

            foreach (var widget in screen.Widgets)
            {
                var wRect = new Rect(widget.X, widget.Y, widget.Width, widget.Height);
                if (leftToRight)
                    widget.IsSelected = rect.Contains(wRect);      // 完全包含
                else
                    widget.IsSelected = rect.IntersectsWith(wRect); // 相交即可
            }

            // 框选收尾：同步属性面板（多选批量编辑 / 单选 / 清空）
            SyncSelectionToPropertyPanel();
        }

        private void ShowDrawPreview()
        {
            _drawPreviewPath = DrawPreviewShape;
            _drawPreviewPath.Visibility = Visibility.Visible;
            if (_currentWidgetCreator is PolygonWidgetCreator && _polygonPoints.Count > 0)
            {
                // P12：多边形预览初始化为「末点 → 末点」零长度——_drawStartPoint 是两点式通用起点（残留旧值），
                // 直接传入会让首点先闪一段到 (0,0) 的线段；鼠标移动后再正常橡皮筋
                UpdateDrawPreview(_polygonPoints[^1]);
            }
            else
            {
                UpdateDrawPreview(_drawStartPoint);
            }
        }

        private void UpdateDrawPreview(Point end)
        {
            if (_drawPreviewPath == null || _currentWidgetCreator == null) return;

            double x = Math.Min(_drawStartPoint.X, end.X);
            double y = Math.Min(_drawStartPoint.Y, end.Y);
            double w = Math.Abs(end.X - _drawStartPoint.X);
            double h = Math.Abs(end.Y - _drawStartPoint.Y);

            switch (_currentWidgetCreator)
            {
                case LineWidgetCreator:
                    _drawPreviewPath.Data = new LineGeometry(_drawStartPoint, end);
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                case PolygonWidgetCreator:
                {
                    // 已收集顶点 + 当前鼠标橡皮筋线段（PathGeometry + PolyLineSegment）
                    var fig = new System.Windows.Media.PathFigure { StartPoint = _polygonPoints[0], IsClosed = false };
                    var segPts = new List<Point>(_polygonPoints.Skip(1)) { end };
                    fig.Segments.Add(new System.Windows.Media.PolyLineSegment(segPts, true));
                    _drawPreviewPath.Data = new System.Windows.Media.PathGeometry(new[] { fig });
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                }
                case CircleWidgetCreator:
                {
                    double dx = end.X - _drawStartPoint.X;
                    double dy = end.Y - _drawStartPoint.Y;
                    double radius = Math.Max(10, Math.Sqrt(dx * dx + dy * dy));  // 与 Creator 一致：最小 10px
                    _drawPreviewPath.Data = new EllipseGeometry(new Point(_drawStartPoint.X, _drawStartPoint.Y), radius, radius);
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                }
                case EllipseWidgetCreator:
                {
                    // 自由椭圆预览（左上角→右下角）
                    double ex = Math.Min(_drawStartPoint.X, end.X);
                    double ey = Math.Min(_drawStartPoint.Y, end.Y);
                    double ew = Math.Max(Math.Abs(end.X - _drawStartPoint.X), 10);
                    double eh = Math.Max(Math.Abs(end.Y - _drawStartPoint.Y), 10);
                    // 椭圆预览（与落点形状一致，避免矩形预览跳变椭圆）
                    _drawPreviewPath.Data = new EllipseGeometry(new Rect(ex, ey, ew, eh));
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                }
                default: // RectangleWidgetCreator
                    _drawPreviewPath.Data = new RectangleGeometry(new Rect(x, y, Math.Max(10, w), Math.Max(10, h)));  // 与 Creator 一致：最小 10px
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
            }
        }

        private void HideDrawPreview()
        {
            _isDrawingPreview = false;
            if (DrawPreviewShape != null)
            {
                DrawPreviewShape.Visibility = Visibility.Collapsed;
                DrawPreviewShape.Data = null;
            }
            _drawPreviewPath = null;
        }

        private static bool IsAdornerHit(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is System.Windows.Documents.Adorner) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 世界地图画面：地图交互优先（平移/缩放）——画布框选/添加模式短路，事件自然到达 MapControl
            if (_viewModel?.IsWorldMapActive == true) return;

            // 画布交互：聚焦画布（方向键微移判定用；延后执行避免干扰本次点击的选中/拖拽逻辑）
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (DrawingCanvas != null) DrawingCanvas.Focus();
            }));

            if (TreeContextMenu.IsOpen) { TreeContextMenu.IsOpen = false; }

            // [修复] 命中 Adorner 缩放手柄（Thumb 或其内部模板元素）时，视为点击选中装饰器：
            // 保持选中状态不清除，让 Thumb 正常接收 MouseDown 进入拖拽缩放
            if (IsAdornerHit(e.OriginalSource as DependencyObject)) return;

            // 多边形连续点击：左键收集顶点（第一点击起绘，后续加点，右键闭合）
            if (_currentWidgetCreator is PolygonWidgetCreator)
            {
                if (_viewModel?.CurrentScreen == null) return;
                var polygonPos = e.GetPosition(DrawingCanvas);
                _polygonPoints.Add(polygonPos);
                _isDrawingPreview = true;
                ShowDrawPreview();
                e.Handled = true;
                return;
            }

            // 两点式绘制：第二点击生成控件（优先响应，不受已有点击拦截影响）
            if (_currentWidgetCreator is ITwoPointCreator twoPoint && _isDrawingPreview)
            {
                if (_viewModel?.CurrentScreen == null) return;
                _viewModel.PushUndoSnapshot();
                Point end = e.GetPosition(DrawingCanvas);
                var widget = twoPoint.Create(_drawStartPoint, end, _viewModel.CurrentScreen);
                _viewModel.CurrentScreen.Widgets.Add(widget);
                MarkProjectDirty();
                HideDrawPreview();
                ExitAddMode();
                _dragBehavior.SuppressDragUntilMouseUp();  // 粘性抑制本次 down→up 周期的拖拽（冒泡阶段仍生效）
                e.Handled = true;  // 阻止事件继续冒泡到被点中的 Widget
                return;
            }

            // 两点式绘制：第一点击记录起点并显示预览（优先于 FindWidgetElement，允许从控件上开始画线）
            if (_currentWidgetCreator is ITwoPointCreator tpc && !_isDrawingPreview)
            {
                if (_viewModel?.CurrentScreen == null) return;
                _drawStartPoint = e.GetPosition(DrawingCanvas);
                _isDrawingPreview = true;
                ShowDrawPreview();
                e.Handled = true;
                return;
            }

            // 点击了 Widget → 交给已有逻辑
            if (FindWidgetElement(e.OriginalSource as DependencyObject) != null) return;

            _selectionManager.ClearAllSelection();
            HidePropertyWindow();

            // 非添加模式 → 开始框选
            if (_currentWidgetCreator == null)
            {
                _marqueeStart = e.GetPosition(DrawingCanvas);
                _isMarquee = true;
                Canvas.SetLeft(MarqueeRect, _marqueeStart.X);
                Canvas.SetTop(MarqueeRect, _marqueeStart.Y);
                MarqueeRect.Width = MarqueeRect.Height = 0;
                MarqueeRect.Visibility = Visibility.Visible;
                e.Handled = true;
                return;
            }

            // 添加 Widget 模式（单点式控件）
            if (_viewModel?.CurrentScreen == null) return;
            Point pos = e.GetPosition(DrawingCanvas);

            // 单点式：直接创建
            _viewModel.PushUndoSnapshot();
            var newWidget = _currentWidgetCreator.Create(pos, _viewModel.CurrentScreen);
            _viewModel.CurrentScreen.Widgets.Add(newWidget);
            MarkProjectDirty();
            ExitAddMode();
            e.Handled = true;
        }

        private void EditWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 焦点在文本框/输入框内时不拦截（避免干扰 TreeView 重命名、属性输入、CLI 输入）
            if (Keyboard.FocusedElement is TextBox or PasswordBox) return;

            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            var none = Keyboard.Modifiers == ModifierKeys.None;

            // ── 全选 ──（焦点在管理器表格/列表内时交给控件级 handler：表格 Ctrl+A 全选行）
            if (ctrl && !shift && e.Key == Key.A && !IsFocusInManagerControl())
            {
                SelectAllWidgets();
                e.Handled = true;
                return;
            }
            // ── 用户管理页 Ctrl+C/V：复制/粘贴选中用户或组（页面级路由，与 A3 同款；UserActive 时画布 Hidden 无冲突；
            //    补 #1：用户/组复制粘贴原仅右键菜单，无键盘快捷键；page==2（安全设置）不命中时放行不吞键）──
            if (ctrl && !shift && !alt && e.Key == Key.C && _viewModel.UserActive)
            {
                if (_viewModel.UserPanelPage == 0 && UsersGrid.SelectedItems.Count > 0)
                    _viewModel.CopyUsersCommand(UsersGrid.SelectedItems.OfType<UserAccount>().ToList());
                else if (_viewModel.UserPanelPage == 1 && GroupsGrid.SelectedItems.Count > 0)
                    _viewModel.CopyGroupsCommand(GroupsGrid.SelectedItems.OfType<UserGroup>().ToList());
                else return;   // page==2 或无选中：放行不吞键（避免未来该页加可复制控件时静默吞键）
                e.Handled = true;
                return;
            }
            if (ctrl && !shift && !alt && e.Key == Key.V && _viewModel.UserActive)
            {
                if (_viewModel.UserPanelPage == 0) _viewModel.PasteUsersCommand();
                else if (_viewModel.UserPanelPage == 1) _viewModel.PasteGroupsCommand();
                else return;   // page==2：放行不吞键
                e.Handled = true;
                return;
            }
            // ── 复制 ──（表格内放行默认复制/不拦截）
            if (ctrl && !shift && e.Key == Key.C && !IsFocusInManagerControl())
            {
                CopyWidgets();
                e.Handled = true;
                return;
            }
            // ── 剪切 ──（表格内放行）
            if (ctrl && !shift && e.Key == Key.X && !IsFocusInManagerControl())
            {
                if (_viewModel.CurrentScreen?.Widgets.Any(w => w.IsSelected) == true)
                {
                    CopyWidgets();
                    DeleteSelectedWidgets();
                }
                e.Handled = true;
                return;
            }
            // ── 粘贴（无右键菜单位置时用当前位置；表格内放行）──
            if (ctrl && !shift && e.Key == Key.V && !IsFocusInManagerControl())
            {
                if (_clipboard.Count > 0)
                    PasteWidgetsAtCurrent();
                e.Handled = true;
                return;
            }
            // ── 方向键微移选中控件（无 Ctrl/Shift：1px；Shift：10px）──
            // 仅当键盘焦点在画布内且画布有选中控件时才拦截（否则放行 TreeView/ComboBox 等键盘导航）
            if ((none || shift) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down
                && DrawingCanvas.IsKeyboardFocusWithin
                && _viewModel.CurrentScreen?.Widgets.Any(w => w.IsSelected) == true)
            {
                NudgeSelectedWidgets(e.Key, shift ? 10 : 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !shift
                && _viewModel.UserActive && _viewModel.UndoUser())
            { e.Handled = true; return; }   // A3：用户/组撤销——按「用户管理 Tab 页面激活」路由（不再要求焦点在管理器控件内：AI 操作后焦点在侧边栏也能撤）；栈空返回 false 不拦截，落到画面撤销/KeyBinding
            if (((e.Key == Key.Y && ctrl) || (e.Key == Key.Z && ctrl && shift))
                && _viewModel.UserActive && _viewModel.RedoUser())
            { e.Handled = true; return; }   // 用户/组重做
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !shift
                && !_viewModel.UserActive && IsFocusInManagerControl())
            { _listManagerVM.UndoList(); e.Handled = true; return; }   // 列表项撤销（焦点在列表管理器时拦截，空栈也不穿透到画面撤销；!UserActive 保证用户页激活时空栈统一落到画面 KeyBinding——行为与焦点位置无关）
            if (((e.Key == Key.Y && ctrl) || (e.Key == Key.Z && ctrl && shift))
                && !_viewModel.UserActive && IsFocusInManagerControl())
            { _listManagerVM.RedoList(); e.Handled = true; return; }   // 列表项重做
            if (e.Key == Key.Delete && none && !IsFocusInManagerControl())
            {
                _widgetContextMenuHandler.DeleteSelectedWidget();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // ESC 退出添加模式（含两点式绘制中途取消）；作业范围加点/作业点选点编辑模式优先退出
                if (_isEditingWorkRange)
                {
                    ExitWorkRangeEditMode();
                    e.Handled = true;
                }
                else if (_isAddingWorkPoint)
                {
                    ExitWorkPointAddMode();   // C12-9：ESC 退出作业点选点模式
                    e.Handled = true;
                }
                else if (_currentWidgetCreator != null)
                {
                    ExitAddMode();
                    e.Handled = true;
                }
            }
        }

        private void SelectAllWidgets()
        {
            var widgets = _viewModel.CurrentScreen?.Widgets;
            if (widgets == null || widgets.Count == 0) return;
            foreach (var w in widgets) w.IsSelected = true;
            _selectionManager.UpdateSelectionUI();
            SyncSelectionToPropertyPanel();
        }

    }
}
