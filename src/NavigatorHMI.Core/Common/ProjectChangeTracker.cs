namespace NavigatorHMI.Common
{
    /// <summary>
    /// 变化域（增量编译链骨架 P13/V-6）：工程数据修改的分类粒度。
    /// 域 = 命令归属的数据段（画面/控件/事件/变量/报警/用户/组/列表/设备/字体/剪贴板/世界地图）。
    /// 画面级命令另记录受影响画面名（改画面 A → 摘要只列 A；校验仍全跑——.navihmi 整包编译不裁剪）。
    /// </summary>
    public enum ChangeDomain
    {
        /// <summary>非修改命令（查询/工程操作/设备运行时），不参与变化跟踪</summary>
        None,
        Screen, Widget, Event, Tag, Alarm, User, Group, List,
        Device, Font, Clipboard, WorldMap,
    }

    /// <summary>变化摘要快照（compile 消费输出；编译成功后重置基线）。</summary>
    public sealed class ChangeSummary
    {
        public List<ChangeDomain> Domains { get; } = new();
        public List<string> Screens { get; } = new();
        public bool Any => Domains.Count > 0 || Screens.Count > 0;
    }

    /// <summary>
    /// 工程变化跟踪器（挂在 HMIProject 上，CommandService.Execute 成功时记录——GUI/CLI/REPL/AI 同进程全部入口自动覆盖）。
    /// 与 GUI OnCommandExecuted（智能刷新 + 标脏）职责分离：本类只做「编译变化摘要」的脏域记录，
    /// 不含 UI 刷新；设备运行时命令（connect/disconnect/scan/deploy/blink/vnc）域 = None 天然不记录。
    /// 画面粒度：命令参数里的 screen_name 值；新建/重命名画面用 name/new_name 值。
    /// </summary>
    public sealed class ProjectChangeTracker
    {
        private readonly ChangeSummary _pending = new();
        private readonly HashSet<ChangeDomain> _domains = new();
        private readonly HashSet<string> _screens = new(StringComparer.Ordinal);

        /// <summary>记录一次成功命令的变化（命令名 → 域 + 画面名；None 域直接忽略）。</summary>
        public void Record(string commandName, Dictionary<string, object?> parameters)
        {
            var domain = DomainOf(commandName);
            if (domain == ChangeDomain.None) return;
            _domains.Add(domain);
            var screen = ScreenOf(commandName, parameters);
            if (screen != null) _screens.Add(screen);
        }

        /// <summary>当前未编译变化摘要（compile 消费输出；不改变状态）。</summary>
        public ChangeSummary Snapshot()
        {
            var s = new ChangeSummary();
            s.Domains.AddRange(_domains);
            s.Screens.AddRange(_screens.OrderBy(x => x, StringComparer.Ordinal));
            return s;
        }

        /// <summary>编译成功后重置基线（新基线 = 本次编译产物）。</summary>
        public void Reset()
        {
            _domains.Clear();
            _screens.Clear();
        }

        /// <summary>
        /// 命令名 → 变化域映射（命令层注册表全命令显式归类；新增命令须在此登记——漏登记 = 变化摘要缺域，
        /// 测试 ChangeTrackerTests.全部命令_域映射完备 锁死）。查询/工程操作/设备运行时命令 → None。
        /// </summary>
        public static ChangeDomain DomainOf(string commandName) => commandName switch
        {
            // 工程操作（非数据修改）
            "create_project" or "open_project" or "save_project" or "compile" => ChangeDomain.None,
            // 设备运行时（不修改工程数据）
            "connect" or "disconnect" or "scan_devices" or "deploy_project"
                or "deploy_firmware" or "blink_device" or "vnc" => ChangeDomain.None,
            // 画面（current_screen = 查询，不参与变化跟踪）
            "create_screen" or "delete_screen" or "rename_screen" or "copy_screen"
                or "paste_screen" => ChangeDomain.Screen,
            // 控件（含层级/布局/剪贴板——均作用于画面内控件）
            "add_widget" or "move_widget" or "resize_widget" or "delete_widget"
                or "set_property" or "bind_robot_slot" or "bind_tag"
                or "bring_to_front" or "bring_forward" or "send_backward" or "send_to_back"
                or "align_widgets" or "array_layout"
                or "copy_widget" or "paste_widget" => ChangeDomain.Widget,
            // 事件
            "bind_event" or "add_event" or "remove_event" or "update_event" => ChangeDomain.Event,
            // 变量
            "create_tag" or "update_tag" or "delete_tag" => ChangeDomain.Tag,
            // 报警
            "create_alarm" or "update_alarm" or "delete_alarm" => ChangeDomain.Alarm,
            // 用户/组（list_users = 查询，不参与变化跟踪）
            "create_user" or "update_user" or "delete_user" => ChangeDomain.User,
            "create_group" or "update_group" or "delete_group" => ChangeDomain.Group,
            // 列表
            "create_list" or "update_list" or "delete_list" => ChangeDomain.List,
            // 设备（通信配置 = 工程数据；MQTT 引用校验细节归入本域，V+1 补全）
            "configure_device" or "update_device" or "delete_device" => ChangeDomain.Device,
            // MQTT 多连接管理器（Y-3b + Z 循环 2026-09-11：连接 CRUD + 映射命令带 connection_name——通信配置域）
            "mqtt_set_enabled" or "mqtt_add_connection" or "mqtt_update_connection" or "mqtt_delete_connection"
                or "mqtt_add_topic" or "mqtt_update_topic" or "mqtt_delete_topic"
                or "mqtt_set_binding" or "mqtt_remove_binding" => ChangeDomain.Device,
            // 字体
            "set_default_font" => ChangeDomain.Font,
            // 世界地图
            "add_work_point" or "delete_work_point" or "add_work_range_point"
                or "clear_work_range" or "update_world_map" => ChangeDomain.WorldMap,
            _ => ChangeDomain.None,
        };

        /// <summary>从命令参数提取受影响画面名（画面粒度摘要；非画面命令/无画面参数 → null）。
        /// 画面内修改命令一律 screen_name 定位——成功路径 handler 均要求有效 screen_name（缺失/空 → 失败不 Record），
        /// 故世界地图等命令无需 name 兜底（其 name 是作业点名非画面名）。</summary>
        public static string? ScreenOf(string commandName, Dictionary<string, object?> parameters)
        {
            // 画面级命令：新建/重命名/复制/粘贴以自身名称为目标画面（paste 未提供 name 时 handler 自动生成唯一名，
            // 摘要无画面名——骨架期信息缺口，记录在案）
            if (commandName is "create_screen" or "delete_screen" or "rename_screen" or "copy_screen" or "paste_screen")
                return Param(parameters, "new_name") ?? Param(parameters, "name");
            return Param(parameters, "screen_name");
        }

        private static string? Param(Dictionary<string, object?> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var v) || v == null) return null;
            var s = v.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
    }
}
