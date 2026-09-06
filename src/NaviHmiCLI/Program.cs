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

    /// <summary>
    /// 路由命令：查 CliCatalog 声明式路由表（单一事实源，命令层 Definition 交叉校验见 CliCatalog.ValidateDefinitions）。
    /// 元数据命令（Handler=None）走通用 MetaCmd（按目录参数视图构建参数字典）；custom 命令按 Handler 分派专用执行器
    /// （工程命令有特殊加载/构造逻辑、事件家族有 params key=value 串解析、ai 有 LLM/规则后端）。
    /// </summary>
    private static int RouteCommand()
    {
        var spec = CliCatalog.Find(_command);
        if (spec == null) return UnknownCommand();
        return spec.Handler switch
        {
            CliCustomHandler.CreateProject => CreateProject(),
            CliCustomHandler.OpenProject   => OpenProject(),
            CliCustomHandler.SaveProject   => SaveProject(),
            CliCustomHandler.Compile       => Compile(),
            CliCustomHandler.BindEvent     => BindEvent(),
            CliCustomHandler.AddEvent      => AddEvent(),
            CliCustomHandler.RemoveEvent   => RemoveEvent(),
            CliCustomHandler.UpdateEvent   => UpdateEvent(),
            CliCustomHandler.Ai            => AiCommand(),
            _ => MetaCmd(spec),
        };
    }

    // ═══════════════════════════════════════════
    // 工程命令（custom：CLI 有特殊装配——经 CommandService.Execute 执行 + 新工程/替换工程引用）
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

    /// <summary>
    /// 编译工程。compile handler 输出变化摘要（V-6：自上次编译以来未编译修改的域/画面）。
    /// 文本模式默认打 data（含 output_path/changed_domains/changed_screens）；--summary 打易读变化摘要行；--json 打机器可读完整输出。
    /// </summary>
    private static int Compile()
    {
        var (service, _) = LoadProject();
        var result = service.Execute("compile", new());
        if (!result.Success)
        {
            PrintError($"[{result.ErrorCode}] {result.ErrorMessage}");
            return 1;
        }
        if (_jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success = true, data = result.Data }, _jsonOpts));
            return 0;
        }
        var dataJson = result.Data != null ? JsonSerializer.Serialize(result.Data, _jsonOpts) : "{}";
        // --summary 为无值 flag：Main 解析归 _flagOpts（不进 _opts，防哨兵值碰撞）——两处都查。
        // 注：变化跟踪挂在 HMIProject 实例（进程内存态）——CLI 单命令与 REPL 每命令都经 LoadProject
        // 从磁盘重建工程（新空 ChangeTracker）→ 摘要恒空；唯一有值场景 = GUI 单一 CommandService
        // 会话内连续修改后编译（V-6 骨架的信息展示定位；TodoQueue 式跨进程脏持久化留 V+1）。
        var summaryWanted = _opts.ContainsKey("summary") || _flagOpts.Contains("summary");
        if (summaryWanted && result.Data != null)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(dataJson);
            var root = doc.RootElement;
            var domains = root.TryGetProperty("changed_domains", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Array
                ? string.Join("、", d.EnumerateArray().Select(x => x.GetString())) : "";
            var screens = root.TryGetProperty("changed_screens", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Array
                ? string.Join("、", s.EnumerateArray().Select(x => x.GetString())) : "";
            var output = root.TryGetProperty("output_path", out var o) ? o.GetString() : "";
            var parts = new List<string>();
            if (domains.Length > 0) parts.Add($"域: {domains}");
            if (screens.Length > 0) parts.Add($"画面: {screens}");
            Console.WriteLine($"✓ compile 成功{(parts.Count > 0 ? " — 变化 " + string.Join(" | ", parts) : " — 无未编译变化")}");
            if (output.Length > 0) Console.WriteLine($"  输出: {output}");
            return 0;
        }
        Console.WriteLine($"✓ compile — {dataJson}");
        return 0;
    }

    // ═══════════════════════════════════════════
    // 通用命令执行（映射 CLI 参数 → CommandService）
    // ═══════════════════════════════════════════

    /// <summary>
    /// 通用命令执行（元数据命令）：按 CliCatalog 参数视图把 CLI 键 → 命令层 cmd 键构建参数字典。
    /// 语义与历史 Cmd() 完全一致：Required → 缺参报错；有默认 → 未提供写默认（含空串 = handler 自行解释）；
    /// 无默认（provided-only，update 类）→ 显式提供才写（未提供保留现值；无值参数只记 _flagOpts 不进 _opts → 天然保留）。
    /// </summary>
    private static int MetaCmd(CliCommandSpec spec)
    {
        var (service, project) = LoadProject();
        var parameters = new Dictionary<string, object?>();
        foreach (var v in spec.Views)
        {
            if (v.Required)
            {
                parameters[v.CmdKey] = RequireVal(v.CliKey);
            }
            else if (v.Default != null)
            {
                // 有默认值：未提供时写入默认值（与 handler 的 GetValueOrDefault 兜底一致）
                parameters[v.CmdKey] = OptVal(v.CliKey, v.Default);
            }
            else if (_opts.ContainsKey(v.CliKey))
            {
                // 无默认（provided-only）：显式提供才写入（含空串 = 清空语义）；
                // 无值参数（--key 后无值）只记入 _flagOpts 不进 _opts → ContainsKey 失败 → handler 保留模型现值
                parameters[v.CmdKey] = OptVal(v.CliKey);
            }
        }
        var exitCode = Execute(service, spec.CommandName!, parameters);
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
            ["widget_name"] = OptVal("widget", ""),   // C12-16：widget 可选（缺省 + 世界地图画面 = 地图级事件，命令层 P9 已支持）
            ["event_type"] = RequireVal("event"),
            ["action_type"] = RequireVal("action"),
            // I-3 condition 可选（OptIfProvided：显式提供才写入，空串=清空；未提供=保持现状——避免 null 净化崩溃）
            ["condition"] = _opts.ContainsKey("condition") ? OptVal("condition", "") : null,
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
            ["widget_name"] = OptVal("widget", ""),   // C12-16：widget 可选（缺省 + 世界地图画面 = 地图级事件，与命令层一致）
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
            ["widget_name"] = OptVal("widget", ""),   // C12-16：widget 可选（与命令层一致）
            ["event_type"] = RequireVal("event"),
            ["action_type"] = RequireVal("action"),
            // I-3 condition 可选（OptIfProvided：显式提供才写入，空串=清空；未提供=保持现状）
            ["condition"] = _opts.ContainsKey("condition") ? OptVal("condition", "") : null,
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
    /// 参数安全净化（三级分类，逻辑统一在 <see cref="NavigatorHMI.Common.CliParamSanitizer"/>）。
    /// </summary>
    private static string SanitizeParam(string key, string value)
    {
        var err = NavigatorHMI.Common.CliParamSanitizer.Validate(key, value);
        if (err != null) { PrintError(err); Environment.Exit(1); }
        return value;
    }

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
            ProjectManager.Save(project, project.ProjectFilePath);
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

    /// <summary>REPL 帮助：命令清单由 CliCatalog 分组生成（单源，无重复手写）；属性键说明随附。</summary>
    private static void ShowReplHelp()
    {
        Console.WriteLine("可用命令:");
        foreach (var group in CliCatalog.All.GroupBy(c => c.Category))
            Console.WriteLine($"  {group.Key}: {string.Join(", ", group.Select(c => c.CliName))}");
        Console.WriteLine();
        Console.WriteLine("参数格式: --key value  或  --key \"value with spaces\"");
        Console.WriteLine("查看完整帮助: navihmi --help（含各命令参数说明）");
        Console.WriteLine("退出: exit / quit / q");
        Console.WriteLine();
        // set-property 属性键说明（与 --help 同源：CliCatalog.SetPropertyKeyHelp 首段）
        Console.WriteLine(CliCatalog.SetPropertyKeyHelp
            .Split("示例:", 2)[0].TrimEnd());
    }

    /// <summary>CLI 帮助：从 CliCatalog 单源生成（路由与帮助双维护已消除）。</summary>
    private static void ShowHelp()
    {
        Console.WriteLine(CliCatalog.BuildHelpText());
    }
}
