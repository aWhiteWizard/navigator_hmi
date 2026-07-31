using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// RectangleWidget 创建策略。两点式绘制：第一点击一个角，第二点击对角。
    /// </summary>
    public class RectangleWidgetCreator : ITwoPointCreator
    {
        /// <summary>单点创建（接口兼容备用路径，默认 100×50 矩形）。</summary>
        public Widget Create(Point position, Screen screen)
        {
            return Create(position, new Point(position.X + 100, position.Y + 50), screen);
        }

        /// <summary>两点式创建：一个角→对角。</summary>
        public Widget Create(Point start, Point end, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("矩形"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            double x = Math.Min(start.X, end.X);
            double y = Math.Min(start.Y, end.Y);

            return new RectangleWidget
            {
                X = x,
                Y = y,
                Width = Math.Max(10, Math.Abs(end.X - start.X)),
                Height = Math.Max(10, Math.Abs(end.Y - start.Y)),
                ObjectName = $"矩形{maxNum + 1}"
            };
        }
    }
}
