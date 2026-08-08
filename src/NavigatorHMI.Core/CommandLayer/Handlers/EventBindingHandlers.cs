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
        internal static Dictionary<string, ParameterDefinition> SwParams(bool requireEvent = true, bool requireAction = false)
        {
            var d = new Dictionary<string, ParameterDefinition>
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
            };
            if (requireEvent) d["event_type"] = new() { Type = "enum", Required = true, EnumValues = AllEventTypes, Description = "事件类型（触发条件）" };
            if (requireAction) d["action_type"] = new() { Type = "enum", Required = true, EnumValues = AllActionTypes, Description = "动作类型（执行的函数）" };
            return d;
        }

        /// <summary>查找控件（复用 WidgetHelper）。</summary>
        internal static (Screen?, Widget?, CommandResult?) FindWidget(HMIProject project, Dictionary<string, object?> p)
            => WidgetHelper.FindWidget(project, p);
    }

    /// <summary>add_event：为控件事件追加动作。同事件类型已存在 → 追加 Action；不存在 → 新建 WidgetEvent。</summary>
    public class AddEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "add_event", Description = "为控件绑定事件-动作（同一事件多次调用累积动作，按序执行）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
                ["event_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllEventTypes },
                ["action_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllActionTypes },
                ["params"] = new() { Type = "dict", Required = false, Description = "动作参数键值对（如 tag_write 的 tag_name/value；screen_switch 的 target_screen）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (_, widget, err) = EventBindingCommon.FindWidget(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event_type"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件类型: {p["event_type"]}");
            if (!Enum.TryParse<ActionType>(p["action_type"]!.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作类型: {p["action_type"]}");
            var actionParams = p.TryGetValue("params", out var raw) && raw is Dictionary<string, string> dict ? dict : new();
            var we = widget!.Events.FirstOrDefault(e => e.Type == evt);
            if (we == null)
            {
                we = new WidgetEvent { Type = evt };
                widget.Events.Add(we);
            }
            we.Actions.Add(new EventAction { Type = act, Parameters = actionParams });
            return CommandResult.Ok(new Dictionary<string, object?> { ["event"] = evt.ToString(), ["action"] = act.ToString(), ["action_index"] = we.Actions.Count - 1 });
        }
    }

    /// <summary>remove_event：移除控件事件。指定 action_type → 移除该事件下匹配动作（空则事件也删）；否则移除整个事件。</summary>
    public class RemoveEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "remove_event", Description = "移除控件事件（指定 action_type 仅移除该动作；否则移除整个事件）",
            Parameters = EventBindingCommon.SwParams(requireAction: true),
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (_, widget, err) = EventBindingCommon.FindWidget(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event_type"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件类型: {p["event_type"]}");
            var we = widget!.Events.FirstOrDefault(e => e.Type == evt);
            if (we == null) return CommandResult.Fail("NOT_FOUND", $"控件 {p["widget_name"]} 未配置事件 {evt}");
            if (p.TryGetValue("action_type", out var raw) && !string.IsNullOrWhiteSpace(raw?.ToString()))
            {
                if (!Enum.TryParse<ActionType>(raw.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作类型: {raw}");
                var removed = we.Actions.RemoveAll(a => a.Type == act);
                if (removed == 0) return CommandResult.Fail("NOT_FOUND", $"事件 {evt} 下未找到动作 {act}");
                if (we.Actions.Count == 0) widget.Events.Remove(we);
                return CommandResult.Ok(new Dictionary<string, object?> { ["removed_actions"] = removed });
            }
            widget.Events.Remove(we);
            return CommandResult.Ok();
        }
    }

    /// <summary>update_event：更新控件事件下某动作的参数（按 action_type 匹配，替换整组参数）。</summary>
    public class UpdateEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_event", Description = "更新控件事件下动作的参数（按 action_type 匹配，整组替换）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
                ["event_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllEventTypes },
                ["action_type"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllActionTypes },
                ["params"] = new() { Type = "dict", Required = true, Description = "替换后的完整参数键值对" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (_, widget, err) = EventBindingCommon.FindWidget(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event_type"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件类型: {p["event_type"]}");
            if (!Enum.TryParse<ActionType>(p["action_type"]!.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作类型: {p["action_type"]}");
            if (!(p.TryGetValue("params", out var raw) && raw is Dictionary<string, string> dict)) return CommandResult.Fail("INVALID_PARAM", "缺少 params 参数（完整键值对）");
            var we = widget!.Events.FirstOrDefault(e => e.Type == evt);
            // 语义：同事件下同 action_type 重复添加时，update 只改首个匹配（与 remove 的 RemoveAll 全删不对称，属有意设计——UI 层按 action_index 精确操作，不会出现重复）
            var action = we?.Actions.FirstOrDefault(a => a.Type == act);
            if (action == null) return CommandResult.Fail("NOT_FOUND", $"控件 {p["widget_name"]} 事件 {evt} 下未找到动作 {act}");
            action.Parameters = dict;
            return CommandResult.Ok();
        }
    }
}
