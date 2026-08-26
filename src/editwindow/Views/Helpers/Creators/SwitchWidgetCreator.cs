using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>开关控件创建器：点击位置居中创建，ObjectName 自动分配（「开关N」序号递增）。</summary>
    public class SwitchWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("开关"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new SwitchWidget
            {
                X = position.X - 30, Y = position.Y - 15,
                Width = 60, Height = 30,
                ObjectName = $"开关{maxNum + 1}"
            };
        }
    }
}
