using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// V-6a：图形控件端点手柄 Adorner（替代 8 方向方框 ResizeAdorner，仅 5 类图形控件选中时使用）。
    /// 按控件类型生成端点手柄：Line 2 端点 / Rectangle 2 对角点（Z-1：左上+右下，用户拍板 4 顶点多余）/ Circle 圆心+4 象限（拖象限=半径，圆心=平移）/
    /// Ellipse 圆心+4 象限（拖象限=rx/ry，圆心=平移）/ Polygon N 顶点（画布绝对坐标）。
    /// 拖拽直接改模型（撤销快照复用 SelectorHelper.ResizeDragStarted/Completed；DragCompleted 刷新端点表格）。
    /// </summary>
    public class EndpointHandleAdorner : Adorner
    {
        private readonly VisualCollection _visualChildren;
        private readonly FrameworkElement _adornedElement;
        private readonly Widget _widget;
        private static readonly Pen _selectionPen = CreateSelectionPen();

        private static Pen CreateSelectionPen()
        {
            var pen = new Pen(Brushes.DodgerBlue, 1)
            {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
            };
            pen.Freeze();
            return pen;
        }

        /// <summary>手柄种类（DragDelta 分支用）。</summary>
        private enum HandleKind { Start, End, Vertex, Center, QuadLeft, QuadTop, QuadRight, QuadBottom, PolygonVertex }

        private sealed class Handle
        {
            public Thumb Thumb = null!;
            public HandleKind Kind;
            public int Index = -1;   // Vertex=角索引（0 左上 / 2 右下，Z-1 只生成 2 对角点）；PolygonVertex=顶点索引
        }

        private readonly List<Handle> _handles = new();

        public EndpointHandleAdorner(FrameworkElement adornedElement, Widget widget) : base(adornedElement)
        {
            _adornedElement = adornedElement;
            _widget = widget;
            _visualChildren = new VisualCollection(this);
            BuildHandles();
        }

        /// <summary>按控件类型生成手柄。</summary>
        private void BuildHandles()
        {
            switch (_widget)
            {
                case LineWidget lw:
                    AddHandle(HandleKind.Start);
                    AddHandle(HandleKind.End);
                    break;
                case RectangleWidget:
                    // Z-1：矩形只生成 2 个对角点手柄（左上=index 0 锚定右下、右下=index 2 锚定左上），与 W-4b 表格 2 对角点对齐（用户拍板：4 顶点多余）
                    AddHandle(HandleKind.Vertex, 0);
                    AddHandle(HandleKind.Vertex, 2);
                    break;
                case CircleWidget:
                    AddHandle(HandleKind.Center);
                    AddHandle(HandleKind.QuadLeft);
                    AddHandle(HandleKind.QuadTop);
                    AddHandle(HandleKind.QuadRight);
                    AddHandle(HandleKind.QuadBottom);
                    break;
                case EllipseWidget:
                    AddHandle(HandleKind.Center);
                    AddHandle(HandleKind.QuadLeft);
                    AddHandle(HandleKind.QuadTop);
                    AddHandle(HandleKind.QuadRight);
                    AddHandle(HandleKind.QuadBottom);
                    break;
                case PolygonWidget poly:
                    for (int i = 0; i < poly.Points.Count; i++) AddHandle(HandleKind.PolygonVertex, i);
                    break;
            }
        }

        /// <summary>Y-2d/Z-1：手柄光标按方位分派——角点对角斜向、边中点水平/垂直、圆心/线端点/多边形顶点 move（对照 ResizeAdorner 8 方向）。</summary>
        private static Cursor CursorForKind(HandleKind kind, int index)
        {
            return kind switch
            {
                HandleKind.Center or HandleKind.Start or HandleKind.End or HandleKind.PolygonVertex => Cursors.SizeAll,
                HandleKind.Vertex => Cursors.SizeNWSE,   // Z-1：仅左上/右下两角点，均对角斜向（0/2）
                HandleKind.QuadLeft or HandleKind.QuadRight => Cursors.SizeWE,
                HandleKind.QuadTop or HandleKind.QuadBottom => Cursors.SizeNS,
                _ => Cursors.SizeAll
            };
        }

        private void AddHandle(HandleKind kind, int index = -1)
        {
            var thumb = new Thumb
            {
                Width = 10,
                Height = 10,
                Background = Brushes.White,
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1.5),
                Cursor = CursorForKind(kind, index),
                IsHitTestVisible = true,
                Focusable = false
            };
            thumb.DragStarted += (_, _) => SelectorHelper.ResizeDragStarted?.Invoke();
            thumb.DragDelta += Thumb_DragDelta;
            thumb.DragCompleted += (_, _) => SelectorHelper.ResizeDragCompleted?.Invoke();
            _visualChildren.Add(thumb);
            _handles.Add(new Handle { Thumb = thumb, Kind = kind, Index = index });
        }

        /// <summary>手柄拖拽：按种类更新模型（拖拽增量应用到绝对坐标；模型 INPC → 控件重渲染 + InvalidateArrange 手柄跟随）。</summary>
        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var handle = _handles.Find(x => ReferenceEquals(x.Thumb, sender));
            if (handle == null) return;
            double dx = e.HorizontalChange, dy = e.VerticalChange;
            double x = _widget.X, y = _widget.Y, w = _widget.Width, h = _widget.Height;
            double cx = x + w / 2, cy = y + h / 2;

            switch (handle.Kind)
            {
                case HandleKind.Start:   // Line 起点（绝对位置 = X/Y）
                    if (_widget is LineWidget lStart) { lStart.X += dx; lStart.Y += dy; }
                    break;
                case HandleKind.End:     // Line 终点（相对偏移 X2/Y2）
                    if (_widget is LineWidget lEnd) { lEnd.X2 += dx; lEnd.Y2 += dy; }
                    break;
                case HandleKind.Vertex:  // Rectangle 2 对角点（Z-1：只生成左上 index 0 / 右下 index 2）→ 锚定对角反推 X/Y/W/H（仿 ResizeAdorner 四角；min 防翻转，向内拖无死区）
                    if (_widget is RectangleWidget && handle.Index is 0 or 2)
                    {
                        const double MIN = 10;   // 与 RectangleWidgetCreator 最小尺寸一致
                        double newX = x, newY = y, newW = w, newH = h;
                        switch (handle.Index)
                        {
                            case 0:   // 左上：锚定右下
                                newW = Math.Max(MIN, w - dx); newH = Math.Max(MIN, h - dy);
                                newX = x + w - newW; newY = y + h - newH;
                                break;
                            case 2:   // 右下：锚定左上
                                newW = Math.Max(MIN, w + dx); newH = Math.Max(MIN, h + dy);
                                break;
                        }
                        _widget.X = newX; _widget.Y = newY; _widget.Width = newW; _widget.Height = newH;
                    }
                    break;
                case HandleKind.Center:  // 圆/椭圆圆心 → 平移
                    if (_widget is CircleWidget or EllipseWidget) { _widget.X += dx; _widget.Y += dy; }
                    break;
                case HandleKind.QuadRight:   // 右象限 → 新半径 = |新右端点 - 圆心|
                    if (_widget is CircleWidget cR)
                    {
                        double r = Math.Max(10, Math.Abs(cx + w / 2 + dx - cx));
                        _widget.X = cx - r; _widget.Y = cy - r; _widget.Width = 2 * r; _widget.Height = 2 * r;
                    }
                    else if (_widget is EllipseWidget eR)
                    {
                        double r = Math.Max(10, Math.Abs(w / 2 + dx));   // Abs：拖过圆心镜像（钳最小 10）
                        _widget.X = cx - r; _widget.Width = 2 * r;
                    }
                    break;
                case HandleKind.QuadLeft:    // 左象限
                    if (_widget is CircleWidget cL)
                    {
                        double r = Math.Max(10, Math.Abs(cx - w / 2 + dx - cx));
                        _widget.X = cx - r; _widget.Y = cy - r; _widget.Width = 2 * r; _widget.Height = 2 * r;
                    }
                    else if (_widget is EllipseWidget eL)
                    {
                        double r = Math.Max(10, Math.Abs(w / 2 - dx));   // Abs：拖过圆心镜像（钳最小 10）
                        _widget.X = cx - r; _widget.Width = 2 * r;
                    }
                    break;
                case HandleKind.QuadBottom:  // 下象限
                    if (_widget is CircleWidget cB)
                    {
                        double r = Math.Max(10, Math.Abs(cy + h / 2 + dy - cy));
                        _widget.X = cx - r; _widget.Y = cy - r; _widget.Width = 2 * r; _widget.Height = 2 * r;
                    }
                    else if (_widget is EllipseWidget eB)
                    {
                        double r = Math.Max(10, Math.Abs(h / 2 + dy));   // Abs：拖过圆心镜像
                        _widget.Y = cy - r; _widget.Height = 2 * r;
                    }
                    break;
                case HandleKind.QuadTop:     // 上象限
                    if (_widget is CircleWidget cT)
                    {
                        double r = Math.Max(10, Math.Abs(cy - h / 2 + dy - cy));
                        _widget.X = cx - r; _widget.Y = cy - r; _widget.Width = 2 * r; _widget.Height = 2 * r;
                    }
                    else if (_widget is EllipseWidget eT)
                    {
                        double r = Math.Max(10, Math.Abs(h / 2 - dy));   // Abs：拖过圆心镜像
                        _widget.Y = cy - r; _widget.Height = 2 * r;
                    }
                    break;
                case HandleKind.PolygonVertex:   // 多边形顶点（画布绝对坐标）
                    if (_widget is PolygonWidget poly && handle.Index >= 0 && handle.Index < poly.Points.Count)
                    {
                        poly.Points[handle.Index].X += dx;
                        poly.Points[handle.Index].Y += dy;
                        RecalcPolygonBounds(poly);
                        poly.Points = new List<PointD>(poly.Points);   // PointD 无 INPC → 重赋值触发通知
                    }
                    break;
            }

            SelectorHelper.ResizeDragDelta?.Invoke(_widget);   // W-4c/Z-2：拖拽过程实时刷新端点表格（携带被拖 widget 按类型刷表，摆脱 _selectedWidget 门控；注入方节流）
            InvalidateArrange();   // 手柄跟随新位置
        }

        private static void RecalcPolygonBounds(PolygonWidget poly)
        {
            if (poly.Points.Count == 0) return;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in poly.Points)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
            poly.X = minX; poly.Y = minY;
            poly.Width = maxX - minX; poly.Height = maxY - minY;
        }

        private Size GetElementSize()
        {
            double w = _adornedElement.RenderSize.Width, h = _adornedElement.RenderSize.Height;
            if (w <= 0) w = _widget.Width;
            if (h <= 0) h = _widget.Height;
            return new Size(w, h);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // W-4a 修复：手柄不显示根因——必须 Measure 子元素（WPF 布局要求 Arrange 前先 Measure，
            // 未 Measure 的 Thumb 不渲染；此前只 return 尺寸导致 10×10 手柄全部不可见，方框（OnRender）正常）
            var size = GetElementSize();
            foreach (var child in _visualChildren)
                if (child is System.Windows.UIElement ue) ue.Measure(size);
            return size;
        }

        // X-2a 修复：缺 VisualChildrenCount/GetVisualChild 则 LayoutManager 不知道 Adorner 有子视觉，
        // 子元素进不了渲染管线（对齐 ResizeAdorner 291-292）——手柄不显示真根因
        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];

        /// <summary>手柄位置 = 按当前模型状态实时计算（拖拽中跟随；Polygon 顶点按 Points 绝对坐标-包围盒左上）。</summary>
        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = GetElementSize();
            double w = size.Width, h = size.Height;
            double half = 5;

            foreach (var handle in _handles)
            {
                (double ox, double oy) = handle.Kind switch
                {
                    HandleKind.Start => (0.0, 0.0),
                    HandleKind.End => (_widget is LineWidget lw ? lw.X2 : 0, _widget is LineWidget l2 ? l2.Y2 : 0),
                    HandleKind.Center => (w / 2, h / 2),
                    HandleKind.Vertex => handle.Index switch
                    {
                        2 => (w, h),   // Z-1：右下
                        _ => (0.0, 0.0)   // index 0：左上
                    },
                    HandleKind.QuadLeft => (0.0, h / 2),
                    HandleKind.QuadTop => (w / 2, 0.0),
                    HandleKind.QuadRight => (w, h / 2),
                    HandleKind.QuadBottom => (w / 2, h),
                    HandleKind.PolygonVertex => (_widget is PolygonWidget pv && handle.Index >= 0 && handle.Index < pv.Points.Count
                        ? pv.Points[handle.Index].X - _widget.X : 0.0,
                        _widget is PolygonWidget pv2 && handle.Index >= 0 && handle.Index < pv2.Points.Count
                        ? pv2.Points[handle.Index].Y - _widget.Y : 0.0),
                    _ => (0.0, 0.0)
                };
                handle.Thumb.Arrange(new Rect(ox - half, oy - half, 10, 10));
            }
            return new Size(w, h);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_widget is LineWidget) return;   // Y-2e：直线只显示 2 端点手柄，不画外圈选中框
            var size = GetElementSize();
            dc.DrawRectangle(null, _selectionPen, new Rect(0, 0, size.Width, size.Height));
        }
    }
}
