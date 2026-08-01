using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// CircleWidget 创建策略。两点式绘制：第一点击圆心，第二点击确定半径。
    /// 生成正圆（Width == Height），拖拽缩放始终正圆（自由椭圆用 EllipseWidget）。
    /// </summary>
    public class CircleWidgetCreator : ITwoPointCreator
    {
        /// <summary>单点创建（接口兼容备用路径，默认半径 30px 正圆）。</summary>
        public Widget Create(Point position, Screen screen)
        {
            return Create(position, new Point(position.X + 30, position.Y), screen);
        }

        public Widget Create(Point start, Point end, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("圆形"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            // 圆心 = start，半径 = start→end 距离
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            radius = Math.Max(10, radius);  // 最小半径 10px

            return new CircleWidget
            {
                X = start.X - radius,
                Y = start.Y - radius,
                Width = radius * 2,
                Height = radius * 2,
                ObjectName = $"圆形{maxNum + 1}"
            };
        }
    }
}
