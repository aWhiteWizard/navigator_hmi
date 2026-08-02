using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 阵列辅助线落点标记覆盖层：蓝色勾边透明空心方块 + 序号数字（1、2、3…）。
    /// 自绘（OnRender）实现，避免动态 Children.Add 在 AvalonDock/ScrollViewer 下不可见的问题。
    /// 方块中心 = 阵列中心点（网格线交点）；数字默认在方块右上方，
    /// 若与其他数字矩形重叠（控件/点重合场景）则按候选位置序列避让，尽可能保证序号可读。
    /// </summary>
    public class ArrayGuideOverlay : FrameworkElement
    {
        private const double SquareSize = 12;
        private const double LabelFontSize = 11;
        private const double LabelOffsetX = 8;      // 数字相对方块的水平间距
        private const double LabelGapY = 5;         // 数字相对方块上/下缘的间距
        private const double LabelStepY = 14;       // 避让阶梯步长（斜下排布）
        private const double LabelPadding = 4;      // 数字矩形避让判定 padding
        private const int MaxLabelTiers = 32;       // 阶梯级数上限（32 级 ≈ 448px，超出视为极端场景）

        private readonly List<Point> _centers = new();
        private static readonly Pen _boxPen = CreateBoxPen();
        private static readonly Typeface _typeface = new Typeface("Segoe UI");
        private static readonly Brush _textBrush = CreateTextBrush();

        private static Pen CreateBoxPen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), 1.5);
            pen.Freeze();
            return pen;
        }

        private static Brush CreateTextBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x00, 0x60, 0xB0));
            brush.Freeze();
            return brush;
        }

        /// <summary>设置落点中心列表并重绘。</summary>
        public void SetMarks(IReadOnlyList<Point> centers)
        {
            _centers.Clear();
            if (centers != null) _centers.AddRange(centers);
            InvalidateVisual();
        }

        /// <summary>清除所有标记。</summary>
        public void ClearMarks()
        {
            _centers.Clear();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_centers.Count == 0) return;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var renderSize = new Size(RenderSize.Width > 0 ? RenderSize.Width : double.PositiveInfinity,
                                      RenderSize.Height > 0 ? RenderSize.Height : double.PositiveInfinity);

            // 预生成全部数字文本（尺寸用于避让判定）
            var texts = new FormattedText[_centers.Count];
            for (int i = 0; i < _centers.Count; i++)
            {
                texts[i] = new FormattedText(
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _typeface, LabelFontSize, _textBrush, dpi);
            }

            // 已放置数字矩形（含 padding），用于避让判定
            var placed = new List<Rect>(_centers.Count);
            for (int i = 0; i < _centers.Count; i++)
            {
                var c = _centers[i];
                double half = SquareSize / 2;
                // 蓝色勾边透明空心方块——Windows 风格选中标记
                dc.DrawRectangle(null, _boxPen, new Rect(c.X - half, c.Y - half, SquareSize, SquareSize));

                var text = texts[i];
                double tw = text.Width + LabelPadding, th = text.Height + LabelPadding;

                // 候选位置：4 角 + 四方向斜下阶梯逐级递增，直到找到不重叠或达级数上限
                Point pos = default;
                bool found = false;
                foreach (var cand in BuildCandidates(c, tw, th, renderSize))
                {
                    var rect = new Rect(cand.X, cand.Y, tw, th);
                    if (!placed.Any(r => r.IntersectsWith(rect)))
                    {
                        pos = cand;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    // 极端密集场景（候选用尽）：选与已放置矩形重叠面积最小的位置，并钳制在可见区域内
                    pos = PickLeastOverlap(c, tw, th, placed, renderSize);
                }
                placed.Add(new Rect(pos.X, pos.Y, tw, th));
                dc.DrawText(text, pos);
            }
        }

        /// <summary>
        /// 生成数字候选位置序列（依次尝试，第一个不与已放置矩形重叠者胜出）：
        /// 右上(默认) → 右下 → 左上 → 左下 → 右上/左上/右下/左下斜下阶梯逐级递增。
        /// 超出可见区域的候选会被钳制回可见区域。
        /// </summary>
        private static IEnumerable<Point> BuildCandidates(Point c, double tw, double th, Size renderSize)
        {
            double top = c.Y - th - LabelGapY;
            double bottom = c.Y + SquareSize / 2 + LabelGapY;
            double left = c.X - tw - LabelOffsetX;
            double right = c.X + LabelOffsetX;

            yield return ClampToVisible(new Point(right, top), tw, th, renderSize);    // 右上（默认）
            yield return ClampToVisible(new Point(right, bottom), tw, th, renderSize); // 右下
            yield return ClampToVisible(new Point(left, top), tw, th, renderSize);     // 左上
            yield return ClampToVisible(new Point(left, bottom), tw, th, renderSize);  // 左下
            for (int k = 1; k <= MaxLabelTiers; k++)
            {
                double step = k * LabelStepY;
                yield return ClampToVisible(new Point(right, top + step), tw, th, renderSize);    // 右上往下
                yield return ClampToVisible(new Point(left, top + step), tw, th, renderSize);     // 左上往下
                yield return ClampToVisible(new Point(right, bottom + step), tw, th, renderSize); // 右下往下
                yield return ClampToVisible(new Point(left, bottom + step), tw, th, renderSize);  // 左下往下
            }
        }

        /// <summary>将数字左上角钳制到可见区域内（0 ≤ X ≤ W-tw，0 ≤ Y ≤ H-th）。</summary>
        private static Point ClampToVisible(Point p, double tw, double th, Size renderSize)
        {
            double x = p.X, y = p.Y;
            if (double.IsFinite(renderSize.Width)) x = Math.Max(0, Math.Min(x, renderSize.Width - tw));
            if (double.IsFinite(renderSize.Height)) y = Math.Max(0, Math.Min(y, renderSize.Height - th));
            return new Point(x, y);
        }

        /// <summary>
        /// 兜底：候选用尽时（极端密集）选与已放置矩形重叠面积最小者，不保证完全避让。
        /// </summary>
        private static Point PickLeastOverlap(Point c, double tw, double th, List<Rect> placed, Size renderSize)
        {
            Point best = default;
            double bestOverlap = double.MaxValue;
            foreach (var cand in BuildCandidates(c, tw, th, renderSize))
            {
                var rect = new Rect(cand.X, cand.Y, tw, th);
                double overlap = 0;
                foreach (var r in placed)
                {
                    double iw = Math.Min(rect.Right, r.Right) - Math.Max(rect.Left, r.Left);
                    double ih = Math.Min(rect.Bottom, r.Bottom) - Math.Max(rect.Top, r.Top);
                    if (iw > 0 && ih > 0) overlap += iw * ih;
                }
                if (overlap == 0) return cand; // 找到完全不重叠的候选，立即返回
                if (overlap < bestOverlap)
                {
                    bestOverlap = overlap;
                    best = cand;
                }
            }
            return best;
        }
    }
}
