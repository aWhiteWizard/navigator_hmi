using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>数值显示控件创建器：点击位置居中创建，ObjectName 自动分配（「数值N」序号递增）。</summary>
    public class NumericDisplayWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("数值显示"))
                    { string rest = w.ObjectName.Substring(4); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new NumericDisplayWidget
            {
                X = position.X - 40, Y = position.Y - 15,
                Width = 80, Height = 30,
                ObjectName = $"数值显示{maxNum + 1}"
            };
        }
    }
}
