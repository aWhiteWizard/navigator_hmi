using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>文本列表控件创建器（替代原 TextBoxWidgetCreator）。</summary>
    public class TextListWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("文本列表"))
                    { string rest = w.ObjectName.Substring(4); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new TextListWidget
            {
                X = position.X - 50, Y = position.Y - 12,
                Width = 100, Height = 24,
                ObjectName = $"文本列表{maxNum + 1}"
            };
        }
    }
}
