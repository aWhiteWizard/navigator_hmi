using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer.Handlers;
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
        // TODO: 死字段——删除/重命名走命令层后不再调用，待构造签名清理
        private readonly Action _markProjectDirty;
        private readonly Action? _pushUndoCallback;
        private readonly NavigatorHMI.CommandLayer.ICommandService _commandService;

        /// <summary>当前右键点击的树节点，用于菜单按钮回调时传递操作目标</summary>
        private ScreenItemNode? _rightClickedTreeNode;

        /// <summary>
        /// 初始化 <see cref="TreeViewContextMenuHandler"/> 实例。
        /// </summary>
        /// <param name="treeContextMenu">XAML 中定义的 TreeContextMenu Popup 控件</param>
        /// <param name="commandService">CommandService 实例（删除/重命名统一走命令层，供 GUI/CLI/AI 同一入口）</param>
        /// <param name="markProjectDirty">标记工程已修改的回调（通常指向 EditWindow.MarkProjectDirty）</param>
        /// <param name="pushUndoCallback">保存 Undo 快照的回调（可选），在修改数据前调用</param>
        public TreeViewContextMenuHandler(Popup treeContextMenu, NavigatorHMI.CommandLayer.ICommandService commandService, Action markProjectDirty, Action? pushUndoCallback = null)
        {
            _treeContextMenu = treeContextMenu ?? throw new ArgumentNullException(nameof(treeContextMenu));
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            _markProjectDirty = markProjectDirty ?? throw new ArgumentNullException(nameof(markProjectDirty));
            _pushUndoCallback = pushUndoCallback;
        }

        /// <summary>当前右键点击的树节点（供 EditWindow 的复制/剪切/粘贴菜单按钮使用）。</summary>
        public ScreenItemNode? GetRightClickedNode() => _rightClickedTreeNode;

        /// <summary>清除右键节点引用（菜单操作完成后调用，防悬空引用）。</summary>
        public void ClearRightClickedNode() => _rightClickedTreeNode = null;

        /// <summary>
        /// 右键点击树节点：选中节点并显示上下文菜单。
        /// 仅对自定义画面（<see cref="ScreenType.Custom"/>）节点生效。
        /// 作为 <c>TreeViewItem.MouseRightButtonDown</c> 事件处理器绑定到 ItemContainerStyle。
        /// </summary>
        public void OnTreeViewItemRightClick(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var item = FindVisualParent<TreeViewItem>(source);

            // 空白处右键（item==null）：视为根节点场景，弹粘贴菜单（粘贴到自定义画面下）
            if (item == null)
            {
                // 滚动条等非内容区域右键不弹菜单（FindVisualParent<ScrollBar> 命中则忽略）
                if (FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(source) != null)
                {
                    e.Handled = true;
                    return;
                }
                _rightClickedTreeNode = null;
                ShowTreeMenu(e, showPasteOnly: true);
                e.Handled = true;
                return;
            }

            item.IsSelected = true;

            // 右键目标：自定义画面根节点（粘贴到自定义画面下）或具体画面节点
            if (item.DataContext is CustomScreensRootNode)
            {
                _rightClickedTreeNode = null;
                // 根节点菜单：仅粘贴可用（粘贴到自定义画面下）
                ShowTreeMenu(e, showPasteOnly: true);
                e.Handled = true;
                return;
            }

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
            ShowTreeMenu(e, showPasteOnly: false);
            e.Handled = true;
        }

        /// <summary>显示树右键菜单；showPasteOnly=true 时隐藏画面级按钮（根节点场景）。</summary>
        private void ShowTreeMenu(MouseButtonEventArgs e, bool showPasteOnly)
        {
            // 粘贴按钮：剪贴板为空时禁用
            if (_treeContextMenu.Child is System.Windows.Controls.Border border
                && border.Child is System.Windows.Controls.StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is System.Windows.Controls.Button btn)
                    {
                        bool isPaste = btn.Name == "ScreenPasteBtn";
                        btn.Visibility = (!showPasteOnly || isPaste) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                        if (isPaste) btn.IsEnabled = ScreenClipboard.GetItems() != null;
                    }
                    else if (child is System.Windows.Controls.Separator sep)
                    {
                        sep.Visibility = showPasteOnly ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
                    }
                }
            }

            var screenPos = e.GetPosition(null);
            _treeContextMenu.HorizontalOffset = screenPos.X + 5;
            _treeContextMenu.VerticalOffset = screenPos.Y + 5;
            _treeContextMenu.IsOpen = true;
        }

        /// <summary>
        /// 树节点右键菜单「删除画面」按钮点击。
        /// 从工程中移除画面并从树中删除对应节点，同时关闭菜单。
        /// </summary>
        public void OnDeleteScreenClick(object sender, RoutedEventArgs e)
        {
            var node = _rightClickedTreeNode;
            if (node == null) return;

            // 统一走 Command Layer（GUI/CLI/AI 同一入口）：命令层校验 Template/WorldMap 保护 + 触发树重建/标签刷新
            var result = _commandService.Execute("delete_screen", new() { ["name"] = node.Screen.Name });
            if (!result.Success)
            {
                // 失败不标脏（成功标脏由 OnCommandExecuted → ProjectDirtyRequested 统一处理），仅提示
                System.Windows.MessageBox.Show($"删除画面失败: {result.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    // GUI 层短路：未修改直接回车 → 不调用命令层（命令层 Success 仍触发事件/标脏）
                    // 比较原始值（不 Trim）：仅空格差异交给命令层处理，避免静默吞掉"去空格"改名
                    if (string.Equals(tb.Text, node.Screen.Name, StringComparison.Ordinal))
                    {
                        node.IsEditing = false;
                        e.Handled = true;
                        return;
                    }
                    // 统一走 Command Layer（重名/受保护由命令层校验），成功后命令层触发树重建
                    var result = _commandService.Execute("rename_screen", new() { ["name"] = node.Screen.Name, ["new_name"] = tb.Text.Trim() });
                    if (!result.Success)
                    {
                        // 失败不标脏（成功由命令层事件统一标脏），仅提示；树重建未发生，需手动退出编辑模式
                        node.IsEditing = false;
                        System.Windows.MessageBox.Show($"重命名画面失败: {result.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
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
