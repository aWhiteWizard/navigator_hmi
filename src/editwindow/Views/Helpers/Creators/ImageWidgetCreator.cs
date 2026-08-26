using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>图片控件创建器：点击位置居中创建，ObjectName 自动分配（「图片N」序号递增）。</summary>
    public class ImageWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w.ObjectName != null && w.ObjectName.StartsWith("图片"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }

            return new ImageWidget
            {
                X = position.X - 50, Y = position.Y - 50,
                Width = 100, Height = 100,
                ObjectName = $"图片{maxNum + 1}"
            };
        }
    }
}
