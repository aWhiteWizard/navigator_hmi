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
                // 宽高：显式传参优先，否则回退到工程设备尺寸（create-project 时设置）
                Width = ResolveDimension(parameters, "width", project.DeviceWidth),
                Height = ResolveDimension(parameters, "height", project.DeviceHeight),
            };

            project.Screens.Add(screen);
            return CommandResult.Ok(new { screen_name = name });
        }

        /// <summary>解析宽/高参数：显式合法值优先，非法/缺失回退默认（工程设备尺寸）。</summary>
        private static double ResolveDimension(Dictionary<string, object?> parameters, string key, int fallback)
        {
            if (!parameters.TryGetValue(key, out var v) || v == null) return fallback;
            // 统一 InvariantCulture 序列化（避免逗号小数点 locale 下 ToString/TryParse 两侧不一致）
            var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            // NumberStyles.Float + IsFinite：拦截 NaN/Infinity 穿透（IEEE 特殊值 > 0 会误过 d>0 校验）
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)
                && double.IsFinite(d) && d > 0) return d;
            return fallback;
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

    /// <summary>
    /// 重命名画面。Template/WorldMap 受保护不可重命名。
    /// </summary>
    public class RenameScreenHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "rename_screen",
            Description = "重命名画面（Template/WorldMap 不可重命名）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "当前画面名称" },
                ["new_name"] = new() { Type = "string", Required = true, Description = "新画面名称" },
            }
        };

        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("name") || string.IsNullOrWhiteSpace(parameters["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            if (!parameters.ContainsKey("new_name") || string.IsNullOrWhiteSpace(parameters["new_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: new_name");
            return ValidationResult.Ok;
        }

        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var name = parameters["name"]?.ToString() ?? "";
            var newName = parameters["new_name"]?.ToString() ?? "";
            var screen = project.Screens.FirstOrDefault(s => s.Name == name);
            if (screen == null)
                return CommandResult.Fail("NOT_FOUND", $"画面 \"{name}\" 不存在");
            if (screen.Type is ScreenType.Template or ScreenType.WorldMap)
                return CommandResult.Fail("PROTECTED", $"画面 \"{name}\" ({screen.Type}) 不可重命名");
            if (project.Screens.Any(s => s.Name == newName && !ReferenceEquals(s, screen)))
                return CommandResult.Fail("DUPLICATE", $"画面名 \"{newName}\" 已存在");

            screen.Name = newName;
            return CommandResult.Ok(new { screen_name = newName });
        }
    }
}
