using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// LineWidget 创建策略。两点式绘制：第一点击起点，第二点击终点。
    /// X = 第一点击点，X2/Y2 = 终点相对起点的签名偏移（支持任意方向拖拽）。
    /// 注意：边界框（Width/Height）仅用于命中测试，可能不完全覆盖负方向线段（设计取舍）。
    /// </summary>
    public class LineWidgetCreator : ITwoPointCreator
    {
        /// <summary>单点创建（接口兼容备用路径，默认水平线 100px）。</summary>
        public Widget Create(Point position, Screen screen)
        {
            return Create(position, new Point(position.X + 100, position.Y), screen);
        }

        /// <summary>两点式创建：起点→终点。</summary>
        public Widget Create(Point start, Point end, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("直线"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            // 起点 = 第一点击点，X2/Y2 = 终点相对起点的签名偏移（支持任意方向）
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double absW = Math.Max(1, Math.Abs(dx));
            double absH = Math.Max(1, Math.Abs(dy));

            return new LineWidget
            {
                X = start.X,
                Y = start.Y,
                Width = absW,   // 边界框仅用于点击检测，覆盖线段范围
                Height = absH,
                X2 = dx,        // 终点相对 Widget 左上角（=起点）的偏移，可为负
                Y2 = dy,
                ObjectName = $"直线{maxNum + 1}"
            };
        }
    }
}
