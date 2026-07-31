using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// Widget 选中状态管理器。
    /// 负责数据模型（<see cref="ButtonWidget.IsSelected"/>）与 UI 装饰器（Adorner）
    /// 之间的双向同步。通过 <see cref="SelectorHelper.SetIsSelected"/> 附加属性
    /// 将数据层的选中状态映射到按钮的视觉高亮效果。
    /// </summary>
    public class WidgetSelectionManager
    {
        private readonly Canvas _canvas;
        private readonly Func<EditWindowViewModel> _viewModelProvider;
        /// <summary>选中状态变化时触发的事件，参数为选中的 Widget（null=取消选中）。</summary>
        public event Action<Widget?>? WidgetSelected;

        /// <summary>
        /// 初始化 <see cref="WidgetSelectionManager"/> 实例。
        /// </summary>
        /// <param name="canvas">
        /// 画布容器，所有 Widget 按钮的父容器。
        /// 用于遍历子元素查找 <see cref="ItemsControl"/> 和 <see cref="Button"/>。
        /// </param>
        /// <param name="viewModelProvider">
        /// ViewModel 提供者委托，延迟获取以确保始终拿到最新的
        /// <see cref="EditWindowViewModel"/> 引用（例如画面切换后 ViewModel 可能变更）。
        /// </param>
        public WidgetSelectionManager(Canvas canvas, Func<EditWindowViewModel> viewModelProvider)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
        }

        /// <summary>
        /// 选中指定的 Widget，同时清除其他所有 Widget 的选中状态。
        /// 修改数据模型后自动调用 <see cref="UpdateSelectionUI"/> 同步 UI 装饰器。
        /// </summary>
        /// <param name="widget">要选中的 <see cref="ButtonWidget"/> 实例</param>
        public void SelectWidget(Widget widget)
        {
            if (widget == null) return;

            var vm = _viewModelProvider();
            if (vm?.CurrentScreen?.Widgets == null) return;

            // 清除所有 Widget 的数据模型选中状态
            foreach (var w in vm.CurrentScreen.Widgets)
            {
                w.IsSelected = false;
            }
            // 设置目标 Widget 为选中
            widget.IsSelected = true;

            // 同步 UI 装饰器
            UpdateSelectionUI();

            System.Diagnostics.Debug.WriteLine($"✅ 选中按钮: {(widget as ButtonWidget)?.Text ?? widget.GetType().Name}");
            // 通知属性窗口（或其它订阅者）选中状态变更
            WidgetSelected?.Invoke(widget);
        }

        /// <summary>
        /// 选中指定的 Widget（仅修改状态，不触发事件通知）。
        /// 清除其他所有 Widget 的选中状态并同步 UI 装饰器，但不触发 <see cref="WidgetSelected"/> 事件。
        /// 用于单击选中等不需要弹出属性窗口的场景。
        /// </summary>
        /// <param name="widget">要选中的 <see cref="ButtonWidget"/> 实例</param>
        public void SelectWidgetSilent(Widget widget)
        {
            if (widget == null) return;

            var vm = _viewModelProvider();
            if (vm?.CurrentScreen?.Widgets == null) return;

            // 清除所有 Widget 的数据模型选中状态
            foreach (var w in vm.CurrentScreen.Widgets)
            {
                w.IsSelected = false;
            }
            // 设置目标 Widget 为选中
            widget.IsSelected = true;

            // 同步 UI 装饰器
            UpdateSelectionUI();

            System.Diagnostics.Debug.WriteLine($"✅ 静默选中: {(widget as ButtonWidget)?.Text ?? widget.GetType().Name}");
        }

        // 双击检测字段
        private DateTime _lastClickTime;
        private Point _lastClickPosition;

        /// <summary>
        /// 统一处理 Widget 的点击事件，自动区分单击和双击。
        /// 单击 → 静默选中（不触发 <see cref="WidgetSelected"/> 事件，不弹出属性窗口）。
        /// 双击 → 完整选中（触发事件弹出属性窗口）。
        /// 所有 Widget 类型统一通过此方法处理点击，新增控件无需重复实现双击检测。
        /// </summary>
        /// <param name="widget">被点击的 Widget 实例</param>
        /// <param name="clickPosition">点击位置（相对于画布坐标，通过 <c>Mouse.GetPosition(canvas)</c> 获取）</param>
        public void HandleWidgetClick(Widget widget, Point clickPosition)
        {
            if (widget == null) return;

            var now = DateTime.Now;

            bool isDoubleClick = (now - _lastClickTime).TotalMilliseconds < 500
                              && Math.Abs(clickPosition.X - _lastClickPosition.X) < 10
                              && Math.Abs(clickPosition.Y - _lastClickPosition.Y) < 10;

            _lastClickTime = now;
            _lastClickPosition = clickPosition;

            if (isDoubleClick)
                SelectWidget(widget);       // 完整选中→弹出属性窗口
            else if (!widget.IsSelected)
                SelectWidgetSilent(widget); // 未选中→静默选中；已选中→保持（不破坏多选）
            // 已选中时不动选中状态：支持框选 A+B 后按住 A 整体拖拽
        }

        /// <summary>
        /// 清除所有 Widget 的选中状态。
        /// 同时清除数据模型层（<see cref="ButtonWidget.IsSelected"/>）
        /// 和 UI 层（通过 <see cref="SelectorHelper.SetIsSelected"/>）的选中标记。
        /// </summary>
        public void ClearAllSelection()
        {
            var vm = _viewModelProvider();
            if (vm?.CurrentScreen?.Widgets == null) return;

            // 清除数据模型的选中状态
            foreach (var widget in vm.CurrentScreen.Widgets)
            {
                widget.IsSelected = false;
            }

            // 清除 UI 上控件的装饰器 — 遍历画布中所有子元素
            foreach (UIElement child in _canvas.Children)
            {
                if (child is FrameworkElement fe)
                {
                    SelectorHelper.SetIsSelected(fe, false);
                }
            }
            // 通知属性窗口（或其它订阅者）选中已清除
            WidgetSelected?.Invoke(null);
        }

        /// <summary>
        /// 遍历 <see cref="_canvas"/> 中的 <see cref="ItemsControl"/>，
        /// 将每个按钮的选中视觉效果同步到对应 <see cref="ButtonWidget.IsSelected"/> 数据状态。
        /// </summary>
        /// <remarks>
        /// 由于 WPF 的 <see cref="ItemsControl"/> 使用虚拟化容器生成 UI 元素，
        /// 数据模型上的 <c>IsSelected</c> 属性变更不会自动刷新按钮的附加属性。
        /// 此方法手动遍历所有已生成的项容器，通过 <see cref="SelectorHelper.SetIsSelected"/>
        /// 将数据层的选中状态同步到 UI 层，确保选中高亮与实际数据一致。
        /// <para>
        /// 调用时机：在修改数据模型选中状态之后（如 <see cref="SelectWidget"/> 内部会自动调用）。
        /// </para>
        /// </remarks>
        public void UpdateSelectionUI()
        {
            // 从 Canvas 子元素中查找 ItemsControl（按钮列表的宿主控件）
            var itemsControl = _canvas.Children.OfType<ItemsControl>().FirstOrDefault();
            if (itemsControl == null) return;

            // 遍历所有数据项对应的 UI 容器，逐一同步选中状态
            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                // 通过 ItemContainerGenerator 获取第 i 个数据项对应的 ContentPresenter
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container != null)
                {
                    // ContentPresenter 的第一个视觉子元素即为数据模板生成的控件
                    var element = VisualTreeHelper.GetChild(container, 0) as FrameworkElement;
                    if (element != null)
                    {
                        // 从控件的 DataContext 获取对应的数据模型
                        var widget = element.DataContext as Widget;
                        if (widget != null)
                        {
                            // 将数据模型的 IsSelected 同步到控件的附加属性，触发选中高亮样式
                            SelectorHelper.SetIsSelected(element, widget.IsSelected);
                        }
                    }
                }
            }
        }
    }
}
