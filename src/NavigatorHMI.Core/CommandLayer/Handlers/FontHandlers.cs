using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>
    /// 设置工厂默认字体（全局 AppData 配置，影响新建控件；GUI「设置→默认字体」同源）。
    /// </summary>
    public class SetDefaultFontHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "set_default_font",
            Description = "设置工厂默认字体（全局配置，新建控件时生效）",
            Parameters = new()
            {
                ["font_family"] = new() { Type = "string", Required = false, Description = "字体族", KeepInCompact = true },
                ["font_size"] = new() { Type = "double", Required = false, Description = "字号", KeepInCompact = true },
                ["font_weight"] = new() { Type = "enum", Required = false, EnumValues = new[] { "Normal", "Bold" }, Description = "字重", KeepInCompact = true },
                ["font_style"] = new() { Type = "enum", Required = false, EnumValues = new[] { "Normal", "Italic" }, Description = "字型", KeepInCompact = true },
                ["text_decoration"] = new() { Type = "enum", Required = false, EnumValues = new[] { "None", "Underline" }, Description = "下划线", KeepInCompact = true },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            // 至少提供一个参数
            bool any = new[] { "font_family", "font_size", "font_weight", "font_style", "text_decoration" }
                .Any(k => parameters.TryGetValue(k, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString()));
            if (!any) return ValidationResult.Fail("至少提供一个字体参数: font_family/font_size/font_weight/font_style/text_decoration");

            if (parameters.TryGetValue("font_size", out var fs) && fs != null && !string.IsNullOrWhiteSpace(fs.ToString())
                && (!double.TryParse(fs.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size) || size <= 0 || !double.IsFinite(size)))
                return ValidationResult.Fail("font_size 必须是正数");

            // 枚举校验（与 GUI 属性面板 ComboBox 精确值一致）
            foreach (var k in new[] { "font_weight", "font_style", "text_decoration" })
            {
                if (!parameters.TryGetValue(k, out var v) || v == null || string.IsNullOrWhiteSpace(v.ToString())) continue;
                var value = v.ToString()!;
                var allowed = k switch
                {
                    "font_weight" => new[] { "Normal", "Bold" },
                    "font_style" => new[] { "Normal", "Italic" },
                    "text_decoration" => new[] { "None", "Underline" },
                    _ => Array.Empty<string>()
                };
                if (!allowed.Contains(value))
                    return ValidationResult.Fail($"{k} 取值无效: \"{value}\"（合法值: {string.Join(", ", allowed)}）");
            }
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            if (parameters.TryGetValue("font_family", out var ff) && ff != null && !string.IsNullOrWhiteSpace(ff.ToString()))
                WidgetFontDefaults.FontFamily = ff.ToString()!;
            if (parameters.TryGetValue("font_size", out var fs) && fs != null && !string.IsNullOrWhiteSpace(fs.ToString())
                && double.TryParse(fs.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size))
                WidgetFontDefaults.FontSize = size;
            if (parameters.TryGetValue("font_weight", out var fw) && fw != null && !string.IsNullOrWhiteSpace(fw.ToString()))
                WidgetFontDefaults.FontWeight = fw.ToString()!;
            if (parameters.TryGetValue("font_style", out var fst) && fst != null && !string.IsNullOrWhiteSpace(fst.ToString()))
                WidgetFontDefaults.FontStyle = fst.ToString()!;
            if (parameters.TryGetValue("text_decoration", out var td) && td != null && !string.IsNullOrWhiteSpace(td.ToString()))
                WidgetFontDefaults.TextDecoration = td.ToString()!;

            WidgetFontDefaults.Save();
            return CommandResult.Ok(new
            {
                font_family = WidgetFontDefaults.FontFamily,
                font_size = WidgetFontDefaults.FontSize,
                font_weight = WidgetFontDefaults.FontWeight,
                font_style = WidgetFontDefaults.FontStyle,
                text_decoration = WidgetFontDefaults.TextDecoration
            });
        }
    }
}
