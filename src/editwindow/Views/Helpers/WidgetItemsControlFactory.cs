using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// Widget 控件列表的 ItemsControl 工厂。
    /// 负责创建并配置用于在画布上渲染 Widget 集合的 ItemsControl 实例。
    /// 纯 UI 构建逻辑，无状态依赖，所有方法均为静态。
    /// </summary>
    /// <remarks>
    /// 事件处理器通过 <see cref="FrameworkElementFactory.AddHandler"/> 
    /// 在 DataTemplate 级别绑定，确保所有通过模板生成的 Button
    /// （包括后续动态添加的 Widget）自动获得拖拽/点击/选中能力。
    /// </remarks>
    public static class WidgetItemsControlFactory
    {
        /// <summary>
        /// 根据指定画面和事件委托创建 ItemsControl。
        /// 配置 Canvas 面板布局、双向位置绑定（X/Y）、按钮模板、
        /// 选中状态绑定及交互事件处理器。
        /// </summary>
        /// <param name="screen">要渲染的画面数据</param>
        /// <param name="clickHandler">按钮 Click 事件处理器（选中逻辑）</param>
        /// <param name="previewMouseLeftButtonDownHandler">PreviewMouseLeftButtonDown（记录拖拽起点）</param>
        /// <param name="mouseLeftButtonDownHandler">MouseLeftButtonDown（开始拖拽）</param>
        /// <param name="mouseMoveHandler">MouseMove（拖拽移动）</param>
        /// <param name="mouseLeftButtonUpHandler">MouseLeftButtonUp（结束拖拽）</param>
        /// <param name="PreviewMouseRightButtonDownEvent">PreviewMouseRightButtonDown（右键控件）</param>
        /// <param name="MouseRightButtonUpEvent">MouseRightButtonUp（打开菜单）</param>
        /// <returns>配置完成的 ItemsControl 实例，其 ItemsSource 由调用方通过 SetBinding 绑定</returns>
        public static ItemsControl Create(
            Screen screen,
            RoutedEventHandler clickHandler,
            MouseButtonEventHandler previewMouseLeftButtonDownHandler,
            MouseButtonEventHandler mouseLeftButtonDownHandler,
            MouseEventHandler mouseMoveHandler,
            MouseButtonEventHandler mouseLeftButtonUpHandler,
            MouseButtonEventHandler previewMouseRightButtonDownHandler,
            MouseButtonEventHandler mouseRightButtonUpHandler)
        {
            var itemsControl = new ItemsControl();
            itemsControl.Name = "MyItemsControl";

            // 设置 ItemsPanel — 使用 Canvas 作为子元素的布局面板
            var panelTemplate = new ItemsPanelTemplate();
            var panelFactory = new FrameworkElementFactory(typeof(Canvas));
            panelTemplate.VisualTree = panelFactory;
            itemsControl.ItemsPanel = panelTemplate;

            // 设置 ItemContainerStyle — 将数据模型的 X/Y 绑定到 Canvas.Left/Top
            var style = new Style(typeof(ContentPresenter));
            style.Setters.Add(new Setter(Canvas.LeftProperty, new Binding("X") { Mode = BindingMode.OneWay }));
            style.Setters.Add(new Setter(Canvas.TopProperty, new Binding("Y") { Mode = BindingMode.OneWay }));
            itemsControl.ItemContainerStyle = style;

            // 设置 ItemTemplate — 为每个 Widget 数据项生成一个 Button
            var dataTemplate = new DataTemplate();
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetBinding(Button.ContentProperty, new Binding("Text"));
            buttonFactory.SetBinding(Button.WidthProperty, new Binding("Width"));
            buttonFactory.SetBinding(Button.HeightProperty, new Binding("Height"));
            buttonFactory.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });

            // 在模板层绑定事件处理器 — 确保所有 Button（包括动态添加的）自动获得交互能力
            buttonFactory.AddHandler(Button.ClickEvent, clickHandler);
            buttonFactory.AddHandler(Button.PreviewMouseLeftButtonDownEvent, previewMouseLeftButtonDownHandler);
            buttonFactory.AddHandler(Button.MouseLeftButtonDownEvent, mouseLeftButtonDownHandler, handledEventsToo: true);
            buttonFactory.AddHandler(Button.MouseMoveEvent, mouseMoveHandler, handledEventsToo: true);
            buttonFactory.AddHandler(Button.MouseLeftButtonUpEvent, mouseLeftButtonUpHandler, handledEventsToo: true);

            buttonFactory.AddHandler(Button.PreviewMouseRightButtonDownEvent, previewMouseRightButtonDownHandler, true);
            buttonFactory.AddHandler(Button.MouseRightButtonUpEvent, mouseRightButtonUpHandler, handledEventsToo: true);

            dataTemplate.VisualTree = buttonFactory;
            itemsControl.ItemTemplate = dataTemplate;

            return itemsControl;
        }
    }
}
