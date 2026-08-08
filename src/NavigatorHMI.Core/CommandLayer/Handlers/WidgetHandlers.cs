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
                ["widget_type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "button", "text", "rectangle", "label", "image", "numeric", "switch", "line", "circle", "ellipse", "iofield", "checkbox", "textlist", "textbox", "frame", "progressbar", "datetime" }, Description = "控件类型（textbox 为 textlist 兼容别名；datetime 为日期时间控件）" },
                ["x"] = new() { Type = "int", DefaultValue = 100 },
                ["y"] = new() { Type = "int", DefaultValue = 100 },
                ["width"] = new() { Type = "int", DefaultValue = 100 },
                ["height"] = new() { Type = "int", DefaultValue = 40 },
                ["bound_tag"] = new() { Type = "string", DefaultValue = "", Description = "绑定变量（可选，创建后立即绑定）" },
                ["center"] = new() { Type = "bool", DefaultValue = false, KeepInCompact = true, Description = "true 时置于画面中心（忽略 x/y；AI 指令「放在中心」用）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: screen_name");
            if (!parameters.ContainsKey("widget_type") || string.IsNullOrWhiteSpace(parameters["widget_type"]?.ToString())) return ValidationResult.Fail("缺少必填参数: widget_type");
            // 控件类型枚举校验（防未知类型静默走 default 创建 Button——静默失败比报错更危险）
            var wt = parameters["widget_type"]!.ToString()!;
            if (wt is not ("button" or "text" or "rectangle" or "label" or "image" or "numeric" or "switch" or "line"
                or "circle" or "ellipse" or "iofield" or "checkbox" or "textlist" or "textbox" or "frame" or "progressbar" or "datetime"))
                return ValidationResult.Fail($"未知控件类型: {wt}");
            if (!parameters.ContainsKey("x") || string.IsNullOrWhiteSpace(parameters["x"]?.ToString()))
                parameters["x"] = 100;   // 未提供默认 (100,100) 放置（AI/CLI 场景省参）
            if (!parameters.ContainsKey("y") || string.IsNullOrWhiteSpace(parameters["y"]?.ToString()))
                parameters["y"] = 100;
            if (parameters.GetValueOrDefault("center") is string cs && bool.TryParse(cs, out var cb)) parameters["center"] = cb;   // 字符串 bool 规范化
            // 数值参数校验（防 Convert.ToDouble 裸转崩溃）
            var numCheck = WidgetHelper.ValidateNumericParams(parameters, "x", "y", "width", "height");
            if (!numCheck.IsValid) return numCheck;
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var screen = WidgetHelper.FindScreen(project, parameters["screen_name"]!.ToString()!);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", "画面不存在");
            var widgetType = parameters["widget_type"]!.ToString()!;
            Widget widget = widgetType switch
            {
                "text" => new TextWidget(),
                "rectangle" => new RectangleWidget(),
                "label" => new LabelWidget { Text = "Label" },
                "image" => new ImageWidget(),
                "numeric" => new NumericDisplayWidget(),
                "switch" => new SwitchWidget(),
                "line" => new LineWidget { X2 = 100, Y2 = 0 },
                "circle" => new CircleWidget(),
                "ellipse" => new EllipseWidget(),
                "iofield" => new IOFieldWidget(),
                "checkbox" => new CheckBoxWidget(),
                "textlist" => new TextListWidget(),
                "textbox" => new TextListWidget(),   // 兼容别名：旧命令 textbox → TextListWidget
                "frame" => new FrameWidget(),
                "progressbar" => new ProgressBarWidget(),
                "datetime" => new DateTimeWidget { Text = "2026-01-01 00:00:00" },
                _ => new ButtonWidget { Text = "Button" }
            };
            widget.X = Convert.ToDouble(parameters["x"] ?? 0); widget.Y = Convert.ToDouble(parameters["y"] ?? 0);
            widget.Width = Convert.ToDouble(parameters.GetValueOrDefault("width", 100)); widget.Height = Convert.ToDouble(parameters.GetValueOrDefault("height", 40));
            if (parameters.GetValueOrDefault("center") is true)   // 置于画面中心（忽略 x/y；AI 指令「放在中心」）
            { widget.X = (screen.Width - widget.Width) / 2; widget.Y = (screen.Height - widget.Height) / 2; }
            widget.ObjectName = $"{widgetType}_{screen.Widgets.Count + 1}";
            // 可选：创建后立即绑定变量（拖拽生成绑定控件用）；类型兼容校验
            var boundTag = parameters.GetValueOrDefault("bound_tag")?.ToString() ?? "";
            if (boundTag.Length > 0)
            {
                var tag = project.Tags.FirstOrDefault(t => t.Name == boundTag);
                if (tag == null)
                    return CommandResult.Fail("NOT_FOUND", $"变量 \"{boundTag}\" 不存在");
                var incompat = TagCompatibility.Check(widget, tag);
                if (incompat != null)
                    return CommandResult.Fail("INVALID_TYPE", incompat);
                widget.BoundTag = boundTag;
            }
            screen.Widgets.Add(widget);
            return CommandResult.Ok(new { widget_name = widget.ObjectName });
        }
    }

    /// <summary>移动控件位置。</summary>
    public class MoveWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "move_widget", Description = "移动控件位置", Parameters = WidgetHelper.ScreenWidgetParams("x", "y") };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            var v = WidgetHelper.ValidateScreenWidget(p);
            if (!v.IsValid) return v;
            return WidgetHelper.ValidateNumericParams(p, "x", "y");
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p) => WidgetHelper.WithWidget(project, p, w => { w.X = Convert.ToDouble(p["x"] ?? 0); w.Y = Convert.ToDouble(p["y"] ?? 0); });
    }

    /// <summary>调整控件尺寸。</summary>
    public class ResizeWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new() { Name = "resize_widget", Description = "调整控件尺寸", Parameters = WidgetHelper.ScreenWidgetParams("width", "height") };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            var v = WidgetHelper.ValidateScreenWidget(p);
            if (!v.IsValid) return v;
            return WidgetHelper.ValidateNumericParams(p, "width", "height");
        }
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
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
                // 枚举：模型可见全部合法属性名（compact 保留 enum），避免猜错（如 imageList → 应为 listRef）
                ["key"] = new()
                {
                    Type = "enum", Required = true,
                    EnumValues = new[]
                    {
                        "text", "content", "title", "imagePath", "listRef", "defaultIndex",
                        "fontSize", "fontFamily", "fontWeight", "fontStyle", "textDecoration",
                        "textColor", "fillColor", "strokeColor", "strokeThickness",
                        "hAlign", "stretchMode", "fillStyle",
                        "isOn", "onText", "offText", "isChecked", "isReadOnly",
                        "value", "min", "max", "x2", "y2",
                    },
                    Description = "属性名（按控件类型生效，见 SetPropertyHandler）",
                },
                ["value"] = new() { Type = "string", Required = true, Description = "属性值（文本/数字/颜色/#RRGGBB/true/false）" },
            }
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

            // listRef 绑定前校验列表存在（防绑定幽灵列表：create_list 未建成/列表名错 → 渲染端列表不生效）
            if (key == "listRef" && !string.IsNullOrWhiteSpace(value)
                && !project.Lists.Any(l => l.Name == value))
                return CommandResult.Fail("NOT_FOUND", $"列表 \"{value}\" 不存在，请先 create-list 创建（或检查列表名是否与已有列表一致）");

            // 数值型 key 统一预校验（防 double.Parse 裸转抛 FormatException 崩溃）
            if (key is "fontSize" or "strokeThickness" or "value" or "min" or "max" or "x2" or "y2")
            {
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num)
                    || !double.IsFinite(num))
                    return CommandResult.Fail("INVALID_VALUE", $"属性 {key} 需要数字，收到: \"{value}\"");
            }
            // 索引型 key 整数预校验（defaultIndex 是列表项索引，拒绝小数/非数字，防 int.Parse 崩溃）
            if (key == "defaultIndex")
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return CommandResult.Fail("INVALID_VALUE", $"属性 {key} 需要整数索引，收到: \"{value}\"");
            }
            // 布尔型 key 统一预校验
            if (key is "isOn" or "isChecked" or "isReadOnly")
            {
                if (!bool.TryParse(value, out _))
                    return CommandResult.Fail("INVALID_VALUE", $"属性 {key} 需要 true/false，收到: \"{value}\"");
            }
            // 枚举型 key 统一预校验（合法值与 GUI 属性面板 ComboBox 一致）
            if (key is "fontWeight" or "fontStyle" or "textDecoration" or "hAlign" or "stretchMode" or "fillStyle")
            {
                if (!IsValidEnum(key, value))
                    return CommandResult.Fail("INVALID_VALUE", $"属性 {key} 取值无效: \"{value}\"（合法值: {EnumOptions(key)}）");
            }
            // 颜色型 key 统一预校验（与渲染端 ColorStringToBrushConverter 同规则：BrushConverter 可解析）
            if (key is "textColor" or "fillColor" or "strokeColor")
            {
                if (!IsValidColor(value))
                    return CommandResult.Fail("INVALID_VALUE", $"属性 {key} 需要颜色值（#RRGGBB / #AARRGGBB / 命名色如 Red），收到: \"{value}\"");
            }
            switch (widget)
            {
                case ButtonWidget btn when key == "text": btn.Text = value; break;
                case ButtonWidget w when key == "fontSize": w.FontSize = double.Parse(value); break;
                case ButtonWidget w when key == "fontFamily": w.FontFamily = value; break;
                case ButtonWidget w when key == "textColor": w.TextColor = value; break;
                case ButtonWidget w when key == "fillColor": w.FillColor = value; break;
                case ButtonWidget w when key == "fontWeight": w.FontWeight = value; break;
                case ButtonWidget w when key == "fontStyle": w.FontStyle = value; break;
                case ButtonWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case TextWidget txt when key == "content": txt.Content = value; break;
                case TextWidget txt when key == "fillColor": txt.FillColor = value; break;
                case TextWidget txt when key == "fontSize": txt.FontSize = double.Parse(value); break;
                case TextWidget txt when key == "fontWeight": txt.FontWeight = value; break;
                case TextWidget txt when key == "textColor": txt.TextColor = value; break;
                case TextWidget txt when key == "hAlign": txt.HAlign = value; break;
                case LabelWidget lbl when key == "hAlign": lbl.HAlign = value; break;
                case TextWidget w when key == "fontFamily": w.FontFamily = value; break;
                case TextWidget w when key == "fontStyle": w.FontStyle = value; break;
                case TextWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case RectangleWidget rect when key == "fillColor": rect.FillColor = value; break;
                case LabelWidget lbl when key == "text": lbl.Text = value; break;
                case LabelWidget lbl when key == "textColor": lbl.TextColor = value; break;
                case LabelWidget lbl when key == "fontSize": lbl.FontSize = double.Parse(value); break;
                case LabelWidget lbl when key == "fillColor": lbl.FillColor = value; break;
                case LabelWidget w when key == "fontFamily": w.FontFamily = value; break;
                case LabelWidget w when key == "fontWeight": w.FontWeight = value; break;
                case LabelWidget w when key == "fontStyle": w.FontStyle = value; break;
                case LabelWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case ImageWidget img when key == "imagePath": img.ImagePath = value; break;
                case ImageWidget img when key == "fillColor": img.FillColor = value; break;
                case ImageWidget img when key == "stretchMode": img.StretchMode = value; break;
                case NumericDisplayWidget nd when key == "value": nd.Value = double.Parse(value); break;
                case NumericDisplayWidget nd when key == "fillColor": nd.FillColor = value; break;
                case NumericDisplayWidget nd when key == "fontSize": nd.FontSize = double.Parse(value); break;
                case NumericDisplayWidget nd when key == "textColor": nd.TextColor = value; break;
                case NumericDisplayWidget w when key == "fontFamily": w.FontFamily = value; break;
                case NumericDisplayWidget w when key == "fontWeight": w.FontWeight = value; break;
                case NumericDisplayWidget w when key == "fontStyle": w.FontStyle = value; break;
                case NumericDisplayWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case SwitchWidget sw when key == "isOn": sw.IsOn = bool.Parse(value); break;
                case SwitchWidget sw when key == "onText": sw.OnText = value; break;
                case SwitchWidget sw when key == "offText": sw.OffText = value; break;
                case SwitchWidget w when key == "fontSize": w.FontSize = double.Parse(value); break;
                case SwitchWidget w when key == "fontFamily": w.FontFamily = value; break;
                case SwitchWidget w when key == "textColor": w.TextColor = value; break;
                case SwitchWidget w when key == "fillColor": w.FillColor = value; break;
                case SwitchWidget w when key == "fontWeight": w.FontWeight = value; break;
                case SwitchWidget w when key == "fontStyle": w.FontStyle = value; break;
                case SwitchWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case LineWidget line when key == "strokeColor": line.StrokeColor = value; break;
                case LineWidget line when key == "strokeThickness": line.StrokeThickness = double.Parse(value); break;
                case LineWidget line when key == "x2": line.X2 = double.Parse(value); break;
                case LineWidget line when key == "y2": line.Y2 = double.Parse(value); break;
                case CircleWidget c when key == "fillColor": c.FillColor = value; break;
                case CircleWidget c when key == "strokeColor": c.StrokeColor = value; break;
                case CircleWidget c when key == "strokeThickness": c.StrokeThickness = double.Parse(value); break;
                case EllipseWidget el when key == "fillColor": el.FillColor = value; break;
                case EllipseWidget el when key == "strokeColor": el.StrokeColor = value; break;
                case EllipseWidget el when key == "strokeThickness": el.StrokeThickness = double.Parse(value); break;
                case IOFieldWidget io when key == "content": io.Content = value; break;
                case IOFieldWidget io when key == "isReadOnly": io.IsReadOnly = bool.Parse(value); break;
                case IOFieldWidget io when key == "textColor": io.TextColor = value; break;
                case IOFieldWidget io when key == "fillColor": io.FillColor = value; break;
                case IOFieldWidget w when key == "fontSize": w.FontSize = double.Parse(value); break;
                case IOFieldWidget w when key == "fontFamily": w.FontFamily = value; break;
                case IOFieldWidget w when key == "fontWeight": w.FontWeight = value; break;
                case IOFieldWidget w when key == "fontStyle": w.FontStyle = value; break;
                case IOFieldWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case CheckBoxWidget cb when key == "text": cb.Text = value; break;
                case CheckBoxWidget cb when key == "isChecked": cb.IsChecked = bool.Parse(value); break;
                case CheckBoxWidget w when key == "fontSize": w.FontSize = double.Parse(value); break;
                case CheckBoxWidget w when key == "fontFamily": w.FontFamily = value; break;
                case CheckBoxWidget w when key == "textColor": w.TextColor = value; break;
                case CheckBoxWidget w when key == "fillColor": w.FillColor = value; break;
                case CheckBoxWidget w when key == "fontWeight": w.FontWeight = value; break;
                case CheckBoxWidget w when key == "fontStyle": w.FontStyle = value; break;
                case CheckBoxWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case TextListWidget tl when key == "textColor": tl.TextColor = value; break;
                case TextListWidget tl when key == "fillColor": tl.FillColor = value; break;
                case TextListWidget tl when key == "listRef": tl.ListRef = value; break;
                case TextListWidget tl when key == "defaultIndex": tl.DefaultIndex = int.Parse(value); break;
                case TextListWidget w when key == "fontSize": w.FontSize = double.Parse(value); break;
                case TextListWidget w when key == "fontFamily": w.FontFamily = value; break;
                case TextListWidget w when key == "fontWeight": w.FontWeight = value; break;
                case TextListWidget w when key == "fontStyle": w.FontStyle = value; break;
                case TextListWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case ImageWidget img when key == "listRef": img.ListRef = value; break;
                case ImageWidget img when key == "defaultIndex": img.DefaultIndex = int.Parse(value); break;
                case FrameWidget f when key == "title": f.Title = value; break;
                case FrameWidget f when key == "imagePath": f.ImagePath = value; break;
                case FrameWidget f when key == "fillColor": f.FillColor = value; break;
                case FrameWidget f when key == "listRef": f.ListRef = value; break;
                case FrameWidget f when key == "defaultIndex": f.DefaultIndex = int.Parse(value); break;
                case FrameWidget w when key == "fontSize": w.FontSize = double.Parse(value); break;
                case FrameWidget w when key == "fontFamily": w.FontFamily = value; break;
                case FrameWidget w when key == "fontWeight": w.FontWeight = value; break;
                case FrameWidget w when key == "fontStyle": w.FontStyle = value; break;
                case FrameWidget w when key == "textDecoration": w.TextDecoration = value; break;
                case ProgressBarWidget pb when key == "value": pb.Value = double.Parse(value); break;
                case ProgressBarWidget pb when key == "min": pb.Min = double.Parse(value); break;
                case ProgressBarWidget pb when key == "max": pb.Max = double.Parse(value); break;
                case ProgressBarWidget pb when key == "fillColor": pb.FillColor = value; break;
                case ProgressBarWidget pb when key == "fillStyle": pb.FillStyle = value; break;
                default: return CommandResult.Fail("UNKNOWN_PROPERTY", $"不支持属性: {key}");
            }
            return CommandResult.Ok();
        }

        /// <summary>枚举型属性合法值（单一数据源，校验与提示共用；与 GUI ComboBox 精确值一致）。</summary>
        private static readonly System.Collections.Generic.IReadOnlyDictionary<string, string[]> _enumOptions =
            new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["fontWeight"] = new[] { "Normal", "Bold" },
                ["fontStyle"] = new[] { "Normal", "Italic" },
                ["textDecoration"] = new[] { "None", "Underline" },
                ["hAlign"] = new[] { "Left", "Center", "Right" },
                ["stretchMode"] = new[] { "None", "Fill", "Uniform", "UniformToFill" },
                ["fillStyle"] = new[] { "Solid", "Diagonal", "Grid" },
            };

        /// <summary>校验枚举型属性值（大小写敏感，与 GUI 属性面板 ComboBox 精确值一致——保证 GUI 下拉回显；
        /// 渲染端除 hAlign 转换器精确匹配外，fontWeight/fontStyle 等走 WPF 原生转换器大小写宽容，
        /// 但保守拒绝小写可避免 GUI 下拉选中丢失）。</summary>
        private static bool IsValidEnum(string key, string value)
            => _enumOptions.TryGetValue(key, out var allowed) && allowed.Contains(value);

        /// <summary>枚举型属性的合法值列表（错误提示用）。</summary>
        private static string EnumOptions(string key)
            => _enumOptions.TryGetValue(key, out var allowed) ? string.Join(", ", allowed) : "";

        /// <summary>
        /// 校验颜色值（纯字符串校验，Core 层无 WPF 依赖）：
        /// 支持 #RGB / #RRGGBB / #ARGB / #AARRGGBB / 命名色（BrushConverter 常用子集）；
        /// 空串 = 透明（与渲染端 ColorStringToBrushConverter / ColorPickerControl 语义一致）；
        /// 拒绝 0x 前缀、其他非法格式。
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> _namedColors = new(System.StringComparer.OrdinalIgnoreCase)
        {
            // BrushConverter 命名色常用子集（完整集 ~141 个，此处收录常用；缺失项 CLI 提示改用 #RRGGBB）
            "Red", "Green", "Blue", "Yellow", "Black", "White", "Gray", "Grey", "Orange", "Pink",
            "Purple", "Brown", "Cyan", "Magenta", "Lime", "Navy", "Teal", "Silver", "Gold", "Beige",
            "Transparent", "LightBlue", "LightGray", "LightGrey", "DarkGray", "DarkGrey", "DarkRed",
            "DarkGreen", "DarkBlue", "LightGreen", "LightYellow", "LightCyan", "LightPink", "LightSalmon",
            "SkyBlue", "RoyalBlue", "SteelBlue", "DodgerBlue", "ForestGreen", "SeaGreen", "OrangeRed",
            "Tomato", "Coral", "Chocolate", "SaddleBrown", "Olive", "Khaki", "Lavender", "Ivory", "MintCream"
        };
        private static bool IsValidColor(string value)
        {
            if (value == null) return false;
            var s = value.Trim();
            // 空串 = 透明（渲染端同语义）
            if (s.Length == 0) return true;
            // 显式拒绝 0x/0X 前缀（十六进制数格式，非 CSS 颜色）
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
            // 命名色
            if (_namedColors.Contains(s)) return true;
            // 十六进制 #RGB / #RRGGBB / #ARGB / #AARRGGBB
            if (s.StartsWith('#'))
            {
                var hex = s[1..];
                if (hex.Length is 3 or 4 or 6 or 8)
                {
                    // 全部字符必须是十六进制数字
                    foreach (var c in hex)
                        if (!Uri.IsHexDigit(c)) return false;
                    return true;
                }
            }
            return false;
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

        /// <summary>校验数值参数（double.TryParse + IsFinite + InvariantCulture，防 Convert 裸转崩溃）。</summary>
        internal static ValidationResult ValidateNumericParams(Dictionary<string, object?> p, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (p.TryGetValue(k, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString())
                    && (!double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)))
                    return ValidationResult.Fail($"{k} 必须是数字");
            }
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
