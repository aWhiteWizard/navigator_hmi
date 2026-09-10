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

        #region 原 变量管理器（第 444-884 行）

        /// <summary>变量删除：确认 → CommandService（delete_tag 自带引用保护）。</summary>
        /// <summary>变量表格快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void TagGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { TagCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { TagCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.Key == Key.Delete) { TagDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { TagGrid.SelectAll(); e.Handled = true; }
        }

        /// <summary>报警表格快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void AlarmGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { AlarmCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { AlarmCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.Key == Key.Delete) { AlarmDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { AlarmGrid.SelectAll(); e.Handled = true; }
        }

        // ── 列表名行内编辑（U 循环 Check 2026-09-06 用户需求：列表管理左侧列表名「双击进入编辑模式」——
        //    回车/点其它处确认（提交 = VM RenameList → update_list 同类型查重+级联）、Esc 取消不提交；
        //    三列表页（文本/图片/视频）ListBox 共用模板 ListNameInlineEditTemplate + 本组 handler）──
        private TextBox? _listNameEditor;      // 当前行内编辑框（任一列表页共用）
        private TextBlock? _listNameDisplay;   // 对应显示名 TextBlock（编辑态隐藏）
        private ListDef? _listNameEditTarget;  // 正在编辑的列表
        private bool _listNameSuppress;        // 提交/取消切换态防 LostFocus 递归（Exit 置 Collapsed 亦触发 LostFocus）

        private void ListNameBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject src) return;
            var item = FindAncestor<ListBoxItem>(src);
            if (item?.DataContext is not ListDef list) return;                                        // 空白处双击忽略
            if (ReferenceEquals(list, _listNameEditTarget) && _listNameEditor != null) return;        // 已在本项编辑（编辑框内双击=选词）忽略
            if (_listNameEditor != null) CommitListNameEdit();                                        // 已在编辑其它项 → 点其它处即确认
            var editor = FindVisualChild<TextBox>(item);        // 模板内编辑框（ListBoxItem 范围唯一）
            var display = FindVisualChild<TextBlock>(item);     // 模板内显示名（ListBoxItem 范围唯一）
            if (editor == null || display == null) return;
            _listNameEditor = editor;
            _listNameDisplay = display;
            _listNameEditTarget = list;
            display.Visibility = Visibility.Collapsed;
            editor.Text = list.Name;
            editor.Visibility = Visibility.Visible;
            editor.Focus();
            editor.SelectAll();
            e.Handled = true;   // 防双击继续冒泡（向后触发拖选等）
        }

        /// <summary>行内编辑键：回车确认、Esc 取消。</summary>
        private void ListNameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitListNameEdit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelListNameEdit(); e.Handled = true; }
        }

        /// <summary>点其它处/切 Tab 等失焦 → 确认提交（用户语义「点其他地方…即确认」）。</summary>
        private void ListNameEditor_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_listNameSuppress) CommitListNameEdit();
        }

        private void CommitListNameEdit()
        {
            if (_listNameEditor == null || _listNameEditTarget == null) return;
            var editor = _listNameEditor;
            var list = _listNameEditTarget;
            _listNameSuppress = true;   // 覆盖 RenameList 全程——失败弹 MessageBox 模态抢焦点防递归
            try { _listManagerVM.RenameList(list, editor.Text); }   // Trim/空名/同名早退；update_list；失败弹窗+模型回滚
            finally
            {
                ExitListNameEdit();
                _listNameSuppress = false;
            }
        }

        private void CancelListNameEdit()
        {
            if (_listNameEditor == null) return;
            _listNameSuppress = true;
            try { ExitListNameEdit(); }   // 不提交——Name 未动（TextBlock 绑定仍原值）
            finally { _listNameSuppress = false; }
        }

        private void ExitListNameEdit()
        {
            if (_listNameEditor != null) { _listNameEditor.Visibility = Visibility.Collapsed; _listNameEditor = null; }
            if (_listNameDisplay != null) { _listNameDisplay.Visibility = Visibility.Visible; _listNameDisplay = null; }
            _listNameEditTarget = null;
        }

        /// <summary>文本列表快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void TextListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (_listNameEditor != null) return;   // 行内编辑中：Delete/Ctrl+A 交给编辑框（防误删列表/误全选）
            if (e.Key == Key.Delete) { TextListDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { TextListBox.SelectAll(); e.Handled = true; }
        }

        /// <summary>图片列表快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void ImageListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (_listNameEditor != null) return;   // 行内编辑中：Delete/Ctrl+A 交给编辑框（防误删列表/误全选）
            if (e.Key == Key.Delete) { ImageListDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { ImageListBox.SelectAll(); e.Handled = true; }
        }
        /// <summary>设备表格快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void DeviceGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { DeviceCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { DeviceCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.Key == Key.Delete) { DeviceDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { DeviceGrid.SelectAll(); e.Handled = true; }
        }
        /// <summary>打开事件配置对话框（属性面板事件栏 [配置…] 按钮）。选中控件 → 控件级事件；选中世界地图画面 → 地图级 onClick 事件。</summary>
        private void OpenEventConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not EventType evt) return;
            if (PropertyVM.SelectedWidget is Widget w)
            {
                var dlg = new EventConfigDialog(_currentProject, w, evt, _viewModel.CommandService) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    MarkProjectDirty();   // WidgetEvent.Actions 就地修改不触发订阅 → 显式标脏防静默丢配置
                    PropertyVM.RefreshEventBar(w);   // 配置后刷新事件栏状态
                }
            }
            else if (PropertyVM.IsWorldMapScreen)
            {
                var dlg = new EventConfigDialog(_currentProject, null, evt, _viewModel.CommandService) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    MarkProjectDirty();
                    PropertyVM.RefreshWorldMapEventBar();   // 配置后刷新事件栏计数
                }
            }
        }
        /// <summary>表格行复制剪贴板（Ctrl+C 深拷贝选中行，Ctrl+V 粘贴新建行副本）。</summary>
        private readonly List<object> _copiedRows = new();

        /// <summary>生成不冲突的名字（原名已存在 → 原名_副本 → 原名_副本2 …）。</summary>
        private static string UniqueName(string name, IEnumerable<string> existing)
        {
            if (!existing.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
            for (int i = 1; ; i++)
            {
                var candidate = $"{name}_副本{i}";
                if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
            }
        }

        /// <summary>变量表格复制粘贴：Ctrl+C 深拷贝选中变量行，Ctrl+V 新建变量副本（重名自动 _副本N）。</summary>
        private void TagCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (Tag t in TagGrid.SelectedItems)
                    _copiedRows.Add(ProtoBuf.Serializer.DeepClone(t));
                return;
            }
            var existing = _viewModel.CurrentProject.Tags.Select(t => t.Name).ToList();
            foreach (Tag t in _copiedRows.OfType<Tag>())
            {
                var name = UniqueName(t.Name, existing);
                existing.Add(name);
                var r = _viewModel.CommandService.Execute("create_tag", new Dictionary<string, object?>
                {
                    ["name"] = name, ["data_type"] = t.DataType.ToString(), ["source"] = t.Source,
                    ["unit"] = t.Unit, ["scan_interval"] = t.ScanIntervalMs, ["deadband"] = t.Deadband,
                    ["description"] = t.Description, ["base_value"] = t.BaseValue,
                });
                if (!r.Success) MessageBox.Show($"{name}: {r.ErrorMessage}", "复制变量", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>报警表格复制粘贴：Ctrl+C 深拷贝选中报警行，Ctrl+V 新建报警副本。</summary>
        private void AlarmCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (AlarmRule a in AlarmGrid.SelectedItems)
                    _copiedRows.Add(ProtoBuf.Serializer.DeepClone(a));
                return;
            }
            var existing = _viewModel.CurrentProject.Alarms.Select(a => a.Name).ToList();
            foreach (AlarmRule a in _copiedRows.OfType<AlarmRule>())
            {
                var name = UniqueName(a.Name, existing);
                existing.Add(name);
                var r = _viewModel.CommandService.Execute("create_alarm", new Dictionary<string, object?>
                {
                    ["name"] = name, ["tag_name"] = a.TagName, ["type"] = a.Type.ToString(),
                    ["threshold"] = a.Threshold, ["deadband"] = a.Deadband, ["delay_ms"] = a.DelayMs,
                    ["severity"] = a.Level.ToString(), ["message"] = a.Message,
                });
                if (!r.Success) MessageBox.Show($"{name}: {r.ErrorMessage}", "复制报警", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>设备表格复制粘贴：Ctrl+C 深拷贝选中设备行，Ctrl+V 新建设备副本。</summary>
        private void DeviceCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (DeviceConfig d in DeviceGrid.SelectedItems)
                    _copiedRows.Add(ProtoBuf.Serializer.DeepClone(d));
                return;
            }
            var existing = _viewModel.CurrentProject.Devices.Select(d => d.Name).ToList();
            foreach (DeviceConfig d in _copiedRows.OfType<DeviceConfig>())
            {
                var name = UniqueName(d.Name, existing);
                existing.Add(name);
                var r = _viewModel.CommandService.Execute("create_device", new Dictionary<string, object?>
                {
                    ["name"] = name, ["protocol"] = d.Protocol.ToString(), ["connection_info"] = d.ConnectionInfo,
                });
                if (!r.Success) MessageBox.Show($"{name}: {r.ErrorMessage}", "复制设备", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>文本列表项复制粘贴：Ctrl+C 存值，Ctrl+V 添加新项（复制值）。</summary>
        private void TextItemsCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (ListItemVM it in TextItemsGrid.SelectedItems) _copiedRows.Add(it.Value);
                return;
            }
            foreach (string v in _copiedRows.OfType<string>())
                _listManagerVM.PasteItem(ListType.Text, v);
        }

        /// <summary>图像列表项复制粘贴：Ctrl+C 存值，Ctrl+V 添加新项（复制值）。</summary>
        private void ImageItemsCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (ListItemVM it in ImageItemsGrid.SelectedItems) _copiedRows.Add(it.Value);
                return;
            }
            foreach (string v in _copiedRows.OfType<string>())
                _listManagerVM.PasteItem(ListType.Image, v);
        }

        /// <summary>P2-1 用户组：新建组弹窗。</summary>
        private void GroupAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new GroupEditDialog { Owner = this };
            if (dlg.ShowDialog() == true)
                _viewModel.AddGroupCommand(dlg.GroupName.Trim(), dlg.GetPermissions());
        }

        /// <summary>P2-1 用户组：双击行编辑弹窗（W-5a：预设组组名只读——防改名绕过删除保护，仅可改权限）。</summary>
        private void GroupsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItem is not UserGroup g) return;
            var dlg = new GroupEditDialog { Owner = this };
            dlg.Prefill(g.Name, g.Permissions);
            dlg.IsPreset = g.Name is "管理员" or "操作员" or "访客";
            if (dlg.ShowDialog() == true)
                _viewModel.UpdateGroupCommand(g.Name, dlg.GroupName.Trim(), dlg.GetPermissions());
        }

        /// <summary>A2：组表格 DELETE 键批量删除选中（一次确认；预设组命令层 BLOCKED 跳过并汇总）。PreviewKeyDown 隧道事件——KeyDown 冒泡会被 DataGridCell 内置删除命令拦截（原 KeyDown 失效根因）；仅无修饰键时触发（对齐窗口级 Delete 分支）。</summary>
        private void GroupsGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None) return;
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItems.Count == 0) return;
            var groups = dg.SelectedItems.OfType<UserGroup>().ToList();
            if (System.Windows.MessageBox.Show($"确定删除选中的 {groups.Count} 个用户组吗？（预设组不可删）", "用户组",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            {
                e.Handled = true;
                return;
            }
            _viewModel.DeleteGroupsBatch(groups.Select(g => g.Name).ToList());
            e.Handled = true;
        }

        /// <summary>P2-1 用户组：删除选中（按钮/右键；预设组命令层 BLOCKED）。</summary>
        private void GroupDelete_Click(object sender, RoutedEventArgs e)
        {
            var g = FindGroup(sender);
            if (g != null) _viewModel.DeleteGroupCommand(g.Name);
        }

        /// <summary>P2-1/任务12 用户组：复制选中组（多选 SelectedItems——右键行已在 PreviewMouseRightButtonDown 选中）。</summary>
        private void GroupCopy_Click(object sender, RoutedEventArgs e)
        {
            var grid = FindName("GroupsGrid") as System.Windows.Controls.DataGrid;
            if (grid == null) return;
            _viewModel.CopyGroupsCommand(grid.SelectedItems.OfType<UserGroup>().ToList());
        }

        /// <summary>P2-1/任务12 用户组：右键未选中行时先选中该行（右键复制语义正确）。</summary>
        private void GroupsGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg) return;
            var row = FindDataGridRow(e);
            if (row?.Item is UserGroup g && !dg.SelectedItems.Contains(g))
                dg.SelectedItem = g;
        }

        /// <summary>从鼠标事件源上溯到 DataGridRow（无则 null）。</summary>
        private static System.Windows.Controls.DataGridRow? FindDataGridRow(System.Windows.Input.MouseButtonEventArgs e)
        {
            var el = e.OriginalSource as System.Windows.DependencyObject;
            while (el != null && el is not System.Windows.Controls.DataGridRow)
                el = System.Windows.Media.VisualTreeHelper.GetParent(el);
            return el as System.Windows.Controls.DataGridRow;
        }

        /// <summary>P2-1/任务12 用户组：粘贴组副本（多副本）。</summary>
        private void GroupPaste_Click(object sender, RoutedEventArgs e) => _viewModel.PasteGroupsCommand();

        /// <summary>从 sender（按钮/菜单项）溯源到组表格并取选中组。</summary>
        private UserGroup? FindGroup(object sender)
        {
            var fe = sender as System.Windows.FrameworkElement;
            if (fe?.DataContext is UserGroup g) return g;   // 菜单项：DataContext=右键行组
            var grid = FindName("GroupsGrid") as System.Windows.Controls.DataGrid;
            return grid?.SelectedItem as UserGroup;
        }
        /// <summary>P2-12 用户：添加用户弹窗（用户名+密码+组下拉）。</summary>
        private void UserAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UserEditDialog(_viewModel.UserGroups) { Owner = this };
            if (dlg.ShowDialog() == true)
                _viewModel.AddUserCommand(dlg.UserName.Trim(), dlg.Password, dlg.SelectedGroup);
        }

        /// <summary>P2-12 用户：双击行编辑弹窗（用户名+密码+组）。</summary>
        private void UsersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItem is not UserAccount u) return;
            var dlg = new UserEditDialog(_viewModel.UserGroups) { Owner = this };
            dlg.Prefill(u.UserName, u.GroupName);
            if (dlg.ShowDialog() == true)
                _viewModel.UpdateUserCommand(u.UserName, dlg.UserName.Trim(), dlg.Password, dlg.SelectedGroup);
        }

        /// <summary>补 #2：用户表格 DELETE 键批量删除选中用户（一次确认；命令层最后管理员保护，失败汇总——对齐 A2 组表格语义）。PreviewKeyDown 隧道事件（KeyDown 冒泡会被 DataGridCell 内置删除命令拦截）。</summary>
        private void UsersGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None) return;
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItems.Count == 0) return;
            var users = dg.SelectedItems.OfType<UserAccount>().ToList();
            if (System.Windows.MessageBox.Show($"确定删除选中的 {users.Count} 个用户吗？（最后管理员不可删）", "用户管理",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            {
                e.Handled = true;
                return;
            }
            _viewModel.DeleteUsersBatch(users.Select(u => u.UserName).ToList());
            e.Handled = true;
        }

        /// <summary>P2-12/任务12 用户：复制选中用户（多选 SelectedItems——右键行已在 PreviewMouseRightButtonDown 选中）。</summary>
        private void UserCopy_Click(object sender, RoutedEventArgs e)
        {
            var grid = FindName("UsersGrid") as System.Windows.Controls.DataGrid;
            if (grid == null) return;
            _viewModel.CopyUsersCommand(grid.SelectedItems.OfType<UserAccount>().ToList());
        }

        /// <summary>P2-12/任务12 用户：右键未选中行时先选中该行（右键复制语义正确）。</summary>
        private void UsersGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg) return;
            var row = FindDataGridRow(e);
            if (row?.Item is UserAccount u && !dg.SelectedItems.Contains(u))
                dg.SelectedItem = u;
        }

        /// <summary>P2-12/任务12 用户：粘贴用户副本（多副本）。</summary>
        private void UserPaste_Click(object sender, RoutedEventArgs e) => _viewModel.PasteUsersCommand();

        /// <summary>W3b 用户面板：删除选中用户（走 VM DeleteUserCommand——最后管理员保护）。</summary>
        private void UserDelete_Click(object sender, RoutedEventArgs e) => _viewModel.DeleteUserCommand();

        /// <summary>W3b 用户面板：保存安全设置（走 VM SaveSecurityCommand）。</summary>
        private void UserSaveSecurity_Click(object sender, RoutedEventArgs e) => _viewModel.SaveSecurityCommand();
        /// <summary>批量删除选中设备（删除按钮 + 右键；多选时全部删除）。</summary>
        private void DeviceDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = DeviceGrid.SelectedItems.Cast<DeviceConfig>().ToList();
            if (selected.Count == 0) return;
            var names = string.Join("、", selected.Select(d => $"\"{d.Name}\""));
            var confirm = MessageBox.Show($"确定删除设备 {names} 吗？", "删除设备",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var dev in selected)
            {
                var r = _viewModel.CommandService.Execute("delete_device", new Dictionary<string, object?> { ["name"] = dev.Name });
                if (!r.Success) failed.Add($"{dev.Name}: {r.ErrorMessage}");
            }
            if (failed.Count > 0)
                MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "设备", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        /// <summary>批量删除选中变量（删除按钮 + 右键菜单；多选时全部删除，被引用项拒绝并提示）。</summary>
        private void TagDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagGrid.SelectedItems.Cast<Tag>().ToList();
            if (selected.Count == 0) return;
            var names = string.Join("、", selected.Select(t => $"\"{t.Name}\""));
            var confirm = MessageBox.Show($"确定删除变量 {names} 吗？", "删除变量",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var tag in selected)
            {
                var r = _viewModel.CommandService.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = tag.Name });
                if (!r.Success) failed.Add($"{tag.Name}: {r.ErrorMessage}");
            }
            if (failed.Count > 0)
                MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "变量", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnTagDeleteRequested(Tag tag)
        {
            var confirm = MessageBox.Show($"确定删除变量 \"{tag.Name}\" 吗？", "删除变量",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var r = _viewModel.CommandService.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = tag.Name });
            if (!r.Success)
                MessageBox.Show(r.ErrorMessage ?? "删除失败", "变量", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>双击变量行 → 编辑。</summary>
        private void TagGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_variableManagerVM.SelectedTag != null)
                _variableManagerVM.EditTagCommand.Execute(null);
        }
        #endregion

        #region 原 变量拖拽（变量行 → 标签页/树节点切画面 → 画布生成绑定 IO Field）（第 1349-1622 行）
        /// <summary>框选起点（空白处按下时为 null；非 null = 框选进行中）。</summary>
        private Point? _tagMarqueeStart;
        /// <summary>悬停切换画面节流时间戳（DragOver 高频触发，200ms 内不重复切换）。</summary>
        private DateTime _lastDragSwitchTime = DateTime.MinValue;

        private void TagGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tagDragStart = e.GetPosition(null);
            var src = e.OriginalSource as DependencyObject;
            // 多选状态下按住已选中行：拦截 DataGrid 单击单选重置（WPF 多选时单击已选中项会重置为单选），
            // 保持多选供拖拽——用户需求：框选后按住不放 = 拖动多个变量。
            // 排除 Ctrl/Shift 修饰键：修饰键点击交还 DataGrid 标准行为（Ctrl 取消多选 / Shift 区间选择）
            if (TagGrid.SelectedItems.Count > 1
             && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0
             && FindDataContext<Tag>(src) is Tag pressedTag
             && TagGrid.SelectedItems.Contains(pressedTag))
            {
                e.Handled = true;
            }
            // 列头（排序）/滚动条/行上按下 → 不进入框选（保持拖拽/排序/滚动正常）；仅数据区空白按下框选
            // Y-5a-2 reviewer 🟡1：分组组头（DataContext=CollectionViewGroup）也排除——防组头按下误拉框选/与展开按钮 capture 竞争
            _tagMarqueeStart = (FindDataContext<Tag>(src) is Tag
                             || FindVisualParent<System.Windows.Controls.Primitives.DataGridColumnHeader>(src) != null
                             || FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(src) != null
                             || FindDataContext<System.Windows.Data.CollectionViewGroup>(src) != null)
                ? null
                : e.GetPosition(TagGrid);
            if (_tagMarqueeStart != null)
            {
                RebuildRowRects();   // 框选启动时刷新行位置缓存（滚动后最新，框选高频不再强制布局）
                TagGrid.Focus();     // 激活 DataGrid（未激活时选中行模板触发器用浅灰≈背景，高亮不可见；激活后选中蓝）
                TagGrid.CaptureMouse();   // 拖出松开也能收到 Up，防 Marquee 残留
            }
        }

        /// <summary>按住变量行拖动超过阈值 → 开始拖拽（数据：当前选中集合 List&lt;Tag&gt;；多选一起拖）。</summary>
        private void TagGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            // 框选模式：空白处拖动 → 画框 + 实时选中相交行
            if (_tagMarqueeStart is { } ms)
            {
                var cur = e.GetPosition(TagGrid);
                var rect = new Rect(ms, cur);
                rect = new Rect(Math.Min(ms.X, cur.X), Math.Min(ms.Y, cur.Y), Math.Abs(cur.X - ms.X), Math.Abs(cur.Y - ms.Y));
                TagGridMarquee.Visibility = Visibility.Visible;
                // 外包 Canvas：SetLeft/Top 权威定位（相对 Canvas 原点 = TagGrid 原点，与 GetPosition(TagGrid) 同源）
                Canvas.SetLeft(TagGridMarquee, rect.X);
                Canvas.SetTop(TagGridMarquee, rect.Y);
                TagGridMarquee.Width = rect.Width;
                TagGridMarquee.Height = rect.Height;
                SelectRowsInRect(rect);
                e.Handled = true;
                return;
            }
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _tagDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
             && Math.Abs(pos.Y - _tagDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (FindDataContext<Tag>(e.OriginalSource as DependencyObject) is not Tag tag) return;
            // 拖拽携带当前选中集合（含当前行；无多选时为单行）
            var tags = TagGrid.SelectedItems.Cast<Tag>().ToList();
            if (!tags.Contains(tag)) tags.Add(tag);
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("NavigatorHMI.TagList", tags), DragDropEffects.Copy);
        }

        /// <summary>框选结束：隐藏覆盖层并清状态（保持选中结果）。</summary>
        private void TagGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            TagGridMarquee.Visibility = Visibility.Collapsed;
            _tagMarqueeStart = null;
            if (TagGrid.IsMouseCaptured) TagGrid.ReleaseMouseCapture();
        }

        /// <summary>框选行位置缓存（相对 TagGrid 坐标）：避免框选 MouseMove 高频 TransformToAncestor 强制布局（主线程忙 → 选中高亮延迟）。</summary>
        private List<Rect>? _tagRowRects;

        private void TagGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 守卫：新旧尺寸相同（初始布局连发/无变化）跳过重建
            if (e.NewSize != e.PreviousSize) RebuildRowRects();
            FillLastColumn();   // 基准值列拉满剩余宽（不用 Width=*——分组视图星号列测量异常，4_bugs wpf-datagrid-grouped-star-columns）
        }

        /// <summary>基准值（末列）占满剩余宽——显式宽 + 运行时计算（用户复测点 1：占满最后一列；
        /// 分组视图下星号列（Width=*）测量异常会挤成一坨，故不用星号而按可视宽动态设宽）。
        /// 前 5 列（名称/类型/来源/单位/周期）固定宽（以 XAML 为准），末列 = 表格可视宽 − 前列合计 − 余量（下限 120）。
        /// 余量 = 垂直滚动条 + 表格左右边框 + 分组行缩进（GroupItem 约 5px——实测分组 extent 比列宽合计多 5px，少算会出横向滚动条）。
        /// 末列为**自适应列**：窗口/面板尺寸变化时重算（用户拖动末列的结果会被下次尺寸变化覆盖）；
        /// 前 5 列可自由拖动微调，其实际宽会被计入 fixedW（拖动后末列在下次尺寸变化时对齐）。</summary>
        private void FillLastColumn()
        {
            if (TagGrid == null || TagGrid.Columns.Count < 6) return;
            double fixedW = 0;
            for (int i = 0; i < TagGrid.Columns.Count - 1; i++)
                fixedW += TagGrid.Columns[i].ActualWidth;
            // 🟡-2（reviewer 复审）：列尚未测量（如空列表首次布局 ActualWidth=0）→ 退回 XAML 显式宽安全态，
            // 防 fixedW=0 时末列被设成整表宽（超宽出横向滚动条，且后续无 SizeChanged 不重算）
            if (fixedW < 1) return;
            // 余量：垂直滚动条 + 表格左右边框（实测 2）+ 分组缩进 5 + 2~3px 富余（防 DPI/主题差异溢出）
            // ——无滚动条时会留 ~18px 空白条：恒定预留的可接受取舍（避免长列表出横向滚动条）
            double reserve = SystemParameters.VerticalScrollBarWidth
                           + TagGrid.BorderThickness.Left + TagGrid.BorderThickness.Right
                           + 8;
            var avail = TagGrid.ActualWidth - fixedW - reserve;
            if (avail < 120) avail = 120;   // 下限防过窄（窄窗口时出横向滚动条）
            var last = TagGrid.Columns[TagGrid.Columns.Count - 1];
            var target = new DataGridLength(avail);
            if (Math.Abs(last.Width.Value - target.Value) > 1)   // 防重复赋值触发布局循环
                last.Width = target;
        }

        /// <summary>重建行位置缓存（Items 数量或视口尺寸变化时；框选启动时确保最新）。</summary>
        private void RebuildRowRects()
        {
            _tagRowRects = new List<Rect>();
            for (int i = 0; i < TagGrid.Items.Count; i++)
            {
                if (TagGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
                {
                    _tagRowRects.Add(Rect.Empty);   // 未生成容器（虚拟化）：跳过命中
                    continue;
                }
                var topLeft = row.TransformToAncestor(TagGrid).Transform(new Point(0, 0));
                _tagRowRects.Add(new Rect(topLeft.X, topLeft.Y, row.ActualWidth, row.ActualHeight));
            }
        }

        /// <summary>按矩形选中与其相交的变量行（用缓存行矩形，框选高频不强制布局；命中集差分更新避免每帧 SelectionChanged）。</summary>
        private void SelectRowsInRect(Rect rect)
        {
            if (_tagRowRects == null || _tagRowRects.Count != TagGrid.Items.Count)
                RebuildRowRects();
            // 计算命中集
            var hit = new List<object>();
            for (int i = 0; i < _tagRowRects!.Count && i < TagGrid.Items.Count; i++)
            {
                var rowRect = _tagRowRects[i];
                if (rowRect.IsEmpty || !rowRect.IntersectsWith(rect)) continue;
                if (TagGrid.Items[i] is Tag t) hit.Add(t);
            }
            // 差分更新：选中集合与命中集相同则跳过（避免每帧 Clear/Add 触发 SelectionChanged 与绑定刷新）
            if (TagGrid.SelectedItems.Count == hit.Count && hit.All(TagGrid.SelectedItems.Contains))
                return;
            TagGrid.SelectedItems.Clear();
            foreach (var t in hit) TagGrid.SelectedItems.Add(t);
        }

        /// <summary>沿视觉树上溯查找指定类型 DataContext（DataGridRow/TreeViewItem 通用）。</summary>
        private static T? FindDataContext<T>(DependencyObject? source) where T : class
        {
            while (source != null)
            {
                if (source is System.Windows.FrameworkElement fe && fe.DataContext is T t) return t;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        /// <summary>拖拽的是否为变量（统一判定；数据为选中集合 List&lt;Tag&gt;）。</summary>
        private static bool IsTagDrag(DragEventArgs e, out List<Tag> tags)
        {
            tags = e.Data.GetDataPresent("NavigatorHMI.TagList")
                && e.Data.GetData("NavigatorHMI.TagList") is List<Tag> list
                ? list : new List<Tag>();
            return tags.Count > 0;
        }

        /// <summary>标签页悬停/放下 → 自动切换画面（200ms 节流防 DragOver 高频来回切换）。</summary>
        private void ScreenTab_DragOver(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                e.Effects = DragDropEffects.Copy;
                if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Screen screen
                 && !ReferenceEquals(screen, _viewModel.CurrentScreen)
                 && (DateTime.Now - _lastDragSwitchTime).TotalMilliseconds >= 200)
                {
                    _lastDragSwitchTime = DateTime.Now;
                    _viewModel.ActivateScreen(screen);   // 悬停即切换（节流）
                }
                e.Handled = true;
            }
            else e.Effects = DragDropEffects.None;
        }

        private void ScreenTab_Drop(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Screen screen)
                    _viewModel.ActivateScreen(screen);
                e.Handled = true;
            }
        }

        /// <summary>树画面节点悬停/放下 → 自动切换画面（200ms 节流防 DragOver 高频来回切换）。</summary>
        private void ProjectTree_DragOver(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                e.Effects = DragDropEffects.Copy;
                if (FindDataContext<ScreenItemNode>(e.OriginalSource as DependencyObject) is { } node
                 && !ReferenceEquals(node.Screen, _viewModel.CurrentScreen)
                 && (DateTime.Now - _lastDragSwitchTime).TotalMilliseconds >= 200)
                {
                    _lastDragSwitchTime = DateTime.Now;
                    _viewModel.ActivateScreen(node.Screen);   // 悬停即切换（节流）
                }
                e.Handled = true;
            }
            else e.Effects = DragDropEffects.None;
        }

        private void ProjectTree_Drop(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                if (FindDataContext<ScreenItemNode>(e.OriginalSource as DependencyObject) is { } node)
                    _viewModel.ActivateScreen(node.Screen);
                e.Handled = true;
            }
        }

        /// <summary>画布悬停：接受变量拖拽（Copy 光标）。</summary>
        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            // 世界地图画面：地图交互优先——不接受拖放（防拖变量到地图误创建控件）
            if (_viewModel?.IsWorldMapActive == true) { e.Effects = DragDropEffects.None; return; }
            if (IsTagDrag(e, out _)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
            else e.Effects = DragDropEffects.None;
        }

        /// <summary>画布放下 → 生成绑定该变量的 IO Field（add_widget + bound_tag 一步落库）。</summary>
        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            if (_viewModel?.IsWorldMapActive == true) return;   // 世界地图画面：地图交互优先，不接受拖放
            if (!IsTagDrag(e, out var tags)) return;
            if (_viewModel.CurrentScreen == null) return;
            // GetPosition(DrawingCanvas) 经 LayoutTransform 逆变换已返回逻辑坐标，禁再次除缩放（双重除 bug）
            var pos = e.GetPosition(DrawingCanvas);
            // 坐标钳制到画布逻辑范围（拖到边框/滚动条区域不生成越界控件）
            pos.X = Math.Clamp(pos.X, 0, Math.Max(0, _viewModel.CurrentScreen.Width - 100));
            pos.Y = Math.Clamp(pos.Y, 0, Math.Max(0, _viewModel.CurrentScreen.Height - 30));
            _viewModel.PushUndoSnapshot();
            // 批量：每个变量生成一个绑定 IO Field（纵向错开 35px，底部钳制）
            // 抑制中间全量重建（N 个变量 = 1 次刷新而非 N 次），结束统一刷新一次
            int success = 0;
            _viewModel.SuppressRefresh = true;
            try
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    var y = Math.Min((int)pos.Y + i * 35, Math.Max(0, _viewModel.CurrentScreen.Height - 30));
                    var r = _viewModel.CommandService.Execute("add_widget", new Dictionary<string, object?>
                    {
                        ["screen_name"] = _viewModel.CurrentScreen.Name,
                        ["widget_type"] = "iofield",
                        ["x"] = (int)pos.X, ["y"] = y,
                        ["width"] = 100, ["height"] = 30,
                        ["bound_tag"] = tags[i].Name,
                    });
                    if (r.Success) success++;
                    else
                        MessageBox.Show($"变量 \"{tags[i].Name}\": {r.ErrorMessage}", "拖拽绑定", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally { _viewModel.SuppressRefresh = false; }
            if (success > 0)
                _viewModel.NotifyCanvasRefreshNeeded();   // 统一刷新一次
            else
                _viewModel.PopUndoSnapshot();   // 全部失败：弹出已推但未生效的空快照（与 OnModifyFailed 语义一致）
            e.Handled = true;
        }
        #endregion

        #region 原 变量管理器 Tab（画布标签栏）（第 4154-4210 行）

        /// <summary>关闭「变量管理器」Tab（当前激活时切回当前画面）。</summary>
        private void VarManagerTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseVariableManagerTab();
            e.Handled = true;
        }

        /// <summary>L-B1：点击「设备管理」标签激活（Tab 已开）。</summary>
        private void DeviceTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateDevice();
            e.Handled = true;
        }

        /// <summary>L-B1：关闭「设备管理」Tab（当前激活时切回当前画面）。</summary>
        private void DeviceTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseDeviceTab();
            e.Handled = true;
        }

        /// <summary>Y-3b：点击「MQTT 设置」标签激活（Tab 已开）。</summary>
        private void MqttTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateMqtt();
            e.Handled = true;
        }

        /// <summary>Y-3b：关闭「MQTT 设置」Tab（当前激活时切回当前画面）。</summary>
        private void MqttTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseMqttTab();
            e.Handled = true;
        }

        /// <summary>🔴C1（reviewer Z-2）：清 MQTT 连接页密码框明文（切连接/应用成功回调）。</summary>
        private void ClearMqttPasswordBox()
        {
            if (MqttConnPasswordBox != null && MqttConnPasswordBox.Password.Length > 0)
                MqttConnPasswordBox.Clear();   // Clear 触发 PasswordChanged → 推空串到 VM.PasswordPlain（幂等）
        }

        /// <summary>Z 循环：MQTT 总览连接列表双击 → 进入该连接页（ViewModel.SelectedConnectionName）。</summary>
        private void MqttConnList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MqttConnListBox?.SelectedItem is NavigatorHMI.ViewModels.MqttSettingsViewModel.ConnectionItem item)
                _viewModel.MqttSettingsVM.SelectedConnectionName = item.Model.Name;
            e.Handled = true;
        }

        /// <summary>Z 循环：连接页密码框输入 → 暂存明文到 VM（「应用连接参数」时 DPAPI 加密提交——不回显密文）。</summary>
        private void MqttConnPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (MqttConnPasswordBox != null)
                _viewModel.MqttSettingsVM.PasswordPlain = MqttConnPasswordBox.Password;
        }
        #endregion

    }
}
