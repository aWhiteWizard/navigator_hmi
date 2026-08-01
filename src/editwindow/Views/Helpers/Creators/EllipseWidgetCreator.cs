using NavigatorHMI.Common;
using System;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// EllipseWidget 创建策略。两点式绘制：第一点击椭圆左上角，第二点击确定宽高（自由椭圆）。
    /// </summary>
    public class EllipseWidgetCreator : ITwoPointCreator
    {
        /// <summary>单点创建（接口兼容备用路径，默认 60x40 椭圆）。</summary>
        public Widget Create(Point position, Screen screen)
        {
            return Create(position, new Point(position.X + 60, position.Y + 40), screen);
        }

        public Widget Create(Point start, Point end, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("椭圆"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            // 左上角 = start，右下角 = end（自由宽高）
            double x = Math.Min(start.X, end.X);
            double y = Math.Min(start.Y, end.Y);
            double wid = Math.Max(Math.Abs(end.X - start.X), 10);
            double hei = Math.Max(Math.Abs(end.Y - start.Y), 10);

            return new EllipseWidget
            {
                X = x,
                Y = y,
                Width = wid,
                Height = hei,
                ObjectName = $"椭圆{maxNum + 1}"
            };
        }
    }
}
