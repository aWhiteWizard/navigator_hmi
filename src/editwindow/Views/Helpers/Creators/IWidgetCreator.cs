using System.Windows;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers.Creators
{
    /// <summary>
    /// Widget 创建策略接口。
    /// 每种控件类型（Button、Text、Image 等）实现自己的创建逻辑。
    /// <see cref="EditWindow"/> 通过此接口在画布点击时创建对应 Widget，无需关心具体类型。
    /// </summary>
    /// <remarks>
    /// 使用方式：
    /// <code>
    /// // 进入添加模式
    /// _currentWidgetCreator = new ButtonWidgetCreator();
    /// // 画布点击时
    /// var widget = _currentWidgetCreator.Create(clickPosition);
    /// // 添加后退出添加模式
    /// _currentWidgetCreator = null;
    /// </code>
    /// 新增控件类型时，只需实现此接口并新建一个 Creator 类，无需修改 <see cref="EditWindow"/>。
    /// </remarks>
    public interface IWidgetCreator
    {
        /// <summary>
        /// 在指定画布坐标处创建一个 Widget 实例。
        /// 各实现类负责设置默认位置（通常以点击位置居中）、尺寸和类型特有属性。
        /// </summary>
        /// <param name="position">相对于画布的点击位置</param>
        /// <returns>创建好的 Widget 实例，已设置默认的 X/Y/Width/Height 及类型特有属性</returns>
        Widget Create(Point position);
    }
}
