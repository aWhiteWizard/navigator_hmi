using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// 树形视图右键菜单处理器。
    /// 负责 TreeView 节点的右键菜单显示、删除画面和重命名画面的交互逻辑。
    /// 将原本集中在 EditWindow code-behind 中的菜单处理代码独立出来，降低窗口的职责复杂度。
    /// </summary>
    public class TreeViewContextMenuHandler
    {
        private readonly Popup _treeContextMenu;
        private readonly Action _markProjectDirty;

        /// <summary>当前右键点击的树节点，用于菜单按钮回调时传递操作目标</summary>
        private ScreenItemNode? _rightClickedTreeNode;

        /// <summary>
        /// 初始化 <see cref="TreeViewContextMenuHandler"/> 实例。
        /// </summary>
        /// <param name="treeContextMenu">XAML 中定义的 TreeContextMenu Popup 控件</param>
        /// <param name="markProjectDirty">标记工程已修改的回调（通常指向 EditWindow.MarkProjectDirty）</param>
        public TreeViewContextMenuHandler(Popup treeContextMenu, Action markProjectDirty)
        {
            _treeContextMenu = treeContextMenu ?? throw new ArgumentNullException(nameof(treeContextMenu));
            _markProjectDirty = markProjectDirty ?? throw new ArgumentNullException(nameof(markProjectDirty));
        }

        /// <summary>
        /// 右键点击树节点：选中节点并显示上下文菜单。
        /// 仅对自定义画面（<see cref="ScreenType.Custom"/>）节点生效。
        /// 作为 <c>TreeViewItem.MouseRightButtonDown</c> 事件处理器绑定到 ItemContainerStyle。
        /// </summary>
        public void OnTreeViewItemRightClick(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var item = FindVisualParent<TreeViewItem>(source);
            if (item == null) return;

            item.IsSelected = true;

            var node = item.DataContext as ScreenItemNode;
            if (node == null)
            {
                e.Handled = true;
                return;
            }

            // 🔒 只有自定义画面（Custom）才允许弹出右键菜单
            if (node.Screen.Type != ScreenType.Custom)
            {
                e.Handled = true;
                return;
            }

            // 把目标存到字段，防止 ContextMenu 弹出后 SelectedItem 丢失
            _rightClickedTreeNode = node;

            // 用 Popup 方式，位置在鼠标右下方
            var screenPos = e.GetPosition(null);
            _treeContextMenu.HorizontalOffset = screenPos.X + 5;
            _treeContextMenu.VerticalOffset = screenPos.Y + 5;
            _treeContextMenu.IsOpen = true;

            e.Handled = true;
        }

        /// <summary>
        /// 树节点右键菜单「删除画面」按钮点击。
        /// 从工程中移除画面并从树中删除对应节点，同时关闭菜单。
        /// </summary>
        public void OnDeleteScreenClick(object sender, RoutedEventArgs e)
        {
            var node = _rightClickedTreeNode;
            if (node == null) return;

            if (node.DeleteCommand.CanExecute(null))
            {
                node.DeleteCommand.Execute(null);
                _markProjectDirty();
            }

            _treeContextMenu.IsOpen = false;
            _rightClickedTreeNode = null;
        }

        /// <summary>
        /// 右键菜单「重命名」按钮点击：进入节点编辑模式。
        /// 关闭菜单后执行 <see cref="ScreenItemNode.StartRenameCommand"/> 让节点切换到可编辑状态。
        /// </summary>
        public void OnRenameScreenClick(object sender, RoutedEventArgs e)
        {
            var node = _rightClickedTreeNode;
            if (node == null) return;

            // 关闭菜单
            _treeContextMenu.IsOpen = false;
            _rightClickedTreeNode = null;

            // 执行 StartRenameCommand — 进入编辑模式
            node.StartRenameCommand.Execute(null);
        }

        /// <summary>
        /// 编辑名称 TextBox 加载后自动获取焦点并全选文本。
        /// 作为 <c>TextBox.Loaded</c> 事件处理器绑定到编辑态 TextBox。
        /// </summary>
        public void OnEditNameTextBoxLoaded(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            tb.Focus();
            tb.SelectAll();
        }

        /// <summary>
        /// 编辑名称 TextBox 按键处理：回车确认，Escape 取消。
        /// 作为 <c>TextBox.KeyDown</c> 事件处理器绑定到编辑态 TextBox。
        /// </summary>
        public void OnEditNameTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var tb = sender as TextBox;
                var node = tb?.DataContext as ScreenItemNode;
                if (node != null)
                {
                    node.ConfirmRenameCommand.Execute(null);
                    _markProjectDirty();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                var tb = sender as TextBox;
                var node = tb?.DataContext as ScreenItemNode;
                if (node != null)
                {
                    node.IsEditing = false;  // 取消编辑，不保存
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// 在视觉树中向上查找指定类型的父元素。
        /// </summary>
        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
