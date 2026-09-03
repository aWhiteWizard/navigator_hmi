using NavigatorHMI.Common;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>历史记录控件创建器（P-5，2026-09-02）：点击位置为中心创建（默认 360×200），ObjectName「历史N」递增。</summary>
    public class HistoryViewWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("历史"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new HistoryViewWidget
            {
                X = position.X - 180, Y = position.Y - 100,
                Width = 360, Height = 200,
                ObjectName = $"历史{maxNum + 1}"
            };
        }
    }
}
