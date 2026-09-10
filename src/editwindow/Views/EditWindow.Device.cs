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

        #region 原 通讯配置（第 918-924 行）

        /// <summary>双击设备行 → 编辑。</summary>
        private void DeviceGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_commDeviceVM.SelectedDevice != null)
                _commDeviceVM.EditDeviceCommand.Execute(null);
        }
        #endregion

        #region 原 报警配置（第 968-1343 行）

        /// <summary>报警删除：确认 → CommandService（delete_alarm 无引用限制，直接删除）。</summary>
        /// <summary>批量删除选中报警（删除按钮 + 右键；多选时全部删除）。</summary>
        private void AlarmDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = AlarmGrid.SelectedItems.Cast<AlarmRule>().ToList();
            if (selected.Count == 0) return;
            var names = string.Join("、", selected.Select(a => $"\"{a.Name}\""));
            var confirm = MessageBox.Show($"确定删除报警 {names} 吗？", "删除报警",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var alarm in selected)
            {
                var r = _viewModel.CommandService.Execute("delete_alarm", new Dictionary<string, object?> { ["name"] = alarm.Name });
                if (!r.Success) failed.Add($"{alarm.Name}: {r.ErrorMessage}");
            }
            if (failed.Count > 0)
                MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "报警", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>批量删除选中文本列表项（删除按钮/Delete 键；多选时全部删除）。</summary>
        private void TextItemsDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TextItemsGrid.SelectedItems.Cast<ListItemVM>().ToList();
            if (selected.Count > 0) _listManagerVM.RemoveItems(ListType.Text, selected);
        }

        /// <summary>批量删除选中图片列表项（删除按钮/Delete 键；多选时全部删除）。</summary>
        private void ImageItemsDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ImageItemsGrid.SelectedItems.Cast<ListItemVM>().ToList();
            if (selected.Count > 0) _listManagerVM.RemoveItems(ListType.Image, selected);
        }

        /// <summary>文本列表项快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void TextItemsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { TextItemsCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { TextItemsCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;   // 单元格编辑态：Delete 交给文本编辑，防整行误删
            if (e.Key == Key.Delete) { TextItemsDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { TextItemsGrid.SelectAll(); e.Handled = true; }
        }

        /// <summary>图片列表项快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void ImageItemsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { ImageItemsCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { ImageItemsCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;   // 单元格编辑态：Delete 交给文本编辑，防整行误删
            if (e.Key == Key.Delete) { ImageItemsDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { ImageItemsGrid.SelectAll(); e.Handled = true; }
        }
        /// <summary>批量删除选中文本列表（删除按钮；多选时全部删除）。</summary>
        private void TextListDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TextListBox.SelectedItems.Cast<ListDef>().ToList();
            if (selected.Count > 0) _listManagerVM.DeleteLists(selected);
        }

        /// <summary>批量删除选中图片列表（删除按钮；多选时全部删除）。</summary>
        private void ImageListDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ImageListBox.SelectedItems.Cast<ListDef>().ToList();
            if (selected.Count > 0) _listManagerVM.DeleteLists(selected);
        }

        // ── S-4 视频源列表页 handlers（2026-09-05：第三列表型，仿文本页纯文本项）──

        /// <summary>视频源列表项复制粘贴：Ctrl+C 存值，Ctrl+V 添加新项（复制值）。</summary>
        private void VideoItemsCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (ListItemVM it in VideoItemsGrid.SelectedItems) _copiedRows.Add(it.Value);
                return;
            }
            foreach (string v in _copiedRows.OfType<string>())
                _listManagerVM.PasteItem(ListType.Video, v);
        }

        /// <summary>批量删除选中视频源列表项（删除按钮/Delete 键；多选时全部删除）。</summary>
        private void VideoItemsDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = VideoItemsGrid.SelectedItems.Cast<ListItemVM>().ToList();
            if (selected.Count > 0) _listManagerVM.RemoveItems(ListType.Video, selected);
        }

        /// <summary>视频源列表项快捷键：Delete 删除选中、Ctrl+A 全选、Ctrl+C/V 复制粘贴。</summary>
        private void VideoItemsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { VideoItemsCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { VideoItemsCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;   // 单元格编辑态：Delete 交给文本编辑，防整行误删
            if (e.Key == Key.Delete) { VideoItemsDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { VideoItemsGrid.SelectAll(); e.Handled = true; }
        }

        /// <summary>视频源列表框快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void VideoListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (_listNameEditor != null) return;   // 行内编辑中：Delete/Ctrl+A 交给编辑框（防误删列表/误全选）
            if (e.Key == Key.Delete) { VideoListDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { VideoListBox.SelectAll(); e.Handled = true; }
        }

        /// <summary>批量删除选中视频源列表（删除按钮；多选时全部删除）。</summary>
        private void VideoListDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = VideoListBox.SelectedItems.Cast<ListDef>().ToList();
            if (selected.Count > 0) _listManagerVM.DeleteLists(selected);
        }

        // ── 视频列表本地项预览（U 循环问题2 三修 2026-09-06 用户裁决交互 A：仅选中行加载（设源即播 +
        //    Position 轮询锁首帧——不依赖 MediaOpened 时序）；控制 = 预览格下方 ▶播放/⏸暂停 按钮（MediaElement
        //    是 HWND 子窗口——视频区点击被原生窗口截获 WPF 收不到，原 Border 点击交互废弃）；RTSP/网络源提示
        //    不可预览（MediaElement 后端不支持 rtsp 直连））──
        private System.Windows.Controls.Border? _videoRowBorder;      // 活动行预览 Border（Tag=播放状态 true=播放中）
        private MediaElement? _videoRowMedia;                         // 活动行预览列 MediaElement
        private TextBlock? _videoRowHint;                             // 活动行预览列提示覆盖层
        private Button? _videoRowPlayBtn;                             // 活动行预览列播放/暂停按钮
        private int _videoRowRetries;                                 // 容器/模板未就绪重试计数（防饿死，每选中 ≤3）
        private ListItemVM? _videoRowRetryItem;                       // 重试预算绑定项（item 变化/置空清零）
        private DispatcherTimer? _videoRowFrameTimer;                 // Position 轮询锁帧定时器（每加载重建防闭包陈旧）

        private void VideoItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => VideoRowRefresh();

        /// <summary>视觉子树查找（预览列模板内元素——DataTemplate 内 x:Name 不可直接寻址）。</summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var hit = FindVisualChild<T>(child);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>视觉祖先查找（MediaEnded/Failed 的 sender=MediaElement → 所属预览 Border）。</summary>
        private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            var p = System.Windows.Media.VisualTreeHelper.GetParent(d);
            while (p != null)
            {
                if (p is T t) return t;
                p = System.Windows.Media.VisualTreeHelper.GetParent(p);
            }
            return null;
        }

        /// <summary>选中变化/切列表/删除 → 清旧行预览 → 新选中行预览列设源并加载（仅选中行加载，未选中行黑底占位）。
        /// 审查 🔴（2026-09-05 PC 复审驳回修复）：**不得** FindVisualChild&lt;Border&gt;(row)——DataGridRow 模板行根即
        /// Border(DGR_Border)、单元格另含 Border(Bd)、文本列含 TextBlock，行级查找会命中行根/序号列——提示被写进序号列
        /// 破坏显示；改为先 FindVisualChild&lt;MediaElement&gt;(row)（预览列模板内唯一）再 FindAncestor 反查预览 Border，
        /// hint 限定在预览 Border 内查找（覆盖层 TextBlock 唯一）。
        /// U 三修（2026-09-06）：media/border 未生成（模板内容异步）与 row 同款有限重试——杜绝「静默放弃永不设源」黑屏；
        /// 加载 = 设源即 Play + Position 轮询锁首帧（不依赖 MediaOpened——探针实证本环境立即 Play 画面即出）。</summary>
        private void VideoRowRefresh()
        {
            _videoRowFrameTimer?.Stop();   // 行切换 → 取消挂起的自动锁帧
            if (_videoRowMedia != null) { _videoRowMedia.Stop(); _videoRowMedia.Source = null; }
            if (_videoRowBorder != null) _videoRowBorder.Tag = null;
            if (_videoRowHint != null) _videoRowHint.Text = "▶ 播放预览";
            if (_videoRowPlayBtn != null) { _videoRowPlayBtn.Content = "▶ 播放"; _videoRowPlayBtn.ToolTip = null; }   // 🔵-1：清旧行错误 ToolTip 残留
            _videoRowBorder = null; _videoRowMedia = null; _videoRowHint = null; _videoRowPlayBtn = null;
            var item = VideoItemsGrid.SelectedItem as ListItemVM;
            if (item == null) { _videoRowRetryItem = null; return; }
            // 审查 🟡（第 3 轮）：重试预算随选中项变化清零——每选中独立 3 次（防共享计数被前项耗尽致新项零重试）
            if (!ReferenceEquals(item, _videoRowRetryItem)) { _videoRowRetryItem = item; _videoRowRetries = 0; }
            var row = VideoItemsGrid.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.DataGridRow;
            var media = row != null ? FindVisualChild<MediaElement>(row) : null;   // 预览列模板内 MediaElement（行内唯一）
            var border = media != null ? FindAncestor<System.Windows.Controls.Border>(media) : null;   // 反查预览 Border
            // 行虚拟化/模板内容异步：容器或模板元素未就绪 → 延后重试（≤3 次 + Background 优先级低于 Render，
            // 先让布局/容器生成跑一轮防饿死）——U 三修：media/border 查找失败同样重试（原静默 return 会永不设源黑屏）
            if (row == null || media == null || border == null)
            {
                if (_videoRowRetries < 3)
                {
                    _videoRowRetries++;
                    Dispatcher.BeginInvoke(new Action(VideoRowRefresh), System.Windows.Threading.DispatcherPriority.Background);
                }
                return;
            }
            _videoRowRetries = 0;   // 全部命中 → 计数清零（新选中从 0 起）
            _videoRowBorder = border;
            _videoRowMedia = media;
            _videoRowHint = FindVisualChild<TextBlock>(border);   // 预览 Border 内覆盖层 TextBlock（唯一）
            _videoRowPlayBtn = FindVisualChild<Button>(row);      // 预览列模板内播放/暂停按钮（行内唯一）
            if (_videoRowPlayBtn != null) _videoRowPlayBtn.Content = "▶ 播放";
            var val = item.Value?.Trim() ?? "";
            if (val.Length == 0) { if (_videoRowHint != null) _videoRowHint.Text = "（空项）"; return; }
            if (val.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                || val.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || val.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            { if (_videoRowHint != null) _videoRowHint.Text = "RTSP/网络源\n不可预览"; return; }
            if (!System.IO.File.Exists(val))
            { if (_videoRowHint != null) _videoRowHint.Text = "文件不存在\n或相对路径"; return; }
            try
            {
                media.Source = new Uri(val);
                if (_videoRowHint != null) _videoRowHint.Text = "加载中…";   // 状态诊断：黑屏时可见到哪一步
                try { media.Play(); }   // 设源即播放（Manual 模式 Play 触发打开+播放；不依赖 MediaOpened 事件时序）
                catch (InvalidOperationException)
                {
                    if (_videoRowHint != null) _videoRowHint.Text = "无法启动播放";
                    return;
                }
                StartVideoFrameLock(media);   // Position 轮询：首帧渲染完成（位置开始推进）即 Pause 锁帧
            }
            catch (UriFormatException)
            {
                if (_videoRowHint != null) _videoRowHint.Text = "路径含非法字符\n无法预览";
            }
        }

        /// <summary>Position 轮询锁帧（U 三修 2026-09-06）：MediaElement 首帧渲染完成的可靠信号 = 播放位置开始推进。
        /// 设源即 Play 后每 100ms 查 Position——&gt;0 即已渲染首帧 → Pause 停住显首帧（不自动继续播）；
        /// 用户已点播放（Tag=true）/切行清源/20s 超时/竞态 → 停止轮询。每次调用重建 timer（闭包捕获当次 me，
        /// 防单例闭包陈旧——上轮 reviewer 🔴 教训）。</summary>
        private void StartVideoFrameLock(MediaElement me)
        {
            _videoRowFrameTimer?.Stop();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            int ticks = 0;
            timer.Tick += (_, _) =>
            {
                ticks++;
                if (me.Source == null || !ReferenceEquals(me, _videoRowMedia)) { timer.Stop(); return; }   // 切行清源/活动行已换
                if (_videoRowBorder?.Tag is true) { timer.Stop(); return; }                                // 用户已点播放 → 不再自动锁帧
                var locked = false;
                try
                {
                    if (me.Position.TotalSeconds > 0.001)   // 播放时钟已推进 = 媒体已打开且首帧已渲染
                    {
                        me.Pause();                        // 锁帧暂停（画面保留当前帧）
                        timer.Stop();
                        if (_videoRowHint != null) _videoRowHint.Text = "";
                        if (_videoRowPlayBtn != null) _videoRowPlayBtn.Content = "▶ 播放";
                        locked = true;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    // 复审 🔴（2026-09-06 reviewer 驳回）：媒体**打开中**读 Position 抛 InvalidOperationException——
                    // 打开流程必经的临时态（大文件 >100ms 常见），catch **不得 Stop**（否则首 tick 即终止轮询、
                    // 媒体稍后打开自动播放无人锁帧 + 「加载中…」残留）——仅记日志继续轮询
                    Log.Debug("VideoRowMedia 锁帧等待媒体打开: {Msg}", ex.Message);
                }
                if (!locked && ticks > 200)   // 20s 上限统一出口（成功已 Stop；异常路径同样受上限约束）
                {
                    timer.Stop();
                    if (_videoRowHint != null) _videoRowHint.Text = "打开较慢\n（可点 ▶ 手动播放）";
                }
            };
            _videoRowFrameTimer = timer;
            timer.Start();
        }

        /// <summary>播放预览（按钮驱动——视频区为 HWND 层点击收不到，控制走预览格下方按钮）。</summary>
        private void PlayVideoPreview()
        {
            var media = _videoRowMedia;
            var border = _videoRowBorder;
            if (media == null || media.Source == null || border == null) return;
            _videoRowFrameTimer?.Stop();   // 取消挂起的自动锁帧（用户要持续播放）
            try { media.Play(); }
            catch (InvalidOperationException) { if (_videoRowHint != null) _videoRowHint.Text = "无法启动播放"; return; }
            border.Tag = true;
            if (_videoRowHint != null) _videoRowHint.Text = "";
            if (_videoRowPlayBtn != null) _videoRowPlayBtn.Content = "⏸ 暂停";
        }

        /// <summary>暂停预览（按钮驱动）。</summary>
        private void PauseVideoPreview()
        {
            var media = _videoRowMedia;
            var border = _videoRowBorder;
            if (media == null || border == null) return;
            try { media.Pause(); }
            catch (InvalidOperationException) { return; }
            border.Tag = false;
            if (_videoRowHint != null) _videoRowHint.Text = "";
            if (_videoRowPlayBtn != null) _videoRowPlayBtn.Content = "▶ 播放";
        }

        /// <summary>点视频区 = 仅阻断（U 四修 2026-09-06 用户复测：点画面停播变默认字样 + 按钮失联——视频 HWND
        /// 区域点击被 WPF 命中为「行外空白」→ DataGrid 取消选中 → SelectionChanged → VideoRowRefresh 复位停播、
        /// 字段清空后按钮无反应）。Preview 隧道阶段截断，不冒泡为 DataGrid 空白点击（选中保持、播放不被打断）；
        /// 播放控制走下方 ▶/⏸ 按钮（交互 A 定案，视频区不做点击 toggle）。</summary>
        private void VideoRowVideoArea_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        /// <summary>预览播放/暂停按钮（交互 A 2026-09-06 用户裁决；U 四修 2026-09-06 起不再用 Tag——改**实时行查找**
        /// 操作：点非选中行按钮先选中（触发加载）再播；仅本地已加载源响应（RTSP/空/缺失由提示说明）；
        /// 不依赖 _videoRowMedia 字段（字段可能因行重建/复位悬空），点按钮即确保该行选中（选中被误清时恢复
        /// 并重新加载）再播放。</summary>
        private void VideoRowPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var row = FindAncestor<System.Windows.Controls.DataGridRow>(btn);
            if (row?.DataContext is not ListItemVM it) return;
            if (!ReferenceEquals(it, VideoItemsGrid.SelectedItem)) VideoItemsGrid.SelectedItem = it;   // 先选中（同步触发加载锁帧；选中已被误清时恢复）
            var media = FindVisualChild<MediaElement>(row);        // 实时行内元素（不依赖字段）
            var border = media != null ? FindAncestor<System.Windows.Controls.Border>(media) : null;
            if (media == null || border == null || media.Source == null) return;   // 该行无源（空项/网络/缺失 → hint 已说明）
            // 同步活动行字段（按钮行必为选中行——上面已确保）
            _videoRowMedia = media;
            _videoRowBorder = border;
            _videoRowPlayBtn = btn;
            _videoRowHint = FindVisualChild<TextBlock>(border);
            if (border.Tag is true) PauseVideoPreview();
            else PlayVideoPreview();
        }

        /// <summary>播放自然结束 → 复位该行并 Stop（Position 回 0——审查 🟡-4：Ended 后 Play 不自动重播，
        /// Stop 后点击 Play 从头播；hint 提示可重播）。</summary>
        private void VideoRowMedia_Ended(object sender, RoutedEventArgs e)
        {
            if (sender is not MediaElement me) return;
            me.Stop();   // Ended 自然结束后 Stop 归零——后续点击 Play 从头（暂停态不触发本 handler）
            if (ReferenceEquals(me, _videoRowMedia)) _videoRowFrameTimer?.Stop();   // 复审 🟡-1：终态停锁帧轮询（防超时出口覆盖「播放结束」文案）
            var border = FindAncestor<System.Windows.Controls.Border>(me);
            if (border == null) return;
            border.Tag = false;
            var hint = FindVisualChild<TextBlock>(border);
            if (hint != null) hint.Text = "播放结束\n▶ 重播";
            var row = FindAncestor<System.Windows.Controls.DataGridRow>(me);
            if (row != null && FindVisualChild<Button>(row) is { } btn) btn.Content = "▶ 播放";
        }

        /// <summary>打开/解码失败 → 复位该行 + 提示（含错误摘要诊断——设备端打包播放不受影响）。</summary>
        private void VideoRowMedia_Failed(object sender, ExceptionRoutedEventArgs e)
        {
            if (sender is not MediaElement me) return;
            if (ReferenceEquals(me, _videoRowMedia)) _videoRowFrameTimer?.Stop();   // 复审 🟡-1：终态停锁帧轮询（防 20s 超时出口用「打开较慢」覆盖「无法播放」错误摘要）
            var border = FindAncestor<System.Windows.Controls.Border>(me);
            if (border == null) return;
            border.Tag = false;
            var hint = FindVisualChild<TextBlock>(border);
            var full = "";
            if (hint != null)
            {
                var msg = e.ErrorException?.Message ?? "";
                full = msg.Length == 0 ? "未知错误" : msg;
                hint.Text = "无法播放\n" + (full.Length > 40 ? full[..40] + "…" : full);   // 格子内摘要
            }
            var row = FindAncestor<System.Windows.Controls.DataGridRow>(me);
            if (row != null && FindVisualChild<Button>(row) is { } btn)
            {
                btn.Content = "▶ 播放";
                if (full.Length > 0) btn.ToolTip = "完整错误: " + full;   // 复审 🟡Y3：覆盖层 TextBlock IsHitTestVisible=false 悬停不触发——完整错误挂按钮（WPF 层可悬停）
            }
        }

        private void OnAlarmDeleteRequested(AlarmRule alarm)
        {
            var confirm = MessageBox.Show($"确定删除报警 \"{alarm.Name}\" 吗？", "删除报警",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var r = _viewModel.CommandService.Execute("delete_alarm", new Dictionary<string, object?> { ["name"] = alarm.Name });
            if (!r.Success)
                MessageBox.Show(r.ErrorMessage ?? "删除失败", "报警", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>双击报警行 → 编辑。</summary>
        private void AlarmGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_alarmVM.SelectedAlarm != null)
                _alarmVM.EditAlarmCommand.Execute(null);
        }
        #endregion

    }
}
