using System.IO;
using System.Text.Json;
using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NaviHmiCLI;

/// <summary>
/// NavigatorHMI CLI — 命令行接口，所有操作通过 CommandService 执行，与 GUI 走同一路径。
/// 用法: navihmi [--project path] [--json] command [--key value ...]
/// </summary>
public static class Program
{
    private static string? _projectPath;
    private static bool _jsonOutput;
    private static string _command = "";
    private static readonly List<string> _promptArgs = new();   // ai 命令的裸文本指令（无需 --prompt）
    private static readonly Dictionary<string, string> _opts = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>无值参数标记（--key 后无值）：仅记录键、不进 _opts（纯语义记录，无读取点——
    /// 有效性由"无值参数不写入 _opts → TryGetValue 天然失败"保证），避免哨兵字符串与合法值域碰撞。</summary>
    private static readonly HashSet<string> _flagOpts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// CLI 入口。解析全局选项和命令，路由到对应处理函数。
    /// </summary>
    /// <param name="args">命令行参数</param>
    /// <returns>0=成功, 1=失败</returns>
    public static int Main(string[] args)
    {
        // 重置静态状态（防止单元测试/托管场景下状态残留）
        _opts.Clear();
        _flagOpts.Clear();
        _projectPath = null;
        _command = "";
        _promptArgs.Clear();
        _jsonOutput = false;

        if (args.Length == 0) { ShowHelp(); return 0; }

        // ── 解析全局选项 + 命令 ──
        int i = 0;
        while (i < args.Length)
        {
            var a = args[i];
            if (a is "--project" or "-p" && i + 1 < args.Length)
                _projectPath = SanitizeParam("project", args[++i]);
            else if (a is "--json")
                _jsonOutput = true;
            else if (a is "--help" or "-h")
                { ShowHelp(); return 0; }
            else if (a.StartsWith("--"))
                { if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) _opts[a[2..]] = args[++i]; else _flagOpts.Add(a[2..]); }
            else if (!a.StartsWith("-"))
                { if (_command.Length == 0) _command = a; else _promptArgs.Add(a); }
            i++;
        }

        // ── 交互模式：无命令时进入 REPL ──
        if (string.IsNullOrEmpty(_command) && !string.IsNullOrEmpty(_projectPath))
            return RunRepl();

        if (string.IsNullOrEmpty(_command)) { PrintError("未指定命令"); return 1; }

        // ── 路由命令 ──
        return RouteCommand();
    }

    private static int RouteCommand() => _command switch
        {
            // 工程
            "create-project" => CreateProject(),
            "open-project"   => OpenProject(),
            "save-project"   => SaveProject(),
            "compile"        => Compile(),

            // 画面
            "create-screen"  => Cmd("create_screen", Require("name"), Opt("type", "custom"), Opt("width", ""), Opt("height", "")),
            "delete-screen"  => Cmd("delete_screen", Require("name")),
            "rename-screen"  => Cmd("rename_screen", Require("name"), Require("new-name", "new_name")),
            "current-screen" => Cmd("current_screen"),
            "copy-screen"    => Cmd("copy_screen", Require("name")),
            "paste-screen"   => Cmd("paste_screen", Opt("name", "")),

            // 控件
            "add-widget"     => Cmd("add_widget", Require("screen", "screen_name"), OptMap("type", "widget_type", "button"), Opt("x", "100"), Opt("y", "100"), Opt("width", "100"), Opt("height", "40"), Opt("center", "false"), OptMap("bound-tag", "bound_tag", ""), OptMap("window-type", "window_type", "userview")),
            "move-widget"    => Cmd("move_widget", Require("screen", "screen_name"), Require("widget", "widget_name"), Require("x"), Require("y")),
            "resize-widget"  => Cmd("resize_widget", Require("screen", "screen_name"), Require("widget", "widget_name"), Require("width"), Require("height")),
            "delete-widget"  => Cmd("delete_widget", Require("screen", "screen_name"), Require("widget", "widget_name")),
            "set-property"   => Cmd("set_property", Require("screen", "screen_name"), Require("widget", "widget_name"), Require("key"), Require("value")),

            // 层级
            "bring-to-front" => Cmd("bring_to_front", Require("screen", "screen_name"), Require("widget", "widget_name")),
            "bring-forward"  => Cmd("bring_forward", Require("screen", "screen_name"), Require("widget", "widget_name")),
            "send-backward"  => Cmd("send_backward", Require("screen", "screen_name"), Require("widget", "widget_name")),
            "send-to-back"   => Cmd("send_to_back", Require("screen", "screen_name"), Require("widget", "widget_name")),

            // 事件
            "bind-event"     => BindEvent(),
            "add-event"      => AddEvent(),
            "remove-event"   => RemoveEvent(),
            "update-event"   => UpdateEvent(),

            // 布局
            "align"          => Cmd("align_widgets", Require("screen", "screen_name"), Require("widgets"), Require("direction")),
            "array"          => Cmd("array_layout", Require("screen", "screen_name"), Require("widgets"), Require("mode"),
                                    OptMap("start-x", "start_x", "0"), OptMap("start-y", "start_y", "0"), OptIfProvided("cols"), OptIfProvided("rows"),
                                    OptMap("spacing-x", "spacing_x", "120"), OptMap("spacing-y", "spacing_y", "80"),
                                    OptMap("center-x", "center_x", "0"), OptMap("center-y", "center_y", "0"),
                                    Opt("radius", "150"), OptMap("start-angle", "start_angle", "0"), OptMap("end-angle", "end_angle", "360")),

            // 变量
            "create-tag"     => Cmd("create_tag", Require("name"), Require("type", "data_type"), Opt("source", ""), Opt("unit"), OptMap("scan-interval", "scan_interval", "100"), Opt("deadband", "0"), Opt("description"), OptMap("base-value", "base_value", "")),
            "update-tag"     => Cmd("update_tag", Require("name"),
                                    OptIfProvided("new-name", "new_name"), OptIfProvided("type", "data_type"), OptIfProvided("source"), OptIfProvided("unit"),
                                    OptIfProvided("scan-interval", "scan_interval"), OptIfProvided("deadband"), OptIfProvided("description"), OptIfProvided("base-value", "base_value")),
            "delete-tag"     => Cmd("delete_tag", Require("name")),
            "create-user"    => Cmd("create_user", Require("user-name", "user_name"), Require("password"), OptMap("group-name", "group_name", "访客")),
            "update-user"    => Cmd("update_user", Require("user-name", "user_name"), OptMap("new-user-name", "new_user_name", ""), OptMap("new-password", "new_password", ""), OptMap("new-group-name", "new_group_name", "")),
            "delete-user"    => Cmd("delete_user", Require("user-name", "user_name")),
            "list-users"     => Cmd("list_users"),
            "create-group"   => Cmd("create_group", Require("group-name", "group_name"), OptIfProvided("permissions")),
            "update-group"   => Cmd("update_group", Require("group-name", "group_name"), Opt("new-group-name", "new_group_name"), OptIfProvided("permissions")),
            "delete-group"   => Cmd("delete_group", Require("group-name", "group_name")),            "bind-tag"       => Cmd("bind_tag", Require("screen", "screen_name"), Require("widget", "widget_name"), Require("tag", "tag_name")),

            // 列表
            "create-list"    => Cmd("create_list", Require("name"), Require("type"), Opt("items", "")),
            "update-list"    => Cmd("update_list", Require("name"), OptIfProvided("new-name", "new_name"), OptIfProvided("items")),
            "delete-list"    => Cmd("delete_list", Require("name")),

            // 剪贴板
            "copy-widget"    => Cmd("copy_widget", Require("screen", "screen_name"), Require("widget", "widget_name")),
            "paste-widget"   => Cmd("paste_widget", Require("screen", "screen_name"), Opt("x", ""), Opt("y", "")),

            // 默认字体
            "set-default-font" => Cmd("set_default_font",
                                    OptMap("font-family", "font_family", ""), OptMap("font-size", "font_size", ""),
                                    OptMap("font-weight", "font_weight", ""), OptMap("font-style", "font_style", ""),
                                    OptMap("text-decoration", "text_decoration", "")),

            // 报警
            "create-alarm"   => Cmd("create_alarm", Require("name"), Require("tag", "tag_name"), Require("type"), Require("threshold"), Opt("deadband", "0"), OptMap("delay", "delay_ms", "0"), Opt("severity", "Warning"), Opt("message"), OptMap("trigger-mode", "trigger_mode", "Threshold"), OptMap("category", "category", "User"), Opt("priority", "0"), OptMap("ack-required", "ack_required", "true"), OptMap("ack-group", "ack_group", ""), OptMap("color-override", "color_override", "")),
            "update-alarm"   => Cmd("update_alarm", Require("name"), OptIfProvided("new-name", "new_name"), OptIfProvided("tag", "tag_name"), OptIfProvided("type"), OptIfProvided("threshold"), OptIfProvided("deadband"), OptIfProvided("delay", "delay_ms"), OptIfProvided("severity"), OptIfProvided("message"), OptIfProvided("trigger-mode", "trigger_mode"), OptIfProvided("category", "category"), OptIfProvided("priority"), OptIfProvided("ack-required", "ack_required"), OptIfProvided("ack-group", "ack_group"), OptIfProvided("color-override", "color_override")),
            "delete-alarm"   => Cmd("delete_alarm", Require("name")),


            // 设备
            "configure-device" => Cmd("configure_device", Require("name"), Require("protocol"), Require("connection", "connection_info")),
            "update-device"   => Cmd("update_device", Require("name"), OptIfProvided("new-name", "new_name"), OptIfProvided("protocol"), OptIfProvided("connection", "connection_info")),
            "delete-device"   => Cmd("delete_device", Require("name")),
            "connect"         => Cmd("connect", Require("ip"), Opt("model", "NavigatorHMI")),
            "scan"            => Cmd("scan_devices", Opt("nic", "eth0")),
            "deploy-project"  => Cmd("deploy_project", Require("ip", "device_ip"), OptMap("file", "file_path", "")),
            "deploy-firmware" => Cmd("deploy_firmware", Require("ip", "device_ip"), OptMap("file", "file_path", "")),

            // AI Agent（本地 LLM Function Calling）
            "ai"              => AiCommand(),

            _ => UnknownCommand()
        };

    // ═══════════════════════════════════════════
    // 工程命令（直接操作，不走 CommandService）
    // ═══════════════════════════════════════════

    private static int CreateProject()
    {
        var name = RequireVal("name");
        var path = OptVal("path", ".");
        var width = OptVal("width", "800");
        var height = OptVal("height", "480");

        var project = new HMIProject { ProjectFilePath = Path.Combine(path, $"{name}.hmiproj") };
        var service = new CommandService(project);
        return Execute(service, "create_project", new()
        {
            ["name"] = name, ["path"] = path, ["width"] = width, ["height"] = height
        });
    }

    private static int OpenProject()
    {
        var path = RequireVal("path");
        // open_project 需要替换 CommandService 持有的工程引用
        var project = new HMIProject();
        var service = new CommandService(project);
        var result = service.Execute("open_project", new() { ["path"] = path });
        if (!result.Success) { PrintError($"[{result.ErrorCode}] {result.ErrorMessage}"); return 1; }
        var loaded = ProjectFileService.Load(path);
        service.ReplaceProject(loaded);
        Ok(new { name = loaded.Name, screens = loaded.Screens.Count, version = loaded.Version });
        return 0;
    }

    private static int SaveProject()
    {
        var (service, _) = LoadProject();
        return Execute(service, "save_project", new() { ["path"] = OptVal("path", "") });
    }

    private static int Compile()
    {
        var (service, _) = LoadProject();
        return Execute(service, "compile", new());
    }

    // ═══════════════════════════════════════════
    // 通用命令执行（映射 CLI 参数 → CommandService）
    // ═══════════════════════════════════════════

    private static int Cmd(string commandName, params ParamDef[] defs)
    {
        var (service, project) = LoadProject();
        var parameters = new Dictionary<string, object?>();
        foreach (var d in defs)
        {
            if (d.Required)
            {
                parameters[d.CmdKey] = RequireVal(d.CliKey);
            }
            else if (d.Default != null)
            {
                // 有默认值：未提供时写入默认值（与 handler 的 GetValueOrDefault 兜底一致）
                parameters[d.CmdKey] = OptVal(d.CliKey, d.Default);
            }
            else if (_opts.ContainsKey(d.CliKey))
            {
                // 无默认值（OptIfProvided）：显式提供才写入（含空串 = 清空语义，如 update-tag --source "" 改回内部变量）；
                // 无值参数（--key 后无值）只记入 _flagOpts 不进 _opts → ContainsKey 失败 → handler 保留模型现值
                parameters[d.CmdKey] = OptVal(d.CliKey);
            }
        }
        var exitCode = Execute(service, commandName, parameters);
        if (exitCode == 0) AutoSave(project);
        return exitCode;
    }

    private static int BindEvent()
    {
        var (service, project) = LoadProject();
        var raw = OptVal("params", "");
        var paramDict = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(raw))
            foreach (var kv in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=', 2);
                if (parts.Length == 2) paramDict[parts[0].Trim()] = parts[1].Trim();
            }
        var exitCode = Execute(service, "bind_event", new()
        {
            ["screen_name"] = RequireVal("screen"),
            ["widget_name"] = RequireVal("widget"),
            ["event"] = RequireVal("event"),
            ["action"] = RequireVal("action"),
            ["params"] = paramDict,
        });
        if (exitCode == 0) AutoSave(project);
        return exitCode;
    }

    /// <summary>add_event：为控件事件追加动作（params 逗号分隔 key=value；同事件累积）。</summary>
    private static int AddEvent()
    {
        var (service, project) = LoadProject();
        var raw = OptVal("params", "");
        var paramDict = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(raw))
            foreach (var kv in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=', 2);
                if (parts.Length == 2) paramDict[parts[0].Trim()] = parts[1].Trim();
            }
        var exitCode = Execute(service, "add_event", new()
        {
            ["screen_name"] = RequireVal("screen"),
            ["widget_name"] = RequireVal("widget"),
            ["event_type"] = RequireVal("event"),
            ["action_type"] = RequireVal("action"),
            ["params"] = paramDict,
        });
        if (exitCode == 0) AutoSave(project);
        return exitCode;
    }

    /// <summary>remove_event：移除控件事件（--action 指定仅移除该动作；否则整个事件）。</summary>
    private static int RemoveEvent()
    {
        var (service, project) = LoadProject();
        var exitCode = Execute(service, "remove_event", new()
        {
            ["screen_name"] = RequireVal("screen"),
            ["widget_name"] = RequireVal("widget"),
            ["event_type"] = RequireVal("event"),
            ["action_type"] = OptVal("action", ""),
        });
        if (exitCode == 0) AutoSave(project);
        return exitCode;
    }

    /// <summary>update_event：更新事件下动作的参数（params 完整替换）。</summary>
    private static int UpdateEvent()
    {
        var (service, project) = LoadProject();
        var raw = OptVal("params", "");
        var paramDict = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(raw))
            foreach (var kv in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=', 2);
                if (parts.Length == 2) paramDict[parts[0].Trim()] = parts[1].Trim();
            }
        var exitCode = Execute(service, "update_event", new()
        {
            ["screen_name"] = RequireVal("screen"),
            ["widget_name"] = RequireVal("widget"),
            ["event_type"] = RequireVal("event"),
            ["action_type"] = RequireVal("action"),
            ["params"] = paramDict,
        });
        if (exitCode == 0) AutoSave(project);
        return exitCode;
    }

    /// <summary>AI Agent：规则映射（默认，离线）或 LLM Function Calling（cloud=DeepSeek / local=本地 Qwen）。</summary>
    private static int AiCommand()
    {
        var prompt = _opts.ContainsKey("prompt") ? RequireVal("prompt") : string.Join(" ", _promptArgs);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            PrintError("缺少指令：navihmi ai \"创建画面 温度监控\" 或 --prompt <指令>");
            return 1;
        }
        var (service, project) = LoadProject();
        var mode = OptVal("mode", "rule").ToLowerInvariant();
        string result;
        try
        {
            switch (mode)
            {
                case "cloud":
                case "local":
                {
                    using NavigatorHMI.AiAgent.ILLMBackend backend = mode == "local"
                        ? CreateLocalBackend()
                        : new NavigatorHMI.AiAgent.CloudLLMBackend();
                    if (!backend.IsAvailable)
                    {
                        PrintError(mode == "local"
                            ? "本地模型不可用：请设置 NAVIGATOR_HMI_MODEL 或放置模型到 models/ 目录"
                            : "云端 API 未配置：请设置环境变量 DEEPSEEK_API（DeepSeek key）");
                        return 1;
                    }
                    var agent = new NavigatorHMI.AiAgent.AIAgent(service, backend);
                    result = agent.ChatAsync(prompt).GetAwaiter().GetResult();
                    break;
                }
                default:   // rule：规则关键词映射（离线、确定性）
                    result = new NavigatorHMI.AiAgent.RuleAgent(service).Process(prompt);
                    break;
            }
        }
        catch (Exception ex)
        {
            PrintError($"AI 执行失败: {ex.Message}");
            return 1;
        }
        Console.WriteLine($"[AI] {prompt}");
        Console.WriteLine(result);
        AutoSave(project);
        return 0;
    }

    private static NavigatorHMI.AiAgent.LocalLLMBackend CreateLocalBackend()
    {
        var modelPath = OptVal("model", NavigatorHMI.AiAgent.LocalLLMBackend.DefaultModelPath);
        var backend = new NavigatorHMI.AiAgent.LocalLLMBackend(modelPath);
        if (!backend.IsAvailable)
            throw new InvalidOperationException($"模型文件不存在: {modelPath}\n请先下载 Qwen2.5-7B-Instruct GGUF（q4_k_m，约 4.4GB）");
        if (!backend.Load())
            throw new InvalidOperationException($"模型加载失败: {backend.LoadError}");
        return backend;
    }

    private static int Execute(CommandService service, string name, Dictionary<string, object?> p)
    {
        var result = service.Execute(name, p);
        if (_jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success = result.Success, data = result.Data,
                error = result.ErrorCode != null ? new { code = result.ErrorCode, message = result.ErrorMessage } : null },
                _jsonOpts));
        }
        else if (result.Success)
        {
            var dataStr = result.Data != null ? JsonSerializer.Serialize(result.Data, _jsonOpts) : "";
            Console.WriteLine($"✓ {name}{(string.IsNullOrEmpty(dataStr) ? "" : $" — {dataStr}")}");
        }
        else
        {
            Console.Error.WriteLine($"✗ [{result.ErrorCode}] {result.ErrorMessage}");
        }
        return result.Success ? 0 : 1;
    }

    // ═══════════════════════════════════════════
    // 帮助方法
    // ═══════════════════════════════════════════

    private static int UnknownCommand()
    {
        PrintError($"未知命令: {_command}\n运行 navihmi --help 查看可用命令");
        return 1;
    }

    private static void Ok(object? data = null)
    {
        if (_jsonOutput)
            Console.WriteLine(JsonSerializer.Serialize(new { success = true, data }, _jsonOpts));
        else if (data != null)
            Console.WriteLine($"✓ {JsonSerializer.Serialize(data, _jsonOpts)}");
        else
            Console.WriteLine("✓ 成功");
    }

    private static void PrintError(string msg)
    {
        if (_jsonOutput)
            Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = new { code = "CLI_ERROR", message = msg } }));
        else
            Console.Error.WriteLine($"✗ {msg}");
    }

    private static string RequireVal(string key)
    {
        if (_opts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            return SanitizeParam(key, v);
        PrintError($"缺少必填参数: --{key}");
        Environment.Exit(1);
        throw new InvalidOperationException("unreachable");
    }

    private static string OptVal(string key, string defaultValue = "")
    {
        var v = _opts.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw) ? raw : defaultValue;
        return SanitizeParam(key, v);
    }

    /// <summary>
    /// 参数安全净化。三级分类:
    /// - 路径类(允许 / \): 只拦 .. 和绝对路径
    /// - 标识符类: 拦 .. / \
    /// - 自由文本类(描述/消息等): 只拦 ..
    /// </summary>
    private static string SanitizeParam(string key, string value)
    {
        bool hasUpDir = value.Contains("..");
        bool hasSeparator = value.Contains('/') || value.Contains('\\');
        bool isPathParam = key is "path" or "project" or "file" or "output" or "connection" or "source";
        bool isNameParam = key is "name" or "screen" or "widget" or "widgets" or "tag" or "key" or "new-name"
            or "user-name" or "new-user-name" or "new-group-name"
            or "event" or "action" or "nic" or "protocol" or "severity" or "direction" or "mode" or "ip" or "device_ip";
        // value 语义由 --key 决定（颜色/文本/路径/数值），静态分类无法覆盖：
        // 归自由文本类仅拦 '..'（imagePath 值含 / 或 \ 是合法的相对/绝对路径）
        // items：| 分隔的预设值集合（图片列表含路径），归自由文本仅拦 '..' 防路径遍历穿透工程
        bool isFreeText = key is "description" or "message" or "params" or "model" or "value" or "font-family" or "base-value" or "items";

        if (isPathParam)
        {
            if (hasUpDir)
                { PrintError($"参数 --{key} 包含非法字符 '..' : {value}"); Environment.Exit(1); }
            // connection 接受 JSON 格式，允许绝对路径（如 /dev/ttyUSB0 应包在 JSON 内）
            if (Path.IsPathRooted(value) && key is not "connection")
                { PrintError($"参数 --{key} 不允许绝对路径: {value}"); Environment.Exit(1); }
        }
        else if (isNameParam && (hasUpDir || hasSeparator))
        {
            PrintError($"参数 --{key} 包含非法字符: {value}"); Environment.Exit(1);
        }
        else if (isFreeText && hasUpDir)
        {
            PrintError($"参数 --{key} 包含非法字符 '..' : {value}"); Environment.Exit(1);
        }
        return value;
    }

    private record ParamDef(string CliKey, string CmdKey, bool Required, string? Default)
    {
        public static ParamDef Require(string key, string? cmdKey = null) => new(key, cmdKey ?? key, true, null);
        public static ParamDef Opt(string key, string defaultValue, string? cmdKey = null) => new(key, cmdKey ?? key, false, defaultValue);
        /// <summary>可选参数（无默认值）：未提供时不进参数字典，handler 保留模型现值。</summary>
        public static ParamDef OptIfProvided(string key, string? cmdKey = null) => new(key, cmdKey ?? key, false, null);
    }

    private static ParamDef Require(string cliKey, string? cmdKey = null) => ParamDef.Require(cliKey, cmdKey);
    private static ParamDef Opt(string cliKey, string defaultValue = "") => ParamDef.Opt(cliKey, defaultValue);
    private static ParamDef OptMap(string cliKey, string cmdKey, string defaultValue) => ParamDef.Opt(cliKey, defaultValue, cmdKey);
    private static ParamDef OptIfProvided(string cliKey, string? cmdKey = null) => ParamDef.OptIfProvided(cliKey, cmdKey);

    /// <summary>
    /// 加载工程文件。优先级: --project 参数 > 当前目录 *.hmiproj。
    /// </summary>
    private static (CommandService service, HMIProject project) LoadProject()
    {
        string path;
        if (!string.IsNullOrEmpty(_projectPath))
        {
            path = _projectPath;
        }
        else
        {
            var files = Directory.GetFiles(Environment.CurrentDirectory, "*.hmiproj");
            if (files.Length == 0) { PrintError("未找到 .hmiproj 文件，请用 --project 指定路径"); Environment.Exit(1); }
            if (files.Length > 1) Console.Error.WriteLine($"⚠ 发现 {files.Length} 个 .hmiproj 文件，使用: {files[0]}");
            path = files[0];
        }

        try
        {
            var project = ProjectFileService.Load(path);
            return (new CommandService(project), project);
        }
        catch (Exception ex) { PrintError($"加载工程失败: {ex.Message}"); Environment.Exit(1); throw; }
    }

    /// <summary>
    /// 修改命令执行成功后自动持久化。失败时输出警告但不阻断流程。
    /// </summary>
    private static void AutoSave(HMIProject project)
    {
        if (string.IsNullOrEmpty(project.ProjectFilePath)) return;
        try
        {
            ProjectFileService.Save(project, project.ProjectFilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠ 自动保存失败: {ex.Message}（内存数据已修改，请手动 save-project）");
        }
    }

    /// <summary>
    /// 交互模式 REPL。读取用户输入 → 解析 → 执行 → 循环。
    /// exit/quit 退出，help 显示命令列表。
    /// </summary>
    private static int RunRepl()
    {
        Console.WriteLine($"NavigatorHMI REPL — 已加载: {_projectPath}");
        Console.WriteLine("输入命令 (exit 退出, help 显示帮助):");

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null) break;  // Ctrl+D
            line = line.Trim();
            if (line is "exit" or "quit" or "q") break;
            if (line is "help" or "?") { ShowReplHelp(); continue; }
            if (string.IsNullOrEmpty(line)) continue;

            // 每轮重置
            _opts.Clear();
            _flagOpts.Clear();
            _command = "";
            _jsonOutput = false;
            var parts = ParseLine(line);
            int i = 0;
            while (i < parts.Length)
            {
                var a = parts[i];
                if (a is "--json")
                    _jsonOutput = true;
                else if (a.StartsWith("--"))
                    { if (i + 1 < parts.Length && !parts[i + 1].StartsWith("--")) _opts[a[2..]] = parts[++i]; else _flagOpts.Add(a[2..]); }
                else if (!a.StartsWith("-"))
                    { _command = a; }
                i++;
            }

            if (string.IsNullOrEmpty(_command)) { Console.WriteLine("请输入命令 (help 查看可用命令)"); continue; }

            // 路由执行
            var exitCode = RouteCommand();
        }

        Console.WriteLine("再见。");
        return 0;
    }

    /// <summary>
    /// 解析一行输入为参数数组，支持引号包裹含空格的参数。
    /// </summary>
    private static string[] ParseLine(string line)
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

    private static void ShowReplHelp()
    {
        Console.WriteLine("""
可用命令:
  工程: create-project, open-project, save-project, compile
  画面: create-screen, delete-screen, rename-screen, copy-screen, paste-screen, current-screen（无参=显示当前画面）
  控件: add-widget, move-widget, resize-widget, delete-widget, set-property
  剪贴板: copy-widget, paste-widget
  默认字体: set-default-font
  层级: bring-to-front, bring-forward, send-backward, send-to-back
  布局: align, array
  事件: bind-event
  变量: create-tag, update-tag, delete-tag, bind-tag
  用户: create-user, update-user, delete-user, list-users
报警: create-alarm, update-alarm, delete-alarm
  设备: configure-device, update-device, delete-device, connect, scan, deploy-project, deploy-firmware

set-property 属性键 (--screen <画面> --widget <控件> --key <键> --value <值>):
  文本: text | content | title | onText | offText
  颜色: textColor | fillColor | strokeColor
  字体: fontFamily | fontSize | fontWeight | fontStyle | textDecoration
  数值: value | min | max | strokeThickness | x2 | y2
  其他: hAlign | imagePath | stretchMode | isOn | isChecked | isReadOnly | fillStyle

参数格式: --key value  或  --key "value with spaces"
退出: exit / quit / q
""");
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
NavigatorHMI CLI — 组态软件命令行接口

用法: navihmi [--project <path>] [--json] <command> [--key value ...]

全局选项:
  --project, -p <path>   工程文件路径 (默认查找当前目录 *.hmiproj)
  --json                 以 JSON 格式输出结果
  --help, -h             显示此帮助

工程命令:
  create-project         --name <name> [--path <dir>] [--width 800] [--height 480]
  open-project           --path <file>
  save-project           [--path <file>]
  compile                [--output <path>]

画面命令:
  create-screen          --name <name> [--type custom] [--width 800] [--height 480]
  delete-screen          --name <name>
  rename-screen          --name <name> --new-name <name>
  copy-screen            --name <name>
  paste-screen           [--name <name>]

控件命令:
  add-widget             --screen <name> --type button --x <n> --y <n> [--width <n>] [--height <n>] [--window-type userview|alarmview|robotlist]
  move-widget            --screen <name> --widget <name> --x <n> --y <n>
  resize-widget          --screen <name> --widget <name> --width <n> --height <n>
  delete-widget          --screen <name> --widget <name>
  set-property           --screen <name> --widget <name> --key <key> --value <val>

set-property 属性键 (--screen <画面> --widget <控件> --key <键> --value <值>):
  文本: text | content | title | onText | offText
  颜色: textColor | fillColor | strokeColor
  字体: fontFamily | fontSize | fontWeight | fontStyle | textDecoration
  数值: value | min | max | strokeThickness | x2 | y2
  其他: hAlign | imagePath | stretchMode | isOn | isChecked | isReadOnly | fillStyle

层级命令:
  bring-to-front         --screen <name> --widget <name>
  bring-forward          --screen <name> --widget <name>
  send-backward          --screen <name> --widget <name>
  send-to-back           --screen <name> --widget <name>

布局命令:
  align                  --screen <name> --widgets <a,b,c> --direction <left|center_h|right|top|center_v|bottom>
  array                  --screen <name> --widgets <a,b,c> --mode <rect|circle>
                         [--start-x <n>] [--start-y <n>] [--cols <n>] [--rows <n>] [--spacing-x <n>] [--spacing-y <n>]
                         [--center-x <n>] [--center-y <n>] [--radius <n>] [--start-angle <deg>] [--end-angle <deg>]
                         （--cols/--rows 省略时按控件数自动计算：4 个 → 2×2）

事件命令:
  bind-event             --screen <name> --widget <name> --event <type> --action <type> [--params "k1=v1,k2=v2"]

变量命令:
  create-tag             --name <name> --type <BOOL|INT16|FLOAT|...> [--source <uri>] [--unit <u>] [--scan-interval <ms>] [--base-value <n>]   # source 缺省 = 内部变量；base-value = 设计态基准值
create-user            --user-name <name> --password <pwd> [--group-name 管理员|操作员|访客]
update-user            --user-name <name> [--new-user-name <n>] [--new-password <p>] [--new-group-name <g>]   # 留空=不改
delete-user            --user-name <name>   # 不能删除最后一个管理员
list-users
  update-tag             --name <name> [--new-name <name>] [--type <...>] [--source <uri>] [--unit <u>] [--scan-interval <ms>] [--deadband <n>] [--description <text>] [--base-value <n>]  重命名自动同步控件/报警引用；--source "" 清空为内部变量；--unit "" / --description "" 清空
  delete-tag             --name <name>   被控件/报警引用时拒绝
  bind-tag               --screen <name> --widget <name> --tag <name>

剪贴板命令:
  copy-widget            --screen <name> --widget <name>
  paste-widget           --screen <name> [--x <n>] [--y <n>]

默认字体命令:
  set-default-font       [--font-family <name>] [--font-size <n>] [--font-weight Normal|Bold] [--font-style Normal|Italic] [--text-decoration None|Underline]

报警命令:
  create-alarm           --name <name> --tag <name> --type <High|Low|...> --threshold <n> [--severity Warning] [--trigger-mode Threshold|OnRising|...] [--category System|User|Error] [--priority <n>] [--ack-group <组>] [--ack-required true|false] [--color-override #RRGGBB]
  update-alarm           --name <name> [--new-name <name>] [--tag <name>] [--type <...>] [--threshold <n>] [--deadband <n>] [--delay <ms>] [--severity <...>] [--message <text>] [--trigger-mode <m>] [--category <c>] [--priority <n>] [--ack-required <b>] [--ack-group <g>] [--color-override <c>]
  delete-alarm           --name <name>

设备命令:
  configure-device       --name <name> --protocol <ModbusRTU|ModbusTCP|MQTT> --connection <json>
  update-device          --name <name> [--new-name <name>] [--protocol <...>] [--connection <json>]
  delete-device          --name <name>
  connect                --ip <addr> [--model NavigatorHMI]
  scan                   [--nic eth0]
  deploy-project         --ip <addr> [--file <path>]
  deploy-firmware        --ip <addr> [--file <path>]

AI 命令:
  ai                     <指令> 或 --prompt <指令> [--mode rule|cloud|local]  规则映射默认（离线）；cloud=DeepSeek API FC；local=本地模型 FC

示例:
  navihmi create-project --name "产线监控" --path "./"
  navihmi --project ./产线监控.hmiproj create-screen --name "温度页"
  navihmi -p ./test.hmiproj add-widget --screen "温度页" --type button --x 100 --y 50
  navihmi compile
  navihmi --json create-screen --name "test" --type custom
""");
    }
}
