using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Helpers;

namespace NavigatorHMI.Views.Behaviors
{
    /// <summary>
    /// Widget 拖拽行为处理器。
    /// 处理 ButtonWidget 在画布上的鼠标拖拽移动、边界限制及脏标记。
    /// 同时托管点击选中逻辑，通过 <see cref="WidgetSelectionManager"/> 统一管理选中状态。
    /// </summary>
    /// <remarks>
    /// 事件处理器（OnButtonClick 等）通过 <see cref="WidgetItemsControlFactory"/> 
    /// 在 DataTemplate 级别绑定，确保每一个生成的 Button（包括动态添加的）自动获得拖拽/选中能力。
    /// </remarks>
    public class WidgetDragBehavior
    {
        #region 拖拽状态字段

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private ButtonWidget? _draggingWidget = null;
        private double _dragStartX;
        private double _dragStartY;
        private const double DRAG_THRESHOLD = 5;

        #endregion

        #region 依赖注入字段

        private readonly Canvas _canvas;
        private readonly Func<EditWindowViewModel> _viewModelProvider;
        private readonly Action _markDirtyCallback;
        private readonly Action<Cursor> _setCursorCallback;
        private readonly WidgetSelectionManager _selectionManager;
        private readonly Action<Widget, Point>? _onRightClickCallback;
        #endregion

        /// <summary>
        /// 初始化 Widget 拖拽行为处理器。
        /// </summary>
        /// <param name="canvas">画布容器，用于坐标计算和鼠标位置获取</param>
        /// <param name="viewModelProvider">ViewModel 提供者委托，延迟获取当前 ViewModel 引用以读取 Screen 边界</param>
        /// <param name="markDirtyCallback">标记工程已修改的回调，拖拽移动时调用以更新窗口标题</param>
        /// <param name="setCursorCallback">设置窗口光标的回调，拖拽时切换为 SizeAll，结束时恢复 Arrow</param>
        /// <param name="selectionManager">选中状态管理器，处理点击选中逻辑（SelectWidget / ClearAllSelection）</param>
        /// <exception cref="ArgumentNullException">任一参数为 null 时抛出</exception>
        public WidgetDragBehavior(
            Canvas canvas,
            Func<EditWindowViewModel> viewModelProvider,
            Action markDirtyCallback,
            Action<Cursor> setCursorCallback,
            WidgetSelectionManager selectionManager,
            Action<Widget, Point>? onRightClickCallback = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _viewModelProvider = viewModelProvider ?? throw new ArgumentNullException(nameof(viewModelProvider));
            _markDirtyCallback = markDirtyCallback ?? throw new ArgumentNullException(nameof(markDirtyCallback));
            _setCursorCallback = setCursorCallback ?? throw new ArgumentNullException(nameof(setCursorCallback));
            _selectionManager = selectionManager ?? throw new ArgumentNullException(nameof(selectionManager));
            _onRightClickCallback = onRightClickCallback;  // ← 在构造函数体里
        }

        #region 事件处理方法（internal — 供 WidgetItemsControlFactory 在 DataTemplate 中绑定）

        /// <summary>
        /// 按钮点击事件：触发点击选中逻辑。
        /// 委托给 <see cref="WidgetSelectionManager.SelectWidget"/> 统一处理。
        /// </summary>
        internal void OnButtonClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var widget = btn.DataContext as ButtonWidget;
            if (widget == null) return;

            _selectionManager.SelectWidget(widget);
        }

        /// <summary>
        /// 预览鼠标按下：记录拖拽起始位置（相对于画布坐标系）。
        /// 此事件在拖拽处理管道中最先触发，确保起始坐标准确。
        /// </summary>
        internal void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button)
            {
                _dragStartPoint = e.GetPosition(_canvas);
            }
        }

        /// <summary>
        /// 鼠标按下：开始拖拽流程。
        /// 如果按钮尚未选中，先通过 <see cref="WidgetSelectionManager.SelectWidget"/> 选中它。
        /// 然后进入拖拽状态，捕获鼠标并切换光标为十字箭头。
        /// </summary>
        internal void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var widget = btn.DataContext as ButtonWidget;
            if (widget == null) return;

            // 如果按钮尚未被选中，先选中它（点击目标即选中）
            if (!widget.IsSelected)
            {
                _selectionManager.SelectWidget(widget);
            }

            // 进入拖拽状态
            _isDragging = true;
            _draggingWidget = widget;
            _dragStartX = widget.X;
            _dragStartY = widget.Y;

            // 切换光标为十字箭头，提示用户进入拖拽模式
            _setCursorCallback(Cursors.SizeAll);

            // 捕获鼠标以确保即使鼠标移出按钮也能继续接收事件
            btn.CaptureMouse();

            System.Diagnostics.Debug.WriteLine($"🔄 开始拖拽: {widget.Text}");
        }

        /// <summary>
        /// 鼠标移动：在拖拽过程中更新 Widget 位置。
        /// 计算相对于拖拽起始点的偏移量，应用画布边界限制（确保 Widget 不超出 Screen 范围），
        /// 并通过 <see cref="_markDirtyCallback"/> 标记工程已修改。
        /// </summary>
        internal void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggingWidget == null) return;

            // 确保光标保持为十字箭头（可能被其他元素暂时修改）
            _setCursorCallback(Cursors.SizeAll);

            Point currentPos = e.GetPosition(_canvas);

            // 计算相对于拖拽起始位置的偏移量
            double offsetX = currentPos.X - _dragStartPoint.X;
            double offsetY = currentPos.Y - _dragStartPoint.Y;

            double newX = _dragStartX + offsetX;
            double newY = _dragStartY + offsetY;

            // 限制在画布（Screen）范围内，确保 Widget 完全可见
            var vm = _viewModelProvider();
            System.Diagnostics.Debug.WriteLine($"current Screen {vm.CurrentScreen.Name}: width = {vm.CurrentScreen.Width}, height = {vm.CurrentScreen.Height}");
            if (vm?.CurrentScreen != null)
            {
                newX = Math.Max(0, Math.Min(newX, vm.CurrentScreen.Width - _draggingWidget.Width));
                newY = Math.Max(0, Math.Min(newY, vm.CurrentScreen.Height - _draggingWidget.Height));
            }

            // 更新数据模型位置（UI 通过 X/Y 的 TwoWay 绑定自动同步）
            _draggingWidget.X = newX;
            _draggingWidget.Y = newY;

            // 标记工程已修改
            _markDirtyCallback();
        }

        /// <summary>
        /// 鼠标释放：结束拖拽流程。
        /// 释放鼠标捕获并恢复默认光标。
        /// 如果移动距离小于 <see cref="DRAG_THRESHOLD"/>（5 像素），视为短点击而非拖拽，
        /// 此时若按钮尚未选中则触发选中操作。
        /// </summary>
        internal void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            var button = sender as Button;
            button?.ReleaseMouseCapture();

            // 恢复默认光标
            _setCursorCallback(Cursors.Arrow);

            // 计算拖拽距离，判断是拖拽还是短点击
            Point currentPos = e.GetPosition(_canvas);
            double distance = Math.Sqrt(
                Math.Pow(currentPos.X - _dragStartPoint.X, 2) +
                Math.Pow(currentPos.Y - _dragStartPoint.Y, 2)
            );

            if (distance < DRAG_THRESHOLD && _draggingWidget != null)
            {
                // 移动距离小于阈值 → 视为点击而非拖拽，确保按钮被选中
                if (!_draggingWidget.IsSelected)
                {
                    _selectionManager.SelectWidget(_draggingWidget);
                }
            }
            else if (_draggingWidget != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"🔄 结束拖拽: {_draggingWidget.Text}, " +
                    $"从 ({_dragStartPoint.X:F0}, {_dragStartPoint.Y:F0}) " +
                    $"到 ({_draggingWidget.X:F0}, {_draggingWidget.Y:F0})");
            }

            // 重置拖拽状态
            _isDragging = false;
            _draggingWidget = null;
        }

        /// <summary>
        /// 右键预览：记录鼠标位置，获取 Widget 数据上下文。
        /// </summary>
        internal void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var widget = btn.DataContext as Widget;
            if (widget == null) return;

            // 先选中该控件
            _selectionManager.SelectWidget(widget);

            // 标记事件已处理，防止冒泡到 Canvas
            e.Handled = true;
        }

        /// <summary>
        /// 右键释放：触发回调，在鼠标位置显示上下文菜单。
        /// </summary>
        internal void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var widget = btn.DataContext as Widget;
            if (widget == null) return;

            // 获取相对于窗口的鼠标位置，传给回调显示 Popup
            Point screenPos = e.GetPosition(null); // 相对于窗口
            _onRightClickCallback?.Invoke(widget, screenPos);

            e.Handled = true;
        }

        #endregion
    }
}
