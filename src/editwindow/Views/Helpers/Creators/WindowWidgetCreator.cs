using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>W4/P1-8 窗口控件创建器：默认 240x180，固定 UserView + 名称「用户视图N」（类型由属性面板切换，名称不随类型改）。</summary>
    public class WindowWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            // P1-8 修复：默认 UserView（不继承已有窗口控件类型——类型由属性面板切换，创建器固定 UserView）
            var type = WindowType.UserView;
            string prefix = "用户视图";
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w is WindowWidget ww && ww.ObjectName != null && ww.ObjectName.StartsWith(prefix))
                    {
                        var rest = ww.ObjectName.Substring(prefix.Length);
                        if (int.TryParse(rest, out var n) && n > maxNum) maxNum = n;
                    }

            return new WindowWidget
            {
                Type = type,
                X = position.X - 120, Y = position.Y - 90,
                Width = 240, Height = 180,
                ObjectName = $"{prefix}{maxNum + 1}"
            };
        }
    }
}
