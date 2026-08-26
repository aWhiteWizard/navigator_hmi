using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>标签控件创建器：点击位置居中创建，ObjectName 自动分配（「标签N」序号递增）。</summary>
    public class LabelWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("标签"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new LabelWidget
            {
                X = position.X - 40, Y = position.Y - 12,
                Width = 80, Height = 24,
                Text = "标签",
                ObjectName = $"标签{maxNum + 1}"
            };
        }
    }
}
