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

        // AB-6 复审（2026-09-11）【编辑辅助域：剪贴板复制粘贴（变量/报警/设备/文本项/图片项）+ 列表名内联编辑】 —— 自 EditWindow.Variables.cs 纯搬家（基线 72daf0e 第 67-287 行，12 个成员；内容零改动）
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

        internal void ListNameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitListNameEdit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelListNameEdit(); e.Handled = true; }
        }

        internal void ListNameEditor_LostFocus(object sender, RoutedEventArgs e)
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

        private readonly List<object> _copiedRows = new();

        private static string UniqueName(string name, IEnumerable<string> existing)
        {
            if (!existing.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
            for (int i = 1; ; i++)
            {
                var candidate = $"{name}_副本{i}";
                if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
            }
        }

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

    }
}
