using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Helpers;

namespace NavigatorHMI.Views.Behaviors
{
    /// <summary>
    /// Widget 拖拽行为处理器。支持单选和多选拖拽——框选多个控件后拖拽任意一个即可整体移动。
    /// 交互控件（CheckBox/TextBox/ProgressBar）默认跳过拖拽以保留原生交互；
    /// 按住 Ctrl 键可强制拖拽交互控件。
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

        // ═══ 长按拖拽（交互控件 TextBox/ProgressBar/CheckBox：单击保留原生交互，长按 400ms 启动拖拽待命） ═══
        private DispatcherTimer? _longPressTimer;
        private FrameworkElement? _longPressFE;
        private Widget? _longPressWidget;
        private bool _longPressArmed;                       // 长按已生效，进入拖拽待命（后续位移即拖动）
        private const double LONG_PRESS_MS = 400;
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

        /// <summary>
        /// 判断 FrameworkElement 是否为交互控件（具有原生交互行为，默认不拖拽）。
        /// TextBoxBase（TextBox/PasswordBox）、ToggleButton（CheckBox/RadioButton）、
        /// RangeBase（ProgressBar/Slider/ScrollBar）属于交互控件。
        /// </summary>
        private static bool IsInteractiveControl(FrameworkElement fe)
        {
            return fe is TextBoxBase || fe is ToggleButton || fe is RangeBase;
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
        /// 清理拖拽泄漏候选状态（pending + 粘性标志 + 长按定时器），但**保留** _isDragging 收尾给冒泡 up。
        /// 容器层 Preview up 调用——不能清 _isDragging，否则 widget 冒泡 up 的光标恢复/捕获释放被跳过。
        /// </summary>
        public void ClearLeakState()
        {
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _suppressDragUntilMouseUp = false;
            CancelLongPressTimer();
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

        // ═══════ 长按拖拽实现 ═══════

        /// <summary>
        /// 启动长按定时器（交互控件按下时调用）。
        /// 按住不动超过 <see cref="LONG_PRESS_MS"/> 后进入拖拽待命；
        /// 期间快速移动（≥阈值）或松开则取消，保持原生交互（文本编辑/选择）。
        /// </summary>
        private void StartLongPressTimer(FrameworkElement fe, Widget widget)
        {
            CancelLongPressTimer();
            _longPressFE = fe;
            _longPressWidget = widget;
            _longPressArmed = false;
            var timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(LONG_PRESS_MS)
            };
            timer.Tick += LongPressTimer_Tick;
            timer.Start();
            _longPressTimer = timer;
        }

        /// <summary>取消长按定时器并清理待命状态（含 pending 残留，防止后续 MouseMove 误启动拖拽）。</summary>
        private void CancelLongPressTimer()
        {
            if (_longPressTimer != null)
            {
                _longPressTimer.Stop();
                _longPressTimer.Tick -= LongPressTimer_Tick;
                _longPressTimer = null;
            }
            // 恢复长按期间设置的元素级光标（Cursor + ForceCursor 成对恢复，防覆盖 TextBox IBeam 悬停提示永久丢失）
            if (_longPressFE != null)
            {
                if (_longPressFE.Cursor == Cursors.SizeAll) _longPressFE.Cursor = null;
                _longPressFE.ForceCursor = false;
            }
            _longPressFE = null;
            _longPressWidget = null;
            _longPressArmed = false;
            // 防残留：armed 后未拖拽即取消时，清理 pending，避免后续 MouseMove 满足启动条件误拖拽
            _pendingDragWidget = null;
            _pendingDragFE = null;
        }

        /// <summary>
        /// 长按超时触发：进入拖拽待命。复用现有 <see cref="_pendingDragWidget"/> 机制——
        /// 后续 MouseMove 位移超阈值即自动启动拖拽（见 OnMouseMove 启动逻辑）。
        /// </summary>
        private void LongPressTimer_Tick(object? sender, EventArgs e)
        {
            if (_longPressTimer != null)
            {
                _longPressTimer.Stop();
                _longPressTimer.Tick -= LongPressTimer_Tick;
                _longPressTimer = null;
            }

            // 长按期间鼠标已松开、元素失效或已移除 → 不进入待命
            if (_longPressFE == null || _longPressWidget == null
             || !_longPressFE.IsLoaded
             || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                _longPressFE = null;
                _longPressWidget = null;
                return;
            }

            _longPressArmed = true;
            _pendingDragWidget = _longPressWidget;
            _pendingDragFE = _longPressFE;
            _isDragging = false;
            _draggingWidgets.Clear();
            // armed 即宣布本次按下属拖拽语义：其时间戳立即作废，不参与后续双击判定
            _selectionManager.ResetDoubleClickDetection();
            _setCursorCallback(Cursors.SizeAll);
            // 元素级光标提示（覆盖 TextBox 的 IBeam；ForceCursor 保证内部元素不覆盖）
            _longPressFE.ForceCursor = true;
            _longPressFE.Cursor = Cursors.SizeAll;
            System.Diagnostics.Debug.WriteLine($"🖱 长按生效，进入拖拽待命: {_longPressWidget.ObjectName}");
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

            // 交互控件（TextBox/ProgressBar/CheckBox 等）：单击保留原生交互（编辑/选择），
            // 长按 400ms 启动拖拽待命（不按 Ctrl 也可拖动）；按住 Ctrl 则立即拖拽
            if (IsInteractiveControl(fe) && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            {
                StartLongPressTimer(fe, widget);
                e.Handled = false;  // 让原生交互继续（TextBox 获得焦点、IBeam 光标等）
                return;
            }

            // 记录按下点，位移超过阈值后才启动拖拽（避免单击/双击误触发）
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

            // 长按等待期（定时器未触发）：快速移动 ≥ 阈值 → 取消长按，保持原生交互（文本选择等）
            // 注：armed 后 timer 已置 null，此分支自动不可达（armed 后移动即拖拽，正确）
            if (_longPressTimer != null && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                var lp = e.GetPosition(_canvas);
                if (Math.Abs(lp.X - _dragStartPoint.X) >= DRAG_THRESHOLD
                 || Math.Abs(lp.Y - _dragStartPoint.Y) >= DRAG_THRESHOLD)
                {
                    CancelLongPressTimer();
                }
            }

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
            // 清理待拖拽状态（未超阈值则视为单击）+ 清除粘性抑制 + 取消长按定时器
            _pendingDragWidget = null;
            _pendingDragFE = null;
            _suppressDragUntilMouseUp = false;
            CancelLongPressTimer();
            if (!_isDragging)
            {
                // 长按 armed 但原地松开（未启动拖拽）→ 恢复光标（窗口级）
                _setCursorCallback(Cursors.Arrow);
                return;
            }
            // 拖拽结束：重置双击检测计时（长按+拖拽的时间戳会干扰下一次点击的双击判定）
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
            CancelLongPressTimer();
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
