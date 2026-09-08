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
                ["data_type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "BOOL", "INT16", "UINT16", "INT32", "FLOAT", "STRING", "DATETIME", "GPS" }, Description = "数据类型" },
                ["source"] = new() { Type = "string", DefaultValue = "", Description = "数据来源 (modbus:// 或 mqtt://)，空/缺省 = 内部变量", KeepInCompact = true },
                ["unit"] = new() { Type = "string", DefaultValue = "", Description = "工程单位", KeepInCompact = true },
                ["scan_interval"] = new() { Type = "int", DefaultValue = 100, Description = "采集周期 ms", KeepInCompact = true },
                ["deadband"] = new() { Type = "double", DefaultValue = 0, Description = "变化死区", KeepInCompact = true },
                ["description"] = new() { Type = "string", DefaultValue = "", Description = "描述" },
                ["base_value"] = new() { Type = "string", DefaultValue = "", Description = "基准值（设计态预览）", KeepInCompact = true },
                ["group"] = new() { Type = "string", DefaultValue = "", Description = "Y-5a 变量分组（空 = 未分组）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            if (!p.ContainsKey("data_type") || string.IsNullOrWhiteSpace(p["data_type"]?.ToString())) return ValidationResult.Fail("缺少必填参数: data_type");
            // Y-5a reviewer 🟡：组名「未分组」为哨兵保留字（list-tags --group 过滤语义）——拒绝真实组用它
            var grp0 = p.GetValueOrDefault("group")?.ToString();
            if (grp0 != null && grp0.Trim() == Tag.UngroupedSentinel)
                return ValidationResult.Fail($"组名 \"{Tag.UngroupedSentinel}\" 为保留字（表示未分组），请换组名");
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
            // 任务8：DATETIME 变量未提供基准值 → 默认全 0 字面（0000:00:00 00:00:00，年月日冒号分隔）
            // D4/#4：数字变量未提供基准值 → 默认 0（FLOAT 用 0.0；整数用 0；BOOL 用 false）
            var baseValue = BaseValueValidator.Normalize(p.GetValueOrDefault("base_value")?.ToString() ?? "", dt);
            if (dt == TagDataType.DATETIME && string.IsNullOrEmpty(baseValue)) baseValue = "0000:00:00 00:00:00";
            else if (string.IsNullOrEmpty(baseValue) && dt is TagDataType.FLOAT) baseValue = "0.0";
            else if (string.IsNullOrEmpty(baseValue) && dt is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32) baseValue = "0";
            else if (string.IsNullOrEmpty(baseValue) && dt is TagDataType.BOOL) baseValue = "false";   // #4：BOOL 基准值默认 false
            if (dt == TagDataType.BOOL && baseValue.Length > 0) baseValue = baseValue.ToLowerInvariant();   // #4：BOOL 归一存储（True/TRUE→true，源头消除大小写变体防 GUI 回填翻转）
            project.Tags.Add(new Tag
            {
                Name = name, DataType = dt,
                Source = p.GetValueOrDefault("source")?.ToString() ?? "",
                Unit = p.GetValueOrDefault("unit")?.ToString() ?? "",
                ScanIntervalMs = Convert.ToInt32(p.GetValueOrDefault("scan_interval", 100)),
                Deadband = Convert.ToDouble(p.GetValueOrDefault("deadband", 0), System.Globalization.CultureInfo.InvariantCulture),
                Description = p.GetValueOrDefault("description")?.ToString() ?? "",
                BaseValue = baseValue,
                Group = p.GetValueOrDefault("group")?.ToString() ?? "",   // Y-5a：变量分组（空 = 未分组）
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
                ["deadband"] = new() { Type = "double", DefaultValue = 0, Description = "回差", KeepInCompact = true },
                ["delay_ms"] = new() { Type = "int", DefaultValue = 0, Description = "延迟 ms", KeepInCompact = true },
                ["severity"] = new() { Type = "enum", DefaultValue = "Warning", EnumValues = new[] { "Emergency", "Important", "Warning", "Info" }, Description = "严重等级", KeepInCompact = true },
                ["message"] = new() { Type = "string", DefaultValue = "", Description = "报警描述", KeepInCompact = true },
                ["trigger_mode"] = new() { Type = "enum", DefaultValue = "Threshold", EnumValues = new[] { "Threshold", "OnRising", "OnFalling", "OnChange" }, Description = "触发模式（BOOL 位沿）", KeepInCompact = true },
                ["category"] = new() { Type = "enum", DefaultValue = "User", EnumValues = new[] { "System", "User", "Error" }, Description = "报警类别" },
                ["priority"] = new() { Type = "int", DefaultValue = 0, Description = "排序优先级（大者靠前）" },
                ["ack_required"] = new() { Type = "bool", DefaultValue = true, Description = "需要确认", KeepInCompact = true },
                ["ack_group"] = new() { Type = "string", DefaultValue = "", Description = "确认组（同组联动确认）" },
                ["color_override"] = new() { Type = "string", DefaultValue = "", Description = "颜色图标覆盖（十六进制）" },
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
            if (p.TryGetValue("severity", out var sv0) && sv0 != null && !string.IsNullOrWhiteSpace(sv0.ToString())
             && !Enum.TryParse<Severity>(sv0.ToString(), ignoreCase: true, out _))
                return ValidationResult.Fail("未知等级: " + sv0);
            if (p.TryGetValue("trigger_mode", out var tm0) && tm0 != null && !string.IsNullOrWhiteSpace(tm0.ToString())
             && !Enum.TryParse<AlarmTriggerMode>(tm0.ToString(), ignoreCase: true, out _))
                return ValidationResult.Fail("未知触发模式: " + tm0);
            if (p.TryGetValue("category", out var cat0) && cat0 != null && !string.IsNullOrWhiteSpace(cat0.ToString())
             && !Enum.TryParse<AlarmCategory>(cat0.ToString(), ignoreCase: true, out _))
                return ValidationResult.Fail("未知类别: " + cat0);
            if (p.TryGetValue("priority", out var pr0) && pr0 != null && !string.IsNullOrWhiteSpace(pr0.ToString())
             && !int.TryParse(pr0.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                return ValidationResult.Fail("priority 必须是整数");
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
                TriggerMode = Enum.TryParse<AlarmTriggerMode>(p.GetValueOrDefault("trigger_mode")?.ToString() ?? "Threshold", ignoreCase: true, out var tm) ? tm : AlarmTriggerMode.Threshold,
                Category = Enum.TryParse<AlarmCategory>(p.GetValueOrDefault("category")?.ToString() ?? "User", ignoreCase: true, out var cat) ? cat : AlarmCategory.User,
                Priority = int.TryParse(p.GetValueOrDefault("priority", 0)?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pri) ? pri : 0,
                AckRequired = p.TryGetValue("ack_required", out var ar) && ar != null
                    ? (ar is bool ab ? ab : (bool.TryParse(ar.ToString(), out var bv) ? bv : ar.ToString() == "1"))
                    : true,
                AckGroup = p.GetValueOrDefault("ack_group")?.ToString() ?? "",
                ColorOverride = p.GetValueOrDefault("color_override")?.ToString() ?? "",
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
                ["new_name"] = new() { Type = "string", Description = "新报警名称（重命名）", KeepInCompact = true },
                ["tag_name"] = new() { Type = "string", Description = "关联变量名（必须已存在）", KeepInCompact = true },
                ["type"] = new() { Type = "enum", EnumValues = new[] { "High", "Low", "RateChange", "Deviation" }, Description = "报警类型", KeepInCompact = true },
                ["threshold"] = new() { Type = "double", Description = "阈值", KeepInCompact = true },
                ["deadband"] = new() { Type = "double", Description = "回差", KeepInCompact = true },
                ["delay_ms"] = new() { Type = "int", Description = "延迟 ms", KeepInCompact = true },
                ["severity"] = new() { Type = "enum", EnumValues = new[] { "Emergency", "Important", "Warning", "Info" }, Description = "严重等级", KeepInCompact = true },
                ["message"] = new() { Type = "string", Description = "报警描述；显式空串 = 清空", KeepInCompact = true },
                ["trigger_mode"] = new() { Type = "enum", EnumValues = new[] { "Threshold", "OnRising", "OnFalling", "OnChange" }, Description = "触发模式", KeepInCompact = true },
                ["category"] = new() { Type = "enum", EnumValues = new[] { "System", "User", "Error" }, Description = "报警类别", KeepInCompact = true },
                ["priority"] = new() { Type = "int", Description = "排序优先级", KeepInCompact = true },
                ["ack_required"] = new() { Type = "bool", Description = "需要确认", KeepInCompact = true },
                ["ack_group"] = new() { Type = "string", Description = "确认组", KeepInCompact = true },
                ["color_override"] = new() { Type = "string", Description = "颜色图标覆盖" },
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
            // W1 枚举/整数校验（与 delay_ms 平级——不随分支跳过）
            foreach (var en in new[] { "severity", "trigger_mode", "category" })
            {
                if (p.TryGetValue(en, out var ev) && ev != null && !string.IsNullOrWhiteSpace(ev.ToString()))
                {
                    bool ok = en switch
                    {
                        "severity" => Enum.TryParse<Severity>(ev.ToString(), ignoreCase: true, out _),
                        "trigger_mode" => Enum.TryParse<AlarmTriggerMode>(ev.ToString(), ignoreCase: true, out _),
                        _ => Enum.TryParse<AlarmCategory>(ev.ToString(), ignoreCase: true, out _),
                    };
                    if (!ok) return ValidationResult.Fail($"未知{en}: " + ev);
                }
            }
            if (p.TryGetValue("priority", out var prv) && prv != null && !string.IsNullOrWhiteSpace(prv.ToString())
             && !int.TryParse(prv.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _))
                return ValidationResult.Fail("priority 必须是整数");
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
            // W1 新字段（显式提供即写入）
            if (p.TryGetValue("trigger_mode", out var tm) && tm != null && !string.IsNullOrWhiteSpace(tm.ToString())
             && Enum.TryParse<AlarmTriggerMode>(tm.ToString(), ignoreCase: true, out var tm2))
                alarm.TriggerMode = tm2;
            if (p.TryGetValue("category", out var cat) && cat != null && !string.IsNullOrWhiteSpace(cat.ToString())
             && Enum.TryParse<AlarmCategory>(cat.ToString(), ignoreCase: true, out var cat2))
                alarm.Category = cat2;
            if (p.TryGetValue("priority", out var pri) && pri != null && !string.IsNullOrWhiteSpace(pri.ToString())
             && int.TryParse(pri.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var pri2))
                alarm.Priority = pri2;
            if (p.TryGetValue("ack_required", out var ar) && ar != null)
                alarm.AckRequired = ar is bool ab2 ? ab2 : (bool.TryParse(ar.ToString(), out var bv2) ? bv2 : ar.ToString() == "1");
            if (p.TryGetValue("ack_group", out var ag) && ag != null)
                alarm.AckGroup = ag.ToString() ?? "";
            if (p.TryGetValue("color_override", out var co) && co != null)
                alarm.ColorOverride = co.ToString() ?? "";

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
            // Y-6（2026-09-10）：MQTT 映射引用（MqttBinding.TagName——三层映射绑定变量被删 → 拒绝）
            var mqttRefs = project.MqttSettings?.Bindings
                .Where(b => b.TagName == name)
                .Select(b => $"MQTT:{b.TopicName}↔{b.FieldName}").ToList() ?? new List<string>();
            // Y-6：事件动作参数引用（tag_write/条件表达式等动作 parameters["tag_name"] == name——画面控件事件 + 世界地图事件）
            var eventRefs = new List<string>();
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                    foreach (var ev in w.Events)
                        foreach (var act in ev.Actions)
                            if (act.Parameters.TryGetValue("tag_name", out var tn) && tn == name)
                                eventRefs.Add($"{screen.Name}/{w.ObjectName}.{ev.Type}");
            if (project.WorldMap != null)
                foreach (var ev in project.WorldMap.Events)
                    foreach (var act in ev.Actions)
                        if (act.Parameters.TryGetValue("tag_name", out var tn) && tn == name)
                            eventRefs.Add($"世界地图.{ev.Type}");
            // Y-6 reviewer 🟡1：世界地图作业点/范围点绑定 GPS 变量（BoundTag）→ 删除拒绝（防地图点悬空失联）
            var mapRefs = new List<string>();
            if (project.WorldMap != null)
            {
                foreach (var wp in project.WorldMap.WorkPoints.Where(w => w.BoundTag == name))
                    mapRefs.Add($"作业点:{wp.Name}");
                int rpCount = project.WorldMap.WorkRangePoints.Count(w => w.BoundTag == name);
                if (rpCount > 0) mapRefs.Add($"作业范围点×{rpCount}");
            }
            if (widgetRefs.Count > 0 || alarmRefs.Count > 0 || mqttRefs.Count > 0 || eventRefs.Count > 0 || mapRefs.Count > 0)
            {
                var parts = new List<string>();
                if (widgetRefs.Count > 0) parts.Add($"控件: {string.Join(", ", widgetRefs)}");
                if (alarmRefs.Count > 0) parts.Add($"报警: {string.Join(", ", alarmRefs)}");
                if (mqttRefs.Count > 0) parts.Add($"MQTT 映射: {string.Join(", ", mqttRefs)}");
                if (eventRefs.Count > 0) parts.Add($"事件动作: {string.Join(", ", eventRefs)}");
                if (mapRefs.Count > 0) parts.Add($"地图绑定: {string.Join(", ", mapRefs)}");
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
                ["new_name"] = new() { Type = "string", Description = "新变量名（重命名）", KeepInCompact = true },
                ["data_type"] = new() { Type = "enum", EnumValues = new[] { "BOOL", "INT16", "UINT16", "INT32", "FLOAT", "STRING", "DATETIME", "GPS" }, Description = "数据类型", KeepInCompact = true },
                ["source"] = new() { Type = "string", Description = "数据来源；未提供=保留现值，空串=清空为内部变量 (modbus:// 或 mqtt://)", KeepInCompact = true },
                ["unit"] = new() { Type = "string", Description = "工程单位", KeepInCompact = true },
                ["scan_interval"] = new() { Type = "int", Description = "采集周期 ms", KeepInCompact = true },
                ["deadband"] = new() { Type = "double", Description = "变化死区", KeepInCompact = true },
                ["description"] = new() { Type = "string", Description = "描述" },
                ["base_value"] = new() { Type = "string", Description = "基准值（设计态预览）", KeepInCompact = true },
                ["group"] = new() { Type = "string", Description = "Y-5a 变量分组；显式空串 = 清空为未分组；留空 = 不改", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            // Y-5a reviewer 🟡：组名「未分组」为哨兵保留字——更新亦拒绝（防存量/误传建立冲突组）
            var grp0 = p.GetValueOrDefault("group")?.ToString();
            if (grp0 != null && grp0.Trim() == Tag.UngroupedSentinel)
                return ValidationResult.Fail($"组名 \"{Tag.UngroupedSentinel}\" 为保留字（表示未分组），请换组名");
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

            // ══ 全部可失败校验前置（组合参数原子性：任何失败不改变任何状态——含重命名级联）══
            string? newName = null;
            if (p.TryGetValue("new_name", out var nn) && nn != null && !string.IsNullOrWhiteSpace(nn.ToString()) && nn.ToString() != name)
            {
                newName = nn.ToString()!;
                if (project.Tags.Any(t => t.Name == newName))
                    return CommandResult.Fail("DUPLICATE", $"变量 \"{newName}\" 已存在");
            }
            TagDataType targetType = tag.DataType;
            if (p.TryGetValue("data_type", out var dtParam) && dtParam != null && !string.IsNullOrWhiteSpace(dtParam.ToString()))
            {
                if (!Enum.TryParse<TagDataType>(dtParam.ToString(), ignoreCase: true, out var td))
                    return CommandResult.Fail("INVALID_PARAM", $"未知数据类型: {dtParam}");
                targetType = td;
            }
            string? newBaseValue = null;   // null = 未提供（保留现值）
            if (p.TryGetValue("base_value", out var bv) && bv != null)
            {
                var raw = bv.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(raw))
                {
                    // D1/D4/#4：不制造无基准值变量——DATETIME 空拒绝；数字/BOOL 空串归一默认（与 create_tag 一致）；其余（STRING）空 = 清空
                    if (targetType == TagDataType.DATETIME)
                        return CommandResult.Fail("INVALID_PARAM", "DATETIME 变量的基准值不能为空（请输入日期时间或全 0 字面 0000:00:00 00:00:00）");
                    newBaseValue = targetType == TagDataType.FLOAT ? "0.0"
                        : targetType is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32 ? "0"
                        : targetType == TagDataType.BOOL ? "false" : "";   // #4：BOOL 空串归一 false
                }
                else
                {
                    var bvErr = BaseValueValidator.Check(raw, targetType);
                    if (bvErr != null) return CommandResult.Fail("INVALID_PARAM", bvErr);
                    newBaseValue = BaseValueValidator.Normalize(raw, targetType);
                    if (targetType == TagDataType.BOOL) newBaseValue = raw.ToLowerInvariant();   // #4：BOOL 归一存储（True/TRUE→true）
                }
            }
            // data_type 变更时处理现有 BaseValue：空 → 自动补默认（DATETIME 全 0 / 数字 0·0.0 / BOOL false，与 create_tag 一致，防无基准值变量）；
            // 非空 → 用新类型校验（防 STRING 旧值残留为数字/日期基准值绕过校验器）
            if (targetType != tag.DataType && newBaseValue == null)
            {
                if (string.IsNullOrEmpty(tag.BaseValue))
                {
                    newBaseValue = targetType == TagDataType.DATETIME ? "0000:00:00 00:00:00"
                        : targetType == TagDataType.FLOAT ? "0.0"
                        : targetType is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32 ? "0"
                        : targetType == TagDataType.BOOL ? "false" : "";   // #4：改类型到 BOOL 补 false
                }
                else
                {
                    var bvErr = BaseValueValidator.Check(tag.BaseValue, targetType);
                    if (bvErr != null)
                        return CommandResult.Fail("INVALID_PARAM", $"改为 {targetType} 后现有基准值 '{tag.BaseValue}' 不合法，请同时提供合法 base_value");
                    // GPS 是首个"合法但需归一"类型：旧值（如小数度）改类型到 GPS 时归 DMS 括号格式，与 create_tag 存储格式一致
                    if (targetType == TagDataType.GPS)
                        newBaseValue = BaseValueValidator.Normalize(tag.BaseValue, targetType);
                }
            }

            // ══ 统一落库（校验全部通过后）══
            if (newName != null)
            {
                // 级联：同步控件 BoundTag + 报警 TagName
                foreach (var screen in project.Screens)
                    foreach (var w in screen.Widgets.Where(w => w.BoundTag == name))
                        w.BoundTag = newName;
                foreach (var alarm in project.Alarms.Where(a => a.TagName == name))
                    alarm.TagName = newName;
                // Y-6（2026-09-10）：MQTT 映射绑定 TagName 级联 + 事件动作参数 tag_name 级联
                if (project.MqttSettings != null)
                    foreach (var b in project.MqttSettings.Bindings.Where(b => b.TagName == name))
                        b.TagName = newName;
                foreach (var screen in project.Screens)
                    foreach (var w in screen.Widgets)
                        foreach (var ev in w.Events)
                            foreach (var act in ev.Actions)
                                if (act.Parameters.TryGetValue("tag_name", out var tn) && tn == name)
                                    act.Parameters["tag_name"] = newName;
                if (project.WorldMap != null)
                    foreach (var ev in project.WorldMap.Events)
                        foreach (var act in ev.Actions)
                            if (act.Parameters.TryGetValue("tag_name", out var tn) && tn == name)
                                act.Parameters["tag_name"] = newName;
                // Y-6 reviewer 🟡1：世界地图作业点/范围点 BoundTag 级联（防改名后地图点悬空）
                if (project.WorldMap != null)
                {
                    foreach (var wp in project.WorldMap.WorkPoints.Where(w => w.BoundTag == name))
                        wp.BoundTag = newName;
                    foreach (var rp in project.WorldMap.WorkRangePoints.Where(r => r.BoundTag == name))
                        rp.BoundTag = newName;
                }
                tag.Name = newName;
            }
            if (p.TryGetValue("data_type", out var dt2) && dt2 != null && !string.IsNullOrWhiteSpace(dt2.ToString()))
                tag.DataType = targetType;
            if (p.TryGetValue("source", out var src) && src != null)
                // 显式空串 = 清空为内部变量（OptIfProvided 语义：CLI 未提供不进字典，保留现值）
                tag.Source = src.ToString()?.Trim() ?? "";
            if (p.TryGetValue("unit", out var un) && un != null)
                tag.Unit = un.ToString() ?? "";
            if (p.TryGetValue("scan_interval", out var si) && si != null && !string.IsNullOrWhiteSpace(si.ToString()))
                tag.ScanIntervalMs = Convert.ToInt32(si);
            if (p.TryGetValue("deadband", out var db) && db != null && !string.IsNullOrWhiteSpace(db.ToString()))
                tag.Deadband = Convert.ToDouble(db, System.Globalization.CultureInfo.InvariantCulture);   // InvariantCulture：与 Validate 一致，防区域性小数点异常
            if (p.TryGetValue("description", out var desc) && desc != null)
                tag.Description = desc.ToString() ?? "";
            if (newBaseValue != null)
            {
                if (targetType == TagDataType.BOOL) newBaseValue = newBaseValue.ToLowerInvariant();   // #4 兜底：改类型/归一路径统一落库小写（防存量变体绕过 create/update 直改路径）
                tag.BaseValue = newBaseValue;
            }
            // Y-5a：group 分组（显式空串 = 清空为未分组——OptIfProvided 语义：未提供保留现值）
            if (p.TryGetValue("group", out var grp) && grp != null)
                tag.Group = grp.ToString() ?? "";

            return CommandResult.Ok(new { tag_name = tag.Name });
        }
    }

    /// <summary>list_tags：列出变量（Y-5a 2026-09-10；可选 --group 过滤；对齐 GUI 变量管理器——v1.1 变量清单 CLI 查询口补齐）。</summary>
    public class ListTagsHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "list_tags", Description = "列出变量（可选按分组过滤）",
            Parameters = new()
            {
                ["group"] = new() { Type = "string", DefaultValue = "", Description = "分组名过滤（空 = 全部；特殊值 \"未分组\" = 列出空分组变量）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => ValidationResult.Ok;
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var groupFilter = p.GetValueOrDefault("group")?.ToString() ?? "";
            IEnumerable<Tag> query = project.Tags;
            if (groupFilter.Length > 0)
            {
                if (groupFilter == "未分组")
                    query = project.Tags.Where(t => string.IsNullOrEmpty(t.Group));
                else
                    query = project.Tags.Where(t => t.Group == groupFilter);
            }
            var list = query.Select(t => new
            {
                name = t.Name,
                type = t.DataType.ToString(),
                source = t.Source,
                unit = t.Unit,
                scan_interval_ms = t.ScanIntervalMs,
                group = string.IsNullOrEmpty(t.Group) ? "未分组" : t.Group,   // 空分组归一显示名
                description = t.Description,
            }).ToList();
            return CommandResult.Ok(new { count = list.Count, tags = list });
        }
    }

    /// <summary>list_tag_groups：列出全部变量分组名（Y-5a 2026-09-10；含「未分组」占位语义说明——
    /// 组重命名/删除 = 批量 update_tag --group（组本身无独立对象）。</summary>
    public class ListTagGroupsHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "list_tag_groups", Description = "列出变量分组名清单",
            Parameters = new()
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => ValidationResult.Ok;
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var groups = project.Tags
                .Where(t => !string.IsNullOrEmpty(t.Group))
                .Select(t => t.Group)
                .Distinct()
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var ungrouped = project.Tags.Count(t => string.IsNullOrEmpty(t.Group));
            return CommandResult.Ok(new { count = groups.Count, groups, ungrouped_count = ungrouped });
        }
    }
}

/// <summary>base_value（设计态预览基准值）按变量类型校验：非空时必须能解析为对应类型的数值，防 AI/CLI 传垃圾字符串进工程文件。
/// P8：事件函数 value 校验复用同一规则（GPS→GeoPoint、数值→InvariantCulture、BOOL→true/false、DATETIME→DateTime/全 0 字面、STRING→任意）。</summary>
public static class BaseValueValidator
{
    /// <summary>C12-13：各类型合法格式示例（错误消息附示例，AI/CLI 用户可直接按格式重试）。</summary>
    private static readonly Dictionary<TagDataType, string> _examples = new()
    {
        [TagDataType.FLOAT] = "如 3.14 / -0.5（小数或负数；不接受 NaN/Infinity）",
        [TagDataType.INT16] = "如 -32768 ~ 32767 的整数",
        [TagDataType.UINT16] = "如 0 ~ 65535 的整数",
        [TagDataType.INT32] = "如 -2147483648 ~ 2147483647 的整数",
        [TagDataType.BOOL] = "仅 true 或 false",
        [TagDataType.DATETIME] = "如 2026-08-12 10:30:00",
        [TagDataType.GPS] = "如 104.06, 30.67 或 E104°3'30\", N30°40'20\"",
        [TagDataType.STRING] = "任意文本",
    };

    public static string? Check(string? raw, TagDataType dt)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;   // 空 = 清空/未提供，放行
        var s = raw.Trim();
        var ok = dt switch
        {
            TagDataType.FLOAT => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d),   // IsFinite：拦 NaN/Infinity（对齐工程文件数值校验纪律）
            TagDataType.INT16 => short.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.UINT16 => ushort.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.INT32 => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _),
            TagDataType.BOOL => bool.TryParse(s, out _),   // #4：BOOL 基准值只能填 false/true（bool.TryParse 含大小写；不再接受 "1"/"0"）
            TagDataType.DATETIME => DateTime.TryParse(s, out _) || IsZeroDateLiteral(s),   // 任意有效日期时间（yyyy-MM-dd HH:mm:ss 等）；任务8：全 0 字面（0000:00:00 00:00:00，0000 年无效）放行
            TagDataType.GPS => GeoPoint.TryParse(s, out _),   // 经纬度：DMS（E104°3'30", N30°40'20"）或小数度，经度±180/纬度±90，前缀-位置匹配
            _ => true,   // STRING 等任意文本
        };
        return ok ? null : $"base_value 不是合法的 {dt} 数值: '{raw}'（{_examples[dt]}）";   // C12-13：附格式示例
    }

    /// <summary>归一化基准值存储：GPS 小数度输入自动转 DMS 括号格式（"104.0583, 30.6722" → "(E104°3'30\", N30°40'20\")"）；其余类型原样。</summary>
    public static string Normalize(string raw, TagDataType dt)
        => dt == TagDataType.GPS && GeoPoint.TryParse(raw, out var gp) && gp != null ? gp.ToBaseValue() : raw;

    /// <summary>任务8：全 0 日期字面（仅 0/数字/冒号/空格/横线/斜杠，无任何非 0 数字）——0000 年 TryParse 失败，需字面放行。</summary>
    internal static bool IsZeroDateLiteral(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!s!.All(c => char.IsDigit(c) || c is ':' or ' ' or '-' or '/')) return false;   // 字符白名单：数字/冒号/空格/横线/斜杠
        var digits = s.Where(char.IsDigit).ToList();
        if (digits.Count == 0) return false;
        return digits.All(d => d == '0');
    }
}
