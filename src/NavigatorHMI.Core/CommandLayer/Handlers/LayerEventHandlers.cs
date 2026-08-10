using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    public class BringToFrontHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "bring_to_front", Description = "控件置于顶层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { var w = s.Widgets[i]; s.Widgets.RemoveAt(i); s.Widgets.Add(w); });
    }
    public class BringForwardHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "bring_forward", Description = "控件上移一层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { if (i < s.Widgets.Count - 1) s.Widgets.Move(i, i + 1); });
    }
    public class SendBackwardHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "send_backward", Description = "控件下移一层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { if (i > 0) s.Widgets.Move(i, i - 1); });
    }
    public class SendToBackHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "send_to_back", Description = "控件置于底层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { var w = s.Widgets[i]; s.Widgets.RemoveAt(i); s.Widgets.Insert(0, w); });
    }
    public class BindEventHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "bind_event", Description = "为控件绑定事件-动作",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
                ["event"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllEventTypes },
                ["action"] = new() { Type = "enum", Required = true, EnumValues = EventBindingCommon.AllActionTypes },
                ["params"] = new() { Type = "dict", Required = false, KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (_, widget, err) = WidgetHelper.FindWidget(project, p);
            if (err != null) return err;
            if (!Enum.TryParse<EventType>(p["event"]!.ToString(), ignoreCase: true, out var evt)) return CommandResult.Fail("INVALID_PARAM", $"未知事件: {p["event"]}");
            if (!Enum.TryParse<ActionType>(p["action"]!.ToString(), ignoreCase: true, out var act)) return CommandResult.Fail("INVALID_PARAM", $"未知动作: {p["action"]}");
            var actionParams = p.TryGetValue("params", out var raw) && raw is Dictionary<string, string> dict ? dict : new();
            widget!.Events.Add(new WidgetEvent { Type = evt, Actions = new() { new EventAction { Type = act, Parameters = actionParams } } });
            return CommandResult.Ok();
        }
    }

    internal static class LayerEventHelper
    {
        internal delegate void LayerAction(Screen screen, int idx);
        internal static Dictionary<string, ParameterDefinition> SwParams() => new() { ["screen_name"] = new() { Type = "string", Required = true }, ["widget_name"] = new() { Type = "string", Required = true } };
        internal static CommandResult LayerOp(HMIProject project, Dictionary<string, object?> p, LayerAction action)
        {
            var (screen, widget, err) = WidgetHelper.FindWidget(project, p);
            if (err != null) return err;
            action(screen!, screen!.Widgets.IndexOf(widget!));
            return CommandResult.Ok();
        }
    }
}
