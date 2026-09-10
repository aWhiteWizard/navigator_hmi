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

        #region 原 CLI 控制台（第 4931-5467 行）

        /// <summary>沿视觉树向上查找点击目标是否属于 CliOutput 子树（输出区不参与聚焦，保留文本选择/复制能力）。</summary>
        private bool IsDescendantOfCliOutput(DependencyObject? node)
        {
            while (node != null)
            {
                // 滚动条 Thumb 视觉链止于 ScrollViewer（模板元素不经过内容 TextBox），须同时检查 Scroller 本身
                if (ReferenceEquals(node, CliOutput) || ReferenceEquals(node, CliOutputScroller)) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        /// <summary>沿视觉树向上查找点击目标是否属于 CliInput 子树。</summary>
        private bool IsDescendantOfCliInput(DependencyObject? node)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, CliInput)) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }


        private void AiInput_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter 发送，Shift+Enter 换行（与聊天工具一致）；IME 候选确认回车（ImeProcessedKey != None）放行给 TextBox 提交候选词，不发送
            if (e.Key == Key.Enter && e.ImeProcessedKey == Key.None && !(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
            {
                e.Handled = true;
                if (_viewModel.AiSendCommand.CanExecute(null))
                    _viewModel.AiSendCommand.Execute(null);
            }
        }

        /// <summary>A7：IME 候选词确认回车（ImeProcessedKey != None）不触发发送——隧道阶段拦截，中文输入法选词不误发送；普通 Enter 在 Preview 发送并 Handled（KeyDown 被屏蔽），IME 回车放行给 TextBox 提交候选词。</summary>
        private void AiInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.ImeProcessedKey == Key.None
                && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                if (_viewModel.AiSendCommand.CanExecute(null))
                    _viewModel.AiSendCommand.Execute(null);
            }
        }

        /// <summary>AI 消息新增时自动滚动到底部（长会话不丢失新消息视野）。</summary>
        private void AiMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || AiMessagesList == null) return;
            AiMessagesList.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (AiMessagesList.Items.Count > 0)
                    AiMessagesList.ScrollIntoView(AiMessagesList.Items[AiMessagesList.Items.Count - 1]);
            }));
        }

        /// <summary>菜单「设置 → AI 助手设置…」：打开 API Key 配置窗口（DPAPI 密文存储），保存后刷新 ViewModel。</summary>
        private void AiSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AiSettingsWindow { Owner = this };
            if (dlg.ShowDialog() == true)
                _viewModel.ReloadAiKeyFromStore();
        }

        /// <summary>AI 消息右键「复制」：拖选了文字则复制选中段，否则复制整条消息。</summary>
        private void AiMsgCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Parent is not ContextMenu cm) return;
            if (cm.PlacementTarget is System.Windows.Controls.TextBox tb)
            {
                try
                {
                    if (tb.SelectionLength > 0)
                        System.Windows.Clipboard.SetText(tb.SelectedText);
                    else if (tb.Tag is string full)
                        System.Windows.Clipboard.SetText(full);
                }
                catch (Exception ex) { Log.Warning(ex, "剪贴板复制失败（其他进程短暂锁定，不阻断 UI）"); }
            }
        }


        private void CliInput_KeyDown(object sender, KeyEventArgs e)
        {
            // 命令历史：↑/↓ 切换
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (_historyIndex > 0)
                {
                    _historyIndex--;
                    CliInput.Text = _cliHistory[_historyIndex];
                    CliInput.CaretIndex = CliInput.Text.Length;
                }
                return;
            }
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (_historyIndex < _cliHistory.Count - 1)
                {
                    _historyIndex++;
                    CliInput.Text = _cliHistory[_historyIndex];
                }
                else
                {
                    _historyIndex = _cliHistory.Count;
                    CliInput.Text = "";
                }
                CliInput.CaretIndex = CliInput.Text.Length;
                return;
            }

            if (e.Key != Key.Enter) return;

            // Shift+Enter：插入换行继续编辑（多行命令输入）；普通 Enter：执行全部行
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                CliInput.SelectedText = "\n"; // 光标处断行，光标跟随
                e.Handled = true;
                return;
            }
            e.Handled = true;

            var input = CliInput.Text;
            CliInput.Clear();

            // 多行支持：按行拆分逐条执行（支持粘贴多行命令脚本）
            // WPF TextBox 行分隔符为 \r\n，显式处理 \r 避免残留（不依赖 Trim 兜底）
            var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(l => l.Trim())
                             .Where(l => l.Length > 0)
                             .ToList();
            if (lines.Count == 0) return;

            // 记入历史（整段，回显逐条）
            _cliHistory.Add(input.Trim());
            _historyIndex = _cliHistory.Count;

            foreach (var line in lines)
                ExecuteCliLine(line);
        }

        /// <summary>执行单条 CLI 命令（含回显与结果输出）。</summary>
        private void ExecuteCliLine(string input)
        {
            // 回显命令
            AppendCliOutput($"> {input}", "LimeGreen");

            // 特殊命令
            if (input is "cls" or "clear") { CliOutput.Clear(); return; }
            if (input is "help" or "?") { AppendCliOutput(CliHelpText, "Gray"); return; }

            try
            {
                // 解析命令：navihmi 格式 → key=value 参数
                var parts = ParseCliLine(input);
                if (parts.Length == 0) return;

                var command = parts[0];
                var opts = new Dictionary<string, string>();
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("--") && i + 1 < parts.Length && !parts[i + 1].StartsWith("--"))
                        opts[parts[i][2..]] = parts[++i];
                    else if (parts[i].StartsWith("--"))
                        opts[parts[i][2..]] = "true";
                }

                // 净化所有参数（防止路径遍历注入）
                SanitizeCliParams(opts);

                // 路由到对应 Handler
                var result = ExecuteGuiCommand(command, opts);
                if (result.Success)
                {
                    if (result.Data is List<string> lines)
                        foreach (var l in lines) AppendCliOutput(l, "Gray");
                    else if (command is "compile" or "b")   // GUI 别名 b = compile（ExecuteGuiCommand 同源）
                        AppendCliOutput($"✓ {command} — {FormatCompileData(result.Data)}", "White");
                    else
                        AppendCliOutput($"✓ {command}" + (result.Data != null ? $" — {result.Data}" : ""), "White");
                }
                else
                    AppendCliOutput($"✗ [{result.ErrorCode}] {result.ErrorMessage}", "Red");
            }
            catch (Exception ex)
            {
                AppendCliOutput($"✗ 错误: {ex.Message}", "Red");
            }
        }

        private CommandResult ExecuteGuiCommand(string command, Dictionary<string, string> opts)
        {

            return command switch
            {
                "create-screen" or "cs" => _viewModel.CommandService.Execute("create_screen",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["type"] = opts.GetValueOrDefault("type", "custom"), ["width"] = opts.GetValueOrDefault("width", ""), ["height"] = opts.GetValueOrDefault("height", "") }),
                "delete-screen" or "ds" => _viewModel.CommandService.Execute("delete_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "rename-screen" => _viewModel.CommandService.Execute("rename_screen", new() { ["name"] = opts.GetValueOrDefault("name", ""), ["new_name"] = opts.GetValueOrDefault("new-name", "") }),
                "copy-screen" => _viewModel.CommandService.Execute("copy_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "paste-screen" => _viewModel.CommandService.Execute("paste_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "copy-widget" => _viewModel.CommandService.Execute("copy_widget", new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widget_name"] = opts.GetValueOrDefault("widget", "") }),
                "paste-widget" => _viewModel.CommandService.Execute("paste_widget", new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["x"] = opts.GetValueOrDefault("x", ""), ["y"] = opts.GetValueOrDefault("y", "") }),
                "set-default-font" => _viewModel.CommandService.Execute("set_default_font",
                    new() { ["font_family"] = opts.GetValueOrDefault("font-family", ""), ["font_size"] = opts.GetValueOrDefault("font-size", ""), ["font_weight"] = opts.GetValueOrDefault("font-weight", ""), ["font_style"] = opts.GetValueOrDefault("font-style", ""), ["text_decoration"] = opts.GetValueOrDefault("text-decoration", "") }),
                "add-widget" or "aw" => _viewModel.CommandService.Execute("add_widget",
                    new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widget_type"] = opts.GetValueOrDefault("type", "button"), ["x"] = opts.GetValueOrDefault("x", "100"), ["y"] = opts.GetValueOrDefault("y", "100"), ["width"] = opts.GetValueOrDefault("width", "100"), ["height"] = opts.GetValueOrDefault("height", "40"), ["center"] = opts.GetValueOrDefault("center", "false"), ["bound_tag"] = opts.GetValueOrDefault("bound-tag", ""), ["window_type"] = opts.GetValueOrDefault("window-type", "userview") }),
                "compile" or "b" => _viewModel.CommandService.Execute("compile", new()),
                "save" => _viewModel.CommandService.Execute("save_project", new()),
                "create-tag" or "ct" => _viewModel.CommandService.Execute("create_tag",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["data_type"] = opts.GetValueOrDefault("type", "FLOAT"), ["source"] = opts.GetValueOrDefault("source", ""), ["unit"] = opts.GetValueOrDefault("unit", ""), ["scan_interval"] = opts.GetValueOrDefault("scan-interval", "100"), ["deadband"] = opts.GetValueOrDefault("deadband", "0"), ["description"] = opts.GetValueOrDefault("description", ""), ["base_value"] = opts.GetValueOrDefault("base-value", "") }),
                "align" => _viewModel.CommandService.Execute("align_widgets",
                    new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widgets"] = opts.GetValueOrDefault("widgets", ""), ["direction"] = opts.GetValueOrDefault("direction", "") }),
                "array" => ArrayLayoutFromGuiCli(opts),
                "list-screens" or "ls" => ListScreens(),
                "scan" => _viewModel.CommandService.Execute("scan_devices",
                    new() { ["nic"] = opts.GetValueOrDefault("nic", "") }),
                "connect" => _viewModel.CommandService.Execute("connect",
                    new() { ["ip"] = opts.GetValueOrDefault("ip", ""), ["model"] = opts.GetValueOrDefault("model", "NavigatorHMI-7"), ["size_inch"] = opts.GetValueOrDefault("size-inch", "") }),
                "disconnect" => _viewModel.CommandService.Execute("disconnect", new()),
                "configure-device" => _viewModel.CommandService.Execute("configure_device",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["protocol"] = opts.GetValueOrDefault("protocol", ""), ["connection_info"] = opts.GetValueOrDefault("connection", "") }),
                "deploy-project" => _viewModel.CommandService.Execute("deploy_project",
                    new() { ["device_ip"] = opts.GetValueOrDefault("ip", ""), ["file_path"] = opts.GetValueOrDefault("file", "") }),
                "deploy-firmware" => _viewModel.CommandService.Execute("deploy_firmware",
                    new() { ["device_ip"] = opts.GetValueOrDefault("ip", ""), ["file_path"] = opts.GetValueOrDefault("file", "") }),
                "blink-device" => _viewModel.CommandService.Execute("blink_device",
                    new() { ["ip"] = opts.GetValueOrDefault("ip", ""), ["enable"] = opts.GetValueOrDefault("enable", "") }),
                "vnc" => _viewModel.CommandService.Execute("vnc",
                    new() { ["ip"] = opts.GetValueOrDefault("ip", ""), ["enable"] = opts.GetValueOrDefault("enable", "") }),
                "create-alarm" => _viewModel.CommandService.Execute("create_alarm",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["tag_name"] = opts.GetValueOrDefault("tag", ""), ["type"] = opts.GetValueOrDefault("type", ""), ["threshold"] = opts.GetValueOrDefault("threshold", ""), ["deadband"] = opts.GetValueOrDefault("deadband", "0"), ["delay_ms"] = opts.GetValueOrDefault("delay", "0"), ["severity"] = opts.GetValueOrDefault("severity", "Warning"), ["message"] = opts.GetValueOrDefault("message", ""), ["trigger_mode"] = opts.GetValueOrDefault("trigger-mode", "Threshold"), ["category"] = opts.GetValueOrDefault("category", "User"), ["priority"] = opts.GetValueOrDefault("priority", "0"), ["ack_required"] = opts.GetValueOrDefault("ack-required", "true"), ["ack_group"] = opts.GetValueOrDefault("ack-group", ""), ["color_override"] = opts.GetValueOrDefault("color-override", "") }),
                "update-alarm" => UpdateAlarm(opts),
                "delete-alarm" => _viewModel.CommandService.Execute("delete_alarm", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "create-list" => _viewModel.CommandService.Execute("create_list",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["type"] = opts.GetValueOrDefault("type", ""), ["items"] = opts.GetValueOrDefault("items", "") }),
                "update-tag" => UpdateTag(opts),
                // 续29/30：GUI CLI add-event 系参数映射（对齐独立 CLI AddEvent/RemoveEvent/UpdateEvent；
                // 原走 ExecuteDefaultCommand 无 event/action 映射 + params 未解析字典 → COMMAND_CRASH）
                "add-event" => AddEventFromGuiCli(opts),
                "remove-event" => RemoveEventFromGuiCli(opts),
                "update-event" => UpdateEventFromGuiCli(opts),
                // 续30 对齐审计：bind-event 原走 ExecuteDefaultCommand，params 未解析字典 → 强转崩溃；补专用分支对齐 CLI BindEvent
                "bind-event" => BindEventFromGuiCli(opts),
                _ => ExecuteDefaultCommand(command, opts)
            };
        }

        /// <summary>GUI CLI bind-event：为控件绑定事件-动作（对齐独立 CLI BindEvent：screen/widget/event/action 必填 + params 字典解析）。</summary>
        private CommandResult BindEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "widget", "event", "action") is { } err) return err;
            return _viewModel.CommandService.Execute("bind_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event"] = opts.GetValueOrDefault("event", ""),
                ["action"] = opts.GetValueOrDefault("action", ""),
                ["params"] = ParseCliParamsDict(opts),
            });
        }

        /// <summary>GUI CLI add-event：--screen→screen_name、--widget→widget_name（可选，缺省+世界地图=地图级事件）、--event→event_type、--action→action_type、--condition（可选，I-3 事件触发条件）、--params "k=v,k=v" 解析为字典；缺必填参数报错（对齐独立 CLI RequireVal）。</summary>
        private CommandResult AddEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "event", "action") is { } err) return err;
            return _viewModel.CommandService.Execute("add_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event_type"] = opts.GetValueOrDefault("event", ""),
                ["action_type"] = opts.GetValueOrDefault("action", ""),
                ["condition"] = opts.TryGetValue("condition", out var c) ? c : null,   // I-3：未传=无条件（保持现状）
                ["params"] = ParseCliParamsDict(opts),
            });
        }

        /// <summary>GUI CLI remove-event：--action 指定仅移除该动作；否则移除整个事件（--event 必填、--action 可选，对齐独立 CLI RemoveEvent）。</summary>
        private CommandResult RemoveEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "event") is { } err) return err;
            return _viewModel.CommandService.Execute("remove_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event_type"] = opts.GetValueOrDefault("event", ""),
                ["action_type"] = opts.GetValueOrDefault("action", ""),
            });
        }

        /// <summary>GUI CLI update-event：更新事件下动作的参数（params 完整替换，--condition 可选更新事件条件，对齐独立 CLI UpdateEvent）。</summary>
        private CommandResult UpdateEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "event", "action") is { } err) return err;
            return _viewModel.CommandService.Execute("update_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event_type"] = opts.GetValueOrDefault("event", ""),
                ["action_type"] = opts.GetValueOrDefault("action", ""),
                ["condition"] = opts.TryGetValue("condition", out var c) ? c : null,   // I-3：未传=保持现状
                ["params"] = ParseCliParamsDict(opts),
            });
        }

        /// <summary>GUI CLI 缺必填参数检查（对齐独立 CLI RequireVal 的缺参报错语义，逐个 key 报缺失）。</summary>
        private static CommandResult? MissingRequired(Dictionary<string, string> opts, params string[] keys)
        {
            foreach (var k in keys)
                if (string.IsNullOrWhiteSpace(opts.GetValueOrDefault(k, "")))
                    return CommandResult.Fail("MISSING_PARAM", $"缺少必填参数: --{k}");
            return null;
        }

        /// <summary>GUI CLI --params "k=v,k=v" 解析为 Dictionary（对齐独立 CLI AddEvent/UpdateEvent 的 raw 解析）。</summary>
        private static Dictionary<string, string> ParseCliParamsDict(Dictionary<string, string> opts)
        {
            var raw = opts.GetValueOrDefault("params", "");
            var dict = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(raw))
                foreach (var kv in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = kv.Split('=', 2);
                    if (parts.Length == 2) dict[parts[0].Trim()] = parts[1].Trim();
                }
            return dict;
        }

        /// <summary>GUI CLI array：OptIfProvided 语义（cols/rows 未提供不进字典 → 命令层自动计算，与独立 CLI Program.cs 对齐；防双入口漂移）。</summary>
        private CommandResult ArrayLayoutFromGuiCli(Dictionary<string, string> opts)
        {
            var p = new Dictionary<string, object?>
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widgets"] = opts.GetValueOrDefault("widgets", ""),
                ["mode"] = opts.GetValueOrDefault("mode", "rect"),
                ["start_x"] = opts.GetValueOrDefault("start-x", "0"),
                ["start_y"] = opts.GetValueOrDefault("start-y", "0"),
                ["spacing_x"] = opts.GetValueOrDefault("spacing-x", "120"),
                ["spacing_y"] = opts.GetValueOrDefault("spacing-y", "80"),
                ["center_x"] = opts.GetValueOrDefault("center-x", "0"),
                ["center_y"] = opts.GetValueOrDefault("center-y", "0"),
                ["radius"] = opts.GetValueOrDefault("radius", "150"),
                ["start_angle"] = opts.GetValueOrDefault("start-angle", "0"),
                ["end_angle"] = opts.GetValueOrDefault("end-angle", "360"),
            };
            // OptIfProvided：显式传了才覆盖（未提供 → 命令层按控件数自动计算 cols/rows）
            if (opts.TryGetValue("cols", out var c) && c.Length > 0) p["cols"] = c;
            if (opts.TryGetValue("rows", out var r) && r.Length > 0) p["rows"] = r;
            return _viewModel.CommandService.Execute("array_layout", p);
        }

        /// <summary>GUI CLI update-alarm：OptIfProvided 语义（未提供的字段不进字典保留现值；message 显式提供才传，含空串 = 清空描述）。</summary>
        private CommandResult UpdateAlarm(Dictionary<string, string> opts)
        {
            var p = new Dictionary<string, object?> { ["name"] = opts.GetValueOrDefault("name", "") };
            if (opts.TryGetValue("new-name", out var nn) && nn.Length > 0) p["new_name"] = nn;
            if (opts.TryGetValue("tag", out var tag) && tag.Length > 0) p["tag_name"] = tag;
            if (opts.TryGetValue("type", out var type) && type.Length > 0) p["type"] = type;
            if (opts.TryGetValue("threshold", out var th) && th.Length > 0) p["threshold"] = th;
            if (opts.TryGetValue("deadband", out var db) && db.Length > 0) p["deadband"] = db;
            if (opts.TryGetValue("delay", out var dl) && dl.Length > 0) p["delay_ms"] = dl;
            if (opts.TryGetValue("severity", out var sv) && sv.Length > 0) p["severity"] = sv;
            if (opts.TryGetValue("message", out var msg)) p["message"] = msg;
            // W1 六参数（OptIfProvided 语义：未提供不进字典保留现值；与 CLI Program.cs:141 逐 key 对齐）
            if (opts.TryGetValue("trigger-mode", out var tm) && tm.Length > 0) p["trigger_mode"] = tm;
            if (opts.TryGetValue("category", out var cat) && cat.Length > 0) p["category"] = cat;
            if (opts.TryGetValue("priority", out var pri) && pri.Length > 0) p["priority"] = pri;
            if (opts.TryGetValue("ack-required", out var ar) && ar.Length > 0) p["ack_required"] = ar;
            if (opts.TryGetValue("ack-group", out var ag) && ag.Length > 0) p["ack_group"] = ag;
            if (opts.TryGetValue("color-override", out var co) && co.Length > 0) p["color_override"] = co;
            return _viewModel.CommandService.Execute("update_alarm", p);
        }

        /// <summary>GUI CLI update-tag：type→data_type 显式映射（防 MapCliKey 全局 type→widget_type 误转，cli-param-sanitize §6）；OptIfProvided 语义，未提供字段保留现值。</summary>
        private CommandResult UpdateTag(Dictionary<string, string> opts)
        {
            var p = new Dictionary<string, object?> { ["name"] = opts.GetValueOrDefault("name", "") };
            if (opts.TryGetValue("new-name", out var nn) && nn.Length > 0) p["new_name"] = nn;
            if (opts.TryGetValue("type", out var ty) && ty.Length > 0) p["data_type"] = ty;
            if (opts.TryGetValue("source", out var src)) p["source"] = src;
            if (opts.TryGetValue("unit", out var un)) p["unit"] = un;
            if (opts.TryGetValue("scan-interval", out var si) && si.Length > 0) p["scan_interval"] = si;
            if (opts.TryGetValue("deadband", out var db) && db.Length > 0) p["deadband"] = db;
            if (opts.TryGetValue("description", out var desc)) p["description"] = desc;
            if (opts.TryGetValue("base-value", out var bv)) p["base_value"] = bv;
            return _viewModel.CommandService.Execute("update_tag", p);
        }

        private static string MapCliKey(string key) => key switch
        {
            "screen" => "screen_name", "widget" => "widget_name", "type" => "widget_type",
            "tag" => "tag_name", "file" => "file_path",
            // 用户/组命令参数键（对齐 CLI OptMap/Require：user-name→user_name 等；缺映射时命令层拿不到必填参数）
            "user-name" => "user_name", "new-user-name" => "new_user_name",
            "new-password" => "new_password", "new-group-name" => "new_group_name",
            "group-name" => "group_name",
            // 控件命令参数键（对齐 CLI OptMap：add_widget 的 bound-tag/window-type）
            "bound-tag" => "bound_tag", "window-type" => "window_type",
            // 布局命令参数键（与 CLI OptMap 对齐）：kebab → snake
            "start-x" => "start_x", "start-y" => "start_y",
            "spacing-x" => "spacing_x", "spacing-y" => "spacing_y",
            "center-x" => "center_x", "center-y" => "center_y",
            "start-angle" => "start_angle", "end-angle" => "end_angle",
            "scan-interval" => "scan_interval",
            "new-name" => "new_name",
            "base-value" => "base_value",
            "connection" => "connection_info",
            // 世界地图命令参数键（2026-08-26 规范整改：对齐 WorldMapHandlers/CLI OptMap——原缺映射致 GUI 控制台参数静默失效）
            "lng-lat" => "lng_lat",
            "tile-source" => "tile_source",
            "zoom-level" => "zoom_level",
            "show-global-overlay" => "show_global_overlay",
            "view-locked" => "view_locked",
            // bind-robot-slot 槽位参数键（对齐 WidgetHandlers：原缺映射致 GUI 控制台绑槽参数静默失效）
            "id-tag" => "id_tag", "status-tag" => "status_tag",
            "location-tag" => "location_tag", "detail-tag" => "detail_tag",
            "oper-tag" => "oper_tag",
            _ => key
        };

        private CommandResult ExecuteDefaultCommand(string command, Dictionary<string, string> opts)
        {
            var mapped = new Dictionary<string, object?>();
            foreach (var kv in opts) mapped[MapCliKey(kv.Key)] = kv.Value;
            return _viewModel.CommandService.Execute(command.Replace("-", "_"), mapped);
        }

        private CommandResult ListScreens()
        {
            var names = _viewModel.CurrentProject.Screens.Select(s => $"  {(s.Type == ScreenType.Custom ? "📄" : s.Type == ScreenType.Template ? "📌" : "🌍")} {s.Name}").ToList();
            return CommandResult.Ok(names);
        }

        /// <summary>参数安全净化（三级分类，逻辑统一在 <see cref="NavigatorHMI.Common.CliParamSanitizer"/>，与 CLI 端逐 key 一致）。</summary>
        private static void SanitizeCliParams(Dictionary<string, string> opts)
        {
            foreach (var kv in opts.ToList())
            {
                var (key, value) = (kv.Key, kv.Value);
                var err = NavigatorHMI.Common.CliParamSanitizer.Validate(key, value);
                if (err != null) throw new ArgumentException(err);
            }
        }

        private void AppendCliOutput(string text, string color)
        {
            Dispatcher.Invoke(() =>
            {
                CliOutput.AppendText(text + "\n");
                // L-B4 输出窗口：命令/编译/连接日志聚合同步到「输出」窗口 + 自动弹出（用户定：有信息自动弹出）
                if (OutputBox != null)
                {
                    OutputBox.AppendText(text + "\n");
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (OutputScroller != null) OutputScroller.ScrollToEnd();
                        else OutputBox.ScrollToEnd();
                    }));
                }
                // 输出更新后自动滚动到新一行：延后到布局完成后（AppendText 只触发 InvalidateMeasure，
                // 立即 ScrollToEnd 时 ScrollableHeight 还是旧值，连续多行会停在旧底部）
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (CliOutputScroller != null)
                        CliOutputScroller.ScrollToEnd();
                    else
                        CliOutput.ScrollToEnd();
                }));
            });
        }

        /// <summary>L-B4：编译/连接日志直接写入输出窗口（自动弹出）。BeginInvoke 异步封送——OnOutputCommandExecuted 可能后台线程持锁回调（审查🟡4：同步 Invoke 会与 UI 线程互等死锁）。</summary>
        private void AppendOutput(string text)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowAnchorable("output");   // 有信息自动弹出输出窗口（用户 2026-08-30 定）
                if (OutputBox != null)
                {
                    OutputBox.AppendText(text + "\n");
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (OutputScroller != null) OutputScroller.ScrollToEnd();
                    }));
                }
            }));
        }

        /// <summary>L-B4：设备命令（connect/disconnect/blink/vnc/deploy/scan）结果日志聚合到输出窗口（可能后台线程——封送）。
        /// N-3 审查 🟡 修复：命令清单复用 IsDeviceRuntimeCommand（消除第二份硬编码清单，防新增设备命令漂移）。</summary>
        private void OnOutputCommandExecuted(string cmdName, Dictionary<string, object?> parameters, NavigatorHMI.CommandLayer.CommandResult result)
        {
            if (!EditWindowViewModel.IsDeviceRuntimeCommand(cmdName)) return;
            var ok = result.Success ? "✓" : $"✗ [{result.ErrorCode}]";
            var msg = result.Success ? (result.Data?.ToString() ?? "") : result.ErrorMessage;
            AppendOutput($"[{cmdName}] {ok} {msg}");
        }

        /// <summary>L-B1：设备面板内部操作（连接测试/搜索）日志 → 输出窗口（DevicePanelVM.OutputRequested 触发）。</summary>
        private void OnDevicePanelOutput(string text)
        {
            AppendOutput(text);
        }

        private static string[] ParseCliLine(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (char.IsWhiteSpace(line[i])) { i++; continue; }
                if (line[i] == '"')
                {
                    int end = line.IndexOf('"', i + 1);
                    if (end < 0) { result.Add(line[(i + 1)..]); break; }
                    result.Add(line[(i + 1)..end]);
                    i = end + 1;
                }
                else
                {
                    int end = i;
                    while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
                    result.Add(line[i..end]);
                    i = end;
                }
            }
            return result.ToArray();
        }

        private const string CliHelpText = NavigatorHMI.Common.CliHelpContent.Text;  // 与帮助对话框共用单一来源
        #endregion

    }
}
