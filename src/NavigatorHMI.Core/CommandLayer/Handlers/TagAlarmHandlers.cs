using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>创建变量（Tag），添加到工程变量列表。</summary>
    public class CreateTagHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "create_tag", Description = "创建变量",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "变量名" },
                ["data_type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "BOOL", "INT16", "UINT16", "INT32", "FLOAT", "STRING" }, Description = "数据类型" },
                ["source"] = new() { Type = "string", Required = true, Description = "数据来源 (modbus:// 或 mqtt://)" },
                ["unit"] = new() { Type = "string", DefaultValue = "", Description = "工程单位" },
                ["scan_interval"] = new() { Type = "int", DefaultValue = 100, Description = "采集周期 ms" },
                ["deadband"] = new() { Type = "double", DefaultValue = 0, Description = "变化死区" },
                ["description"] = new() { Type = "string", DefaultValue = "", Description = "描述" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            if (!p.ContainsKey("data_type") || !p.ContainsKey("source") || string.IsNullOrWhiteSpace(p["source"]?.ToString())) return ValidationResult.Fail("缺少必填参数: data_type/source");
            // 数值参数预校验：整数+正数、有限数字（防 NaN/Infinity/负数写入工程，bugs §7）
            if (p.TryGetValue("scan_interval", out var si) && si != null && !string.IsNullOrWhiteSpace(si.ToString()))
            {
                if (!int.TryParse(si.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var scan))
                    return ValidationResult.Fail("scan_interval 必须是整数");
                if (scan <= 0) return ValidationResult.Fail("scan_interval 必须是正整数");
            }
            if (p.TryGetValue("deadband", out var db) && db != null && !string.IsNullOrWhiteSpace(db.ToString()))
            {
                if (!double.TryParse(db.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ddb))
                    return ValidationResult.Fail("deadband 必须是数字");
                if (!double.IsFinite(ddb)) return ValidationResult.Fail("deadband 必须是有限数字");
            }
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            if (project.Tags.Any(t => t.Name == name)) return CommandResult.Fail("DUPLICATE", $"变量 \"{name}\" 已存在");
            if (!Enum.TryParse<TagDataType>(p["data_type"]!.ToString(), ignoreCase: true, out var dt))
                return CommandResult.Fail("INVALID_PARAM", $"未知数据类型: {p["data_type"]}");
            project.Tags.Add(new Tag
            {
                Name = name, DataType = dt,
                Source = p["source"]!.ToString()!,
                Unit = p.GetValueOrDefault("unit")?.ToString() ?? "",
                ScanIntervalMs = Convert.ToInt32(p.GetValueOrDefault("scan_interval", 100)),
                Deadband = Convert.ToDouble(p.GetValueOrDefault("deadband", 0)),
                Description = p.GetValueOrDefault("description")?.ToString() ?? "",
            });
            return CommandResult.Ok(new { tag_name = name });
        }
    }

    /// <summary>将控件绑定到变量（设置 Widget.BoundTag）。</summary>
    public class BindTagHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "bind_tag", Description = "将控件绑定到变量",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
                ["tag_name"] = new() { Type = "string", Required = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name") || !p.ContainsKey("widget_name") || !p.ContainsKey("tag_name"))
                return ValidationResult.Fail("缺少必填参数");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            if (!project.Tags.Any(t => t.Name == p["tag_name"]!.ToString()))
                return CommandResult.Fail("NOT_FOUND", $"变量 \"{p["tag_name"]}\" 不存在，请先 create-tag");
            var (_, widget, err) = WidgetHelper.FindWidget(project, p);
            if (err != null) return err;
            widget!.BoundTag = p["tag_name"]!.ToString()!;
            return CommandResult.Ok();
        }
    }

    /// <summary>创建报警规则。</summary>
    public class CreateAlarmHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "create_alarm", Description = "创建报警规则",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "报警名称" },
                ["tag_name"] = new() { Type = "string", Required = true, Description = "关联变量名" },
                ["type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "High", "Low", "RateChange", "Deviation" }, Description = "报警类型" },
                ["threshold"] = new() { Type = "double", Required = true, Description = "阈值" },
                ["deadband"] = new() { Type = "double", DefaultValue = 0, Description = "回差" },
                ["delay_ms"] = new() { Type = "int", DefaultValue = 0, Description = "延迟 ms" },
                ["severity"] = new() { Type = "enum", DefaultValue = "Warning", EnumValues = new[] { "Emergency", "Important", "Warning", "Info" }, Description = "严重等级" },
                ["message"] = new() { Type = "string", DefaultValue = "", Description = "报警描述" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())
             || !p.ContainsKey("tag_name") || string.IsNullOrWhiteSpace(p["tag_name"]?.ToString())
             || !p.ContainsKey("type") || string.IsNullOrWhiteSpace(p["type"]?.ToString())
             || !p.ContainsKey("threshold"))
                return ValidationResult.Fail("缺少必填参数: name/tag_name/type/threshold");
            // 数值参数 TryParse 校验（拒绝非法/空字符串，防 Convert.ToDouble 抛 FormatException；IsFinite 拦截 NaN/Infinity）
            if (p.TryGetValue("threshold", out var t) && t != null
             && !double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tv))
                return ValidationResult.Fail("threshold 必须是数字");
            if (p.TryGetValue("threshold", out t) && t != null && double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tv2)
             && !double.IsFinite(tv2))
                return ValidationResult.Fail("threshold 必须是有限数字");
            if (p.TryGetValue("deadband", out var d) && d != null && !string.IsNullOrWhiteSpace(d.ToString())
             && !double.TryParse(d.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                return ValidationResult.Fail("deadband 必须是数字");
            if (p.TryGetValue("deadband", out d) && d != null && !string.IsNullOrWhiteSpace(d.ToString())
             && double.TryParse(d.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv2)
             && !double.IsFinite(dv2))
                return ValidationResult.Fail("deadband 必须是有限数字");
            if (p.TryGetValue("delay_ms", out var dm) && dm != null && !string.IsNullOrWhiteSpace(dm.ToString())
             && !int.TryParse(dm.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                return ValidationResult.Fail("delay_ms 必须是整数");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var tagName = p["tag_name"]!.ToString()!;
            if (!project.Tags.Any(t => t.Name == tagName))
                return CommandResult.Fail("NOT_FOUND", $"变量 \"{tagName}\" 不存在，请先 create-tag");
            if (!Enum.TryParse<AlarmType>(p["type"]!.ToString(), ignoreCase: true, out var at))
                return CommandResult.Fail("INVALID_PARAM", $"未知报警类型: {p["type"]}");
            if (!Enum.TryParse<Severity>(p.GetValueOrDefault("severity")?.ToString() ?? "Warning", ignoreCase: true, out var sv))
                return CommandResult.Fail("INVALID_PARAM", $"未知等级: {p["severity"]}");
            if (project.Alarms.Any(a => a.Name == name))
                return CommandResult.Fail("DUPLICATE", $"报警 \"{name}\" 已存在");
            project.Alarms.Add(new AlarmRule
            {
                Name = name, TagName = tagName, Type = at,
                Threshold = Convert.ToDouble(p["threshold"] ?? 0),
                Deadband = Convert.ToDouble(p.GetValueOrDefault("deadband", 0)),
                DelayMs = Convert.ToInt32(p.GetValueOrDefault("delay_ms", 0)),
                Level = sv,
                Message = p.GetValueOrDefault("message")?.ToString() ?? "",
            });
            return CommandResult.Ok(new { alarm_name = name });
        }
    }
    /// <summary>删除变量。若被控件或报警引用则拒绝删除（引用保护，防止绑定悬空）。</summary>
    public class DeleteTagHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_tag", Description = "删除变量（被引用时拒绝）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "变量名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var tag = project.Tags.FirstOrDefault(t => t.Name == name);
            if (tag == null) return CommandResult.Fail("NOT_FOUND", $"变量 \"{name}\" 不存在");

            // 引用检查：控件 BoundTag
            var widgetRefs = project.Screens
                .SelectMany(s => s.Widgets.Where(w => w.BoundTag == name)
                    .Select(w => $"{s.Name}/{w.ObjectName}"))
                .ToList();
            // 引用检查：报警 TagName
            var alarmRefs = project.Alarms.Where(a => a.TagName == name)
                .Select(a => a.Name).ToList();
            if (widgetRefs.Count > 0 || alarmRefs.Count > 0)
            {
                var parts = new List<string>();
                if (widgetRefs.Count > 0) parts.Add($"控件: {string.Join(", ", widgetRefs)}");
                if (alarmRefs.Count > 0) parts.Add($"报警: {string.Join(", ", alarmRefs)}");
                return CommandResult.Fail("IN_USE", $"变量 \"{name}\" 仍被引用（{string.Join("；", parts)}），请先解除绑定");
            }

            project.Tags.Remove(tag);
            return CommandResult.Ok(new { tag_name = name });
        }
    }

    /// <summary>更新变量定义。仅更新提供的字段；重命名时级联同步控件 BoundTag 与报警 TagName，保持引用一致。</summary>
    public class UpdateTagHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_tag", Description = "更新变量（重命名级联同步引用）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "原变量名" },
                ["new_name"] = new() { Type = "string", Description = "新变量名（重命名）" },
                ["data_type"] = new() { Type = "enum", EnumValues = new[] { "BOOL", "INT16", "UINT16", "INT32", "FLOAT", "STRING" }, Description = "数据类型" },
                ["source"] = new() { Type = "string", Description = "数据来源 (modbus:// 或 mqtt://)" },
                ["unit"] = new() { Type = "string", Description = "工程单位" },
                ["scan_interval"] = new() { Type = "int", Description = "采集周期 ms" },
                ["deadband"] = new() { Type = "double", Description = "变化死区" },
                ["description"] = new() { Type = "string", Description = "描述" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            if (p.TryGetValue("scan_interval", out var si) && si != null && !string.IsNullOrWhiteSpace(si.ToString()))
            {
                if (!int.TryParse(si.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var scan))
                    return ValidationResult.Fail("scan_interval 必须是整数");
                if (scan <= 0) return ValidationResult.Fail("scan_interval 必须是正整数");
            }
            if (p.TryGetValue("deadband", out var db) && db != null && !string.IsNullOrWhiteSpace(db.ToString()))
            {
                if (!double.TryParse(db.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ddb))
                    return ValidationResult.Fail("deadband 必须是数字");
                if (!double.IsFinite(ddb)) return ValidationResult.Fail("deadband 必须是有限数字");
            }
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var tag = project.Tags.FirstOrDefault(t => t.Name == name);
            if (tag == null) return CommandResult.Fail("NOT_FOUND", $"变量 \"{name}\" 不存在");

            // 重命名：唯一性校验 + 级联同步引用
            if (p.TryGetValue("new_name", out var nn) && nn != null && !string.IsNullOrWhiteSpace(nn.ToString()) && nn.ToString() != name)
            {
                var newName = nn.ToString()!;
                if (project.Tags.Any(t => t.Name == newName))
                    return CommandResult.Fail("DUPLICATE", $"变量 \"{newName}\" 已存在");
                // 级联：同步控件 BoundTag + 报警 TagName
                foreach (var screen in project.Screens)
                    foreach (var w in screen.Widgets.Where(w => w.BoundTag == name))
                        w.BoundTag = newName;
                foreach (var alarm in project.Alarms.Where(a => a.TagName == name))
                    alarm.TagName = newName;
                tag.Name = newName;
            }

            if (p.TryGetValue("data_type", out var dt) && dt != null && !string.IsNullOrWhiteSpace(dt.ToString()))
            {
                if (!Enum.TryParse<TagDataType>(dt.ToString(), ignoreCase: true, out var td))
                    return CommandResult.Fail("INVALID_PARAM", $"未知数据类型: {dt}");
                tag.DataType = td;
            }
            if (p.TryGetValue("source", out var src) && src != null && !string.IsNullOrWhiteSpace(src.ToString()))
                tag.Source = src.ToString()!;
            if (p.TryGetValue("unit", out var un) && un != null)
                tag.Unit = un.ToString() ?? "";
            if (p.TryGetValue("scan_interval", out var si) && si != null && !string.IsNullOrWhiteSpace(si.ToString()))
                tag.ScanIntervalMs = Convert.ToInt32(si);
            if (p.TryGetValue("deadband", out var db) && db != null && !string.IsNullOrWhiteSpace(db.ToString()))
                tag.Deadband = Convert.ToDouble(db);
            if (p.TryGetValue("description", out var desc) && desc != null)
                tag.Description = desc.ToString() ?? "";

            return CommandResult.Ok(new { tag_name = tag.Name });
        }
    }
}
