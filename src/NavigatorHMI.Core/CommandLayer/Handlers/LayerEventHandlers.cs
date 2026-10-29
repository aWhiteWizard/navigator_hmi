using System.Linq;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>bring_to_front 命令：控件置于顶层（图层操作）。</summary>
    public class BringToFrontHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "bring_to_front", Description = "控件置于顶层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { var w = s.Widgets[i]; s.Widgets.RemoveAt(i); s.Widgets.Add(w); });
    }
    /// <summary>bring_forward 命令：控件上移一层（图层操作）。</summary>
    public class BringForwardHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "bring_forward", Description = "控件上移一层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { if (i < s.Widgets.Count - 1) s.Widgets.Move(i, i + 1); });
    }
    /// <summary>send_backward 命令：控件下移一层（图层操作）。</summary>
    public class SendBackwardHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "send_backward", Description = "控件下移一层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { if (i > 0) s.Widgets.Move(i, i - 1); });
    }
    /// <summary>send_to_back 命令：控件置于底层（图层操作）。</summary>
    public class SendToBackHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "send_to_back", Description = "控件置于底层", Parameters = LayerEventHelper.SwParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) =>
            LayerEventHelper.LayerOp(project, p, (s, i) => { var w = s.Widgets[i]; s.Widgets.RemoveAt(i); s.Widgets.Insert(0, w); });
    }
    /// <summary>bind_event 命令：为控件绑定事件-动作（事件类型/动作类型枚举校验 + 参数透传）。</summary>
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
            // AH 修复（2026-10-30 用户实测）：**同事件类型必须合并，不得每次都新建 WidgetEvent**。
            // 原实现无条件 Events.Add(new WidgetEvent...) ⇒ AI 反复调 bind_event 会堆出 N 条同类型事件；
            // FW 生成器逐条输出 `onHmiClicked:` ⇒ QML 报 "Property value set multiple times" ⇒
            // **整个画面组件加载失败 = 设备纯色屏**（实测 7 条同类型 onClick 即白屏）。
            // 语义对齐 AddEventHandler：同类型事件已存在则追加动作，不存在才新建。
            var we = widget!.Events.FirstOrDefault(e => e.Type == evt);
            if (we == null)
            {
                we = new WidgetEvent { Type = evt };
                widget.Events.Add(we);
            }
            we.Actions.Add(new EventAction { Type = act, Parameters = actionParams });
            return CommandResult.Ok(new Dictionary<string, object?>
            {
                ["event"] = evt.ToString(),
                ["action"] = act.ToString(),
                ["action_index"] = we.Actions.Count - 1,
                ["merged"] = we.Actions.Count > 1,   // true = 并入已有同类型事件（非新建）
            });
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
