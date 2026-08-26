using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>进度条控件创建器：点击位置居中创建，ObjectName 自动分配（「进度条N」序号递增）。</summary>
    public class ProgressBarWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("进度条"))
                    { string rest = w.ObjectName.Substring(3); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new ProgressBarWidget
            {
                X = position.X - 75, Y = position.Y - 10,
                Width = 150, Height = 20,
                ObjectName = $"进度条{maxNum + 1}"
            };
        }
    }
}
