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
    /// 按控件类型生成端点手柄：Line 2 端点 / Rectangle 4 顶点 / Circle 圆心+4 象限（拖象限=半径，圆心=平移）/
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
            public int Index = -1;   // Vertex=角索引(0-3)；PolygonVertex=顶点索引
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
                    for (int i = 0; i < 4; i++) AddHandle(HandleKind.Vertex, i);
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

        private void AddHandle(HandleKind kind, int index = -1)
        {
            var thumb = new Thumb
            {
                Width = 10,
                Height = 10,
                Background = Brushes.White,
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1.5),
                Cursor = kind == HandleKind.Center ? Cursors.SizeAll : Cursors.SizeNWSE,
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
                case HandleKind.Vertex:  // Rectangle 角 → 更新对应角绝对坐标 → 反推 X/Y/W/H（min/max 防翻转）
                    if (_widget is RectangleWidget && handle.Index is >= 0 and <= 3)
                    {
                        var corners = new[]
                        {
                            new Point(x, y), new Point(x + w, y),
                            new Point(x + w, y + h), new Point(x, y + h)
                        };
                        var c = corners[handle.Index];
                        corners[handle.Index] = new Point(c.X + dx, c.Y + dy);
                        ApplyRectCorners(corners);
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

            InvalidateArrange();   // 手柄跟随新位置
        }

        private void ApplyRectCorners(Point[] c)
        {
            double minX = Math.Min(Math.Min(c[0].X, c[1].X), Math.Min(c[2].X, c[3].X));
            double minY = Math.Min(Math.Min(c[0].Y, c[1].Y), Math.Min(c[2].Y, c[3].Y));
            double maxX = Math.Max(Math.Max(c[0].X, c[1].X), Math.Max(c[2].X, c[3].X));
            double maxY = Math.Max(Math.Max(c[0].Y, c[1].Y), Math.Max(c[2].Y, c[3].Y));
            _widget.X = minX; _widget.Y = minY;
            _widget.Width = Math.Max(10, maxX - minX);
            _widget.Height = Math.Max(10, maxY - minY);
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

        protected override Size MeasureOverride(Size constraint) => GetElementSize();

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
                        1 => (w, 0.0),
                        2 => (w, h),
                        3 => (0.0, h),
                        _ => (0.0, 0.0)
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
            var size = GetElementSize();
            dc.DrawRectangle(null, _selectionPen, new Rect(0, 0, size.Width, size.Height));
        }
    }
}
