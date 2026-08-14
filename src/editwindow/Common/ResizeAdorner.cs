using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NavigatorHMI.Common
{
    public class ResizeAdorner : Adorner
    {
        private readonly VisualCollection _visualChildren;
        private readonly Thumb[] _thumbs;
        private readonly FrameworkElement _adornedElement;
        private readonly Widget _widget;
        private static readonly Pen _selectionPen = CreateSelectionPen();

        private static Pen CreateSelectionPen()
        {
            var pen = new Pen(Brushes.Black, 1)
            {
                DashStyle = new DashStyle(new double[] { 3, 3 }, 0)
            };
            pen.Freeze();
            return pen;
        }

        public ResizeAdorner(FrameworkElement adornedElement, Widget widget) : base(adornedElement)
        {
            _adornedElement = adornedElement;
            _widget = widget;
            _visualChildren = new VisualCollection(this);
            _thumbs = new Thumb[8];
            for (int i = 0; i < 8; i++)
            {
                var thumb = new Thumb
                {
                    Width = 10,
                    Height = 10,
                    Background = Brushes.White,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Cursor = GetCursor((ResizeDirection)i),
                    IsHitTestVisible = true,   // 显式确保 Thumb 可命中（防御 Adorner 层命中测试被关闭的情况）
                    Focusable = false
                };
                thumb.DragStarted += Thumb_DragStarted;
                thumb.DragDelta += Thumb_DragDelta;
                thumb.DragCompleted += Thumb_DragCompleted;
                thumb.Tag = (ResizeDirection)i;
                _visualChildren.Add(thumb);
                _thumbs[i] = thumb;
            }
        }

        /// <summary>缩放手柄拖拽开始（诊断用）。</summary>
        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            // 缩放开始：Push 撤销快照（一次缩放一次快照，每帧 DragDelta 不 Push）
            SelectorHelper.ResizeDragStarted?.Invoke();
            System.Diagnostics.Debug.WriteLine($"🎯 Resize 开始: {_widget.ObjectName}, X={_widget.X}, Y={_widget.Y}, W={_widget.Width}, H={_widget.Height}");
        }

        /// <summary>缩放手柄拖拽结束：通知刷新（C12-12——多边形缩放平移顶点后端点表格实时更新）。</summary>
        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SelectorHelper.ResizeDragCompleted?.Invoke(_widget);
            System.Diagnostics.Debug.WriteLine($"🎯 Resize 完成: {_widget.ObjectName}, X={_widget.X}, Y={_widget.Y}, W={_widget.Width}, H={_widget.Height}");
        }

        private enum ResizeDirection { TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight }

        private Cursor GetCursor(ResizeDirection dir)
        {
            switch (dir)
            {
                case ResizeDirection.TopLeft: case ResizeDirection.BottomRight: return Cursors.SizeNWSE;
                case ResizeDirection.TopRight: case ResizeDirection.BottomLeft: return Cursors.SizeNESW;
                case ResizeDirection.Top: case ResizeDirection.Bottom: return Cursors.SizeNS;
                case ResizeDirection.Left: case ResizeDirection.Right: return Cursors.SizeWE;
                default: return Cursors.Arrow;
            }
        }

        /// <summary>
        /// 获取被装饰元素的实际尺寸（统一取值源）。
        /// RenderSize 为 (0,0)（元素未完成首次布局）时回退到模型尺寸。
        /// </summary>
        private Size GetElementSize()
        {
            double w = _adornedElement.RenderSize.Width;
            double h = _adornedElement.RenderSize.Height;
            if (w <= 0) w = _widget.Width;
            if (h <= 0) h = _widget.Height;
            return new Size(w, h);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            return GetElementSize();
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // 以被装饰元素的实际渲染尺寸为基准，不依赖 AdornerLayer 传入的 finalSize
            var size = GetElementSize();
            double w = size.Width;
            double h = size.Height;
            double right = w, bottom = h;
            double half = 5;
            _thumbs[0].Arrange(new Rect(-half, -half, 10, 10)); // TopLeft
            _thumbs[1].Arrange(new Rect((right - 10) / 2, -half, 10, 10)); // Top
            _thumbs[2].Arrange(new Rect(right - half, -half, 10, 10)); // TopRight
            _thumbs[3].Arrange(new Rect(-half, (bottom - 10) / 2, 10, 10)); // Left
            _thumbs[4].Arrange(new Rect(right - half, (bottom - 10) / 2, 10, 10)); // Right
            _thumbs[5].Arrange(new Rect(-half, bottom - half, 10, 10)); // BottomLeft
            _thumbs[6].Arrange(new Rect((right - 10) / 2, bottom - half, 10, 10)); // Bottom
            _thumbs[7].Arrange(new Rect(right - half, bottom - half, 10, 10)); // BottomRight
            // 返回实际摆放尺寸（非 finalSize），保证 Adorner 自身 RenderSize 与元素一致（虚线框/手柄不被裁切）
            return new Size(w, h);
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = sender as Thumb;
            var dir = (ResizeDirection)thumb!.Tag;
            double oldX = _widget.X, oldY = _widget.Y;
            double oldW = _widget.Width, oldH = _widget.Height;
            double newX = oldX, newY = oldY, newW = oldW, newH = oldH;

            // 先按拖拽增量计算目标尺寸（含最小尺寸钳制），再按“锚点不动”重算位置：
            // 对角/对边锚定，尺寸钳到最小 20 时控件不会被拖走（旧逻辑先移位置再钳尺寸导致漂移）
            switch (dir)
            {
                case ResizeDirection.TopLeft: // 右下角固定
                    newW = Math.Max(20, oldW - e.HorizontalChange);
                    newH = Math.Max(20, oldH - e.VerticalChange);
                    newX = oldX + oldW - newW;
                    newY = oldY + oldH - newH;
                    break;
                case ResizeDirection.Top: // 底边固定
                    newH = Math.Max(20, oldH - e.VerticalChange);
                    newY = oldY + oldH - newH;
                    break;
                case ResizeDirection.TopRight: // 左下角固定
                    newW = Math.Max(20, oldW + e.HorizontalChange);
                    newH = Math.Max(20, oldH - e.VerticalChange);
                    newY = oldY + oldH - newH;
                    break;
                case ResizeDirection.Left: // 右边固定
                    newW = Math.Max(20, oldW - e.HorizontalChange);
                    newX = oldX + oldW - newW;
                    break;
                case ResizeDirection.Right: // 左边固定
                    newW = Math.Max(20, oldW + e.HorizontalChange);
                    break;
                case ResizeDirection.BottomLeft: // 右上角固定
                    newW = Math.Max(20, oldW - e.HorizontalChange);
                    newH = Math.Max(20, oldH + e.VerticalChange);
                    newX = oldX + oldW - newW;
                    break;
                case ResizeDirection.Bottom: // 顶边固定
                    newH = Math.Max(20, oldH + e.VerticalChange);
                    break;
                case ResizeDirection.BottomRight: // 左上角固定
                    newW = Math.Max(20, oldW + e.HorizontalChange);
                    newH = Math.Max(20, oldH + e.VerticalChange);
                    break;
            }

            // 类型特化处理
            if (_widget is LineWidget line)
            {
                // Line: X2/Y2 跟随边界框增量同步（保持视觉效果一致）
                line.X2 += (newW - oldW);
                line.Y2 += (newH - oldH);
            }
            else if (_widget is CircleWidget)
            {
                // 圆形控件：所有方向拖拽都保持 1:1 正圆（中心锚定，手感一致）
                // 椭圆由独立 EllipseWidget 支持（可自由宽高）
                double centerX = oldX + oldW / 2;
                double centerY = oldY + oldH / 2;
                // 按手柄方向取变化维（Top/Bottom → 高度、Left/Right → 宽度、角 → 取大），边拖拽向内缩小也生效
                double size = dir switch
                {
                    ResizeDirection.Top or ResizeDirection.Bottom => newH,
                    ResizeDirection.Left or ResizeDirection.Right => newW,
                    _ => Math.Max(newW, newH)
                };
                newX = centerX - size / 2;
                newY = centerY - size / 2;
                newW = size;
                newH = size;
            }
            else if (_widget is PolygonWidget poly && poly.Points.Count > 0)
            {
                // P7：多边形顶点为画布绝对坐标——按包围盒左上(oldX,oldY)锚定缩放（旧偏移 × 比例 + 新左上 newX/newY）
                double scaleX = oldW > 0 ? newW / oldW : 1;
                double scaleY = oldH > 0 ? newH / oldH : 1;
                foreach (var pt in poly.Points)
                {
                    pt.X = newX + (pt.X - oldX) * scaleX;
                    pt.Y = newY + (pt.Y - oldY) * scaleY;
                }
                // PointD 无 INPC：集合元素修改不触发绑定刷新——重赋值集合触发 Points setter 通知（wpf-property-sync-clamp 教训）
                poly.Points = new List<PointD>(poly.Points);
            }

            _widget.X = newX;
            _widget.Y = newY;
            _widget.Width = newW;
            _widget.Height = newH;

            // 缩放钳制（与属性面板同规则：X+W 不超画布、最小 20）——缩放直改模型绕过 VM setter，此处显式钳制
            ClampToCanvas();

            // 数据更新后重新测量 Adorner（InvalidateMeasure 级联触发 arrange 失效），确保缩放手柄跟随新尺寸
            InvalidateMeasure();
        }

        /// <summary>
        /// 将控件位置/尺寸钳制到画布内（0 ≤ X ≤ 画布宽-W，X+W ≤ 画布宽；最小 20 由 DragDelta 的 Math.Max 保证）。
        /// Line 的 X2/Y2（相对偏移）同步钳制到新边界框内。
        /// </summary>
        private void ClampToCanvas()
        {
            var size = SelectorHelper.GetCanvasSize?.Invoke();
            if (size == null || size.Value.Width <= 0 || size.Value.Height <= 0) return;
            double cw = size.Value.Width, ch = size.Value.Height;

            // 防 NaN/Infinity 传播（与属性面板 setter 同规则）
            if (!double.IsFinite(_widget.X) || !double.IsFinite(_widget.Y)
             || !double.IsFinite(_widget.Width) || !double.IsFinite(_widget.Height))
                return;

            double w = Math.Min(_widget.Width, cw);
            double h = Math.Min(_widget.Height, ch);
            double x, y;
            // 圆形：钳制后重算正圆（直径不超画布短边，中心锚定）
            if (_widget is CircleWidget)
            {
                double csize = Math.Min(Math.Max(w, h), Math.Min(cw, ch));
                double centerX = _widget.X + _widget.Width / 2;
                double centerY = _widget.Y + _widget.Height / 2;
                // 对称钳制：下界 0 + 上界 画布-尺寸（超界状态可自愈）
                x = Math.Min(Math.Max(0, centerX - csize / 2), cw - csize);
                y = Math.Min(Math.Max(0, centerY - csize / 2), ch - csize);
                w = csize;
                h = csize;
            }
            else
            {
                x = Math.Max(0, Math.Min(_widget.X, cw - w));
                y = Math.Max(0, Math.Min(_widget.Y, ch - h));
            }

            // Line：X2/Y2 是相对 Widget 左上角的偏移，随边界框钳制同步（防线终点超画布/线与框错位）
            if (_widget is LineWidget line)
            {
                line.X2 = Math.Min(Math.Max(0, line.X2), w);
                line.Y2 = Math.Min(Math.Max(0, line.Y2), h);
            }
            // Polygon：边界框钳制产生的 ΔX/ΔY 统一平移顶点（防形状与命中框错位）
            else if (_widget is PolygonWidget poly && poly.Points.Count > 0)
            {
                double dx = x - _widget.X, dy = y - _widget.Y;
                if (dx != 0 || dy != 0)
                {
                    foreach (var pt in poly.Points) { pt.X += dx; pt.Y += dy; }
                    poly.Points = new List<PointD>(poly.Points);   // 无 INPC → 重赋值触发通知
                }
            }

            _widget.X = x;
            _widget.Y = y;
            _widget.Width = w;
            _widget.Height = h;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Rect rect = new Rect(0, 0, _adornedElement.ActualWidth, _adornedElement.ActualHeight);
            drawingContext.DrawRectangle(null, _selectionPen, rect);
        }

        protected override int VisualChildrenCount => _visualChildren.Count;
        protected override Visual GetVisualChild(int index) => _visualChildren[index];
    }
}
