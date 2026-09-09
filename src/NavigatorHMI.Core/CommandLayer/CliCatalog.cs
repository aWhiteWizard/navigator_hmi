using System.Text;

namespace NavigatorHMI.CommandLayer
{
    /// <summary>
    /// CLI 参数视图。描述一个 CLI 参数：CLI 键 → 命令层 cmd 键的映射 + CLI 侧取值语义。
    /// 注意：CLI 语义层 ≠ AI compact schema 语义层——元数据 Required/DefaultValue 面向 AI 调用，
    /// CLI 侧更宽容（如 add-widget --type 默认 button 而元数据 Required；create-screen --width 空串
    /// 走 handler 设备尺寸兜底而元数据 DefaultValue=800）。故本视图保留 CLI 自身默认，不直接抄元数据。
    /// 与命令层 CommandDefinition 的一致性由 <see cref="CliCatalog.ValidateDefinitions"/> 交叉校验（防 cmdKey 漂移）。
    /// </summary>
    public sealed class CliParameterView
    {
        /// <summary>CLI 键（如 --screen 的 "screen"）</summary>
        public string CliKey { get; init; } = "";

        /// <summary>命令层参数键（如 screen_name）</summary>
        public string CmdKey { get; init; } = "";

        /// <summary>CLI 必填（缺参报错）。为 false 时由 Default/ProvidedOnly 语义决定。</summary>
        public bool Required { get; init; }

        /// <summary>
        /// CLI 默认值（未提供时写入该串；含空串 = 显式写空，handler 自行解释，如 create-screen --width "" → 设备尺寸兜底）。
        /// null = 未提供不进参数字典（保留现值语义，update 类命令）。
        /// </summary>
        public string? Default { get; init; }

        public static CliParameterView R(string cliKey, string cmdKey) => new() { CliKey = cliKey, CmdKey = cmdKey, Required = true };
        public static CliParameterView D(string cliKey, string cmdKey, string defaultValue) => new() { CliKey = cliKey, CmdKey = cmdKey, Default = defaultValue };
        /// <summary>同键必填（cli 键 = cmd 键）</summary>
        public static CliParameterView Rk(string key) => R(key, key);
        /// <summary>同键带默认</summary>
        public static CliParameterView Dk(string key, string defaultValue) => D(key, key, defaultValue);
        /// <summary>同键 provided-only（显式提供才写，未提供保留现值）</summary>
        public static CliParameterView Pk(string key) => new() { CliKey = key, CmdKey = key };
        public static CliParameterView P(string cliKey, string cmdKey) => new() { CliKey = cliKey, CmdKey = cmdKey };
    }

    /// <summary>
    /// CLI 命令规格（声明式路由表）。<see cref="CommandName"/> 非空 = 命令层有同名命令；
    /// <see cref="Handler"/> 非 None = 走 CLI 专用执行器（工程命令/事件家族/ai 等有特殊工程/参数解析逻辑）。
    /// 帮助文本（--help）签名行由 <see cref="Summary"/>（人工润色的完整签名 + 语义说明）呈现——
    /// Summary 与 <see cref="Views"/>（执行语义：键映射/必填/默认）是两套文本须人工保持一致；
    /// 一致性由 CliCatalogConsistencyTests（元数据命令 Summary 的 --键 ⊆ Views 键）交叉钳制。
    /// </summary>
    public sealed class CliCommandSpec
    {
        public string CliName { get; init; } = "";
        public string? CommandName { get; init; }
        public string Category { get; init; } = "";
        public string Summary { get; init; } = "";
        /// <summary>CLI 专用执行器标识（None = 元数据命令走通用 MetaCmd）。</summary>
        public CliCustomHandler Handler { get; init; }
        /// <summary>是否 CLI 专用执行器（由 Handler 派生，杜绝 IsCustom/Handler 双字段漂移）。</summary>
        public bool IsCustom => Handler != CliCustomHandler.None;
        public IReadOnlyList<CliParameterView> Views { get; init; } = Array.Empty<CliParameterView>();
    }

    /// <summary>CLI 专用执行器标识（CliCommandSpec.Handler → NaviHmiCLI.Program 路由 switch）。</summary>
    public enum CliCustomHandler
    {
        None,
        CreateProject, OpenProject, SaveProject, Compile,
        BindEvent, AddEvent, RemoveEvent, UpdateEvent,
        Ai,
    }

    /// <summary>
    /// CLI 命令目录（单一事实源）。用途：
    /// ① NaviHmiCLI/Program.cs 路由（表驱动，替代手写 switch）；
    /// ② 帮助文本生成（--help / REPL help）；
    /// ③ 与命令层 CommandDefinition 交叉校验（cmdKey 漂移即测试红——gui-cli-param-map-missing 负样本直接对治）；
    /// ④ 通信批新增命令（如 configure-device MQTT 全字段）时此处加一行 + 命令层 Definition 加参数，
    ///    两处由 ValidateDefinitions 测试互相钳制。
    /// </summary>
    public static class CliCatalog
    {
        /// <summary>全部 CLI 命令（按帮助分组顺序）。</summary>
        public static IReadOnlyList<CliCommandSpec> All { get; } = BuildAll();

        public static CliCommandSpec? Find(string cliName)
            => All.FirstOrDefault(c => c.CliName == cliName);

        private static List<CliCommandSpec> BuildAll()
        {
            var list = new List<CliCommandSpec>();
            void Add(CliCommandSpec s) => list.Add(s);

            // ══ 工程（custom：CLI 有特殊加载/构造逻辑）══
            Add(new CliCommandSpec { CliName = "create-project", CommandName = "create_project", Category = "工程命令", Handler = CliCustomHandler.CreateProject, Summary = "新建工程 (--name <名称> [--path <目录>] [--width 800] [--height 480])",
                Views = new[] { CliParameterView.Rk("name"), CliParameterView.Dk("path", "."), CliParameterView.Dk("width", "800"), CliParameterView.Dk("height", "480") } });
            Add(new CliCommandSpec { CliName = "open-project", CommandName = "open_project", Category = "工程命令", Handler = CliCustomHandler.OpenProject, Summary = "打开工程文件 (--path <file>)",
                Views = new[] { CliParameterView.Rk("path") } });
            Add(new CliCommandSpec { CliName = "save-project", CommandName = "save_project", Category = "工程命令", Handler = CliCustomHandler.SaveProject, Summary = "保存工程 ([--path <file>])",
                Views = new[] { CliParameterView.Pk("path") } });
            Add(new CliCommandSpec { CliName = "compile", CommandName = "compile", Category = "工程命令", Handler = CliCustomHandler.Compile, Summary = "编译工程并输出变化摘要 (--summary 易读摘要 | --json 机器可读)",
                Views = new[] { CliParameterView.P("summary", "summary"), CliParameterView.P("json", "json") } });

            // ══ 画面 ══
            Add(Meta("create-screen", "create_screen", "画面命令", "创建画面 (--name <name> [--type custom] [--width 800] [--height 480])",
                CliParameterView.Rk("name"), CliParameterView.Dk("type", "custom"), CliParameterView.Dk("width", ""), CliParameterView.Dk("height", "")));
            Add(Meta("delete-screen", "delete_screen", "画面命令", "删除画面 (--name <name>)",
                CliParameterView.Rk("name")));
            Add(Meta("rename-screen", "rename_screen", "画面命令", "重命名画面 (--name <name> --new-name <name>)",
                CliParameterView.Rk("name"), CliParameterView.R("new-name", "new_name")));
            Add(Meta("current-screen", "current_screen", "画面命令", "当前画面（无参=显示当前画面）"));
            Add(Meta("copy-screen", "copy_screen", "画面命令", "复制画面 (--name <name>)",
                CliParameterView.Rk("name")));
            Add(Meta("paste-screen", "paste_screen", "画面命令", "粘贴画面 ([--name <name>])",
                CliParameterView.Pk("name")));

            // ══ 控件 ══
            Add(Meta("add-widget", "add_widget", "控件命令", "添加控件 (--screen <name> --type button [--x 100] [--y 100] [--width 100] [--height 40] [--center false] [--bound-tag <tag>] [--window-type userview|alarmview|robotlist])",
                CliParameterView.R("screen", "screen_name"), CliParameterView.D("type", "widget_type", "button"),
                CliParameterView.Dk("x", "100"), CliParameterView.Dk("y", "100"), CliParameterView.Dk("width", "100"), CliParameterView.Dk("height", "40"),
                CliParameterView.Dk("center", "false"), CliParameterView.D("bound-tag", "bound_tag", ""), CliParameterView.D("window-type", "window_type", "userview")));
            Add(Meta("move-widget", "move_widget", "控件命令", "移动控件 (--screen <name> --widget <name> --x <n> --y <n>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name"), CliParameterView.Rk("x"), CliParameterView.Rk("y")));
            Add(Meta("resize-widget", "resize_widget", "控件命令", "调整控件尺寸 (--screen <name> --widget <name> --width <n> --height <n>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name"), CliParameterView.Rk("width"), CliParameterView.Rk("height")));
            Add(Meta("delete-widget", "delete_widget", "控件命令", "删除控件 (--screen <name> --widget <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name")));
            Add(Meta("set-property", "set_property", "控件命令", "修改控件属性 (--screen <name> --widget <name> --key <键> --value <值>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name"), CliParameterView.Rk("key"), CliParameterView.Rk("value")));
            Add(Meta("bind-robot-slot", "bind_robot_slot", "控件命令", "机器人列表槽位绑定变量 (--screen <name> --widget <name> --slot <n> [--id-tag <t>] [--status-tag <t>] [--location-tag <t>] [--detail-tag <t>] [--oper-tag <t>]；--slot 指定未占用序号=新增槽位，5 变量留空=不改)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name"), CliParameterView.Rk("slot"),
                CliParameterView.P("id-tag", "id_tag"), CliParameterView.P("status-tag", "status_tag"), CliParameterView.P("location-tag", "location_tag"),
                CliParameterView.P("detail-tag", "detail_tag"), CliParameterView.P("oper-tag", "oper_tag")));

            // ══ 层级 ══
            Add(Meta("bring-to-front", "bring_to_front", "层级命令", "控件置于顶层 (--screen <name> --widget <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name")));
            Add(Meta("bring-forward", "bring_forward", "层级命令", "控件上移一层 (--screen <name> --widget <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name")));
            Add(Meta("send-backward", "send_backward", "层级命令", "控件下移一层 (--screen <name> --widget <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name")));
            Add(Meta("send-to-back", "send_to_back", "层级命令", "控件置于底层 (--screen <name> --widget <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name")));

            // ══ 布局 ══
            Add(Meta("align", "align_widgets", "布局命令", "对齐控件 (--screen <name> --widgets <a,b,c> --direction <left|center_h|right|top|center_v|bottom>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.Rk("widgets"), CliParameterView.Rk("direction")));
            Add(Meta("array", "array_layout", "布局命令", "阵列排列 (--screen <name> --widgets <a,b,c> --mode <rect|circle> [--start-x 0] [--start-y 0] [--cols <n>] [--rows <n>] [--spacing-x 120] [--spacing-y 80] [--center-x 0] [--center-y 0] [--radius 150] [--start-angle 0] [--end-angle 360]；--cols/--rows 省略时按控件数自动计算)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.Rk("widgets"), CliParameterView.Rk("mode"),
                CliParameterView.D("start-x", "start_x", "0"), CliParameterView.D("start-y", "start_y", "0"),
                CliParameterView.P("cols", "cols"), CliParameterView.P("rows", "rows"),
                CliParameterView.D("spacing-x", "spacing_x", "120"), CliParameterView.D("spacing-y", "spacing_y", "80"),
                CliParameterView.D("center-x", "center_x", "0"), CliParameterView.D("center-y", "center_y", "0"),
                CliParameterView.Dk("radius", "150"), CliParameterView.D("start-angle", "start_angle", "0"), CliParameterView.D("end-angle", "end_angle", "360")));

            // ══ 事件（custom：params key=value 逗号串解析）══
            Add(Custom("bind-event", "bind_event", CliCustomHandler.BindEvent, "事件命令", "绑定事件动作 (--screen <name> --widget <name> --event <type> --action <type> [--params \"k1=v1,k2=v2\"])"));
            Add(Custom("add-event", "add_event", CliCustomHandler.AddEvent, "事件命令", "新增事件动作 (--screen <name> [--widget <name>] --event <type> --action <type> [--params \"k1=v1,k2=v2\"] [--condition <expr>])"));
            Add(Custom("remove-event", "remove_event", CliCustomHandler.RemoveEvent, "事件命令", "移除事件/动作 (--screen <name> [--widget <name>] --event <type> [--action <type>])"));
            Add(Custom("update-event", "update_event", CliCustomHandler.UpdateEvent, "事件命令", "更新事件动作参数 (--screen <name> [--widget <name>] --event <type> --action <type> [--params \"k1=v1,k2=v2\"] [--condition <expr>])"));

            // ══ 世界地图 ══
            // 注：lng_lat/bound_tag 用 provided-only（旧 OptMap 默认 "" 恒写 → P 语义）。等价性已逐行核对 handler：
            // ResolveGeo 用 TryGetValue && !IsNullOrWhiteSpace——"" 与 absent 同路（双缺/双""均报「lng-lat 与 bound-tag 二选一」）。
            // 未来若 handler 区分「显式空串=清空」与「未提供」，此处须同步改回带默认视图。
            Add(Meta("add-work-point", "add_work_point", "世界地图命令", "添加作业点 (--screen <name> --name <n> [--lng-lat <经纬度>] [--bound-tag <GPS变量>])",
                CliParameterView.R("screen", "screen_name"), CliParameterView.Rk("name"),
                CliParameterView.P("lng-lat", "lng_lat"), CliParameterView.P("bound-tag", "bound_tag")));
            Add(Meta("add-work-range-point", "add_work_range_point", "世界地图命令", "添加围栏顶点 (--screen <name> [--lng-lat <经纬度>] [--bound-tag <GPS变量>])",
                CliParameterView.R("screen", "screen_name"),
                CliParameterView.P("lng-lat", "lng_lat"), CliParameterView.P("bound-tag", "bound_tag")));
            Add(Meta("clear-work-range", "clear_work_range", "世界地图命令", "清除围栏 (--screen <name>)",
                CliParameterView.R("screen", "screen_name")));
            Add(Meta("delete-work-point", "delete_work_point", "世界地图命令", "删除作业点 (--screen <name> --name <n>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.Rk("name")));
            // 注：update_world_map 全参数 provided-only（旧 OptMap 默认 "" 恒写 → P 语义；handler 对 "" 与 absent 均 no-op——
            // UpdateWorldMapHandler 仅对非空 TryParse/赋值，空串不落模型），等价已证。
            Add(Meta("update-world-map", "update_world_map", "世界地图命令", "更新世界地图设置 (--screen <name> [--tile-source <offline|amap|自定义>] [--zoom-level <n>] [--show-global-overlay true|false] [--view-locked true|false])",
                CliParameterView.R("screen", "screen_name"),
                CliParameterView.P("tile-source", "tile_source"), CliParameterView.P("zoom-level", "zoom_level"),
                CliParameterView.P("show-global-overlay", "show_global_overlay"), CliParameterView.P("view-locked", "view_locked")));

            // ══ 变量/用户 ══
            Add(Meta("create-tag", "create_tag", "变量命令", "创建变量 (--name <name> --type <BOOL|INT16|UINT16|INT32|FLOAT|STRING|DATETIME|GPS> [--source <uri>] [--unit <u>] [--scan-interval 100] [--deadband 0] [--description <text>] [--base-value <n>] [--group <组名>]；source 缺省=内部变量)",
                CliParameterView.Rk("name"), CliParameterView.R("type", "data_type"), CliParameterView.Dk("source", ""), CliParameterView.Dk("unit", ""),
                CliParameterView.D("scan-interval", "scan_interval", "100"), CliParameterView.Dk("deadband", "0"), CliParameterView.Dk("description", ""),
                CliParameterView.D("base-value", "base_value", ""), CliParameterView.D("group", "group", "")));
            Add(Meta("update-tag", "update_tag", "变量命令", "更新变量 (--name <name> [--new-name <name>] [--type <...>] [--source <uri>] [--unit <u>] [--scan-interval <ms>] [--deadband <n>] [--description <text>] [--base-value <n>] [--group <组名>]；--source \"\" 清空为内部变量；--unit \"\" / --description \"\" / --group \"\" 清空)",
                CliParameterView.Rk("name"),
                CliParameterView.P("new-name", "new_name"), CliParameterView.P("type", "data_type"), CliParameterView.Pk("source"), CliParameterView.Pk("unit"),
                CliParameterView.P("scan-interval", "scan_interval"), CliParameterView.Pk("deadband"), CliParameterView.Pk("description"),
                CliParameterView.P("base-value", "base_value"), CliParameterView.P("group", "group")));
            Add(Meta("delete-tag", "delete_tag", "变量命令", "删除变量 (--name <name>；被控件/报警引用时拒绝)",
                CliParameterView.Rk("name")));
            Add(Meta("list-tags", "list_tags", "变量命令", "列出变量 ([--group <组名>]；空=全部；\"未分组\"=空分组变量——Y-5a)",
                CliParameterView.D("group", "group", "")));
            Add(Meta("list-tag-groups", "list_tag_groups", "变量命令", "列出变量分组名（组重命名/删除 = 批量 update-tag --group）——Y-5a"));
            Add(Meta("bind-tag", "bind_tag", "变量命令", "绑定变量到控件 (--screen <name> --widget <name> --tag <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name"), CliParameterView.R("tag", "tag_name")));
            Add(Meta("create-user", "create_user", "用户/组命令", "创建用户 (--user-name <name> --password <pwd> [--group-name 管理员|操作员|访客])",
                CliParameterView.R("user-name", "user_name"), CliParameterView.Rk("password"), CliParameterView.D("group-name", "group_name", "访客")));
            Add(Meta("update-user", "update_user", "用户/组命令", "更新用户 (--user-name <name> [--new-user-name <n>] [--new-password <p>] [--new-group-name <g>]；留空=不改)",
                CliParameterView.R("user-name", "user_name"),
                CliParameterView.D("new-user-name", "new_user_name", ""), CliParameterView.D("new-password", "new_password", ""), CliParameterView.D("new-group-name", "new_group_name", "")));
            Add(Meta("delete-user", "delete_user", "用户/组命令", "删除用户 (--user-name <name>；不能删除最后一个管理员)",
                CliParameterView.R("user-name", "user_name")));
            Add(Meta("list-users", "list_users", "用户/组命令", "列出用户"));
            Add(Meta("create-group", "create_group", "用户/组命令", "创建组 (--group-name <name> [--permissions <a,b,c>])",
                CliParameterView.R("group-name", "group_name"), CliParameterView.Pk("permissions")));
            Add(Meta("update-group", "update_group", "用户/组命令", "更新组 (--group-name <name> [--new-group-name <name>] [--permissions <a,b,c>])",
                CliParameterView.R("group-name", "group_name"), CliParameterView.P("new-group-name", "new_group_name"), CliParameterView.Pk("permissions")));
            Add(Meta("delete-group", "delete_group", "用户/组命令", "删除组 (--group-name <name>)",
                CliParameterView.R("group-name", "group_name")));

            // ══ 列表 ══
            Add(Meta("create-list", "create_list", "列表命令", "创建列表 (--name <name> --type <Text|Image|Video> [--items <a,b,c>])",
                CliParameterView.Rk("name"), CliParameterView.Rk("type"), CliParameterView.Dk("items", "")));
            Add(Meta("update-list", "update_list", "列表命令", "更新列表 (--name <name> [--type <type>] [--new-name <name>] [--items <a,b,c>]；--type 跨类型同名时定位)",
                CliParameterView.Rk("name"), CliParameterView.Pk("type"), CliParameterView.P("new-name", "new_name"), CliParameterView.Pk("items")));
            Add(Meta("delete-list", "delete_list", "列表命令", "删除列表 (--name <name> [--type <type>])",
                CliParameterView.Rk("name"), CliParameterView.Pk("type")));

            // ══ 剪贴板 ══
            Add(Meta("copy-widget", "copy_widget", "剪贴板命令", "复制控件 (--screen <name> --widget <name>)",
                CliParameterView.R("screen", "screen_name"), CliParameterView.R("widget", "widget_name")));
            // 注：paste_widget x/y 用 provided-only（旧 Opt 默认 "" 恒写 → P 语义；PasteWidgetHandler 对缺失或
            // TryParse 失败均落「原位置 +20 偏移」，等价已证）。
            Add(Meta("paste-widget", "paste_widget", "剪贴板命令", "粘贴控件 (--screen <name> [--x <n>] [--y <n>])",
                CliParameterView.R("screen", "screen_name"), CliParameterView.Pk("x"), CliParameterView.Pk("y")));

            // ══ 默认字体 ══
            Add(Meta("set-default-font", "set_default_font", "默认字体命令", "设置默认字体 ([--font-family <name>] [--font-size <n>] [--font-weight Normal|Bold] [--font-style Normal|Italic] [--text-decoration None|Underline])",
                CliParameterView.D("font-family", "font_family", ""), CliParameterView.D("font-size", "font_size", ""),
                CliParameterView.D("font-weight", "font_weight", ""), CliParameterView.D("font-style", "font_style", ""),
                CliParameterView.D("text-decoration", "text_decoration", "")));

            // ══ 报警 ══
            Add(Meta("create-alarm", "create_alarm", "报警命令", "创建报警 (--name <name> --tag <name> --type <High|Low|RateChange|Deviation> --threshold <n> [--deadband 0] [--delay <ms>] [--severity Warning] [--message <text>] [--trigger-mode Threshold] [--category User] [--priority 0] [--ack-required true] [--ack-group <组>] [--color-override #RRGGBB])",
                CliParameterView.Rk("name"), CliParameterView.R("tag", "tag_name"), CliParameterView.Rk("type"), CliParameterView.Rk("threshold"),
                CliParameterView.Dk("deadband", "0"), CliParameterView.D("delay", "delay_ms", "0"), CliParameterView.Dk("severity", "Warning"),
                CliParameterView.Dk("message", ""), CliParameterView.D("trigger-mode", "trigger_mode", "Threshold"), CliParameterView.Dk("category", "User"),
                CliParameterView.Dk("priority", "0"), CliParameterView.D("ack-required", "ack_required", "true"), CliParameterView.D("ack-group", "ack_group", ""),
                CliParameterView.D("color-override", "color_override", "")));
            Add(Meta("update-alarm", "update_alarm", "报警命令", "更新报警 (--name <name> [--new-name <name>] [--tag <name>] [--type <...>] [--threshold <n>] [--deadband <n>] [--delay <ms>] [--severity <s>] [--message <text>] [--trigger-mode <m>] [--category <c>] [--priority <n>] [--ack-required <b>] [--ack-group <g>] [--color-override <c>])",
                CliParameterView.Rk("name"),
                CliParameterView.P("new-name", "new_name"), CliParameterView.P("tag", "tag_name"), CliParameterView.Pk("type"), CliParameterView.Pk("threshold"),
                CliParameterView.Pk("deadband"), CliParameterView.P("delay", "delay_ms"), CliParameterView.Pk("severity"), CliParameterView.Pk("message"),
                CliParameterView.P("trigger-mode", "trigger_mode"), CliParameterView.Pk("category"), CliParameterView.Pk("priority"),
                CliParameterView.P("ack-required", "ack_required"), CliParameterView.P("ack-group", "ack_group"), CliParameterView.P("color-override", "color_override")));
            Add(Meta("delete-alarm", "delete_alarm", "报警命令", "删除报警 (--name <name>)",
                CliParameterView.Rk("name")));

            // ══ 设备 ══
            Add(Meta("configure-device", "configure_device", "设备命令", "配置设备通信 (--name <name> --protocol <ModbusRTU|ModbusTCP|MQTT> --connection <json>；MQTT JSON 全字段: broker/port/version(0=3.1.1,1=5.0)/clientId/username/password/keepAlive/enableTls/statusTag)",
                CliParameterView.Rk("name"), CliParameterView.Rk("protocol"), CliParameterView.R("connection", "connection_info")));
            Add(Meta("update-device", "update_device", "设备命令", "更新设备 (--name <name> [--new-name <name>] [--protocol <...>] [--connection <json>])",
                CliParameterView.Rk("name"),
                CliParameterView.P("new-name", "new_name"), CliParameterView.Pk("protocol"), CliParameterView.P("connection", "connection_info")));
            Add(Meta("delete-device", "delete_device", "设备命令", "删除设备 (--name <name>)",
                CliParameterView.Rk("name")));
            Add(Meta("list-devices", "list_devices", "设备命令", "列出全部设备 (对齐 GUI 通讯表格；每设备含 name/protocol/连接摘要——MQTT 掩码凭据不显示)",
                Array.Empty<CliParameterView>()));
            Add(Meta("connect", "connect", "设备命令", "连接设备 (--ip <addr> [--model NavigatorHMI-7] [--size-inch 7寸])",
                CliParameterView.Rk("ip"), CliParameterView.Dk("model", "NavigatorHMI-7"), CliParameterView.D("size-inch", "size_inch", "")));
            Add(Meta("disconnect", "disconnect", "设备命令", "断开设备连接"));
            Add(Meta("scan", "scan_devices", "设备命令", "扫描设备 ([--nic eth0])",
                CliParameterView.Dk("nic", "eth0")));
            Add(Meta("deploy-project", "deploy_project", "设备命令", "编译+打包+传输工程（deploy 前置编译门禁）(--ip <addr> [--file <path>])",
                CliParameterView.R("ip", "device_ip"), CliParameterView.D("file", "file_path", "")));
            Add(Meta("deploy-firmware", "deploy_firmware", "设备命令", "下载固件 OTA (--ip <addr> [--file <path>])",
                CliParameterView.R("ip", "device_ip"), CliParameterView.D("file", "file_path", "")));
            Add(Meta("blink-device", "blink_device", "设备命令", "设备闪烁定位 (--ip <addr> --enable <on|off>)",
                CliParameterView.Rk("ip"), CliParameterView.Rk("enable")));
            Add(Meta("vnc", "vnc", "设备命令", "VNC 运行时启停 (--ip <addr> --enable <on|off>)",
                CliParameterView.Rk("ip"), CliParameterView.Rk("enable")));

            // ══ MQTT 多连接（Y-3b + Z 循环 2026-09-11：连接 CRUD + 映射带 --connection-name；mqtt-set-device 废弃删除）══
            Add(Meta("mqtt-set-enabled", "mqtt_set_enabled", "MQTT 命令", "设置 MQTT 总开关 (--enabled <true|false>)",
                CliParameterView.Rk("enabled")));
            Add(Meta("mqtt-add-connection", "mqtt_add_connection", "MQTT 命令", "新增 MQTT 连接 (--name <连接名> [--broker <ip>] [--port 1883] [--version 0|1] [--client-id] [--username] [--password dpapi:密文] [--keep-alive-sec 60] [--status-tag <变量>])",
                CliParameterView.Rk("name"), CliParameterView.Pk("broker"), CliParameterView.P("port", "port"),
                CliParameterView.P("version", "version"), CliParameterView.P("client-id", "client_id"),
                CliParameterView.P("username", "username"), CliParameterView.P("password", "password"),
                CliParameterView.P("keep-alive-sec", "keep_alive_sec"), CliParameterView.P("status-tag", "status_tag")));
            Add(Meta("mqtt-update-connection", "mqtt_update_connection", "MQTT 命令", "更新连接参数 (--name <连接名> [--broker] [--port] [--version] [--client-id] [--username] [--password] [--keep-alive-sec] [--status-tag]；留空=不改)",
                CliParameterView.Rk("name"), CliParameterView.Pk("broker"), CliParameterView.P("port", "port"),
                CliParameterView.P("version", "version"), CliParameterView.P("client-id", "client_id"),
                CliParameterView.P("username", "username"), CliParameterView.P("password", "password"),
                CliParameterView.P("keep-alive-sec", "keep_alive_sec"), CliParameterView.P("status-tag", "status_tag")));
            Add(Meta("mqtt-delete-connection", "mqtt_delete_connection", "MQTT 命令", "删除 MQTT 连接 (--name <连接名>；连带其主题与绑定)",
                CliParameterView.Rk("name")));
            Add(Meta("mqtt-add-topic", "mqtt_add_topic", "MQTT 命令", "向连接新增主题配置 (--connection-name <连接名> --name <配置名> --topic <路径> [--direction publish|subscribe] [--qos 0] [--retain false] [--publish-interval-ms 0] [--json-template 0])",
                CliParameterView.R("connection-name", "connection_name"), CliParameterView.Rk("name"), CliParameterView.Rk("topic"), CliParameterView.D("direction", "direction", "publish"),
                CliParameterView.Dk("qos", "0"), CliParameterView.Dk("retain", "false"),
                CliParameterView.D("publish-interval-ms", "publish_interval_ms", "0"),
                CliParameterView.D("json-template", "json_template", "0")));
            Add(Meta("mqtt-update-topic", "mqtt_update_topic", "MQTT 命令", "更新连接内主题配置属性 (--connection-name <连接名> --name <配置名> [--qos] [--retain] [--publish-interval-ms] [--json-template])",
                CliParameterView.R("connection-name", "connection_name"), CliParameterView.Rk("name"), CliParameterView.Pk("qos"), CliParameterView.Pk("retain"),
                CliParameterView.P("publish-interval-ms", "publish_interval_ms"), CliParameterView.P("json-template", "json_template")));
            Add(Meta("mqtt-delete-topic", "mqtt_delete_topic", "MQTT 命令", "删除连接内主题配置 (--connection-name <连接名> --name <配置名>；连带删除其绑定)",
                CliParameterView.R("connection-name", "connection_name"), CliParameterView.Rk("name")));
            Add(Meta("mqtt-set-binding", "mqtt_set_binding", "MQTT 命令", "设置连接内变量↔字段绑定 (--connection-name <连接名> --topic-name <配置名> --tag-name <变量> --field-name <字段>)",
                CliParameterView.R("connection-name", "connection_name"), CliParameterView.R("topic-name", "topic_name"), CliParameterView.R("tag-name", "tag_name"), CliParameterView.R("field-name", "field_name")));
            Add(Meta("mqtt-remove-binding", "mqtt_remove_binding", "MQTT 命令", "移除连接内变量↔字段绑定 (--connection-name <连接名> --topic-name <配置名> --tag-name <变量>)",
                CliParameterView.R("connection-name", "connection_name"), CliParameterView.R("topic-name", "topic_name"), CliParameterView.R("tag-name", "tag_name")));

            // ══ AI（custom）══
            Add(new CliCommandSpec { CliName = "ai", Category = "AI 命令", Handler = CliCustomHandler.Ai, Summary = "<指令> 或 --prompt <指令> [--mode rule|cloud|local]（规则映射默认离线；cloud=DeepSeek API FC；local=本地模型 FC）",
                Views = new[] { CliParameterView.Pk("prompt"), CliParameterView.Dk("mode", "rule"), CliParameterView.Pk("model") } });

            return list;
        }

        /// <summary>元数据命令规格（IsCustom=false，走通用 Cmd 执行器）。</summary>
        private static CliCommandSpec Meta(string cli, string cmd, string category, string summary, params CliParameterView[] views)
            => new() { CliName = cli, CommandName = cmd, Category = category, Summary = summary, Views = views };

        /// <summary>自定义命令规格（Handler 非 None；Views 仅供帮助签名展示）。</summary>
        private static CliCommandSpec Custom(string cli, string cmd, CliCustomHandler handler, string category, string summary)
            => new() { CliName = cli, CommandName = cmd, Handler = handler, Category = category, Summary = summary };

        /// <summary>
        /// 与命令层 CommandDefinition 交叉校验（防 CLI 参数键漂移——负样本 gui-cli-param-map-missing）。
        /// 规则：① CommandName 非空的命令（custom 与元数据命令）其命令名必须存在于命令层注册
        ///          （bind_event/事件家族等 custom 命令命令层有同名 handler；create-project 等有直接构造逻辑也应同步注册）；
        ///       ② 元数据命令（非 custom）每个视图的 CmdKey 必须存在于对应 Definition.Parameters
        ///          （缺 = 命令层改名/删参未同步 CLI；custom 命令的 Views 仅供帮助展示，含 summary/json 等 CLI
        ///          私有参数，不进命令层——不校验）；
        ///       ③ 不做 Required/Default 强一致：CLI 更宽容是特性（如 add-widget --type 默认 button 而元数据 Required；
        ///          create-screen --width 空串走 handler 设备尺寸兜底而元数据 DefaultValue=800）。
        /// 返回错误清单（空 = 通过）。
        /// </summary>
        public static List<string> ValidateDefinitions(IEnumerable<CommandDefinition> definitions)
        {
            var errors = new List<string>();
            var byName = definitions.ToDictionary(d => d.Name);
            foreach (var spec in All)
            {
                if (string.IsNullOrEmpty(spec.CommandName)) continue;
                if (!byName.TryGetValue(spec.CommandName!, out var def))
                {
                    errors.Add($"[{spec.CliName}] 命令层无 {spec.CommandName}（命令层应同步注册；若为 CLI 专用命令请确认）");
                    continue;
                }
                if (spec.IsCustom) continue;   // custom 命令 Views 仅供帮助展示，不进命令层
                foreach (var v in spec.Views)
                {
                    if (v.CmdKey.Length == 0) continue;
                    if (!def.Parameters.ContainsKey(v.CmdKey))
                        errors.Add($"[{spec.CliName}] 参数 --{v.CliKey} → {v.CmdKey} 不在命令层 {spec.CommandName} 的参数集中（命令层现有: {string.Join(", ", def.Parameters.Keys)}）");
                }
            }
            return errors;
        }

        /// <summary>帮助文本（单源生成）。分组输出 + 每命令签名行；尾部附 set-property 属性键说明。</summary>
        public static string BuildHelpText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NavigatorHMI CLI — 组态软件命令行接口");
            sb.AppendLine();
            sb.AppendLine("用法: navihmi [--project <path>] [--json] <command> [--key value ...]");
            sb.AppendLine();
            sb.AppendLine("全局选项:");
            sb.AppendLine("  --project, -p <path>   工程文件路径 (默认查找当前目录 *.hmiproj)");
            sb.AppendLine("  --json                 以 JSON 格式输出结果");
            sb.AppendLine("  --help, -h             显示此帮助");
            sb.AppendLine();
            foreach (var group in All.GroupBy(c => c.Category))
            {
                sb.AppendLine($"{group.Key}:");
                foreach (var spec in group)
                {
                    var note = spec.Summary.Length > 0 ? $"  {spec.Summary}" : "";
                    sb.AppendLine($"  {spec.CliName}{note}");
                }
                sb.AppendLine();
            }
            sb.AppendLine(SetPropertyKeyHelp);
            return sb.ToString();
        }

        /// <summary>set-property 属性键说明（补充静态文本，随帮助尾部输出）。</summary>
        public const string SetPropertyKeyHelp = """
set-property 属性键 (--screen <画面> --widget <控件> --key <键> --value <值>):
  文本: text | content | title | onText | offText
  颜色: textColor | fillColor | strokeColor
  字体: fontFamily | fontSize | fontWeight | fontStyle | textDecoration
  数值: value | min | max | strokeThickness | x2 | y2
  其他: hAlign | imagePath | stretchMode | isOn | isChecked | isReadOnly | fillStyle

示例:
  navihmi create-project --name "产线监控" --path "./"
  navihmi --project ./产线监控.hmiproj create-screen --name "温度页"
  navihmi -p ./test.hmiproj add-widget --screen "温度页" --type button --x 100 --y 50
  navihmi compile
  navihmi --json create-screen --name "test" --type custom
""";
    }
}
