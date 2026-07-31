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
    /// 交互控件（CheckBox/TextBox/ProgressBar）默认跳过拖拽以保留原生交互；
    /// 按住 Ctrl 键可强制拖拽交互控件。
    /// </summary>
    public class WidgetDragBehavior
    {
        private bool _isDragging;
        private Point _dragStartPoint;
        private readonly List<(Widget widget, double startX, double startY)> _draggingWidgets = new();
        private const double DRAG_THRESHOLD = 5;
        private readonly Canvas _canvas;
        private readonly Func<EditWindowViewModel> _viewModelProvider;
        private readonly Action _markDirtyCallback;
        private readonly Action<Cursor> _setCursorCallback;
        private readonly WidgetSelectionManager _selectionManager;
        private readonly Action<Widget, Point>? _onRightClickCallback;
        private readonly Action? _pushUndoCallback;

        public WidgetDragBehavior(Canvas canvas, Func<EditWindowViewModel> viewModelProvider,
            Action markDirtyCallback, Action<Cursor> setCursorCallback,
            WidgetSelectionManager selectionManager, Action<Widget, Point>? onRightClickCallback = null,
            Action? pushUndoCallback = null)
        {
            _canvas = canvas;
            _viewModelProvider = viewModelProvider;
            _markDirtyCallback = markDirtyCallback;
            _setCursorCallback = setCursorCallback;
            _selectionManager = selectionManager;
            _onRightClickCallback = onRightClickCallback;
            _pushUndoCallback = pushUndoCallback;
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

        internal void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Widget widget) return;
            _selectionManager.HandleWidgetClick(widget, Mouse.GetPosition(_canvas));
        }

        internal void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement) _dragStartPoint = e.GetPosition(_canvas);
        }

        internal void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Widget widget) return;

            // 交互控件默认不拖拽，必须按住 Ctrl 才能拖拽（保留原生交互行为）
            if (IsInteractiveControl(fe) && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            {
                // 仍可选中，但不开始拖拽
                if (!widget.IsSelected) _selectionManager.SelectWidgetSilent(widget);
                e.Handled = false;  // 让原生交互继续
                return;
            }

            if (!widget.IsSelected) _selectionManager.SelectWidgetSilent(widget);
            _pushUndoCallback?.Invoke();

            var vm = _viewModelProvider();
            _draggingWidgets.Clear();
            if (vm?.CurrentScreen != null)
                foreach (var w in vm.CurrentScreen.Widgets.Where(w => w.IsSelected))
                    _draggingWidgets.Add((w, w.X, w.Y));

            _isDragging = true;
            _setCursorCallback(Cursors.SizeAll);
            fe.CaptureMouse();
        }

        internal void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggingWidgets.Count == 0 || Mouse.LeftButton != MouseButtonState.Pressed) return;
            _setCursorCallback(Cursors.SizeAll);

            var currentPos = e.GetPosition(_canvas);
            double offsetX = currentPos.X - _dragStartPoint.X;
            double offsetY = currentPos.Y - _dragStartPoint.Y;

            var vm = _viewModelProvider();
            double maxW = vm?.CurrentScreen?.Width ?? double.MaxValue;
            double maxH = vm?.CurrentScreen?.Height ?? double.MaxValue;

            foreach (var (w, sx, sy) in _draggingWidgets)
            {
                w.X = Math.Max(0, Math.Min(sx + offsetX, maxW - w.Width));
                w.Y = Math.Max(0, Math.Min(sy + offsetY, maxH - w.Height));
            }
            _markDirtyCallback();
        }

        internal void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
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
            fe.ReleaseMouseCapture();
            _setCursorCallback(Cursors.Arrow);
            _selectionManager.SelectWidgetSilent(widget);
            e.Handled = true;
        }

        internal void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Widget widget) return;
            _onRightClickCallback?.Invoke(widget, e.GetPosition(null));
            e.Handled = true;
        }
    }
}
