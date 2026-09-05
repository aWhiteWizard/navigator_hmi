using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>框架/视频控件创建器：点击位置居中创建，ObjectName 自动分配（「视频N」序号递增——S-2 2026-09-05
    /// 用户拍板：Frame 现以视频用途为主，默认名「框架N」→「视频N」；老工程已有「框架N」不迁移，扫描兼容两种前缀防重名）。</summary>
    public class FrameWidgetCreator : IWidgetCreator
    {
        public Widget Create(Point position, Screen screen)
        {
            int maxNum = 0;
            if (screen?.Widgets != null)
                foreach (var w in screen.Widgets)
                {
                    if (w.ObjectName != null && w.ObjectName.StartsWith("框架"))   // 兼容旧前缀（老工程对象仍存在时新控件不撞名）
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }
                    if (w.ObjectName != null && w.ObjectName.StartsWith("视频"))
                    { string rest = w.ObjectName.Substring(2); if (int.TryParse(rest, out int n) && n > maxNum) maxNum = n; }
                }

            return new FrameWidget
            {
                X = position.X - 75, Y = position.Y - 50,
                Width = 150, Height = 100,
                ObjectName = $"视频{maxNum + 1}"
            };
        }
    }
}
