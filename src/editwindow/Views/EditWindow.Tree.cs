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

#region 原 树形视图 & 消息（72daf0e^ 第 4117-4135 行；2 个成员块）

        private void TreeViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem;
            var node = item?.DataContext as ProjectTreeViewModel;
            node?.DoubleClickCommand?.Execute(null);
        }



        /// <summary>画布顶部页面标签点击：切换当前编辑画面（同时退出变量管理器视图）。</summary>
        private void ScreenTab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Screen screen) return;
            _viewModel.ActivateScreen(screen);
            e.Handled = true;
        }
        #endregion

#region 原 树形视图右键编辑菜单（委托给 TreeViewContextMenuHandler）（72daf0e^ 第 4136-4346 行；12 个成员块）

        /// <summary>画布顶部页面标签关闭：从打开集合移除（画面不删除）。</summary>
        private void ScreenTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Screen screen)
            {
                _viewModel.CloseScreenTab(screen);
                e.Handled = true;
            }
        }

        /// <summary>点击「变量管理器」Tab：切到变量管理器视图（与画面 Tab 互斥切换）。</summary>
        private void VarManagerTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateVariableManager();
            e.Handled = true;
        }


        /// <summary>
        /// 右键点击树节点：选中节点并显示上下文菜单。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnTreeViewItemRightClick"/>。
        /// </summary>
        private void TreeViewItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 粘贴按钮：剪贴板为空时禁用（避免静默失败）
            if (ScreenPasteBtn != null)
                ScreenPasteBtn.IsEnabled = ScreenClipboard.GetItems() != null;
            _treeContextMenuHandler.OnTreeViewItemRightClick(sender, e);
        }

        /// <summary>
        /// 项目树空白处右键：弹粘贴画面菜单（粘贴到自定义画面下）。
        /// 节点右键已在 ItemContainerStyle 的 MouseRightButtonDown 处理（Handled=true），此事件仅在空白处触发。
        /// </summary>
        private void ProjectTree_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 已由节点处理器处理（Handled=true）则不重复弹菜单
            if (e.Handled) return;
            // 粘贴按钮：剪贴板为空时禁用
            if (ScreenPasteBtn != null)
                ScreenPasteBtn.IsEnabled = ScreenClipboard.GetItems() != null;
            _treeContextMenuHandler.OnTreeViewItemRightClick(sender, e);
        }

        /// <summary>
        /// 树节点右键菜单「删除画面」点击。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnDeleteScreenClick"/>。
        /// </summary>
        private void DeleteScreen_Click(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnDeleteScreenClick(sender, e);

        /// <summary>树节点右键「复制画面」：走 CommandLayer copy_screen（含控件深拷贝）。画面级操作不推 Undo 快照。</summary>
        private void CopyScreen_Click(object sender, RoutedEventArgs e)
        {
            var node = _treeContextMenuHandler.GetRightClickedNode();
            if (node == null) return;
            var result = _viewModel.CommandService.Execute("copy_screen", new() { ["name"] = node.Screen.Name });
            if (!result.Success)
                System.Windows.MessageBox.Show($"复制失败: {result.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
            TreeScreenMenu.IsOpen = false;
            _treeContextMenuHandler.ClearRightClickedNode();
        }

        /// <summary>树节点右键「剪切画面」：复制 + 删除（可粘贴还原）。画面级操作不推 Undo 快照。</summary>
        private void CutScreen_Click(object sender, RoutedEventArgs e)
        {
            var node = _treeContextMenuHandler.GetRightClickedNode();
            if (node == null) return;
            // 复制成功后再删除（copy_screen 触发 CommandExecuted → 树重建，旧 node 引用失效，删除必须走 CommandLayer 保持一致）
            var copyResult = _viewModel.CommandService.Execute("copy_screen", new() { ["name"] = node.Screen.Name });
            if (copyResult.Success)
            {
                var deleteResult = _viewModel.CommandService.Execute("delete_screen", new() { ["name"] = node.Screen.Name });
                if (!deleteResult.Success)
                    System.Windows.MessageBox.Show($"剪切失败（画面未删除）: {deleteResult.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
                System.Windows.MessageBox.Show($"剪切失败: {copyResult.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
            TreeScreenMenu.IsOpen = false;
            _treeContextMenuHandler.ClearRightClickedNode();
        }

        /// <summary>树节点右键「粘贴画面」：走 CommandLayer paste_screen（新画面名自动去重），完成后切换到新画面。</summary>
        private void PasteScreen_Click(object sender, RoutedEventArgs e)
        {
            var result = _viewModel.CommandService.Execute("paste_screen", new() { });
            if (result.Success)
            {
                // 从匿名结果取 screen_name（CommandResult.Data 为 { screen_name, widgets }）
                var newName = result.Data?.GetType().GetProperty("screen_name")?.GetValue(result.Data)?.ToString();
                if (!string.IsNullOrEmpty(newName))
                {
                    var screen = _viewModel.CurrentProject.Screens.FirstOrDefault(s => s.Name == newName);
                    if (screen != null) _viewModel.CurrentScreen = screen;
                }
            }
            TreeScreenMenu.IsOpen = false;
            _treeContextMenuHandler.ClearRightClickedNode();
        }

        /// <summary>
        /// 右键菜单「重命名」按钮点击：进入编辑模式。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnRenameScreenClick"/>。
        /// </summary>
        private void RenameScreen_Click(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnRenameScreenClick(sender, e);

        /// <summary>
        /// EditNameTextBox 加载后自动获取焦点并全选文本。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnEditNameTextBoxLoaded"/>。
        /// </summary>
        private void EditNameTextBox_Loaded(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnEditNameTextBoxLoaded(sender, e);

        /// <summary>
        /// EditNameTextBox 按键处理：回车确认，Escape 取消。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnEditNameTextBoxKeyDown"/>。
        /// </summary>
        private void EditNameTextBox_KeyDown(object sender, KeyEventArgs e)
            => _treeContextMenuHandler.OnEditNameTextBoxKeyDown(sender, e);


        /// <summary>
        /// 编译结果可读渲染（V-6 摘要消费端）：compile 的 Data 为匿名对象（output_path + changed_domains/changed_screens
        /// 中文标签数组）——直接 ToString 会把 List 打印成类型名噪声。此处提取 output_path，变化为空则只显示路径；
        /// 有变化则附「变化: 域[…] 画面[…]」（changed_domains 已是中文标签，直接透出）。
        /// </summary>
        private static string FormatCompileData(object? data)
        {
            if (data == null) return "";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data));
                var root = doc.RootElement;
                var output = root.TryGetProperty("output_path", out var o) ? o.GetString() ?? "" : "";
                var domains = root.TryGetProperty("changed_domains", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join("、", d.EnumerateArray().Select(x => x.GetString())) : "";
                var screens = root.TryGetProperty("changed_screens", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join("、", s.EnumerateArray().Select(x => x.GetString())) : "";
                var parts = new List<string>();
                if (domains.Length > 0) parts.Add($"域: {domains}");
                if (screens.Length > 0) parts.Add($"画面: {screens}");
                return output + (parts.Count > 0 ? $"（变化 {string.Join(" | ", parts)}）" : "");
            }
            catch (System.Text.Json.JsonException)
            {
                return data.ToString() ?? "";
            }
        }
        #endregion

#region 原 生成xml文件（72daf0e^ 第 4347-4365 行；1 个成员块）

        /// <summary>
        /// 生成项目（F5 / 菜单点击）：先校验错误，无错误则输出 XML。
        /// </summary>
        private void BuildProject_Click(object sender, RoutedEventArgs e)
        {
            AppendOutput($"[编译] 开始编译 {DateTime.Now:HH:mm:ss}");
            var result = _viewModel.CommandService.Execute("compile", new());

            if (!result.Success)
            {
                // X 循环 Check（2026-09-10 用户）：编译失败不弹窗——只下方输出窗口显示错误
                AppendOutput($"[编译] ✗ [{result.ErrorCode}] {result.ErrorMessage}");
                return;
            }
            var summary = FormatCompileData(result.Data);
            AppendOutput($"[编译] ✓ 编译成功，输出: {summary}");
            MessageBox.Show($"编译成功！\n输出: {summary}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion

        // AB-6 复审（2026-09-11）：画面新增处理器（树域归位） —— 自 EditWindow.xaml.cs 纯搬家（基线 72daf0e 第 994-1001 行，1 个成员；内容零改动）
        private void OnScreenAdded(object recipient, ScreenAddedMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                MarkProjectDirty();
                // 新增画面不自动加标签（打开才显示），ObservableCollection 通知冗余
            });
        }

    }
}
