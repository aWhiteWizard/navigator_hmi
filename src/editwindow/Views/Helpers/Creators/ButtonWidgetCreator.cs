using NavigatorHMI.Common;
using System.Linq;
using System.Windows;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// ButtonWidget 的创建策略。
    /// 在画布上以点击位置为中心创建一个 80×30 的按钮控件，并自动分配唯一 ObjectName。
    /// </summary>
    public class ButtonWidgetCreator : IWidgetCreator
    {
    /// <summary>
        /// 在指定位置创建一个默认尺寸为 80×30 的 <see cref="ButtonWidget"/>，
        /// 以点击位置为控件中心进行偏移，并根据画面中已有控件自动生成唯一 ObjectName。
        /// </summary>
        /// <param name="position">相对于画布的点击位置</param>
        /// <param name="screen">当前画面，用于扫描已有 ObjectName 避免重复</param>
        /// <returns>已设置默认属性的 ButtonWidget 实例</returns>
        public Widget Create(Point position, Screen screen)
        {
            // 自动分配唯一 ObjectName：查找画面中已有 "按钮N" 的最大编号
            int maxNum = 0;
            if (screen?.Widgets != null)
            {
                foreach (var w in screen.Widgets)
                {
                    if (w.ObjectName != null && w.ObjectName.StartsWith("按钮"))
                    {
                        string rest = w.ObjectName.Substring(2);
                        if (int.TryParse(rest, out int n) && n > maxNum)
                            maxNum = n;
                    }
                }
            }
            string objectName = $"按钮{maxNum + 1}";

            return new ButtonWidget
            {
                X = position.X - 40,   // 居中偏移：宽度的一半
                Y = position.Y - 15,   // 居中偏移：高度的一半
                Width = 80,
                Height = 30,
                Text = "新按钮",
                ObjectName = objectName
            };
        }
    }
}
