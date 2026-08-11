using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// 多边形创建器（世界地图批 3）。交互流程：连续左键放置顶点、右键闭合。
    /// <see cref="EditWindow"/> 检测到本创建器时走 N 点连续点击状态机（收集顶点 → 闭合时调 <see cref="Create(IReadOnlyList{Point}, Screen)"/>）。
    /// 顶点存画布绝对坐标（P7）；X/Y/Width/Height = 顶点包围盒（派生态，命中框/选中框用）。
    /// </summary>
    public class PolygonWidgetCreator : IWidgetCreator
    {
        /// <summary>单点创建（退路：仅 1 点，不闭合——调用方不应走此路径）。</summary>
        public Widget Create(Point position, Screen screen) => Create(new[] { position }, screen);

        /// <summary>按顶点序列创建多边形（顶点画布绝对坐标；X/Y/Width/Height = 包围盒）。</summary>
        public PolygonWidget Create(IReadOnlyList<Point> points, Screen screen)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("多边形至少需要一个顶点", nameof(points));
            double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X), maxY = points.Max(p => p.Y);
            var poly = new PolygonWidget
            {
                X = minX,
                Y = minY,
                Width = Math.Max(maxX - minX, 1),
                Height = Math.Max(maxY - minY, 1),
                ObjectName = $"polygon_{screen.Widgets.Count + 1}",
            };
            foreach (var p in points)
                poly.Points.Add(new PointD(p.X, p.Y));   // P7：绝对坐标（不再 -minX/-minY 归一化）
            return poly;
        }
    }
}
