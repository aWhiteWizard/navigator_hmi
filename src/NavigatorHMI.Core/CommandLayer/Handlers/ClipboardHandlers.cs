using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>控件剪贴板存储（CommandService 级，同进程 REPL/GUI 面板会话内有效；独立 CLI 每次进程不共享）。
    /// GUI 按钮复制后同步写入（CopyWidgets 调用），使 GUI 复制的内容可在 CLI 面板粘贴。</summary>
    public static class WidgetClipboard
    {
        internal static List<byte[]>? Items { get; set; }

        /// <summary>写入剪贴板（GUI 复制时调用，与 CLI copy-widget 共用）。</summary>
        public static void SetItems(List<byte[]> items) => Items = items;

        /// <summary>读取剪贴板（供 GUI 粘贴时与自身 _clipboard 合并可选；当前仅 CLI 面板用）。</summary>
        public static List<byte[]>? GetItems() => Items;
    }

    /// <summary>复制控件到剪贴板（ProtoBuf 序列化，同进程内可粘贴）。</summary>
    public class CopyWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "copy_widget",
            Description = "复制控件到剪贴板（同一 CLI 会话内可 paste-widget）",
            Parameters = WidgetHelper.ScreenWidgetParams()
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters)
            => WidgetHelper.ValidateScreenWidget(parameters);

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var (_, widget, err) = WidgetHelper.FindWidget(project, parameters);
            if (err != null) return err;
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, widget!);
            WidgetClipboard.Items = new List<byte[]> { ms.ToArray() };
            return CommandResult.Ok(new { copied = widget!.ObjectName });
        }
    }

    /// <summary>从剪贴板粘贴控件到指定画面（新 ObjectName，偏移 +20px 防重叠）。</summary>
    public class PasteWidgetHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "paste_widget",
            Description = "从剪贴板粘贴控件到指定画面",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["x"] = new() { Type = "double", Required = false, Description = "粘贴位置 X（默认原位置+20）" },
                ["y"] = new() { Type = "double", Required = false, Description = "粘贴位置 Y（默认原位置+20）" },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: screen_name");
            // x/y 数值校验（防非法值静默回退默认）
            foreach (var k in new[] { "x", "y" })
            {
                if (parameters.TryGetValue(k, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString())
                    && (!double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)))
                    return ValidationResult.Fail($"{k} 必须是数字");
            }
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var screen = WidgetHelper.FindScreen(project, parameters["screen_name"]!.ToString()!);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", "画面不存在");
            if (WidgetClipboard.Items == null || WidgetClipboard.Items.Count == 0)
                return CommandResult.Fail("EMPTY_CLIPBOARD", "剪贴板为空，请先 copy-widget");

            double offsetX = 20, offsetY = 20;
            if (parameters.TryGetValue("x", out var xv) && xv != null && double.TryParse(xv.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) offsetX = x;
            if (parameters.TryGetValue("y", out var yv) && yv != null && double.TryParse(yv.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)) offsetY = y;

            var added = new List<string>();
            foreach (var data in WidgetClipboard.Items)
            {
                using var ms = new MemoryStream(data);
                var w = Serializer.Deserialize<Widget>(ms);
                w.X += offsetX;
                w.Y += offsetY;
                w.ObjectName = UniqueName(screen, w.ObjectName);
                screen.Widgets.Add(w);
                added.Add(w.ObjectName);
            }
            return CommandResult.Ok(new { count = added.Count, widgets = added });
        }

        private static string UniqueName(Screen screen, string baseName)
        {
            var name = baseName;
            int n = 1;
            while (screen.Widgets.Any(w => w.ObjectName == name))
            {
                // 尝试 button_1 → button_2 → button_3...（数字后缀递增）；无数字后缀则追加 _1 _2
                int i = baseName.Length;
                while (i > 0 && char.IsDigit(baseName[i - 1])) i--;
                var prefix = baseName[..i];
                var digits = baseName[i..];
                if (digits.Length > 0 && int.TryParse(digits, out var num))
                    name = prefix + (num + n);
                else
                    name = baseName + "_" + n;
                n++;
            }
            return name;
        }
    }
}
