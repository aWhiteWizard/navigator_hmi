using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// Widget 右键菜单处理器。
    /// 负责画布上 Widget 按钮的右键菜单显示和删除交互逻辑。
    /// 将原本集中在 EditWindow code-behind 中的菜单处理代码独立出来，降低窗口的职责复杂度。
    /// </summary>
    public class WidgetContextMenuHandler
    {
        private readonly Popup _widgetContextMenu;
        private readonly Func<EditWindowViewModel> _viewModelProvider;
        private readonly WidgetSelectionManager _selectionManager;
        private readonly Action _markProjectDirty;
        private readonly Action _notifyCanvasRefresh;

        /// <summary>
        /// 初始化 <see cref="WidgetContextMenuHandler"/> 实例。
        /// </summary>
        /// <param name="widgetContextMenu">XAML 中定义的 WidgetContextMenu Popup 控件</param>
        /// <param name="viewModelProvider">ViewModel 提供者委托，延迟获取以确保始终拿到最新引用</param>
        /// <param name="selectionManager">Widget 选中状态管理器，用于删除后清除选中状态</param>
        /// <param name="markProjectDirty">标记工程已修改的回调</param>
        /// <param name="notifyCanvasRefresh">通知画布刷新的回调</param>
        public WidgetContextMenuHandler(
            Popup widgetContextMenu,
            Func<EditWindowViewModel> viewModelProvider,
            WidgetSelectionManager selectionManager,
            Action markProjectDirty,
            Action notifyCanvasRefresh)
        {
            _widgetContextMenu = widgetContextMenu ?? throw new ArgumentNullException(nameof(widgetContextMenu));
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
            _selectionManager = selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
            _markProjectDirty = markProjectDirty ?? throw new ArgumentNullException(nameof(markProjectDirty));
            _notifyCanvasRefresh = notifyCanvasRefresh ?? throw new ArgumentNullException(nameof(notifyCanvasRefresh));
        }

        /// <summary>
        /// 右键点击 Widget 时触发，在鼠标位置显示上下文菜单 Popup。
        /// 被 <see cref="Behaviors.WidgetDragBehavior"/> 通过回调调用。
        /// </summary>
        /// <param name="widget">被右键点击的 Widget 数据模型</param>
        /// <param name="screenPos">相对于窗口的鼠标坐标</param>
        public void Show(Widget widget, Point screenPos)
        {
            // 保存当前操作的 Widget 引用（用 Tag 暂存）
            _widgetContextMenu.Tag = widget;

            // 设置 Popup 位置（偏移一点避免遮挡鼠标）
            _widgetContextMenu.HorizontalOffset = screenPos.X + 5;
            _widgetContextMenu.VerticalOffset = screenPos.Y + 20;

            // 显示 Popup
            _widgetContextMenu.IsOpen = true;
        }

        /// <summary>
        /// 右键菜单「删除」按钮点击：从当前画面移除选中的 Widget。
        /// 作为 XAML 中 Button.Click 事件处理器绑定。
        /// </summary>
        public void OnDeleteWidgetClick(object sender, RoutedEventArgs e)
        {
            var widget = _widgetContextMenu.Tag as Widget;
            if (widget == null) return;

            var vm = _viewModelProvider();
            if (vm?.CurrentScreen == null) return;

            // 从集合中移除
            vm.CurrentScreen.Widgets.Remove(widget);

            // 清除选中状态（装饰器也会随之清除）
            _selectionManager.ClearAllSelection();

            // 标记工程已修改
            _markProjectDirty();

            // 刷新画布
            _notifyCanvasRefresh();

            // 关闭 Popup
            _widgetContextMenu.IsOpen = false;

            System.Diagnostics.Debug.WriteLine($"🗑 已删除 Widget: {(widget as ButtonWidget)?.Text ?? widget.GetType().Name}");
        }
    }
}
