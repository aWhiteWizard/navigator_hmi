using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>框架控件创建器：点击位置居中创建，ObjectName 自动分配（「框架N」序号递增）。</summary>
    public class FrameWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("框架"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new FrameWidget
            {
                X = position.X - 75, Y = position.Y - 50,
                Width = 150, Height = 100,
                ObjectName = $"框架{maxNum + 1}"
            };
        }
    }
}
