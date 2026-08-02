using System.Collections.ObjectModel;
using NavigatorHMI.Common;
using ProtoBuf;

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
            // TODO: 名称 Trim 不对称——create_screen 不 Trim 而 rename_screen 的 GUI 层传 Trim 值，待统一命令层 Trim 规则
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
            // 同值重命名（未修改直接回车）：短路成功，不触发事件/标脏/树重建（GUI 层已短路，此处防 CLI 直接调用）
            if (string.Equals(name, newName, StringComparison.Ordinal))
                return CommandResult.Ok(new { screen_name = newName });

            screen.Name = newName;
            return CommandResult.Ok(new { screen_name = newName });
        }
    }

    /// <summary>画面剪贴板（CommandService 级，同进程会话内有效；含控件深拷贝）。</summary>
    public static class ScreenClipboard
    {
        internal static byte[]? Items { get; set; }

        /// <summary>写入剪贴板（GUI 复制时调用，与 CLI copy-screen 共用）。</summary>
        public static void SetItems(byte[] items) => Items = items;

        /// <summary>读取剪贴板。</summary>
        public static byte[]? GetItems() => Items;
    }

    /// <summary>复制画面到剪贴板（ProtoBuf 深拷贝含全部控件；Template/WorldMap 受保护）。</summary>
    public class CopyScreenHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "copy_screen", Description = "复制画面到剪贴板（含全部控件，同进程内可粘贴）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("name") || string.IsNullOrWhiteSpace(parameters["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var name = parameters["name"]?.ToString() ?? "";
            var screen = project.Screens.FirstOrDefault(s => s.Name == name);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", $"画面 \"{name}\" 不存在");
            if (screen.Type is ScreenType.Template or ScreenType.WorldMap)
                return CommandResult.Fail("PROTECTED", $"画面 \"{name}\" ({screen.Type}) 不可复制");

            using var ms = new MemoryStream();
            Serializer.Serialize(ms, screen);
            ScreenClipboard.SetItems(ms.ToArray());
            return CommandResult.Ok(new { copied = name, widgets = screen.Widgets.Count });
        }
    }

    /// <summary>从剪贴板粘贴画面（新名称自动去重，控件深拷贝）。</summary>
    public class PasteScreenHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "paste_screen", Description = "从剪贴板粘贴画面（新画面名自动去重）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = false, Description = "新画面名（默认 原名称_副本）" },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters) => ValidationResult.Ok;

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            if (ScreenClipboard.Items == null)
                return CommandResult.Fail("EMPTY_CLIPBOARD", "剪贴板为空，请先 copy-screen");

            Screen newScreen;
            using (var ms = new MemoryStream(ScreenClipboard.Items))
                newScreen = Serializer.Deserialize<Screen>(ms);
            if (newScreen == null) return CommandResult.Fail("CLIPBOARD_ERROR", "剪贴板数据无效");

            // 新画面名：显式指定或自动 原名称_副本
            string baseName = parameters.TryGetValue("name", out var nv) && nv != null && !string.IsNullOrWhiteSpace(nv.ToString())
                ? nv.ToString()!.Trim()
                : newScreen.Name + "_副本";
            var newName = UniqueScreenName(project, baseName);
            newScreen.Name = newName;
            newScreen.Type = ScreenType.Custom;   // 粘贴的画面始终为自定义（可再改名/删除）
            newScreen.IsGlobal = false;           // 全局画面工程中最多一个，粘贴副本不能是全局
            // 反序列化已创建全新对象图（含 Widgets 深拷贝），无需再复制集合
            project.Screens.Add(newScreen);
            return CommandResult.Ok(new { screen_name = newName, widgets = newScreen.Widgets.Count });
        }

        private static string UniqueScreenName(HMIProject project, string baseName)
        {
            var name = baseName;
            int n = 1;
            while (project.Screens.Any(s => s.Name == name))
            {
                name = baseName + "_" + n;
                n++;
            }
            return name;
        }
    }
}
