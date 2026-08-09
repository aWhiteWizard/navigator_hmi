using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>W4/任务A 窗口控件创建器：默认 240x180，按类型建 WindowWidget（Type 判别——UserView/AlarmView/RobotList），名称前缀随类型（用户视图N/报警视图N/机器人列表N）。</summary>
    public class WindowWidgetCreator : IWidgetCreator
    {
        private readonly WindowType _type;

        public WindowWidgetCreator(WindowType type = WindowType.UserView) => _type = type;

        public Widget Create(Point position, Screen screen)
        {
            string prefix = _type switch
            {
                WindowType.AlarmView => "报警视图",
                WindowType.RobotList => "机器人列表",
                _ => "用户视图",
            };
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                    if (w is WindowWidget ww && ww.ObjectName != null && ww.ObjectName.StartsWith(prefix))
                    {
                        var rest = ww.ObjectName.Substring(prefix.Length);
                        if (int.TryParse(rest, out var n) && n > maxNum) maxNum = n;
                    }

            var title = _type switch { WindowType.AlarmView => "报警", WindowType.RobotList => "机器人列表", _ => "用户" };
            return new WindowWidget
            {
                Type = _type,
                Title = title,
                X = position.X - 120, Y = position.Y - 90,
                Width = 240, Height = 180,
                ObjectName = $"{prefix}{maxNum + 1}"
            };
        }
    }
}
