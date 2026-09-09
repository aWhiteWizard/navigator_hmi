using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>MQTT 三层映射命令的共享校验（Y-3b 2026-09-10；主题规则对齐 v1.1-design §5.4 动态 topic 校验——
    /// 发布禁 +/#、≤512、同父禁重复；订阅合法 wildcard（+ 整级、# 独占末级）、同父节点禁重复）。</summary>
    internal static class MqttTopicValidator
    {
        /// <summary>Topic 最大长度（对齐 MQTT 规范 512 上限）。</summary>
        public const int MaxTopicLength = 512;

        /// <summary>校验 topic 路径合法性（方向区分）；返回 null=合法，否则错误消息。</summary>
        public static string? CheckPath(string topic, MqttTopicDirection direction)
        {
            if (string.IsNullOrWhiteSpace(topic)) return "Topic 路径不能为空";
            if (topic.Length > MaxTopicLength) return $"Topic 路径不能超过 {MaxTopicLength} 字符";
            if (direction == MqttTopicDirection.Publish)
            {
                if (topic.Contains('+') || topic.Contains('#'))
                    return "发布 Topic 禁止使用通配符 +/#";
            }
            else
            {
                // 订阅 wildcard 规则（MQTT 3.1.1 §4.7.1.2）：+ 占整级（须单独一级）；# 仅可独占末级
                var levels = topic.Split('/');
                for (int i = 0; i < levels.Length; i++)
                {
                    var lv = levels[i];
                    if (lv.Contains('#'))
                    {
                        // reviewer 🟡1（2026-09-10）：# 必须独占整级且只能末级——a/b#、#x 非法（原只查 Contains 放行）
                        if (lv != "#") return "订阅 Topic 的 # 必须单独占一级（如 a/b/#）";
                        if (i != levels.Length - 1) return "订阅 Topic 的 # 只能出现在末尾（如 a/b/#）";
                    }
                    else if (lv.Contains('+') && lv != "+")
                    {
                        return "订阅 Topic 的 + 必须单独占一级（如 a/+/b）";
                    }
                }
            }
            return null;
        }

        /// <summary>QoS 校验（MQTT 规范 0/1/2）；返回 null=合法。</summary>
        public static string? CheckQos(object? qos)
        {
            if (qos == null) return null;   // 缺省 0
            if (!int.TryParse(qos.ToString(), out var q)) return "QoS 必须是整数 0/1/2";
            return q is < 0 or > 2 ? "QoS 必须是 0/1/2" : null;
        }
    }

    /// <summary>mqtt_set_enabled：EnableMqtt 总开关（禁用：FW 不建连接对象；PC 灰显由 UI 状态驱动）。</summary>
    public class MqttSetEnabledHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_set_enabled", Description = "设置 MQTT 总开关",
            Parameters = new()
            {
                ["enabled"] = new() { Type = "bool", Required = true, Description = "true=启用 / false=禁用" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("enabled")) return ValidationResult.Fail("缺少必填参数: enabled");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var enabled = p["enabled"] is bool b ? b : bool.Parse(p["enabled"]!.ToString()!);
            var s = project.MqttSettings ??= new MqttSettings { SchemaVersion = 1 };
            s.EnableMqtt = enabled;
            return CommandResult.Ok(new { enable_mqtt = s.EnableMqtt });
        }
    }

    /// <summary>mqtt_set_device：选定本工程 MQTT 连接使用的 MQTT 设备（DeviceConfig.Name，Protocol==MQTT——
    /// Y Check 裁决 2026-09-11：设备在通讯配置页创建，本命令只做「引用选定」；空 = 未选定/清除）。
    /// 连接参数真源 = DeviceConfig.connection_info（Y-3a 防双源不变；MqttSettings.config 不填充）。</summary>
    public class MqttSetDeviceHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_set_device", Description = "选定 MQTT 连接使用的 MQTT 设备（通讯配置中已建，Protocol==MQTT）",
            Parameters = new()
            {
                ["device_name"] = new() { Type = "string", Required = true, Description = "MQTT 设备名（通讯配置中创建；空串 = 清除选定）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("device_name")) return ValidationResult.Fail("缺少必填参数: device_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var deviceName = p["device_name"]?.ToString() ?? "";
            if (deviceName.Length > 0)
            {
                var dev = project.Devices.FirstOrDefault(d => d.Name == deviceName && d.Protocol == ProtocolType.MQTT);
                if (dev == null)
                    return CommandResult.Fail("INVALID_PARAM", $"MQTT 设备 \"{deviceName}\" 不存在或协议非 MQTT——请在通讯配置中创建 MQTT 设备");
            }
            var s = project.MqttSettings ??= new MqttSettings { SchemaVersion = 1 };
            s.DeviceName = deviceName;
            return CommandResult.Ok(new { device_name = s.DeviceName });
        }
    }

    /// <summary>mqtt_add_topic：新增主题配置（发布/订阅分离；含 topic 路径校验 + 同父重复/同名查重）。</summary>
    public class MqttAddTopicHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_add_topic", Description = "新增 MQTT 主题配置",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "主题配置名（工程内唯一）" },
                ["topic"] = new() { Type = "string", Required = true, Description = "Topic 路径（发布禁 +/#、≤512；订阅合法 wildcard）" },
                ["direction"] = new() { Type = "enum", EnumValues = new[] { "publish", "subscribe" }, DefaultValue = "publish", Description = "方向（发布/订阅）" },
                ["qos"] = new() { Type = "int", DefaultValue = 0, Description = "QoS 0/1/2", KeepInCompact = true },
                ["retain"] = new() { Type = "bool", DefaultValue = false, Description = "保留消息（发布侧）", KeepInCompact = true },
                ["publish_interval_ms"] = new() { Type = "int", DefaultValue = 0, Description = "周期发布间隔 ms（0=仅按需）", KeepInCompact = true },
                ["json_template"] = new() { Type = "int", DefaultValue = 0, Description = "JSON 模板 0=KV / 1=带时间戳", KeepInCompact = true },
                ["response_topic"] = new() { Type = "string", DefaultValue = "", Description = "响应主题（订阅用）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            if (!p.ContainsKey("topic") || string.IsNullOrWhiteSpace(p["topic"]?.ToString())) return ValidationResult.Fail("缺少必填参数: topic");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var topic = p["topic"]!.ToString()!;
            var dir = MqttTopicDirection.Publish;
            if (p.TryGetValue("direction", out var d) && d != null && d.ToString() == "subscribe") dir = MqttTopicDirection.Subscribe;
            var pathErr = MqttTopicValidator.CheckPath(topic, dir);
            if (pathErr != null) return CommandResult.Fail("INVALID_PARAM", pathErr);
            var qosErr = MqttTopicValidator.CheckQos(p.GetValueOrDefault("qos", 0));
            if (qosErr != null) return CommandResult.Fail("INVALID_PARAM", qosErr);
            var s = project.MqttSettings ??= new MqttSettings { SchemaVersion = 1 };
            if (s.Topics.Any(t => t.Name == name)) return CommandResult.Fail("DUPLICATE", $"主题配置 \"{name}\" 已存在");
            // 同父节点禁重复（同方向同路径 → 重复订阅/发布冲突）
            if (s.Topics.Any(t => t.Direction == dir && t.Topic == topic))
                return CommandResult.Fail("DUPLICATE", $"同方向已有相同 Topic 路径: \"{topic}\"（同父节点重复）");
            s.Topics.Add(new MqttTopic
            {
                Name = name,
                Direction = dir,
                Topic = topic,
                Qos = Convert.ToInt32(p.GetValueOrDefault("qos", 0)),
                Retain = p.TryGetValue("retain", out var r) && r is bool rb ? rb : (p.GetValueOrDefault("retain")?.ToString() == "true"),
                PublishIntervalMs = Convert.ToInt32(p.GetValueOrDefault("publish_interval_ms", 0)),
                JsonTemplate = (MqttJsonTemplate)Convert.ToInt32(p.GetValueOrDefault("json_template", 0)),
                ResponseTopic = p.GetValueOrDefault("response_topic")?.ToString() ?? "",
            });
            return CommandResult.Ok(new { topic_name = name, count = s.Topics.Count });
        }
    }

    /// <summary>mqtt_update_topic：更新主题配置属性（QoS/Retain/发布周期/JSON 模板/响应主题；reviewer 🔴 F 建议——
    /// Topic 表格编辑走命令层，防直绑模型无脏标记/绕过校验）。路径/方向变更 = 删除+新增（配置名不变）。</summary>
    public class MqttUpdateTopicHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_update_topic", Description = "更新 MQTT 主题配置属性",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "主题配置名" },
                ["qos"] = new() { Type = "int", Description = "QoS 0/1/2（留空=不改）", KeepInCompact = true },
                ["retain"] = new() { Type = "bool", Description = "保留消息（留空=不改）", KeepInCompact = true },
                ["publish_interval_ms"] = new() { Type = "int", Description = "周期发布间隔 ms（0=仅按需；留空=不改）", KeepInCompact = true },
                ["json_template"] = new() { Type = "int", Description = "JSON 模板 0=KV / 1=带时间戳（留空=不改）", KeepInCompact = true },
                ["response_topic"] = new() { Type = "string", Description = "响应主题（空串=清除；留空=不改）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var t = s.Topics.FirstOrDefault(x => x.Name == name);
            if (t == null) return CommandResult.Fail("NOT_FOUND", $"主题配置 \"{name}\" 不存在");
            if (p.TryGetValue("qos", out var q) && q != null && q.ToString()!.Length > 0)
            {
                var qosErr = MqttTopicValidator.CheckQos(q);
                if (qosErr != null) return CommandResult.Fail("INVALID_PARAM", qosErr);
                t.Qos = int.Parse(q.ToString()!);
            }
            if (p.TryGetValue("retain", out var rt) && rt != null && rt.ToString()!.Length > 0)
                t.Retain = rt is bool rb ? rb : bool.Parse(rt.ToString()!);
            if (p.TryGetValue("publish_interval_ms", out var pi) && pi != null && pi.ToString()!.Length > 0)
            {
                if (!int.TryParse(pi.ToString(), out var ms) || ms < 0) return CommandResult.Fail("INVALID_PARAM", "publish_interval_ms 必须 ≥0");
                t.PublishIntervalMs = ms;
            }
            if (p.TryGetValue("json_template", out var jt) && jt != null && jt.ToString()!.Length > 0)
            {
                var v = int.Parse(jt.ToString()!);
                if (v is not (0 or 1)) return CommandResult.Fail("INVALID_PARAM", "json_template 必须是 0（KV）或 1（KV+时间戳）");
                t.JsonTemplate = (MqttJsonTemplate)v;
            }
            if (p.TryGetValue("response_topic", out var rsp) && rsp != null)
                t.ResponseTopic = rsp.ToString() ?? "";   // 显式空串 = 清除
            return CommandResult.Ok(new { topic_name = name });
        }
    }

    /// <summary>mqtt_delete_topic：删除主题配置（连带其 Bindings——绑定引用一致性由删除联动保证）。</summary>
    public class MqttDeleteTopicHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_delete_topic", Description = "删除 MQTT 主题配置（连带删除其变量绑定）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "主题配置名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var topic = s.Topics.FirstOrDefault(t => t.Name == name);
            if (topic == null) return CommandResult.Fail("NOT_FOUND", $"主题配置 \"{name}\" 不存在");
            s.Topics.Remove(topic);
            s.Bindings.RemoveAll(b => b.TopicName == name);   // 级联：绑定的变量映射随主题删除移除
            return CommandResult.Ok(new { topic_name = name, topics = s.Topics.Count, bindings = s.Bindings.Count });
        }
    }

    /// <summary>mqtt_set_binding：新增/更新 变量↔字段绑定（三层映射最底层；tag_name 引用 Tag.Name——不存在拒绝）。</summary>
    public class MqttSetBindingHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_set_binding", Description = "设置 MQTT 变量↔字段绑定（同名 tag+topic 覆盖更新）",
            Parameters = new()
            {
                ["topic_name"] = new() { Type = "string", Required = true, Description = "所属主题配置名" },
                ["tag_name"] = new() { Type = "string", Required = true, Description = "变量名（Tag.Name）" },
                ["field_name"] = new() { Type = "string", Required = true, Description = "JSON 字段名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            foreach (var k in new[] { "topic_name", "tag_name", "field_name" })
                if (!p.ContainsKey(k) || string.IsNullOrWhiteSpace(p[k]?.ToString()))
                    return ValidationResult.Fail($"缺少必填参数: {k}");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var topicName = p["topic_name"]!.ToString()!;
            var tagName = p["tag_name"]!.ToString()!;
            var fieldName = p["field_name"]!.ToString()!;
            if (string.IsNullOrWhiteSpace(fieldName))
                return CommandResult.Fail("INVALID_PARAM", "field_name 不能为空（JSON 字段名）");
            var s = project.MqttSettings ??= new MqttSettings { SchemaVersion = 1 };
            if (!s.Topics.Any(t => t.Name == topicName))
                return CommandResult.Fail("NOT_FOUND", $"主题配置 \"{topicName}\" 不存在（先 mqtt_add_topic）");
            if (!project.Tags.Any(t => t.Name == tagName))
                return CommandResult.Fail("NOT_FOUND", $"变量 \"{tagName}\" 不存在（先 create_tag）");
            var existing = s.Bindings.FirstOrDefault(b => b.TopicName == topicName && b.TagName == tagName);
            if (existing != null)
                existing.FieldName = fieldName;   // 覆盖更新（幂等语义）
            else
                s.Bindings.Add(new MqttBinding { TopicName = topicName, TagName = tagName, FieldName = fieldName });
            return CommandResult.Ok(new { topic_name = topicName, tag_name = tagName });
        }
    }

    /// <summary>mqtt_remove_binding：移除变量↔字段绑定。</summary>
    public class MqttRemoveBindingHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_remove_binding", Description = "移除 MQTT 变量↔字段绑定",
            Parameters = new()
            {
                ["topic_name"] = new() { Type = "string", Required = true, Description = "所属主题配置名" },
                ["tag_name"] = new() { Type = "string", Required = true, Description = "变量名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("topic_name") || !p.ContainsKey("tag_name")) return ValidationResult.Fail("缺少必填参数: topic_name/tag_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var topicName = p["topic_name"]!.ToString()!;
            var tagName = p["tag_name"]!.ToString()!;
            var binding = s.Bindings.FirstOrDefault(b => b.TopicName == topicName && b.TagName == tagName);
            if (binding == null) return CommandResult.Fail("NOT_FOUND", $"绑定 {topicName}/{tagName} 不存在");
            s.Bindings.Remove(binding);
            return CommandResult.Ok(new { bindings = s.Bindings.Count });
        }
    }
}
