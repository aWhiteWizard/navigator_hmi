using System.Windows;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// 点控件创建器（世界地图批 3）。单点式：点击处放置标记（24×24），右上角标签可后设。
    /// 普通画面按画面坐标放置；世界地图模式经纬度由批 4 处理（FixedPoint/绑 GPS 变量）。
    /// </summary>
    public class PointWidgetCreator : IWidgetCreator
    {
        private const double MarkerSize = 24;

        public Widget Create(Point position, Screen screen)
        {
            return new PointWidget
            {
                X = position.X - MarkerSize / 2,
                Y = position.Y - MarkerSize / 2,
                Width = MarkerSize,
                Height = MarkerSize,
                ObjectName = $"point_{screen.Widgets.Count + 1}",
            };
        }
    }
}
