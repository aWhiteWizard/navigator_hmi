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
                ["source"] = new() { Type = "string", DefaultValue = "", Description = "数据来源 (modbus:// 或 mqtt://)，空/缺省 = 内部变量" },
                ["unit"] = new() { Type = "string", DefaultValue = "", Description = "工程单位" },
                ["scan_interval"] = new() { Type = "int", DefaultValue = 100, Description = "采集周期 ms" },
                ["deadband"] = new() { Type = "double", DefaultValue = 0, Description = "变化死区" },
                ["description"] = new() { Type = "string", DefaultValue = "", Description = "描述" },
                ["base_value"] = new() { Type = "string", DefaultValue = "", Description = "基准值（设计态预览）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            if (!p.ContainsKey("data_type") || string.IsNullOrWhiteSpace(p["data_type"]?.ToString())) return ValidationResult.Fail("缺少必填参数: data_type");
            // source 可选：空/缺省 = 内部变量（无外部来源）
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
            var bvErr = BaseValueValidator.Check(p.GetValueOrDefault("base_value")?.ToString(), dt);
            if (bvErr != null) return CommandResult.Fail("INVALID_PARAM", bvErr);
            project.Tags.Add(new Tag
            {
                Name = name, DataType = dt,
                Source = p.GetValueOrDefault("source")?.ToString() ?? "",
                Unit = p.GetValueOrDefault("unit")?.ToString() ?? "",
                ScanIntervalMs = Convert.ToInt32(p.GetValueOrDefault("scan_interval", 100)),
                Deadband = Convert.ToDouble(p.GetValueOrDefault("deadband", 0), System.Globalization.CultureInfo.InvariantCulture),
                Description = p.GetValueOrDefault("description")?.ToString() ?? "",
                BaseValue = p.GetValueOrDefault("base_value")?.ToString() ?? "",
            });
            return CommandResult.Ok(new { tag_name = name });
        }
    }

    /// <summary>将控件绑定到变量（tag_name 为空 = 解除绑定）。</summary>
    public class BindTagHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "bind_tag", Description = "将控件绑定到变量（空 tag_name = 解绑）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["widget_name"] = new() { Type = "string", Required = true },
                // Required：AI compact schema 才保留 tag_name（绑定必须能指定变量）；空串 = 解绑（不设 DefaultValue，避免与 Required 矛盾诱导模型省略）
                ["tag_name"] = new() { Type = "string", Required = true, Description = "变量名，空 = 解除绑定" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name") || string.IsNullOrWhiteSpace(p["screen_name"]?.ToString())
             || !p.ContainsKey("widget_name") || string.IsNullOrWhiteSpace(p["widget_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: screen_name/widget_name");
            // tag_name 键必须存在（AI 忘传会静默解绑；解绑必须显式传空串）
            if (!p.ContainsKey("tag_name"))
                return ValidationResult.Fail("缺少必填参数: tag_name（绑定必须指定变量名；解绑请传空串）");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            // 键缺失（未提供）→ 报错防静默解绑；显式空串 = 解绑
            if (!p.ContainsKey("tag_name"))
                return CommandResult.Fail("INVALID_PARAM", "缺少必填参数: tag_name（绑定必须指定变量名；解绑请传空串）");
            var tagName = p["tag_name"]?.ToString() ?? "";
            var (_, widget, err) = WidgetHelper.FindWidget(project, p);
            if (err != null) return err;
            if (tagName.Length > 0)
            {
                var tag = project.Tags.FirstOrDefault(t => t.Name == tagName);
                if (tag == null) return CommandResult.Fail("NOT_FOUND", $"变量 \"{tagName}\" 不存在，请先 create-tag");
                // 类型兼容校验（数字控件绑数字、图片控件绑 STRING）
                var incompat = TagCompatibility.Check(widget!, tag);
                if (incompat != null) return CommandResult.Fail("INVALID_TYPE", incompat);
            }
            widget!.BoundTag = tagName;
            // 绑定非空变量后：控件设计态值由变量基准值控制，清除旧值（避免残留旧路径/旧数值）
            if (tagName.Length > 0)
                WidgetDesignValue.Clear(widget);
            return CommandResult.Ok(new { bound_tag = tagName });
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
            if (p.TryGetValue("deadband", out var d) && d != null)
            {
                var deadbandStr = d.ToString();
                if (string.IsNullOrWhiteSpace(deadbandStr))
                    return ValidationResult.Fail("deadband 必须是数字");
                if (!double.TryParse(deadbandStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    return ValidationResult.Fail("deadband 必须是数字");
                if (!double.IsFinite(dv))
                    return ValidationResult.Fail("deadband 必须是有限数字");
            }
            if (p.TryGetValue("delay_ms", out var dm) && dm != null)
            {
                var delayStr = dm.ToString();
                if (string.IsNullOrWhiteSpace(delayStr))
                    return ValidationResult.Fail("delay_ms 必须是整数");
                if (!int.TryParse(delayStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var dvc))
                    return ValidationResult.Fail("delay_ms 必须是整数");
                if (dvc < 0) return ValidationResult.Fail("delay_ms 不能为负");
            }
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
                Threshold = Convert.ToDouble(p["threshold"] ?? 0, System.Globalization.CultureInfo.InvariantCulture),
                Deadband = Convert.ToDouble(p.GetValueOrDefault("deadband", 0), System.Globalization.CultureInfo.InvariantCulture),
                DelayMs = Convert.ToInt32(p.GetValueOrDefault("delay_ms", 0), System.Globalization.CultureInfo.InvariantCulture),
                Level = sv,
                Message = p.GetValueOrDefault("message")?.ToString() ?? "",
            });
            return CommandResult.Ok(new { alarm_name = name });
        }
    }
    /// <summary>更新报警规则。仅更新提供的字段；重命名时校验唯一性（报警名无外部引用，直接改名）。</summary>
    public class UpdateAlarmHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_alarm", Description = "更新报警规则",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "原报警名称" },
                ["new_name"] = new() { Type = "string", Description = "新报警名称（重命名）" },
                ["tag_name"] = new() { Type = "string", Description = "关联变量名（必须已存在）" },
                ["type"] = new() { Type = "enum", EnumValues = new[] { "High", "Low", "RateChange", "Deviation" }, Description = "报警类型" },
                ["threshold"] = new() { Type = "double", Description = "阈值" },
                ["deadband"] = new() { Type = "double", Description = "回差" },
                ["delay_ms"] = new() { Type = "int", Description = "延迟 ms" },
                ["severity"] = new() { Type = "enum", EnumValues = new[] { "Emergency", "Important", "Warning", "Info" }, Description = "严重等级" },
                ["message"] = new() { Type = "string", Description = "报警描述；显式空串 = 清空" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            // 数值参数预校验（与 create_alarm 一致：TryParse + IsFinite 拦截 NaN/Infinity/非法字符串）
            foreach (var key in new[] { "threshold", "deadband" })
            {
                if (p.TryGetValue(key, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString())
                 && !double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    return ValidationResult.Fail($"{key} 必须是数字");
                if (p.TryGetValue(key, out v) && v != null && !string.IsNullOrWhiteSpace(v.ToString())
                 && double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv2)
                 && !double.IsFinite(dv2))
                    return ValidationResult.Fail($"{key} 必须是有限数字");
            }
            if (p.TryGetValue("delay_ms", out var dm) && dm != null && !string.IsNullOrWhiteSpace(dm.ToString()))
            {
                if (!int.TryParse(dm.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    return ValidationResult.Fail("delay_ms 必须是整数");
                if (dv < 0) return ValidationResult.Fail("delay_ms 不能为负");
            }
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var alarm = project.Alarms.FirstOrDefault(a => a.Name == name);
            if (alarm == null) return CommandResult.Fail("NOT_FOUND", $"报警 \"{name}\" 不存在");

            // ⚠️ 先验证后修改（强原子性）：全部存在性/枚举校验前置，任何字段落库前失败不产生半更新
            string? newName = null;
            if (p.TryGetValue("new_name", out var nn) && nn != null)
            {
                var trimmed = nn.ToString()!.Trim();
                if (trimmed.Length > 0 && trimmed != name)
                {
                    if (project.Alarms.Any(a => a.Name == trimmed))
                        return CommandResult.Fail("DUPLICATE", $"报警 \"{trimmed}\" 已存在");
                    newName = trimmed;
                }
            }

            string? tagName = null;
            if (p.TryGetValue("tag_name", out var tn) && tn != null && !string.IsNullOrWhiteSpace(tn.ToString()))
            {
                tagName = tn.ToString()!.Trim();
                if (!project.Tags.Any(t => t.Name == tagName))
                    return CommandResult.Fail("NOT_FOUND", $"变量 \"{tagName}\" 不存在，请先 create-tag");
            }

            if (p.TryGetValue("type", out var ty) && ty != null && !string.IsNullOrWhiteSpace(ty.ToString())
             && !Enum.TryParse<AlarmType>(ty.ToString(), ignoreCase: true, out _))
                return CommandResult.Fail("INVALID_PARAM", $"未知报警类型: {ty}");

            if (p.TryGetValue("severity", out var sv) && sv != null && !string.IsNullOrWhiteSpace(sv.ToString())
             && !Enum.TryParse<Severity>(sv.ToString(), ignoreCase: true, out _))
                return CommandResult.Fail("INVALID_PARAM", $"未知等级: {sv}");

            // 全部校验通过 → 统一落库
            if (newName != null) alarm.Name = newName;
            if (tagName != null) alarm.TagName = tagName;
            if (p.TryGetValue("type", out ty) && ty != null && !string.IsNullOrWhiteSpace(ty.ToString())
             && Enum.TryParse<AlarmType>(ty.ToString(), ignoreCase: true, out var at))
                alarm.Type = at;
            if (p.TryGetValue("threshold", out var th) && th != null && !string.IsNullOrWhiteSpace(th.ToString()))
                alarm.Threshold = Convert.ToDouble(th, System.Globalization.CultureInfo.InvariantCulture);
            if (p.TryGetValue("deadband", out var db) && db != null && !string.IsNullOrWhiteSpace(db.ToString()))
                alarm.Deadband = Convert.ToDouble(db, System.Globalization.CultureInfo.InvariantCulture);
            if (p.TryGetValue("delay_ms", out var dm) && dm != null && !string.IsNullOrWhiteSpace(dm.ToString()))
                alarm.DelayMs = Convert.ToInt32(dm, System.Globalization.CultureInfo.InvariantCulture);
            if (p.TryGetValue("severity", out sv) && sv != null && !string.IsNullOrWhiteSpace(sv.ToString())
             && Enum.TryParse<Severity>(sv.ToString(), ignoreCase: true, out var sv2))
                alarm.Level = sv2;

            // message 显式提供即写入（含空串 = 清空描述）
            if (p.TryGetValue("message", out var msg) && msg != null)
                alarm.Message = msg.ToString() ?? "";

            return CommandResult.Ok(new { alarm_name = alarm.Name });
        }
    }

    /// <summary>删除报警规则。报警不被人引用（反向：变量被报警引用由 delete_tag 保护），直接删除。</summary>
    public class DeleteAlarmHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_alarm", Description = "删除报警规则",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "报警名称" },
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
            var alarm = project.Alarms.FirstOrDefault(a => a.Name == name);
            if (alarm == null) return CommandResult.Fail("NOT_FOUND", $"报警 \"{name}\" 不存在");
            project.Alarms.Remove(alarm);
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
                ["source"] = new() { Type = "string", Description = "数据来源；未提供=保留现值，空串=清空为内部变量 (modbus:// 或 mqtt://)" },
                ["unit"] = new() { Type = "string", Description = "工程单位" },
                ["scan_interval"] = new() { Type = "int", Description = "采集周期 ms" },
                ["deadband"] = new() { Type = "double", Description = "变化死区" },
                ["description"] = new() { Type = "string", Description = "描述" },
                ["base_value"] = new() { Type = "string", Description = "基准值（设计态预览）", KeepInCompact = true },
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
            if (p.TryGetValue("source", out var src) && src != null)
                // 显式空串 = 清空为内部变量（OptIfProvided 语义：CLI 未提供不进字典，保留现值）
                tag.Source = src.ToString()?.Trim() ?? "";
            if (p.TryGetValue("unit", out var un) && un != null)
                tag.Unit = un.ToString() ?? "";
            if (p.TryGetValue("scan_interval", out var si) && si != null && !string.IsNullOrWhiteSpace(si.ToString()))
                tag.ScanIntervalMs = Convert.ToInt32(si);
            if (p.TryGetValue("deadband", out var db) && db != null && !string.IsNullOrWhiteSpace(db.ToString()))
                tag.Deadband = Convert.ToDouble(db);
            if (p.TryGetValue("description", out var desc) && desc != null)
                tag.Description = desc.ToString() ?? "";
            if (p.TryGetValue("base_value", out var bv) && bv != null)
            {
                var bvErr = BaseValueValidator.Check(bv.ToString(), tag.DataType);
                if (bvErr != null) return CommandResult.Fail("INVALID_PARAM", bvErr);
                tag.BaseValue = bv.ToString() ?? "";
            }

            return CommandResult.Ok(new { tag_name = tag.Name });
        }
    }
}

/// <summary>base_value（设计态预览基准值）按变量类型校验：非空时必须能解析为对应类型的数值，防 AI/CLI 传垃圾字符串进工程文件。</summary>
internal static class BaseValueValidator
{
    public static string? Check(string? raw, TagDataType dt)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;   // 空 = 清空/未提供，放行
        var s = raw.Trim();
        var ok = dt switch
        {
            TagDataType.FLOAT => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.INT16 => short.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.UINT16 => ushort.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.INT32 => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.BOOL => bool.TryParse(s, out _) || s is "1" or "0",
            _ => true,   // STRING 等任意文本
        };
        return ok ? null : $"base_value 不是合法的 {dt} 数值: '{raw}'";
    }
}
