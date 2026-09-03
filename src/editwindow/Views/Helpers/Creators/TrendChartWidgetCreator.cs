using NavigatorHMI.Common;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>趋势图控件创建器（P-4，2026-09-02）：点击位置为中心创建（默认 300×150 较大尺寸），ObjectName「趋势图N」递增。</summary>
    public class TrendChartWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("趋势图"))
                    { string rest = w.ObjectName.Substring(3); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new TrendChartWidget
            {
                X = position.X - 150, Y = position.Y - 75,
                Width = 300, Height = 150,
                ObjectName = $"趋势图{maxNum + 1}"
            };
        }
    }
}
