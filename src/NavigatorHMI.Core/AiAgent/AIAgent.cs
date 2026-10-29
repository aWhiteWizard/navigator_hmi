using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NavigatorHMI.CommandLayer;

namespace NavigatorHMI.AiAgent
{
    /// <summary>
    /// AI Agent（LLM 语义驱动）：自然语言 → 模型 tool_calls → CommandService 执行 → 结果回填 → 多轮，
    /// 直到模型返回文本（回复/总结）或 __complete__ 哨兵。支持多轮会话（历史保留，跨指令上下文延续）。
    /// 云端（OpenAI tools）与本地（chatml &lt;tool_call&gt;）后端输出统一为 ChatResponse，本类与后端无关。
    /// </summary>
    public class AIAgent
    {
        private readonly ICommandService _commands;
        private readonly ILLMBackend _backend;
        private readonly string _toolsJson;
        private readonly List<ChatMessage> _history = new();

        /// <summary>单次指令的最大模型调用轮次（多步操作 + 失败修正共用）；GUI 推理深度选项可调。</summary>
        public int MaxIterations { get; set; } = 12;

        /// <summary>本地后端宣告"全部完成"的哨兵命令名（云端不需要，模型自然返回文本）。</summary>
        public const string CompleteSentinel = "__complete__";

        /// <summary>
        /// 命令黑名单（K-6 安全闸）：默认含高风险下载命令（deploy_project/deploy_firmware）——AI 不能执行，命中 BLOCKED + 提醒手动；
        /// 设置窗 GUI 可配增删（AiConfigStore.Blacklist 持久化 → SetBlacklist 注入）。
        /// </summary>
        private static List<string> _blacklist = new() { "deploy_project", "deploy_firmware" };

        /// <summary>当前黑名单（只读视图）。</summary>
        public static IReadOnlyList<string> Blacklist => _blacklist;

        /// <summary>设置黑名单（GUI 设置窗保存后注入；幂等去重）。</summary>
        public static void SetBlacklist(IEnumerable<string> names)
            => _blacklist = (names ?? Enumerable.Empty<string>()).Distinct().ToList();

        /// <summary>确认闸命令（K-6）：AI 发起这些操作需会话内用户明确授权（防误触）——未授权返回 CONFIRM_REQUIRED。
        /// 含部署类（纵深防御：黑名单在则 BLOCKED 优先，用户移出黑名单后仍需确认——设计 P0 ② B2 SOFT 取保守值）。</summary>
        private static readonly string[] ConfirmCommands = { "connect", "deploy_project", "deploy_firmware" };

        /// <summary>确认命令中文别名（用户按提示回复「确认连接」等自然语言可授权——命令名 ASCII 与中文别名任一命中）。</summary>
        private static readonly Dictionary<string, string[]> ConfirmAliases = new()
        {
            ["connect"] = new[] { "connect", "连接" },
            ["deploy_project"] = new[] { "deploy_project", "下载工程", "部署工程" },
            ["deploy_firmware"] = new[] { "deploy_firmware", "下载固件", "升级固件" },
        };

        /// <summary>会话历史保留最近 N 轮 user/assistant 对（防长会话超模型上下文）；GUI 上下文长度选项可调。</summary>
        public int MaxHistoryTurns { get; set; } = 10;

        public AIAgent(ICommandService commands, ILLMBackend backend, bool enableTools = true, Action<Action>? commandDispatcher = null)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _enableTools = enableTools;
            _commandDispatcher = commandDispatcher;   // GUI 注入 Dispatcher：命令执行（改模型集合）必须在 UI 线程，防 CollectionView 跨线程异常
            var defs = commands.GetAvailableCommands().Where(c => !_blacklist.Contains(c.Name));   // 全量命令（黑名单除外）
            _toolsJson = _enableTools ? ToolsSchemaBuilder.Build(defs, compact: true) : "";   // 精简 schema（省略参数描述）：模型调工具准确率高；无工具模式空串（后端跳过 tools 注入）
            _history.Add(new ChatMessage("system", BuildSystemPrompt()));   // 会话首条：规则
        }

        /// <summary>AI 执行的操作记录（清单：本次会话最新一批做了什么，GUI 展示 + 撤销参考）。</summary>
        public record AiOperation(string CommandName, string ArgsSummary, bool Success);

        public IReadOnlyList<AiOperation> LastOperations => _lastOperations;
        private readonly List<AiOperation> _lastOperations = new();

        private readonly bool _enableTools;
        private readonly Action<Action>? _commandDispatcher;
        private bool _retriedPrompt;   // 当前指令是否已做过未命中重试（每指令限 1 次）
        private int _retryRollbackIndex;   // 未命中重试回滚点（重试成功后移除 assistant 文本 + 强调消息）

        /// <summary>操作意图动词：用户指令含这些词视为组态操作指令（冷启动未命中重试判定用）。</summary>
        private static readonly string[] OperationVerbs =
        {
            "创建", "新建", "放", "加", "添加", "绑定", "解绑", "删除", "改名", "重命名",
            "设置", "改成", "设为", "移动", "调整", "阵列", "对齐", "配置", "复制", "粘贴", "画", "显示",
        };

        private static bool HasOperationIntent(List<ChatMessage> history)
        {
            var lastUser = history.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            return OperationVerbs.Any(v => lastUser.Contains(v));
        }

        /// <summary>
        /// AH（2026-10-30 用户实测）：把模型/后端传来的 dict 型参数归一化成命令层要的 Dictionary&lt;string,string&gt;。
        ///
        /// 背景：`CloudLLMBackend.JsonValueToObject` 对嵌套 JSON 对象走 `_ => el.GetRawText()` ⇒ 拿到的是
        /// **JSON 字符串**；命令层 handler 统一写 `raw is Dictionary&lt;string, string&gt;` ⇒ 不匹配就当空字典，
        /// 表现为「AI 明明传了 params，动作却报缺必填参数」（用户实测 bind_event 传 9 步全无效即此因）。
        ///
        /// 支持的输入形态（按顺序尝试）：
        ///   · 已是 Dictionary&lt;string,string&gt; ⇒ 原样
        ///   · Dictionary&lt;string,object?&gt;（本地模型路径产出）⇒ 值 ToString
        ///   · JSON 对象字符串 {"k":"v"} ⇒ 解析
        ///   · K=V 逗号串 "k=v,k2=v2"（CLI 风格，模型有时也这么传）⇒ 拆解
        /// 兼容兜底：模型偶尔把 dict 参数改名成 `params_json`，或把动作参数平铺在顶层
        /// （如直接传 target_screen=设备诊断）——两种情况都并进 dict 参数。
        /// </summary>
        private Dictionary<string, object?> NormalizeDictArgs(string commandName, Dictionary<string, object?> args)
        {
            if (args == null || args.Count == 0) return args ?? new Dictionary<string, object?>();

            // 找出该命令定义的 dict 型参数名（如 params）
            var dictParamNames = new List<string>();
            try
            {
                var def = _commands.GetAvailableCommands().FirstOrDefault(c => c.Name == commandName);
                if (def?.Parameters != null)
                    foreach (var kv in def.Parameters)
                        if (string.Equals(kv.Value.Type, "dict", StringComparison.OrdinalIgnoreCase))
                            dictParamNames.Add(kv.Key);
            }
            catch { /* 取定义失败不阻断执行 */ }
            if (dictParamNames.Count == 0) return args;

            var result = new Dictionary<string, object?>(args);
            foreach (var pn in dictParamNames)
            {
                var normalized = ToStringDict(result.TryGetValue(pn, out var cur) ? cur : null);
                // 别名兜底：params 缺失时尝试 params_json
                if (normalized.Count == 0 && result.TryGetValue(pn + "_json", out var alias))
                    normalized = ToStringDict(alias);
                // 平铺兜底：把除已知键之外的标量参数并进 dict（模型常把 target_screen 直接放顶层）
                if (normalized.Count == 0)
                {
                    foreach (var kv in args)
                    {
                        if (kv.Key == pn || kv.Key == pn + "_json") continue;
                        if (kv.Value is string or bool or int or long or double)
                        {
                            var s = kv.Value?.ToString() ?? "";
                            // 只并入"看起来像动作参数"的键：命令行/事件命令的固定入参不并入
                            if (IsEngineArg(kv.Key)) continue;
                            normalized[kv.Key] = s;
                        }
                    }
                }
                result[pn] = normalized;
            }
            return result;
        }

        /// <summary>命令层固定入参（不属于 dict 内容的键）。</summary>
        private static bool IsEngineArg(string key) => key is
            "screen_name" or "widget_name" or "event" or "event_type" or "action" or "action_type"
            or "condition" or "name" or "type" or "params";

        /// <summary>任意形态 → Dictionary&lt;string,string&gt;（无法识别时返回空字典）。</summary>
        private static Dictionary<string, string> ToStringDict(object? raw)
        {
            var dict = new Dictionary<string, string>();
            switch (raw)
            {
                case null:
                    return dict;
                case Dictionary<string, string> ds:
                    foreach (var kv in ds) dict[kv.Key] = kv.Value;
                    return dict;
                case IDictionary<string, object?> dobj:
                    foreach (var kv in dobj) dict[kv.Key] = kv.Value?.ToString() ?? "";
                    return dict;
                case string s:
                    var t = s.Trim();
                    if (t.Length == 0) return dict;
                    if (t.StartsWith("{"))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(t);
                            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                                foreach (var p in doc.RootElement.EnumerateObject())
                                    dict[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                                        ? (p.Value.GetString() ?? "")
                                        : p.Value.GetRawText().Trim('"');
                            return dict;
                        }
                        catch { /* 非法 JSON：退回 K=V 解析 */ }
                    }
                    foreach (var part in t.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var i = part.IndexOf('=');
                        if (i > 0) dict[part[..i].Trim()] = part[(i + 1)..].Trim();
                    }
                    return dict;
                default:
                    return dict;
            }
        }

        /// <summary>执行命令：GUI 场景经 Dispatcher 封送到 UI 线程（handler 直接改 ObservableCollection，WPF CollectionView 线程亲和）；CLI 无 WPF 直接执行。</summary>
        private CommandResult ExecuteCommand(string name, Dictionary<string, object?> args)
        {
            // AH 修复（2026-10-30 用户实测「AI 改了 9 步还是缺参数」根因）：
            // 云端后端把 tool_call 里的**嵌套 JSON 对象**参数（如 params:{"target_screen":"x"}）经
            // JsonValueToObject 的 `_ => el.GetRawText()` 转成了**字符串**；而命令层约定 dict 参数必须是
            // Dictionary<string,string>（`raw is Dictionary<string, string>` 判定）⇒ 不匹配就静默变空字典
            // ⇒ bind_event/add_event 的 params 全丢 ⇒ 动作永远"缺必填参数"，模型怎么传都无效。
            // 这里在**执行前**统一归一化（JSON 对象串 / Dictionary<string,object> / "k=v,k2=v2" 都转成
            // Dictionary<string,string>），并兼容模型常见兜底写法（params_json 别名、动作参数平铺在顶层）。
            args = NormalizeDictArgs(name, args);
            // 操作清单记录提前：BLOCKED 拒绝、CLI 直接执行都计入（清单是 GUI 展示，但记录逻辑统一）
            if (_blacklist.Contains(name))
            {
                var blocked = CommandResult.Fail("BLOCKED", $"操作 \"{name}\" 已在 AI 黑名单中，AI 不能执行；请手动操作。");
                _lastOperations.Add(new AiOperation(name, SummarizeArgs(args), false));
                return blocked;
            }
            // K-6 确认闸：需确认命令（connect）——会话内用户明确授权才执行（防误触；授权检测最近 user 消息含确认词）
            if (ConfirmCommands.Contains(name) && !UserApprovedSensitiveCommand(name))
            {
                var confirm = CommandResult.Fail("CONFIRM_REQUIRED",
                    $"操作 \"{name}\" 需您确认——请明确回复「确认{name}」后重试（或手动 connect）。");
                _lastOperations.Add(new AiOperation(name, SummarizeArgs(args), false));
                return confirm;
            }
            if (_commandDispatcher == null)
            {
                var direct = _commands.Execute(name, args);
                _lastOperations.Add(new AiOperation(name, SummarizeArgs(args), direct.Success));
                return direct;
            }
            CommandResult result = CommandResult.Fail("COMMAND_CRASH", "命令未执行");
            try
            {
                _commandDispatcher(() => result = _commands.Execute(name, args));
            }
            catch (Exception ex)
            {
                // Dispatcher 关闭等竞态（窗口退出中）：返回失败而非上抛，保持 assistant tool_calls 与 tool 消息配对闭合（防下一轮 API 400）
                result = CommandResult.Fail("COMMAND_CRASH", $"命令执行调度失败: {ex.Message}");
            }
            _lastOperations.Add(new AiOperation(name, SummarizeArgs(args), result.Success));
            return result;
        }

        /// <summary>K-6 确认闸授权检测：最近 user 消息含明确授权词（确认/同意）+ 命令名或中文别名。</summary>
        private bool UserApprovedSensitiveCommand(string commandName)
        {
            var lastUser = _history.LastOrDefault(m => m.Role == "user")?.Content ?? "";
            if (string.IsNullOrEmpty(lastUser)) return false;
            var normalized = lastUser.ToLowerInvariant();
            var confirm = normalized.Contains("确认") || normalized.Contains("同意");
            if (!confirm) return false;
            var cmdMention = normalized.Contains(commandName.ToLowerInvariant());
            if (!cmdMention && ConfirmAliases.TryGetValue(commandName, out var aliases))
                cmdMention = aliases.Any(a => normalized.Contains(a));
            return cmdMention;
        }

        /// <summary>命令参数摘要（清单展示用：键=值，值裁剪防刷屏）。</summary>
        private static string SummarizeArgs(Dictionary<string, object?> args)
        {
            if (args.Count == 0) return "";
            return string.Join(" ", args.Select(kv =>
            {
                var v = kv.Value?.ToString() ?? "";
                return v.Length > 40 ? $"{kv.Key}={v[..40]}…" : $"{kv.Key}={v}";
            }));
        }

        /// <summary>
        /// 多轮会话入口：追加用户消息 → agent loop（执行 tool_calls 直到模型返回文本）→ 返回模型回复。
        /// 历史保留在 _history，多次调用形成连续对话（GUI 侧边栏复用同一实例）。
        /// </summary>
        public async Task<string> ChatAsync(string userInput, CancellationToken ct = default)
        {
            _lastOperations.Clear();   // 新一批操作清单（GUI 展示 AI 本次做了什么）
            TrimHistory();   // 裁剪旧轮次，防会话无限增长
            _retriedPrompt = false;   // 每次新指令重置未命中重试机会
            _retryRollbackIndex = 0;   // 回滚点入口统一复位（含重试失败路径，防下一轮误删历史）
            _history.Add(new ChatMessage("user", AutoInjectReferencedFiles(InjectCurrentScreen(userInput ?? ""))));   // P1-9：注入当前画面；AH：注入消息里引用的需求文档
            return await RunLoopAsync(ct);
        }

        /// <summary>P1-9/P2-3/B③：用户消息前注入当前画面 + 可用用户组 + 画面控件清单（实时读命令层，每轮跟随切换；不污染 system 规则、无历史残留）。</summary>
        private string InjectCurrentScreen(string userInput)
        {
            var cur = _commands.CurrentScreenName;
            var prefix = string.IsNullOrWhiteSpace(cur) ? "" : $"[当前画面：{SanitizeForPrompt(cur)}]";
            // P2-3：注入用户组清单（AI 创建用户时知道合法 group_name——避免 NOT_FOUND）
            var groups = _commands.GetGroupNames();
            var cleanGroups = groups.Select(SanitizeForPrompt).Where(g => g.Length > 0).ToList();
            if (cleanGroups.Count > 0)
                prefix += (prefix.Length > 0 ? " " : "") + $"[可用用户组：{string.Join("/", cleanGroups)}]";
            // B③：注入当前画面控件清单（ObjectName+类型，上限 50——AI「所有XX」/操作既有控件时知道名字）；整体限长防挤占上下文
            var widgets = _commands.GetCurrentScreenWidgets();
            if (widgets.Count > 0)
            {
                var list = string.Join("；", widgets.Select(w => $"{SanitizeForPrompt(w.Name)}({w.Type})"));
                if (list.Length > 2000) list = list[..2000] + "…";   // 总长截断（50 控件最坏可达数 KB）
                prefix += (prefix.Length > 0 ? " " : "") + $"[画面控件：{list}]";
            }
            return string.IsNullOrEmpty(prefix) ? userInput : $"{prefix} {userInput}";
        }

        /// <summary>
        /// AH（2026-10-28 用户需求）：用户消息里出现**本地文本文件路径**时，自动读入内容注入上下文。
        ///
        /// 为什么需要auto-inject（而不只依赖工具调用）：用户会把需求文档路径直接丢进对话
        ///（如「按 D:\docs\AI建工程_需求文件.md 实现」），而模型不认识路径 ⇒ 以前只会回"我读不到文件"。
        /// 这里在**发请求前**把文件正文拼进 user 消息，模型第一轮就能看到内容，无需教它先调 read_text_file。
        /// 工具仍然保留（长文档可让模型自己分段再读）。
        ///
        /// 边界：最多 3 个文件、单文件经 read_text_file 的 200KB/扩展名白名单校验、总注入 ≤ 60KB 字符。
        /// </summary>
        private string AutoInjectReferencedFiles(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput)) return userInput;
            var paths = ExtractLocalPaths(userInput);
            if (paths.Count == 0) return userInput;

            var sb = new System.Text.StringBuilder();
            var injected = 0;
            foreach (var p in paths.Take(3))
            {
                var r = ExecuteCommand("read_text_file", new Dictionary<string, object?> { ["path"] = p, ["start_line"] = 1, ["max_lines"] = 0 });
                if (!r.Success)
                {
                    sb.AppendLine($"[文件读取失败：{SanitizeForPrompt(p)} -> {SanitizeForPrompt(r.ErrorCode + " " + r.ErrorMessage)}]");
                    continue;
                }
                var json = CommandDataToJson(r.Data);
                var content = ExtractJsonString(json, "content");
                if (content.Length > 60000)
                    content = content.Substring(0, 60000) + "\n…（内容过长已截断，可用 read_text_file 分段读取剩余部分）";
                sb.AppendLine($"[需求文档内容：{SanitizeForPrompt(p)}]");
                sb.AppendLine(content);
                sb.AppendLine("[/需求文档内容]");
                injected++;
            }
            if (injected == 0 && sb.Length == 0) return userInput;
            return $"{sb}\n{userInput}";
        }

        /// <summary>从文本里抽取看起来是本地绝对路径的片段（Windows 盘符路径，允许中文/空格/方括号，以常见文本扩展名结尾）。</summary>
        private static List<string> ExtractLocalPaths(string text)
        {
            var result = new List<string>();
            // 例：D:\workspace\FutureTechVideo\[自制]xx\02_脚本\AI建工程_需求文件.md
            var re = new System.Text.RegularExpressions.Regex(
                @"[A-Za-z]:\\[^\r\n""'<>|*?]*?\.(?:md|markdown|txt|json|csv|ya?ml|xml|log|ini|conf)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in re.Matches(text))
            {
                var raw = m.Value.Trim();
                if (raw.Length > 0 && !result.Contains(raw)) result.Add(raw);
            }
            return result;
        }

        /// <summary>命令返回数据 → JSON 文本（read_text_file 返回匿名对象；避免依赖具体类型）。</summary>
        private static string CommandDataToJson(object? data)
        {
            if (data == null) return "{}";
            try { return System.Text.Json.JsonSerializer.Serialize(data); }
            catch { return "{}"; }
        }

        /// <summary>从 JSON 文本取字符串字段（轻量解析，失败返回空串）。</summary>
        private static string ExtractJsonString(string json, string field)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty(field, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                    ? (v.GetString() ?? "") : "";
            }
            catch { return ""; }
        }

        /// <summary>任务 3 加固：注入 AI user 消息前的工程数据清洗——剔除方括号/双引号/换行/控制字符，防恶意名称闭合前缀注入伪指令。</summary>
        private static string SanitizeForPrompt(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch is '[' or ']' or '"' or '\r' or '\n' or '\t' || char.IsControl(ch)) continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>新建会话：清空历史，仅保留 system 规则消息（GUI「新建会话」调用）。</summary>
        public void Reset()
        {
            _history.Clear();
            _retriedPrompt = false;
            _retryRollbackIndex = 0;
            _history.Add(new ChatMessage("system", BuildSystemPrompt()));
        }

        /// <summary>保留 system + 最近 MaxHistoryTurns 轮 user 对话（按 user 消息计数，保证轮次边界完整）。</summary>
        private void TrimHistory()
        {
            if (_history.Count <= 1) return;
            var maxTurns = Math.Max(MaxHistoryTurns, 1);   // 钳制下限：0 时保留最近 1 轮
            // 从第 2 条起删除，直到剩余轮数 ≤ maxTurns（以 user 消息计数；system 恒在 [0]）
            var userCount = 0;
            for (int i = 1; i < _history.Count; i++)
                if (_history[i].Role == "user") userCount++;
            if (userCount <= maxTurns) return;
            // 定位第 (userCount - maxTurns + 1) 个 user 消息，删除它之前的所有消息（含不完整轮次的 assistant/tool 残留）
            var toRemove = 0;
            var seen = 0;
            for (int i = 1; i < _history.Count; i++)
            {
                if (_history[i].Role == "user") seen++;
                if (seen == userCount - maxTurns + 1) { toRemove = i; break; }
            }
            if (toRemove > 0)
                _history.RemoveRange(1, toRemove - 1);
        }

        /// <summary>执行 agent loop（当前历史基础上），返回模型最终文本回复。</summary>
        private async Task<string> RunLoopAsync(CancellationToken ct)
        {
            var executed = new List<string>();
            for (int i = 0; i < MaxIterations; i++)
            {
                ct.ThrowIfCancellationRequested();
                ChatResponse resp;
                try
                {
                    resp = await _backend.ChatAsync(_history, _toolsJson, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;   // 取消语义向上传播（HttpClient 超时抛 TaskCanceledException），不转成"推理失败"文本
                }
                catch (Exception ex)
                {
                    return $"AI 推理失败: {ex.Message}";
                }

                if (!resp.IsToolCall)
                {
                    // 模型返回文本：对话回复/总结/澄清 → 保留历史，返回给用户
                    var text = resp.Content?.Trim() ?? "";
                    // 冷启动未命中重试：首轮模型返回纯文本但用户指令含组态操作动词（创建/放/绑定等）
                    // → 追加强调消息后重试（限 1 次，防死循环）；命中率低是 DeepSeek 冷启动常见问题
                    if (i == 0 && !_retriedPrompt && HasOperationIntent(_history))
                    {
                        _retriedPrompt = true;
                        _retryRollbackIndex = _history.Count;   // 回滚点：未命中产物（assistant 文本 + 强调）重试成功后移除
                        _history.Add(new ChatMessage("assistant", text));
                        // 强调消息用 user 角色：本地后端只取首条 system（system 会被丢弃），user 两端都保留
                        _history.Add(new ChatMessage("user",
                            "（系统提示：检测到组态操作意图。请直接调用 tools 中的函数执行，不要用文本描述计划或询问确认。）"));
                        continue;
                    }
                    _history.Add(new ChatMessage("assistant", text));
                    return string.IsNullOrEmpty(text) ? "（模型未回复）" : text;
                }

                // 执行本轮全部 tool_calls，结果回填历史
                var assistantMsg = new ChatMessage("assistant", "") { ToolCalls = resp.ToolCalls };
                _history.Add(assistantMsg);
                // 未命中重试成功（本轮是重试后的工具调用）：回滚移除未命中产物（assistant 计划文本 + 强调 user），
                // 防污染后续轮次（过度驱动工具调用）；assistant(tool_calls) 保留
                if (_retryRollbackIndex > 0)
                {
                    var removeCount = _history.Count - _retryRollbackIndex - 1;
                    if (removeCount > 0)
                        _history.RemoveRange(_retryRollbackIndex, removeCount);
                    _retryRollbackIndex = 0;
                }
                for (int ti = 0; ti < resp.ToolCalls!.Count; ti++)
                {
                    var tc = resp.ToolCalls[ti];
                    ct.ThrowIfCancellationRequested();
                    if (tc.Name == CompleteSentinel)
                    {
                        // 哨兵收尾：补齐本轮剩余全部 tc（含哨兵自身）的 tool 响应，保证 assistant tool_calls 与 tool 消息完全配对，防下一轮 API 400
                        var summary = executed.Count > 0 ? Summarize(executed) : "好的，没有需要执行的操作。";
                        return FinishAborted(resp.ToolCalls.GetRange(ti, resp.ToolCalls.Count - ti), summary);
                    }

                    var result = ExecuteCommand(tc.Name, tc.Arguments ?? new Dictionary<string, object?>());
                    executed.Add($"{tc.Name} → {(result.Success ? "✓" : "✗ " + result.ErrorCode)}");
                    var resultText = result.Success
                        ? $"命令 {tc.Name} 执行成功" + (result.Data != null ? $"，结果: {JsonSerializer.Serialize(result.Data)}" : "")
                        : $"命令 {tc.Name} 执行失败 [{result.ErrorCode}]: {result.ErrorMessage}，请修正后重试或说明原因";
                    _history.Add(new ChatMessage("tool", resultText) { ToolCallId = tc.Id });

                    if (!result.Success && i >= MaxIterations - 2)
                        return FinishAborted(resp.ToolCalls.GetRange(ti + 1, resp.ToolCalls.Count - ti - 1),
                            $"操作失败：{result.ErrorMessage}（已尝试自动修正）");
                }
            }
            // MaxIterations 耗尽：补齐未配对 tool 响应 + assistant 收尾，保证下一轮消息序列闭合（防 API 400）
            return FinishAborted(new List<ToolCall>(), "步骤较多未全部完成，已执行：" + string.Join("；", executed));
        }

        private static string Summarize(List<string> executed) => "已完成：" + string.Join("；", executed);

        /// <summary>非正常退出收尾：给未执行的 tool_calls 补齐 tool 响应（配对完整）+ assistant 收尾，保证下一轮消息序列闭合（防 API 400）。</summary>
        private string FinishAborted(List<ToolCall> pendingCalls, string summary)
        {
            foreach (var tc in pendingCalls)
                _history.Add(new ChatMessage("tool", $"命令 {tc.Name} 未执行（提前终止）") { ToolCallId = tc.Id });
            _history.Add(new ChatMessage("assistant", summary));
            return summary;
        }

        private string BuildSystemPrompt()
        {
            var cur = SanitizeForPrompt(_commands.CurrentScreenName ?? "");   // 工程数据进 prompt 统一清洗（画面名可含换行/引号）
            var curLine = string.IsNullOrEmpty(cur)
                ? "当前画面：未指定（操作前先确认画面名）。\n"
                : $"当前画面：\"{cur}\"——用户说「当前画面/本画面/这个画面」= 该画面；未指定画面时默认操作该画面。\n";
            return (_enableTools
                ? "你是 NavigatorHMI 组态软件 AI 助手，把用户的中文自然语言指令转换为对组态软件的命令调用。\n" +
                "规则：\n" +
                curLine +
            "1. 判断用户意图：需要操作工程（建画面/放控件/建变量/建报警等）时，直接调用 tools 中的函数；" +
            "不要询问确认、不要用文本描述过程、不要编造不存在的工具名。\n" +
            "2. 参数必须符合函数 schema；不确定的次要参数可省略（命令层有默认值/校验）。\n" +
            "3. 需要多步操作时，先调用一步，等工具执行结果返回后再调用下一步。\n" +
            "4. 直接执行用户要求的操作，不要试图预先查看工程状态（没有查询类工具）；画面/变量不存在时工具会返回失败，再修正。\n" +
            "5. 工具执行失败（如对象不存在/参数错误）时：分析原因修正后重试；确实无法完成则用中文说明。\n" +
            "6. 工具返回 DUPLICATE（对象已存在）时：不要重复创建、不要放弃——直接改用已存在的对象继续" +
            "（如画面已存在则直接在该画面放控件，列表已存在则直接绑定该列表，变量已存在则直接使用），后续操作照常执行。\n" +
            "7. 用户只是提问/闲聊/感谢，或全部操作已完成时，直接用中文回复，不要再调用工具。\n" +
            "8. 涉及设备连接、部署、删除画面等破坏性或外部操作时，先确认用户意图再执行。\n" +
            "9. 复杂指令（多画面/多控件/阵列等）可一次调用多个工具，加快完成。\n" +
            "9b. 画面名直接用（无需加「画面」后缀）：用户说「在世界地图中放置」= 画面「世界地图」；「放在画面中心/居中」= add_widget 传 center=true（画面中心坐标）；「阵列以画面中心为起点/从中心开始」= array_layout 传 center_start=true。注意「全局画面」本身含「画面」二字——用户说「在全局画面放」= 画面「全局画面」（用全名，不是去掉后缀）。\n" +
            "9c. 阵列行列：用户说「M×N 阵列」（如 2×2/3×4）时，array_layout 显式传 cols=M rows=N（先传参）；没说行列数时省略 cols/rows——命令层按控件数自动计算（4 个 → 2×2）。\n" +
            "9d. 画面控件清单：用户消息前缀的「画面控件：xxx(类型)；yyy(类型)」是当前画面已有控件（ObjectName+类型）；用户说「所有XX」/「全部XX」（如" +
            "\"把所有标签背景去掉\"）= 对画面内该类型全部控件逐个操作（先按清单确定目标控件名，再逐一对 set_property/add_widget 等操作）。\n" +
            "9e. 修改已有阵列（Z2）：用户说「把阵列半径改为 X/圆心移到 Y/改成矩形/改列距」时，重跑 array_layout——widgets 必须仍传该阵列的全部控件名（保持原对象，不新建），" +
            "mode 保持原模式（circle→circle / rect→rect，除非明确说改成矩形/圆形），只改要调整的参数（radius/center_x/center_y/spacing_x/spacing_y/cols/rows 等）。" +
            "注意：未传的参数回落到命令层默认值（spacing 120/80、圆心=画面中心、半径 150、角度 0~360、起点 0,0），不是沿用原值——若原阵列用过非默认值（如自定义圆心/角度/间距/半径），须显式传回原值；" +
            "cols/rows 未显式提供时按控件数自动计算（4 个 → 2×2），非方形排列须显式传回原 cols/rows；start_x/start_y/center_x/center_y/radius 等参数均已对 AI 开放，可直接传。\n" +
            "10. 控件与列表：用户让 image/frame/文本列表控件\"按列表显示/显示列表内容/绑定列表\"时，用 set_property 的 listRef 属性" +
            "绑定到列表名（列表不存在则先 create_list）；不要用 imagePath 设单张静态图代替（只有明确要求显示某一张固定图时才设 imagePath）。\n" +
            "11. Text 控件已下线：新建文本输入控件请用 IOField（widget_type=iofield）；旧工程已存在的 Text 控件只读兼容展示，不要新建 text 类型控件。\n" +
            // ── AH（2026-10-28 用户需求）：读需求文档 + 自建 ToDo + 自动做完整套工程 ──
            "12. 需求文档：用户消息里若带 `[需求文档内容：路径] … [/需求文档内容]`，那就是需求文档正文——**直接按它执行**，" +
            "不要再问用户要内容、不要说自己读不到文件。需要读文档的其它部分时调用 read_text_file（path / start_line / max_lines）。\n" +
            "13. 大任务先立清单再动手：用户给了需求文档或要求『按文档实现』时，**第一步先用文本输出一个执行清单**（Markdown 勾选框），" +
            "格式严格为 `- [ ] 第N步：<动作>（画面/控件/变量明细）`，一步一行、按依赖排序（先变量与列表 → 再画面 → 再控件 → 再事件/绑定 → 最后编译自检）。" +
            "清单之后**立即开始执行第一步**（同一轮里继续调用工具），不要停在只给计划。\n" +
            "14. 边做边勾：每完成清单里的一步，就在下一条文本回复里重发整份清单并把该步改成 `- [x]`（未做的保持 `- [ ]`），" +
            "这样用户能随时看到进度；不要重复创建已完成的对象。\n" +
            "15. 自检：一组操作完成后调用 compile 验证；返回错误时按错误信息修正后重试（同一错误最多修 2 次）。全部完成后再回复总结。\n" +
            "16. 工具轮次不够时：回复一句『已完成 X/Y，回复「继续」从第 Z 步接着做』，用户回「继续」后按清单断点继续，不要从头再来。\n" +
            // ── AH 修复（2026-10-30 用户实测 AI 建工程的三类错误，逐条立规矩）──
            "17. 事件**每个控件每种事件只绑定一次**：同一控件的 onClick 只能调一次 bind_event/add_event；" +
            "要改动作参数必须用 update_event（不是再调一次 bind_event——重复绑定会让设备端画面白屏）。" +
            "同一事件要做多件事时用 add_event 追加动作（按序执行）。\n" +
            "18. 动作**必须带 params**（否则设备端点了没反应，编译期会报错拦住）：" +
            "screen_switch → \"target_screen=画面名\"；tag_write → \"tag_name=变量,value=值\"；" +
            "tag_toggle → \"tag_name=变量\"；set_property → \"key=属性名,value=值\"；run_command → \"command=命令名\"。\n" +
            "19. 图片**列表** vs 单图：需求说「图标列表/图片列表/按变量切换图片/列表轮播」时，必须" +
            "先 create_list --type Image --name <列表名> --items <文件名1,文件名2>，再用 set_property key=listRef 绑定；" +
            "**绝不能用 imagePath 顶替**（单图不随变量变化，一眼能看出没按要求做）。" +
            "只有需求明确说「固定显示某一张图」时才用 imagePath。\n" +
            "20. 编译自检必须真做：每组操作后调 compile；报错逐条修（错误信息点名了画面/控件/参数），" +
            "警告也要读（如重复事件类型），修完再 compile 一次确认 0 错误再汇报完成。\n" +
            "示例（严格照此模式：操作指令第一步必须调用工具，不要用文本描述计划）：\n" +
            "用户：创建一个画面叫温度监控\n" +
            "助手：调用 create_screen，参数 {\"name\": \"温度监控\"}\n" +
            "用户：在温度监控画面放一个按钮\n" +
            "助手：调用 add_widget，参数 {\"screen_name\": \"温度监控\", \"widget_type\": \"button\"}\n" +
            "用户：把 image_2 绑定到图片列表\n" +
            "助手：调用 set_property，参数 {\"screen_name\": \"温度监控\", \"widget_name\": \"image_2\", \"key\": \"listRef\", \"value\": \"图片列表\"}"
            : "你是 NavigatorHMI 组态软件 AI 助手（深度思考模式，未启用函数调用）。\n" +
            "规则：\n" +
            "1. 当前模式不提供工程操作工具，无法直接创建/修改画面、变量、报警。\n" +
            "2. 用户要求执行操作时，说明该操作需要切换到函数调用模式（模型下拉选 DeepSeek Chat），并给出对应的自然语言指令示例。\n" +
            "3. 用户提问、分析、咨询时正常回答。");
    }
}
}
