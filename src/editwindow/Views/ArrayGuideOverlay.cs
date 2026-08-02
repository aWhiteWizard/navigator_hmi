using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 阵列辅助线落点标记覆盖层：蓝色勾边透明空心方块 + 序号数字（1、2、3…）。
    /// 自绘（OnRender）实现，避免动态 Children.Add 在 AvalonDock/ScrollViewer 下不可见的问题。
    /// 方块中心 = 阵列中心点（网格线交点），数字显示在方块右上方。
    /// </summary>
    public class ArrayGuideOverlay : FrameworkElement
    {
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

            for (int i = 0; i < _centers.Count; i++)
            {
                var c = _centers[i];
                // 蓝色勾边透明空心方块（12×12，中心对齐）——Windows 风格选中标记
                dc.DrawRectangle(null, _boxPen, new Rect(c.X - 6, c.Y - 6, 12, 12));
                // 重合点阶梯偏移：与前一点距离 < 12px（方块尺寸）判为重合，序号按 8px 斜下错开，保证数字可区分
                double offset = 0;
                if (i > 0)
                {
                    var prev = _centers[i - 1];
                    double dx = c.X - prev.X, dy = c.Y - prev.Y;
                    if (dx * dx + dy * dy < 12 * 12) offset = (i % 5) * 8; // 0/8/16/24/32 斜下阶梯
                }
                // 序号数字（方块右上方 + 重合错位偏移）
                var text = new FormattedText(
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    _typeface, 11, _textBrush, dpi);
                dc.DrawText(text, new Point(c.X + 8, c.Y - 16 + offset));
            }
        }
    }
}
