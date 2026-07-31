using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>添加控件到指定画面。</summary>
    public class AddWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "add_widget", Description = "在指定画面上放置一个控件",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "button", "text", "rectangle" } },
                ["x"] = new() { Type = "int", Required = true },
                ["y"] = new() { Type = "int", Required = true },
                ["width"] = new() { Type = "int", DefaultValue = 100 },
                ["height"] = new() { Type = "int", DefaultValue = 40 },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: screen_name");
            if (!parameters.ContainsKey("widget_type") || string.IsNullOrWhiteSpace(parameters["widget_type"]?.ToString())) return ValidationResult.Fail("缺少必填参数: widget_type");
            if (!parameters.ContainsKey("x") || !parameters.ContainsKey("y")) return ValidationResult.Fail("缺少必填参数: x/y");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var screen = WidgetHelper.FindScreen(project, parameters["screen_name"]!.ToString()!);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", "画面不存在");
            var widgetType = parameters["widget_type"]!.ToString()!;
            Widget widget = widgetType switch { "text" => new TextWidget(), "rectangle" => new RectangleWidget(), _ => new ButtonWidget { Text = "Button" } };
            widget.X = Convert.ToDouble(parameters["x"] ?? 0); widget.Y = Convert.ToDouble(parameters["y"] ?? 0);
            widget.Width = Convert.ToDouble(parameters.GetValueOrDefault("width", 100)); widget.Height = Convert.ToDouble(parameters.GetValueOrDefault("height", 40));
            widget.ObjectName = $"{widgetType}_{screen.Widgets.Count + 1}";
            screen.Widgets.Add(widget);
            return CommandResult.Ok(new { widget_name = widget.ObjectName });
        }
    }

    /// <summary>移动控件位置。</summary>
    public class MoveWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "move_widget", Description = "移动控件位置", Parameters = WidgetHelper.ScreenWidgetParams("x", "y") };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) => WidgetHelper.WithWidget(project, p, w => { w.X = Convert.ToDouble(p["x"] ?? 0); w.Y = Convert.ToDouble(p["y"] ?? 0); });
    }

    /// <summary>调整控件尺寸。</summary>
    public class ResizeWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "resize_widget", Description = "调整控件尺寸", Parameters = WidgetHelper.ScreenWidgetParams("width", "height") };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) => WidgetHelper.WithWidget(project, p, w => { w.Width = Convert.ToDouble(p["width"] ?? 0); w.Height = Convert.ToDouble(p["height"] ?? 0); });
    }

    /// <summary>删除控件。</summary>
    public class DeleteWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "delete_widget", Description = "删除控件", Parameters = WidgetHelper.ScreenWidgetParams() };
        public ValidationResult Validate(Dictionary<string, object?> p) => WidgetHelper.ValidateScreenWidget(p);
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (screen, widget, err) = WidgetHelper.FindWidget(project, p);
            if (err != null) return err;
            screen!.Widgets.Remove(widget!);
            return CommandResult.Ok();
        }
    }

    /// <summary>修改控件属性。</summary>
    public class SetPropertyHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "set_property", Description = "修改控件属性",
            Parameters = WidgetHelper.ScreenWidgetParams("key", "value")
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            var v = WidgetHelper.ValidateScreenWidget(p);
            if (!v.IsValid) return v;
            if (!p.ContainsKey("key") || !p.ContainsKey("value")) return ValidationResult.Fail("缺少必填参数: key/value");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (_, widget, err) = WidgetHelper.FindWidget(project, p);
            if (err != null) return err;
            var key = p["key"]!.ToString()!; var value = p["value"]!.ToString()!;
            switch (widget)
            {
                case ButtonWidget btn when key == "text": btn.Text = value; break;
                case TextWidget txt when key == "content": txt.Content = value; break;
                case RectangleWidget rect when key == "fillColor": rect.FillColor = value; break;
                default: return CommandResult.Fail("UNKNOWN_PROPERTY", $"不支持属性: {key}");
            }
            return CommandResult.Ok();
        }
    }

    /// <summary>Widget 操作工具方法。</summary>
    internal static class WidgetHelper
    {
        internal static Screen? FindScreen(HMIProject project, string name) => project.Screens.FirstOrDefault(s => s.Name == name);

        internal static (Screen?, Widget?, CommandResult?) FindWidget(HMIProject project, Dictionary<string, object?> p)
        {
            var screenName = p["screen_name"]!.ToString()!; var widgetName = p["widget_name"]!.ToString()!;
            var screen = project.Screens.FirstOrDefault(s => s.Name == screenName);
            if (screen == null) return (null, null, CommandResult.Fail("NOT_FOUND", $"画面 \"{screenName}\" 不存在"));
            var widget = screen.Widgets.FirstOrDefault(w => w.ObjectName == widgetName);
            if (widget == null) return (null, null, CommandResult.Fail("NOT_FOUND", $"控件 \"{widgetName}\" 不存在"));
            return (screen, widget, null);
        }

        internal static ValidationResult ValidateScreenWidget(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name") || !p.ContainsKey("widget_name")) return ValidationResult.Fail("缺少必填参数: screen_name/widget_name");
            return ValidationResult.Ok;
        }

        internal static CommandResult WithWidget(HMIProject project, Dictionary<string, object?> p, Action<Widget> action)
        {
            var (_, widget, err) = FindWidget(project, p);
            if (err != null) return err;
            action(widget!);
            return CommandResult.Ok();
        }

         internal static Dictionary<string, ParameterDefinition> ScreenWidgetParams(params string[] extra)
        {
            var d = new Dictionary<string, ParameterDefinition> {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true }
            };
            foreach (var e in extra) d[e] = new() { Type = "string", Required = true };
            return d;
        }
    }
}
