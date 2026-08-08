using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>C13 日期时间控件创建器：默认 180x28，名称 日期时间N。</summary>
    public class DateTimeWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("日期时间"))
                    { string rest = w.ObjectName.Substring(4); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new DateTimeWidget
            {
                X = position.X - 90, Y = position.Y - 14,
                Width = 180, Height = 28,
                ObjectName = $"日期时间{maxNum + 1}"
            };
        }
    }
}
