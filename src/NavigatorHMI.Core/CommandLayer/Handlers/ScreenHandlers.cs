using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>
    /// 创建新画面。
    /// 向当前工程的 Screens 列表中添加一个 Screen 对象。
    /// 校验：画面名称不能与已有画面重复。
    /// </summary>
    public class CreateScreenHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "create_screen",
            Description = "创建新画面",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "画面名称" },
                ["type"] = new() { Type = "enum", DefaultValue = "custom", EnumValues = new[] { "custom", "template", "worldmap" }, Description = "画面类型" },
                ["width"] = new() { Type = "int", DefaultValue = 800, Description = "宽度" },
                ["height"] = new() { Type = "int", DefaultValue = 480, Description = "高度" },
            }
        };

        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("name") || string.IsNullOrWhiteSpace(parameters["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }

        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var name = parameters["name"]?.ToString() ?? "";
            if (project.Screens.Any(s => s.Name == name))
                return CommandResult.Fail("DUPLICATE", $"画面 \"{name}\" 已存在");

            var typeStr = parameters.GetValueOrDefault("type")?.ToString() ?? "custom";
            var type = typeStr switch
            {
                "template" => ScreenType.Template,
                "worldmap" => ScreenType.WorldMap,
                _ => ScreenType.Custom
            };

            var screen = new Screen
            {
                Name = name,
                Type = type,
                Width = Convert.ToDouble(parameters.GetValueOrDefault("width", 800)),
                Height = Convert.ToDouble(parameters.GetValueOrDefault("height", 480)),
            };

            project.Screens.Add(screen);
            return CommandResult.Ok(new { screen_name = name });
        }
    }

    /// <summary>
    /// 删除画面。
    /// Template 和 WorldMap 类型画面受保护，不可删除。
    /// </summary>
    public class DeleteScreenHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "delete_screen",
            Description = "删除画面（Template/WorldMap 不可删除）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "画面名称" },
            }
        };

        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("name") || string.IsNullOrWhiteSpace(parameters["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }

        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var name = parameters["name"]?.ToString() ?? "";
            var screen = project.Screens.FirstOrDefault(s => s.Name == name);
            if (screen == null)
                return CommandResult.Fail("NOT_FOUND", $"画面 \"{name}\" 不存在");
            if (screen.Type is ScreenType.Template or ScreenType.WorldMap)
                return CommandResult.Fail("PROTECTED", $"画面 \"{name}\" ({screen.Type}) 不可删除");

            project.Screens.Remove(screen);
            return CommandResult.Ok();
        }
    }
}
