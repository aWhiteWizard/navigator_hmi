using System.IO;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>W-A（P12 画面模板）：把指定画面控件布局保存为模板（深拷贝快照；重名 = 覆盖更新）。
    /// Template/WorldMap 画面受保护不可作模板源（与 copy_screen 保护一致——全局叠加/地图布局不进复用库）。</summary>
    public class SaveScreenTemplateHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "save_screen_template", Description = "保存画面为模板（控件布局深拷贝快照；重名=覆盖更新）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true, Description = "源画面（自定义画面）" },
                ["template_name"] = new() { Type = "string", Required = true, Description = "模板名（重名=覆盖更新）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: screen_name");
            if (!parameters.ContainsKey("template_name") || string.IsNullOrWhiteSpace(parameters["template_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: template_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var screenName = parameters["screen_name"]!.ToString()!;
            var templateName = parameters["template_name"]!.ToString()!;
            var screen = project.Screens.FirstOrDefault(s => s.Name == screenName);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", $"画面 \"{screenName}\" 不存在");
            if (screen.Type is ScreenType.Template or ScreenType.WorldMap)
                return CommandResult.Fail("PROTECTED", $"画面 \"{screenName}\" ({screen.Type}) 不可保存为模板（受保护画面）");

            // 控件布局深拷贝快照（独立对象图，无共享引用——改源画面/应用结果不影响模板）
            var snapshot = new ScreenTemplate { Name = templateName };
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, screen.Widgets);
                ms.Position = 0;
                var cloned = Serializer.Deserialize<System.Collections.ObjectModel.ObservableCollection<Widget>>(ms) ?? new();
                foreach (var w in cloned) snapshot.Widgets.Add(w);
            }

            var existing = project.Templates.FirstOrDefault(t => t.Name == templateName);
            bool updated = existing != null;
            if (updated)
            {
                existing.Widgets.Clear();
                foreach (var w in snapshot.Widgets) existing.Widgets.Add(w);
                // 🟡2：覆盖更新改的是嵌套集合（模板内部 Widgets），不在根集合 Templates 的置脏钩子观察范围——
                // 显式 MarkDirty（对齐 PropertyViewModel 对嵌套对象组显式标脏纪律；新增路径 Templates.Add 自动置脏）
                project.MarkDirty();
            }
            else
            {
                project.Templates.Add(snapshot);
            }
            return CommandResult.Ok(new { template_name = templateName, widgets = snapshot.Widgets.Count, updated });
        }
    }

    /// <summary>W-A（P12 画面模板）：把模板控件布局（深拷贝）应用到目标画面——目标画面控件整体替换为模板快照。
    /// Template/WorldMap 画面受保护不可作应用目标（全局叠加/地图不改）；应用后控件独立编辑（不联动模板/源画面）。</summary>
    public class ApplyScreenTemplateHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "apply_screen_template", Description = "应用模板到目标画面（目标画面控件整体替换为模板布局深拷贝）",
            Parameters = new()
            {
                ["template_name"] = new() { Type = "string", Required = true, Description = "模板名" },
                ["screen_name"] = new() { Type = "string", Required = true, Description = "目标画面（自定义画面，控件被模板布局替换）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("template_name") || string.IsNullOrWhiteSpace(parameters["template_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: template_name");
            if (!parameters.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(parameters["screen_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var templateName = parameters["template_name"]!.ToString()!;
            var screenName = parameters["screen_name"]!.ToString()!;
            var template = project.Templates.FirstOrDefault(t => t.Name == templateName);
            if (template == null) return CommandResult.Fail("NOT_FOUND", $"模板 \"{templateName}\" 不存在（请先 save-screen-template）");
            var screen = project.Screens.FirstOrDefault(s => s.Name == screenName);
            if (screen == null) return CommandResult.Fail("NOT_FOUND", $"画面 \"{screenName}\" 不存在");
            if (screen.Type is ScreenType.Template or ScreenType.WorldMap)
                return CommandResult.Fail("PROTECTED", $"画面 \"{screenName}\" ({screen.Type}) 受保护，不可应用模板");

            // 目标画面控件整体替换为模板布局深拷贝（独立对象图——应用后可独立编辑，不联动模板/源）
            var widgets = template.CloneWidgets();
            screen.Widgets.Clear();
            foreach (var w in widgets) screen.Widgets.Add(w);
            return CommandResult.Ok(new { screen_name = screenName, template_name = templateName, widgets = screen.Widgets.Count });
        }
    }
}
