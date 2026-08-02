using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>共享布局计算：阵列位置、对齐边界。GUI 预览与 Command 共用，避免逻辑分叉。</summary>
    public static class LayoutMath
    {
        /// <summary>
        /// 计算阵列中心点坐标（矩形网格交点 / 圆形轨迹）。
        /// 整圈（0~360°）按 360°/count 均匀分布首尾不重叠；非整圈首尾各在弧线两端。
        /// 返回 (X, Y) 元组列表，兼容无 WPF 依赖的 Core 层。
        /// </summary>
        public static List<(double X, double Y)> CalcPositions(bool isCircle, int count, int cols, int rows,
            double startX, double startY, double sx, double sy,
            double centerX, double centerY, double radius, double startAngle, double endAngle)
        {
            cols = Math.Max(1, cols); rows = Math.Max(1, rows);
            var result = new List<(double, double)>();
            if (isCircle)
            {
                double start = startAngle * Math.PI / 180, end = endAngle * Math.PI / 180;
                double total = (end > start ? end - start : 2 * Math.PI + end - start);
                bool fullCircle = total >= 2 * Math.PI - 1e-9;
                for (int i = 0; i < count; i++)
                {
                    double a = fullCircle
                        ? start + 2 * Math.PI * i / count
                        : start + total * i / Math.Max(count - 1, 1);
                    result.Add((centerX + radius * Math.Cos(a), centerY + radius * Math.Sin(a)));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    int r = i / cols, c = i % cols;
                    result.Add((startX + c * sx, startY + r * sy));
                }
            }
            return result;
        }

        /// <summary>按方向对齐控件（中心基准，与 GUI 对齐菜单同规则）。</summary>
        internal static void Align(List<Widget> widgets, string direction)
        {
            if (widgets.Count == 0) return;
            double minX = widgets.Min(w => w.X), minY = widgets.Min(w => w.Y);
            double maxX = widgets.Max(w => w.X + w.Width), maxY = widgets.Max(w => w.Y + w.Height);
            foreach (var w in widgets)
            {
                switch (direction)
                {
                    case "left": w.X = minX; break;
                    case "center_h": w.X = minX + (maxX - minX) / 2 - w.Width / 2; break;
                    case "right": w.X = maxX - w.Width; break;
                    case "top": w.Y = minY; break;
                    case "center_v": w.Y = minY + (maxY - minY) / 2 - w.Height / 2; break;
                    case "bottom": w.Y = maxY - w.Height; break;
                }
            }
        }
    }

    /// <summary>按名称列表解析画面中的控件（保持传入顺序）。</summary>
    internal static class WidgetListHelper
    {
        internal static List<Widget>? Resolve(Screen? screen, Dictionary<string, object?> p)
        {
            if (screen == null || !p.TryGetValue("widgets", out var raw) || raw == null) return null;
            var names = raw.ToString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 0) return null;
            var list = new List<Widget>(names.Length);
            foreach (var n in names)
            {
                var w = screen.Widgets.FirstOrDefault(x => x.ObjectName == n);
                if (w != null) list.Add(w);
            }
            return list.Count > 0 ? list : null;
        }
    }

    /// <summary>对齐多个控件（左/水平居中/右/顶/垂直居中/底）。</summary>
    public class AlignWidgetsHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "align_widgets", Description = "按方向对齐多个控件（左/水平居中/右/顶/垂直居中/底）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widgets"] = new() { Type = "string", Required = true, Description = "逗号分隔的控件名列表" },
                ["direction"] = new() { Type = "enum", Required = true, EnumValues = new[] { "left", "center_h", "right", "top", "center_v", "bottom" } },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: screen_name");
            if (!parameters.ContainsKey("widgets") || string.IsNullOrWhiteSpace(parameters["widgets"]?.ToString())) return ValidationResult.Fail("缺少必填参数: widgets");
            if (!parameters.ContainsKey("direction") || string.IsNullOrWhiteSpace(parameters["direction"]?.ToString())) return ValidationResult.Fail("缺少必填参数: direction");
            var dir = parameters["direction"]!.ToString()!;
            if (!new[] { "left", "center_h", "right", "top", "center_v", "bottom" }.Contains(dir)) return ValidationResult.Fail("无效方向: " + dir);
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var screen = WidgetHelper.FindScreen(project, parameters["screen_name"]!.ToString()!);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", "画面不存在");
            var widgets = WidgetListHelper.Resolve(screen, parameters);
            if (widgets == null) return CommandResult.Fail("NOT_FOUND", "未找到指定控件");
            LayoutMath.Align(widgets, parameters["direction"]!.ToString()!);
            return CommandResult.Ok(new { count = widgets.Count, direction = parameters["direction"] });
        }
    }

    /// <summary>阵列排列控件（矩形网格 / 圆形轨迹，中心点基准）。</summary>
    public class ArrayLayoutHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "array_layout", Description = "阵列排列多个控件（矩形: 起始中心/列行数/间距；圆形: 圆心/半径/起止角）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widgets"] = new() { Type = "string", Required = true, Description = "逗号分隔的控件名列表" },
                ["mode"] = new() { Type = "enum", Required = true, EnumValues = new[] { "rect", "circle" } },
                ["start_x"] = new() { Type = "double", DefaultValue = 0.0 },
                ["start_y"] = new() { Type = "double", DefaultValue = 0.0 },
                ["cols"] = new() { Type = "int", DefaultValue = 3 },
                ["rows"] = new() { Type = "int", DefaultValue = 2 },
                ["spacing_x"] = new() { Type = "double", DefaultValue = 120.0 },
                ["spacing_y"] = new() { Type = "double", DefaultValue = 80.0 },
                ["center_x"] = new() { Type = "double", DefaultValue = 0.0 },
                ["center_y"] = new() { Type = "double", DefaultValue = 0.0 },
                ["radius"] = new() { Type = "double", DefaultValue = 150.0 },
                ["start_angle"] = new() { Type = "double", DefaultValue = 0.0 },
                ["end_angle"] = new() { Type = "double", DefaultValue = 360.0 },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: screen_name");
            if (!parameters.ContainsKey("widgets") || string.IsNullOrWhiteSpace(parameters["widgets"]?.ToString())) return ValidationResult.Fail("缺少必填参数: widgets");
            if (!parameters.ContainsKey("mode") || string.IsNullOrWhiteSpace(parameters["mode"]?.ToString())) return ValidationResult.Fail("缺少必填参数: mode");
            var mode = parameters["mode"]!.ToString()!;
            if (mode != "rect" && mode != "circle") return ValidationResult.Fail("无效模式: " + mode);
            // 全量数值校验（无论当前 mode 是否使用该参数，所有数值参数都必须合法类型）——
            // 防止 Validate 按分支跳过、Execute 却 Convert 全部导致 FormatException 崩溃（校验不对称）
            foreach (var k in new[] { "start_x", "start_y", "spacing_x", "spacing_y", "center_x", "center_y", "radius", "start_angle", "end_angle" })
                if (parameters.TryGetValue(k, out var v) && v != null && !double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return ValidationResult.Fail($"{k} 必须是数字");
            foreach (var k in new[] { "cols", "rows" })
                if (parameters.TryGetValue(k, out var iv) && iv != null && !int.TryParse(iv.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                    return ValidationResult.Fail($"{k} 必须是整数");
            if (parameters.TryGetValue("cols", out var cv) && cv != null && int.TryParse(cv.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var cols) && cols < 1) return ValidationResult.Fail("列数必须 ≥ 1");
            if (parameters.TryGetValue("rows", out var rv) && rv != null && int.TryParse(rv.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var rows) && rows < 1) return ValidationResult.Fail("行数必须 ≥ 1");
            if (parameters.TryGetValue("radius", out var rv2) && rv2 != null && double.TryParse(rv2.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rad) && rad <= 0) return ValidationResult.Fail("半径必须 > 0");
            if (parameters.TryGetValue("start_angle", out var sav) && sav != null && double.TryParse(sav.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sa) && (sa < 0 || sa > 360)) return ValidationResult.Fail("起始角度必须在 0~360");
            if (parameters.TryGetValue("end_angle", out var eav) && eav != null && double.TryParse(eav.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ea) && (ea < 0 || ea > 360)) return ValidationResult.Fail("终止角度必须在 0~360");
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var screen = WidgetHelper.FindScreen(project, parameters["screen_name"]!.ToString()!);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", "画面不存在");
            var widgets = WidgetListHelper.Resolve(screen, parameters);
            if (widgets == null || widgets.Count < 2) return CommandResult.Fail("NOT_FOUND", "需要至少 2 个控件");

            bool isCircle = parameters["mode"]!.ToString() == "circle";
            // Execute 侧 TryParse + 默认值兜底（纵深防御；Validate 已全量校验，此处理论上不可达非法值）
            static int GetInt(Dictionary<string, object?> p, string k, int def)
                => p.TryGetValue(k, out var v) && v != null && int.TryParse(v.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var iv) ? iv : def;
            static double GetDbl(Dictionary<string, object?> p, string k, double def)
                => p.TryGetValue(k, out var v) && v != null && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : def;
            int cols = Math.Max(1, GetInt(parameters, "cols", 3));
            int rows = Math.Max(1, GetInt(parameters, "rows", 2));
            double sx = GetDbl(parameters, "spacing_x", 120);
            double sy = GetDbl(parameters, "spacing_y", 80);
            double cx = GetDbl(parameters, "center_x", 0);
            double cy = GetDbl(parameters, "center_y", 0);
            double radius = GetDbl(parameters, "radius", 150);
            double startAngle = GetDbl(parameters, "start_angle", 0);
            double endAngle = GetDbl(parameters, "end_angle", 360);
            double startX = GetDbl(parameters, "start_x", 0);
            double startY = GetDbl(parameters, "start_y", 0);

            var positions = LayoutMath.CalcPositions(isCircle, widgets.Count, cols, rows, startX, startY, sx, sy,
                cx, cy, radius, startAngle, endAngle);

            // 中心点基准落位：中心点 - 自身宽高/2，再钳制到画布（与 GUI 预览一致）
            double maxW = screen.Width > 0 ? screen.Width : project.DeviceWidth;
            double maxH = screen.Height > 0 ? screen.Height : project.DeviceHeight;
            for (int i = 0; i < Math.Min(positions.Count, widgets.Count); i++)
            {
                var w = widgets[i];
                w.X = Math.Max(0, Math.Min(positions[i].X - w.Width / 2, maxW - w.Width));
                w.Y = Math.Max(0, Math.Min(positions[i].Y - w.Height / 2, maxH - w.Height));
            }
            return CommandResult.Ok(new { count = widgets.Count, mode = parameters["mode"] });
        }
    }
}
