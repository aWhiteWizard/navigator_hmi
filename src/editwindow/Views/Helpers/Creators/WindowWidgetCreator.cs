using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>W4 窗口控件创建器：默认 240x180，名称按类型（用户视图N/报警视图N/机器人列表N）——名称与 Type 一致。</summary>
    public class WindowWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            var type = WindowType.UserView;
            string prefix = "用户视图";
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w is WindowWidget ww)
                    {
                        if (ww.Type != WindowType.UserView) { type = ww.Type; prefix = ww.Type == WindowType.AlarmView ? "报警视图" : "机器人列表"; }
                        if (ww.ObjectName != null && ww.ObjectName.StartsWith(prefix))
                        {
                            var rest = ww.ObjectName.Substring(prefix.Length);
                            if (int.TryParse(rest, out var n) && n > maxNum) maxNum = n;
                        }
                    }

            return new WindowWidget
            {
                Type = type,   // 名称前缀与类型一致（W4 审查：名实相符）
                X = position.X - 120, Y = position.Y - 90,
                Width = 240, Height = 180,
                ObjectName = $"{prefix}{maxNum + 1}"
            };
        }
    }
}
