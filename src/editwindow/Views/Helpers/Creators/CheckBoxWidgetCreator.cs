using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    public class CheckBoxWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("复选框"))
                    { string rest = w.ObjectName.Substring(3); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new CheckBoxWidget
            {
                X = position.X - 50, Y = position.Y - 12,
                Width = 100, Height = 24,
                ObjectName = $"复选框{maxNum + 1}"
            };
        }
    }
}
