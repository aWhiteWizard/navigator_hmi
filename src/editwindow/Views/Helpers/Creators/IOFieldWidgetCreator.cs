using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>IO 域控件创建器：点击位置居中创建，ObjectName 自动分配（「IO域N」序号递增）。</summary>
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
