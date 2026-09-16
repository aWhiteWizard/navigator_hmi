using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Behaviors;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using ProtoBuf;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;   // P10：XmlLayoutSerializer（Dirkster.AvalonDock）

namespace NavigatorHMI.Views
{
    /// <summary>
    /// EditWindow 的交互逻辑。作为 WPF 窗口的 code-behind，
    /// 负责响应 UI 事件并将业务逻辑委托给拆分后的专职类。
    /// </summary>
    public partial class EditWindow : Window
    {

#region 原 属性窗口（72daf0e^ 第 3945-4029 行；5 个成员块）

        /// <summary>选中画面时更新属性面板。</summary>
        private void ShowScreenProperty(Screen screen)
        {
            _propertyViewModel.SelectedScreen = screen;
            ShowAnchorable("property");
            WidgetContextMenu.IsOpen = false;
        }

        /// <summary>清空属性面板选中。</summary>
        private void HidePropertyWindow()
        {
            _propertyViewModel.SelectedScreen = null;
        }

        private DateTime _lastClickTime;
        private Point _lastClickPosition;

        /// <summary>
        /// 画布鼠标按下事件：检测单击和双击。
        /// </summary>
        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                // 点击了 Widget 则交给 Widget 自己的右键菜单
                if (FindWidgetElement(e.OriginalSource as DependencyObject) != null) return;

                // 菜单互斥：打开画布菜单前先关树菜单（两菜单独立 Popup，避免重叠）
                if (TreeScreenMenu.IsOpen) TreeScreenMenu.IsOpen = false;

                var mousePos = Mouse.GetPosition(this);
                TreeContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
                TreeContextMenu.HorizontalOffset = mousePos.X;
                TreeContextMenu.VerticalOffset = mousePos.Y;
                _contextMenuPos = e.GetPosition(DrawingCanvas);   // 逻辑坐标（LayoutTransform 逆变换），禁除缩放（双重除 bug）
                UpdateCanvasMenuButtons();
                TreeContextMenu.IsOpen = true;
                e.Handled = true;
                return;
            }

            if (e.ChangedButton != MouseButton.Left) return;

            var now = DateTime.Now;
            var pos = e.GetPosition(DrawingCanvas);

            // 检测双击：两次点击间隔 < 500ms 且距离 < 10px（与 WidgetSelectionManager 共用常量）
            bool isDoubleClick = (now - _lastClickTime).TotalMilliseconds < WidgetSelectionManager.DoubleClickIntervalMs
                              && Math.Abs(pos.X - _lastClickPosition.X) < WidgetSelectionManager.DoubleClickSlopPx
                              && Math.Abs(pos.Y - _lastClickPosition.Y) < WidgetSelectionManager.DoubleClickSlopPx;

            _lastClickTime = now;
            _lastClickPosition = pos;

            if (isDoubleClick)
            {
                // 添加模式（单点/两点/多边形绘制）：双击不放控件——每次左键都是"放点/加点"，不弹画面属性（批 3a 遗留）
                if (_currentWidgetCreator != null) return;
                var source = e.OriginalSource as DependencyObject;
                if (FindWidgetElement(source) != null) return;

                // 双击画布空白处 → 显示画面属性
                if (_viewModel?.CurrentScreen != null)
                    ShowScreenProperty(_viewModel.CurrentScreen);
            }

        }




        /// <summary>查找视觉树中第一个 DataContext 为 Widget 的 FrameworkElement。</summary>
        private FrameworkElement? FindWidgetElement(DependencyObject? child)
        {
            while (child != null)
            {
                if (child is FrameworkElement fe && fe.DataContext is Widget)
                    return fe;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
        #endregion

        // AB-6 复审 🟡-1（2026-09-11）：属性面板输入/字体默认/帮助 自 EditWindow.Canvas.cs 归位（属性域收拢）
        internal void PropField_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void FontDefaults_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FontDefaultsDialog { Owner = this };
            dialog.ShowDialog();
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HelpDialog { Owner = this };
            dialog.ShowDialog();
        }

    }
}
