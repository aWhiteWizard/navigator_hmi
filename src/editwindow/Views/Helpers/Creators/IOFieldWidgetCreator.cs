using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    public class IOFieldWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("IO域"))
                    { string rest = w.ObjectName.Substring(3); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new IOFieldWidget
            {
                X = position.X - 40, Y = position.Y - 12,
                Width = 80, Height = 24,
                ObjectName = $"IO域{maxNum + 1}"
            };
        }
    }
}
