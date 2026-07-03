using NavigatorHMI.Common;
using System.Windows;
using static System.Net.Mime.MediaTypeNames;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// ButtonWidget 的创建策略。
    /// 在画布上以点击位置为中心创建一个 80×30 的按钮控件。
    /// </summary>
    public class ButtonWidgetCreator : IWidgetCreator
    {
        /// <summary>
        /// 在指定位置创建一个默认尺寸为 80×30 的 <see cref="ButtonWidget"/>，
        /// 以点击位置为控件中心进行偏移。
        /// </summary>
        /// <param name="position">相对于画布的点击位置</param>
        /// <returns>已设置默认属性的 ButtonWidget 实例</returns>
        public Widget Create(Point position)
        {
            return new ButtonWidget
            {
                X = position.X - 40,   // 居中偏移：宽度的一半
                Y = position.Y - 15,   // 居中偏移：高度的一半
                Width = 80,
                Height = 30,
                Text = "新按钮",
                ObjectName = "新按钮"
            };
        }
    }
}
