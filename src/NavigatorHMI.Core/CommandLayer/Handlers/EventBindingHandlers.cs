using System.Text;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>事件绑定命令：add_event / remove_event / update_event（GUI/CLI/AI 统一入口）。</summary>
    public static class EventBindingCommon
    {
        /// <summary>全部事件类型（bind_event 与 add_event 共用枚举，含新扩展）。</summary>
        internal static string[] AllEventTypes => Enum.GetNames<EventType>();
        /// <summary>全部动作类型（含新扩展）。</summary>
        internal static string[] AllActionTypes => Enum.GetNames<ActionType>();

        /// <summary>控件 + 事件/动作 公共参数。</summary>
        internal static Dictionary<string, ParameterDefinition> SwParams(bool requireEvent = true, bool requireAction = false, bool widgetOptional = false)
        {
            var d = new Dictionary<string, ParameterDefinition>
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = !widgetOptional, Description = widgetOptional ? "控件名；缺省 = 世界地图级事件（仅世界地图画面支持）" : null },
            };
            if (requireEvent) d["event_type"] = new() { Type = "enum", Required = true, EnumValues = AllEventTypes, Description = "事件类型（触发条件）" };
            if (requireAction) d["action_type"] = new() { Type = "enum", Required = true, EnumValues = AllActionTypes, Description = "动作类型（执行的函数）" };
            return d;
        }

        /// <summary>查找控件（复用 WidgetHelper）。</summary>
        internal static (Screen?, Widget?, CommandResult?) FindWidget(HMIProject project, Dictionary<string, object?> p)
            => WidgetHelper.FindWidget(project, p);

        /// <summary>P9：定位事件目标列表——指定 widget_name → 控件 Events；缺省且画面为世界地图 → WorldMapConfig.Events（地图级点击切换事件）。</summary>
        internal static (List<WidgetEvent> events, CommandResult? err) FindEventTargets(HMIProject project, Dictionary<string, object?> p)
        {
            var screenName = p["screen_name"]!.ToString()!;
            var screen = project.Screens.FirstOrDefault(s => s.Name == screenName);
            if (screen == null) return (new(), CommandResult.Fail("NOT_FOUND", $"画面 \"{screenName}\" 不存在"));
            if (p.TryGetValue("widget_name", out var wn) && !string.IsNullOrWhiteSpace(wn?.ToString()))
            {
                var widget = screen.Widgets.FirstOrDefault(w => w.ObjectName == wn.ToString());
                if (widget == null) return (new(), CommandResult.Fail("NOT_FOUND", $"控件 \"{wn}\" 不存在"));
                return (widget.Events, null);
            }
            if (screen.Type != ScreenType.WorldMap)
                return (new(), CommandResult.Fail("INVALID_PARAM", $"画面 \"{screenName}\" 不是世界地图——未指定控件时仅世界地图画面支持地图级事件（点击切换）"));
            project.WorldMap ??= new WorldMapConfig();
            return (project.WorldMap.Events, null);
        }

        /// <summary>C12-15：screen_switch 目标画面名容错解析——精确 → 去空白 → 去末尾括号段 → 唯一包含匹配；
        /// 命中返回正式画面名（调用方改写落库）；失败返回 null 并填充候选列表（调用方回执列候选）。</summary>
        internal static string? ResolveScreenSwitchTarget(HMIProject project, string raw, out List<string> candidates)
        {
            candidates = new();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var t = raw.Trim();
            if (project.Screens.Any(s => s.Name == t)) return t;
            string norm = NormalizeScreenName(t);
            var byNorm = project.Screens.Where(s => NormalizeScreenName(s.Name) == norm).Select(s => s.Name).ToList();
            if (byNorm.Count == 1) return byNorm[0];
            if (byNorm.Count > 1) { candidates = byNorm; return null; }   // 规范化后多命中等价 → 不自动选，列候选
            var byContains = project.Screens
                .Where(s => NormalizeScreenName(s.Name).Contains(norm) || norm.Contains(NormalizeScreenName(s.Name)))
                .Select(s => s.Name).ToList();
            if (byContains.Count == 1) return byContains[0];
            candidates = byContains.Count > 0 ? byContains : project.Screens.Select(s => s.Name).ToList();
            return null;
        }

        /// <summary>画面名规范化：去全部空白 + 去末尾括号段（"世界地图(主)" → "世界地图"；保留中间括号）。</summary>
        private static string NormalizeScreenName(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s) if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"[（(][^（()）]*[）)]$", "");
        }
    }

    /// <summary>add_event：为控件/世界地图追加动作。同事件类型已存在 → 追加 Action；不存在 → 新建 WidgetEvent。P9：widget_name 缺省且画面为世界地图 → 地图级事件（点击切换）。</summary>
    public class AddEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "add_event", Description = "为控件/世界地图绑定事件-动作（同一事件多次调用累积动作，按序执行）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = false, Description = "控件名；缺省且画面为世界地图 = 地图级点击切换事件" },
                ["event_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllEventTypes },
                ["action_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllActionTypes },
                ["condition"] = new() { Type = "string", Required = false, Description = "I-3 事件触发条件（如 value > 80；传了则设置该事件条件，未传保持现状）", KeepInCompact = true },
                ["params"] = new() { Type = "dict", Required = false, Description = "动作参数键值对（如 tag_write 的 tag_name/value；screen_switch 的 target_screen）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (events, err) = EventBindingCommon.FindEventTargets(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event_type"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件类型: {p["event_type"]}");
            if (!Enum.TryParse<ActionType>(p["action_type"]!.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作类型: {p["action_type"]}");
            var actionParams = p.TryGetValue("params", out var raw) && raw is Dictionary<string, string> dict ? dict : new();
            // C12-15：screen_switch 目标画面容错匹配（精确/去空白/去括号后缀/包含），失败回执列候选
            if (act == ActionType.screen_switch && actionParams.TryGetValue("target_screen", out var target) && !string.IsNullOrWhiteSpace(target))
            {
                var resolved = EventBindingCommon.ResolveScreenSwitchTarget(project, target, out var cands);
                if (resolved == null)
                    return CommandResult.Fail("INVALID_PARAM", $"目标画面 \"{target}\" 未找到" + (cands.Count > 0 ? $"；候选: {string.Join(", ", cands)}" : ""));
                actionParams["target_screen"] = resolved;   // 落库为正式画面名
            }
            var we = events.FirstOrDefault(e => e.Type == evt);
            if (we == null)
            {
                we = new WidgetEvent { Type = evt };
                events.Add(we);
            }
            // I-3：condition 参数（可选）——传了则设置该事件触发条件（未传保持现状）
            if (p.TryGetValue("condition", out var cond) && cond != null)
                we.Condition = cond.ToString() ?? "";
            // AH 修复（2026-10-30 用户实测）：**完全相同的动作（类型+参数）不再重复追加**——
            // 旧行为让 AI 反复调 add_event 累积出 N 份相同动作（点一次执行 N 遍），并把编译错误刷爆。
            if (we.Actions.Any(a => a.Type == act && SameParams(a.Parameters, actionParams)))
                return CommandResult.Fail("DUPLICATE",
                    $"事件 {evt} 下已存在相同动作 {act}（参数一致）——如需改参数请用 update_event");
            we.Actions.Add(new EventAction { Type = act, Parameters = actionParams });
            return CommandResult.Ok(new Dictionary<string, object?> { ["event"] = evt.ToString(), ["action"] = act.ToString(), ["action_index"] = we.Actions.Count - 1 });
        }

        /// <summary>参数键值对是否完全一致（顺序无关）——重复动作判据（AH 2026-10-30）。</summary>
        private static bool SameParams(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }

    /// <summary>remove_event：移除控件/世界地图事件。指定 action_type → 移除该事件下匹配动作（空则事件也删）；否则移除整个事件。P9：widget_name 缺省 → 地图级。</summary>
    public class RemoveEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "remove_event", Description = "移除控件/世界地图事件（指定 action_type 仅移除该动作；否则移除整个事件）",
            Parameters = EventBindingCommon.SwParams(requireAction: true, widgetOptional: true),
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (events, err) = EventBindingCommon.FindEventTargets(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event_type"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件类型: {p["event_type"]}");
            var we = events.FirstOrDefault(e => e.Type == evt);
            if (we == null) return CommandResult.Fail("NOT_FOUND", $"未配置事件 {evt}");
            if (p.TryGetValue("action_type", out var raw) && !string.IsNullOrWhiteSpace(raw?.ToString()))
            {
                if (!Enum.TryParse<ActionType>(raw.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作类型: {raw}");
                var removed = we.Actions.RemoveAll(a => a.Type == act);
                if (removed == 0) return CommandResult.Fail("NOT_FOUND", $"事件 {evt} 下未找到动作 {act}");
                if (we.Actions.Count == 0) events.Remove(we);
                return CommandResult.Ok(new Dictionary<string, object?> { ["removed_actions"] = removed });
            }
            events.Remove(we);
            return CommandResult.Ok();
        }
    }

    /// <summary>update_event：更新控件/世界地图事件下某动作的参数（按 action_type 匹配，替换整组参数）。P9：widget_name 缺省 → 地图级。</summary>
    public class UpdateEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_event", Description = "更新控件/世界地图事件下动作的参数（按 action_type 匹配，整组替换）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = false, Description = "控件名；缺省且画面为世界地图 = 地图级点击切换事件" },
                ["event_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllEventTypes },
                ["action_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllActionTypes },
                ["condition"] = new() { Type = "string", Required = false, Description = "I-3 事件触发条件（如 value > 80；传了则更新该事件条件，未传保持现状）", KeepInCompact = true },
                ["params"] = new() { Type = "dict", Required = true, Description = "替换后的完整参数键值对" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (events, err) = EventBindingCommon.FindEventTargets(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event_type"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件类型: {p["event_type"]}");
            if (!Enum.TryParse<ActionType>(p["action_type"]!.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作类型: {p["action_type"]}");
            if (!(p.TryGetValue("params", out var raw) && raw is Dictionary<string, string> dict)) return CommandResult.Fail("INVALID_PARAM", "缺少 params 参数（完整键值对）");
            // C12-15：screen_switch 目标画面容错匹配（与 add_event 同规则）
            if (act == ActionType.screen_switch && dict.TryGetValue("target_screen", out var target) && !string.IsNullOrWhiteSpace(target))
            {
                var resolved = EventBindingCommon.ResolveScreenSwitchTarget(project, target, out var cands);
                if (resolved == null)
                    return CommandResult.Fail("INVALID_PARAM", $"目标画面 \"{target}\" 未找到" + (cands.Count > 0 ? $"；候选: {string.Join(", ", cands)}" : ""));
                dict["target_screen"] = resolved;   // 落库为正式画面名
            }
            // AH 修复（2026-10-30 用户实测「改了不生效」）：原来取 events.FirstOrDefault(...) + Actions.FirstOrDefault(...)，
            // **只改第一条**——而历史数据里同一事件类型可能有多条（旧 bind_event 每次新建一条），
            // 于是 AI 改完 7 条里的 1 条、编译仍报错、用户看到"改了没生效"。
            // 改为**更新所有匹配的（事件类型 × 动作类型）组合**，语义与 remove_event 的 RemoveAll 对齐。
            var targets = events.Where(e => e.Type == evt).ToList();
            if (targets.Count == 0) return CommandResult.Fail("NOT_FOUND", $"未找到事件 {evt}");
            var updated = 0;
            foreach (var we0 in targets)
            {
                // I-3：condition 参数（可选）——传了则更新该事件触发条件
                if (p.TryGetValue("condition", out var cond) && cond != null)
                    we0.Condition = cond.ToString() ?? "";
                foreach (var a in we0.Actions.Where(x => x.Type == act))
                {
                    a.Parameters = dict;   // 整组替换（同一份 dict 只读共享即可：后续不再改动）
                    updated++;
                }
            }
            if (updated == 0) return CommandResult.Fail("NOT_FOUND", $"事件 {evt} 下未找到动作 {act}");
            return CommandResult.Ok(new Dictionary<string, object?> { ["updated_actions"] = updated });
        }
    }
}
