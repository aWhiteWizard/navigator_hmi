using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    public class LineWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("直线"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new LineWidget
            {
                X = position.X - 50, Y = position.Y,
                Width = 100, Height = 1, X2 = position.X + 50, Y2 = position.Y,
                ObjectName = $"直线{maxNum + 1}"
            };
        }
    }
}
