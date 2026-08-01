using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Helpers;

namespace NavigatorHMI.Views.Behaviors
{
    /// <summary>
    /// Widget 拖拽行为处理器。支持单选和多选拖拽——框选多个控件后拖拽任意一个即可整体移动。
    /// 设计态所有控件（含 TextBox/ProgressBar/CheckBox）统一按下即拖拽：
    /// 控件值通过属性面板写入（TextBox 模板只读），不保留画布上的原生编辑交互。
    /// </summary>
    public class WidgetDragBehavior
    {
        private bool _isDragging;
        private Point _dragStartPoint;
        private readonly List<(Widget widget, double startX, double startY)> _draggingWidgets = new();
        private Widget? _pendingDragWidget;      // 按下待拖拽的 Widget（位移超阈值才启动）
        private FrameworkElement? _pendingDragFE; // 对应按下元素（用于 CaptureMouse）
        private bool _suppressDragUntilMouseUp;   // 粘性抑制：两点式第二点击后，本次 down→up 周期内禁止拖拽
        private const double DRAG_THRESHOLD = 5;
        private readonly Canvas _canvas;
        private readonly Func<EditWindowViewModel> _viewModelProvider;
        private readonly Action _markDirtyCallback;
        private readonly Action<Cursor> _setCursorCallback;
        private readonly WidgetSelectionManager _selectionManager;
        private readonly Action<Widget, Point>? _onRightClickCallback;
        private readonly Action? _pushUndoCallback;
        private readonly Func<bool>? _isAddMode;   // 添加/绘制模式标志（绘制模式下不启动拖拽）

        public WidgetDragBehavior(Canvas canvas, Func<EditWindowViewModel> viewModelProvider,
            Action markDirtyCallback, Action<Cursor> setCursorCallback,
            WidgetSelectionManager selectionManager, Action<Widget, Point>? onRightClickCallback = null,
            Action? pushUndoCallback = null, Func<bool>? isAddMode = null)
        {
            _canvas = canvas;
            _viewModelProvider = viewModelProvider;
            _markDirtyCallback = markDirtyCallback;
            _setCursorCallback = setCursorCallback;
            _selectionManager = selectionManager;
            _onRightClickCallback = onRightClickCallback;
            _pushUndoCallback = pushUndoCallback;
            _isAddMode = isAddMode;
        }

        /// <summary>沿视觉树向上查找 Thumb（ResizeAdorner 的缩放手柄）。命中则返回，未命中返回 null。</summary>
        private static Thumb? FindVisualParentThumb(DependencyObject? child)
        {
            while (child != null)
            {
                if (child is Thumb thumb) return thumb;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        /// <summary>
        /// 清理拖拽泄漏候选状态（pending + 粘性标志），但**保留** _isDragging 收尾给冒泡 up。
        /// 容器层 Preview up 调用——不能清 _isDragging，否则 widget 冒泡 up 的光标恢复/捕获释放被跳过。
        /// </summary>
        public void ClearLeakState()
        {
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _suppressDragUntilMouseUp = false;
        }

        /// <summary>
        /// 取消待拖拽状态。
        /// </summary>
        /// <remarks>
        /// ⚠️ 已废弃：此方法清空 _isDragging，会破坏 widget 冒泡 up 的收尾（光标恢复/捕获释放被跳过）。
        /// 容器层兜底请使用 <see cref="ClearLeakState"/>。保留仅因历史调用兼容。
        /// </remarks>
        [Obsolete("会破坏拖拽收尾不变量，禁止使用；请用 ClearLeakState")]
        public void CancelPendingDrag()
        {
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _isDragging = false;
            _draggingWidgets.Clear();
        }

        /// <summary>
        /// 抑制本次鼠标按下周期的拖拽（两点式绘制第二点击时调用）。
        /// 粘性标志在 MouseLeftButtonUp / 右键按下时清除，覆盖 down→move→up 全程。
        /// </summary>
        public void SuppressDragUntilMouseUp()
        {
            _suppressDragUntilMouseUp = true;
        }

        /// <summary>清除粘性抑制标志（已由 ClearLeakState 统一处理，保留幂等备用）。</summary>
        public void ClearSuppressDrag()
        {
            _suppressDragUntilMouseUp = false;
        }

        internal void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement) _dragStartPoint = e.GetPosition(_canvas);
        }

        internal void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Widget widget) return;

            // ═══ 修复 3：ResizeAdorner 的 Thumb 按下时不拦截（否则抢走 capture 导致拖不动） ═══
            if (FindVisualParentThumb(e.OriginalSource as DependencyObject) != null)
            {
                _isDragging = false;
                _draggingWidgets.Clear();
                ClearLeakState();   // 对称清理 pending + 粘性标志，防止残留
                return;
            }

            // 添加/绘制模式下：不设置拖拽 pending（两阶段绘制第二点击可能落在 widget 上）
            // 粘性标志覆盖整个 down→up 周期：隧道阶段 ExitAddMode 清空 creator 后冒泡阶段仍有效
            if (_isAddMode?.Invoke() == true || _suppressDragUntilMouseUp)
            {
                _pendingDragWidget = null;
                _pendingDragFE = null;
                return;
            }

            // 统一双击检测：单击静默选中，双击触发完整选中（弹出属性栏）
            _selectionManager.HandleWidgetClick(widget, Mouse.GetPosition(_canvas));

            // 记录按下点，位移超过阈值后才启动拖拽（避免单击/双击误触发）
            // 注：设计态所有控件（含 TextBox/ProgressBar/CheckBox）统一可拖拽——
            // 值通过属性面板写入（TextBox 模板只读），无需保留原生编辑交互
            _dragStartPoint = e.GetPosition(_canvas);
            _pendingDragWidget = widget;
            _pendingDragFE = fe;
            _isDragging = false;
            _draggingWidgets.Clear();
        }

        internal void OnMouseMove(object sender, MouseEventArgs e)
        {
            // 添加/绘制模式下不启动拖拽
            if (_isAddMode?.Invoke() == true || _suppressDragUntilMouseUp) return;

            // 位移超过阈值 → 启动拖拽（此时才 PushUndo + 记录初始位置）
            if (!_isDragging && _pendingDragWidget != null && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(_canvas);
                if (Math.Abs(currentPos.X - _dragStartPoint.X) >= DRAG_THRESHOLD
                 || Math.Abs(currentPos.Y - _dragStartPoint.Y) >= DRAG_THRESHOLD)
                {
                    _pushUndoCallback?.Invoke();
                    var vm = _viewModelProvider();
                    _draggingWidgets.Clear();
                    if (vm?.CurrentScreen != null)
                        foreach (var w in vm.CurrentScreen.Widgets.Where(w => w.IsSelected))
                            _draggingWidgets.Add((w, w.X, w.Y));
                    _isDragging = true;
                    _setCursorCallback(Cursors.SizeAll);
                    _pendingDragFE?.CaptureMouse();
                }
            }

            if (!_isDragging || _draggingWidgets.Count == 0 || Mouse.LeftButton != MouseButtonState.Pressed) return;
            _setCursorCallback(Cursors.SizeAll);

            var pos = e.GetPosition(_canvas);
            double offsetX = pos.X - _dragStartPoint.X;
            double offsetY = pos.Y - _dragStartPoint.Y;

            var vm2 = _viewModelProvider();
            double maxW = vm2?.CurrentScreen?.Width ?? double.MaxValue;
            double maxH = vm2?.CurrentScreen?.Height ?? double.MaxValue;

            foreach (var (w, sx, sy) in _draggingWidgets)
            {
                w.X = Math.Max(0, Math.Min(sx + offsetX, maxW - w.Width));
                w.Y = Math.Max(0, Math.Min(sy + offsetY, maxH - w.Height));
            }
            _markDirtyCallback();
        }

        internal void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 清理待拖拽状态（未超阈值则视为单击）+ 清除粘性抑制
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _suppressDragUntilMouseUp = false;
            if (!_isDragging)
            {
                _setCursorCallback(Cursors.Arrow);   // 恢复光标（防任何残留）
                return;
            }
            // 拖拽结束：重置双击检测计时（按下时间戳不参与下一次点击的双击判定）
            _selectionManager.ResetDoubleClickDetection();
            (sender as FrameworkElement)?.ReleaseMouseCapture();
            _setCursorCallback(Cursors.Arrow);
            _isDragging = false;
            _draggingWidgets.Clear();
        }

        internal void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Widget widget) return;
            _isDragging = false;
            _draggingWidgets.Clear();
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _suppressDragUntilMouseUp = false;
            // 右键中断拖拽：本次按下时间戳作废，防后续快速左键点击误判双击
            _selectionManager.ResetDoubleClickDetection();
            fe.ReleaseMouseCapture();
            _setCursorCallback(Cursors.Arrow);
            _selectionManager.SelectWidgetSilent(widget);
            e.Handled = true;
        }

        internal void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Widget widget) return;
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _onRightClickCallback?.Invoke(widget, e.GetPosition(null));
            e.Handled = true;
        }
    }
}
