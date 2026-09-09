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

    /// <summary>MQTT 连接参数校验（Z 循环多连接 2026-09-11——连接参数内联 MqttConnection.Config；
    /// 规则对齐原 DeviceConnectionInfoValidator MQTT case（DeviceHandlers.cs:41-78），通讯页收窄后校验随迁连接层；
    /// 凭据 dpapi: 前缀强制——明文拒入库；编译侧再加密 Z-4 补齐）。</summary>
    internal static class MqttConnectionValidator
    {
        /// <summary>校验单个连接参数字段（cfg 参数名 → 值）；返回 null=合法，否则错误消息。缺省字段不校验。</summary>
        public static string? CheckField(string field, object? value)
        {
            var s = value?.ToString() ?? "";
            switch (field)
            {
                case "broker":
                    if (s.Length == 0) return "broker 不能为空";
                    if (s.StartsWith("mqtt://") || s.StartsWith("tcp://")) return "broker 不能带协议前缀（mqtt:// / tcp://）——直接填主机/IP";
                    if (s.Contains('/')) return "broker 不能包含 /（路径不属于连接地址）";
                    break;
                case "port":
                    if (!int.TryParse(s, out var port) || port is < 1 or > 65535) return "port 必须在 1-65535";
                    break;
                case "version":
                    if (s.Length > 0 && s is not ("0" or "1")) return "version 必须是 0（3.1.1）或 1（5.0）";
                    break;
                case "client_id":
                    if (s.Length > 64 || s.Contains(' ')) return "client_id 不能超过 64 字符且不能含空格";
                    break;
                case "password":
                    if (s.Length > 0 && !s.StartsWith("dpapi:")) return "password 必须为 dpapi: 加密包（明文拒入库——凭据两级加密）";
                    break;
                case "keep_alive_sec":
                    if (s.Length > 0 && (!int.TryParse(s, out var ka) || ka < 0)) return "keep_alive_sec 必须 ≥0（0 = MQTT 禁用心跳）";
                    break;
            }
            return null;
        }

        /// <summary>把参数字典（仅非空/非缺省字段）应用到 MqttConfig（供 add/update_connection 共用）。</summary>
        public static void Apply(Dictionary<string, object?> p, MqttConfig cfg)
        {
            if (p.TryGetValue("broker", out var v) && v != null && v.ToString()!.Length > 0) cfg.Broker = v.ToString()!;
            if (p.TryGetValue("port", out v) && v != null && v.ToString()!.Length > 0) cfg.Port = int.Parse(v.ToString()!);
            if (p.TryGetValue("version", out v) && v != null && v.ToString()!.Length > 0) cfg.Version = v.ToString() == "1" ? MqttVersion.V5_0 : MqttVersion.V3_1_1;
            if (p.TryGetValue("client_id", out v) && v != null) cfg.ClientId = v.ToString() ?? "";
            if (p.TryGetValue("username", out v) && v != null) cfg.Username = v.ToString() ?? "";
            if (p.TryGetValue("password", out v) && v != null && v.ToString()!.Length > 0) cfg.Password = v.ToString()!;
            if (p.TryGetValue("keep_alive_sec", out v) && v != null && v.ToString()!.Length > 0) cfg.KeepAliveSec = int.Parse(v.ToString()!);
            if (p.TryGetValue("status_tag", out v) && v != null) cfg.StatusTag = v.ToString() ?? "";
        }
    }

    /// <summary>MQTT 命令共享支持（Z 循环多连接 2026-09-11：主题/绑定归属连接——经 connection_name 定位 MqttConnection）。</summary>
    internal static class MqttHandlerSupport
    {
        /// <summary>按 connection_name 定位连接；失败置 err（null=成功）。</summary>
        public static MqttConnection? FindConnection(MqttSettings s, Dictionary<string, object?> p, out CommandResult? err)
        {
            var name = p.GetValueOrDefault("connection_name")?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                err = CommandResult.Fail("INVALID_PARAM", "缺少必填参数: connection_name（Z 循环多连接——主题/绑定归属连接）");
                return null;
            }
            var c = s.Connections.FirstOrDefault(x => x.Name == name);
            if (c == null)
            {
                err = CommandResult.Fail("NOT_FOUND", $"MQTT 连接 \"{name}\" 不存在（先 mqtt_add_connection）");
                return null;
            }
            err = null;
            return c;
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

    /// <summary>mqtt_add_connection：新增 MQTT 连接（Z 循环多连接管理器——连接参数内联 MqttConnection.Config；
    /// 取代 Y 循环 mqtt_set_device + DeviceConfig 连接真源；name 工程内查重）。</summary>
    public class MqttAddConnectionHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_add_connection", Description = "新增 MQTT broker 连接（多连接管理器；连接参数内联）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "连接名（工程内唯一；树节点显示）" },
                ["broker"] = new() { Type = "string", Description = "broker 主机/IP（不含协议前缀）", KeepInCompact = true },
                ["port"] = new() { Type = "int", DefaultValue = 1883, Description = "端口（默认 1883）", KeepInCompact = true },
                ["version"] = new() { Type = "enum", EnumValues = new[] { "0", "1" }, DefaultValue = "0", Description = "MQTT 版本 0=3.1.1 / 1=5.0", KeepInCompact = true },
                ["client_id"] = new() { Type = "string", Description = "客户端 ID（空 = FW 自动生成）", KeepInCompact = true },
                ["username"] = new() { Type = "string", Description = "用户名（匿名 = 空）", KeepInCompact = true },
                ["password"] = new() { Type = "string", Description = "密码 dpapi: 加密包（明文拒收）", KeepInCompact = true },
                ["keep_alive_sec"] = new() { Type = "int", DefaultValue = 60, Description = "keepAlive 心跳秒（0=禁用）", KeepInCompact = true },
                ["status_tag"] = new() { Type = "string", Description = "连接状态回写变量名（4 态 0-3；空=不回写）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            foreach (var f in new[] { "broker", "port", "version", "client_id", "password", "keep_alive_sec" })
            {
                if (!p.TryGetValue(f, out var v) || v == null || v.ToString()!.Length == 0) continue;
                var err = MqttConnectionValidator.CheckField(f, v);
                if (err != null) return ValidationResult.Fail($"INVALID_PARAM: {err}");
            }
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var s = project.MqttSettings ??= new MqttSettings { SchemaVersion = 1 };
            if (s.Connections.Any(c => c.Name == name)) return CommandResult.Fail("DUPLICATE", $"连接 \"{name}\" 已存在");
            var conn = new MqttConnection { Name = name, Config = new MqttConfig() };
            MqttConnectionValidator.Apply(p, conn.Config);
            s.Connections.Add(conn);
            return CommandResult.Ok(new { connection = name, connections = s.Connections.Count });
        }
    }

    /// <summary>mqtt_update_connection：更新连接参数（留空参数 = 不改；改 name 不做——删除重建）。</summary>
    public class MqttUpdateConnectionHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_update_connection", Description = "更新 MQTT 连接参数（留空 = 不改）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "连接名" },
                ["broker"] = new() { Type = "string", Description = "broker 主机/IP（不含协议前缀；留空=不改）", KeepInCompact = true },
                ["port"] = new() { Type = "int", Description = "端口（留空=不改）", KeepInCompact = true },
                ["version"] = new() { Type = "enum", EnumValues = new[] { "0", "1" }, Description = "MQTT 版本（留空=不改）", KeepInCompact = true },
                ["client_id"] = new() { Type = "string", Description = "客户端 ID（空串=清除；留空=不改）", KeepInCompact = true },
                ["username"] = new() { Type = "string", Description = "用户名（空串=清除；留空=不改）", KeepInCompact = true },
                ["password"] = new() { Type = "string", Description = "密码 dpapi: 加密包（明文拒收；留空=不改）", KeepInCompact = true },
                ["keep_alive_sec"] = new() { Type = "int", Description = "keepAlive 心跳秒（留空=不改）", KeepInCompact = true },
                ["status_tag"] = new() { Type = "string", Description = "连接状态回写变量名（空串=清除；留空=不改）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            foreach (var f in new[] { "broker", "port", "version", "client_id", "password", "keep_alive_sec" })
            {
                if (!p.TryGetValue(f, out var v) || v == null || v.ToString()!.Length == 0) continue;
                var err = MqttConnectionValidator.CheckField(f, v);
                if (err != null) return ValidationResult.Fail($"INVALID_PARAM: {err}");
            }
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var c = s.Connections.FirstOrDefault(x => x.Name == name);
            if (c == null) return CommandResult.Fail("NOT_FOUND", $"连接 \"{name}\" 不存在");
            MqttConnectionValidator.Apply(p, c.Config);
            return CommandResult.Ok(new { connection = name });
        }
    }

    /// <summary>mqtt_delete_connection：删除连接（连带其 topics/bindings——连接整体移除）。</summary>
    public class MqttDeleteConnectionHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_delete_connection", Description = "删除 MQTT 连接（连带其主题与绑定）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "连接名" },
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
            var c = s.Connections.FirstOrDefault(x => x.Name == name);
            if (c == null) return CommandResult.Fail("NOT_FOUND", $"连接 \"{name}\" 不存在");
            s.Connections.Remove(c);
            return CommandResult.Ok(new { connection = name, connections = s.Connections.Count });
        }
    }

    /// <summary>mqtt_add_topic：向指定连接新增主题配置（发布/订阅分离；查重收窄连接内——跨连接同名 Topic 合法）。
    /// Z 循环多连接：connection_name 必填（旧工程级单份语义废弃）。</summary>
    public class MqttAddTopicHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_add_topic", Description = "向 MQTT 连接新增主题配置",
            Parameters = new()
            {
                ["connection_name"] = new() { Type = "string", Required = true, Description = "所属连接名（mqtt_add_connection 已建）" },
                ["name"] = new() { Type = "string", Required = true, Description = "主题配置名（连接内唯一）" },
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
            if (!p.ContainsKey("connection_name") || string.IsNullOrWhiteSpace(p["connection_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: connection_name");
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
            var c = MqttHandlerSupport.FindConnection(s, p, out var err);
            if (c == null) return err!;
            if (c.Topics.Any(t => t.Name == name)) return CommandResult.Fail("DUPLICATE", $"连接 \"{c.Name}\" 内主题配置 \"{name}\" 已存在");
            // 同父节点禁重复（同方向同路径 → 重复订阅/发布冲突；收窄本连接内——不同连接不同 broker 命名空间互不干扰）
            if (c.Topics.Any(t => t.Direction == dir && t.Topic == topic))
                return CommandResult.Fail("DUPLICATE", $"连接 \"{c.Name}\" 内同方向已有相同 Topic 路径: \"{topic}\"（同父节点重复）");
            c.Topics.Add(new MqttTopic
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
            return CommandResult.Ok(new { connection = c.Name, topic_name = name, count = c.Topics.Count });
        }
    }

    /// <summary>mqtt_update_topic：更新指定连接内主题配置属性（QoS/Retain/发布周期/JSON 模板/响应主题；
    /// 路径/方向变更 = 删除+新增。Z 循环：connection_name 必填）。</summary>
    public class MqttUpdateTopicHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_update_topic", Description = "更新 MQTT 连接内主题配置属性",
            Parameters = new()
            {
                ["connection_name"] = new() { Type = "string", Required = true, Description = "所属连接名" },
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
            if (!p.ContainsKey("connection_name") || string.IsNullOrWhiteSpace(p["connection_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: connection_name");
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var c = MqttHandlerSupport.FindConnection(s, p, out var err);
            if (c == null) return err!;
            var t = c.Topics.FirstOrDefault(x => x.Name == name);
            if (t == null) return CommandResult.Fail("NOT_FOUND", $"连接 \"{c.Name}\" 内主题配置 \"{name}\" 不存在");
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
            return CommandResult.Ok(new { connection = c.Name, topic_name = name });
        }
    }

    /// <summary>mqtt_delete_topic：删除指定连接内主题配置（连带其 Bindings——绑定引用一致性由删除联动保证）。</summary>
    public class MqttDeleteTopicHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_delete_topic", Description = "删除 MQTT 连接内主题配置（连带删除其变量绑定）",
            Parameters = new()
            {
                ["connection_name"] = new() { Type = "string", Required = true, Description = "所属连接名" },
                ["name"] = new() { Type = "string", Required = true, Description = "主题配置名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("connection_name") || string.IsNullOrWhiteSpace(p["connection_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: connection_name");
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var c = MqttHandlerSupport.FindConnection(s, p, out var err);
            if (c == null) return err!;
            var topic = c.Topics.FirstOrDefault(t => t.Name == name);
            if (topic == null) return CommandResult.Fail("NOT_FOUND", $"连接 \"{c.Name}\" 内主题配置 \"{name}\" 不存在");
            c.Topics.Remove(topic);
            c.Bindings.RemoveAll(b => b.TopicName == name);   // 级联：绑定的变量映射随主题删除移除（本连接内）
            return CommandResult.Ok(new { connection = c.Name, topic_name = name, topics = c.Topics.Count, bindings = c.Bindings.Count });
        }
    }

    /// <summary>mqtt_set_binding：向指定连接新增/更新 变量↔字段绑定（topic/tag 引用存在校验；topic 归属该连接）。</summary>
    public class MqttSetBindingHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_set_binding", Description = "设置 MQTT 连接内变量↔字段绑定（同名 tag+topic 覆盖更新）",
            Parameters = new()
            {
                ["connection_name"] = new() { Type = "string", Required = true, Description = "所属连接名" },
                ["topic_name"] = new() { Type = "string", Required = true, Description = "所属主题配置名（该连接内）" },
                ["tag_name"] = new() { Type = "string", Required = true, Description = "变量名（Tag.Name）" },
                ["field_name"] = new() { Type = "string", Required = true, Description = "JSON 字段名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            foreach (var k in new[] { "connection_name", "topic_name", "tag_name", "field_name" })
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
            var c = MqttHandlerSupport.FindConnection(s, p, out var err);
            if (c == null) return err!;
            if (!c.Topics.Any(t => t.Name == topicName))
                return CommandResult.Fail("NOT_FOUND", $"连接 \"{c.Name}\" 内主题配置 \"{topicName}\" 不存在（先 mqtt_add_topic）");
            if (!project.Tags.Any(t => t.Name == tagName))
                return CommandResult.Fail("NOT_FOUND", $"变量 \"{tagName}\" 不存在（先 create_tag）");
            var existing = c.Bindings.FirstOrDefault(b => b.TopicName == topicName && b.TagName == tagName);
            if (existing != null)
                existing.FieldName = fieldName;   // 覆盖更新（幂等语义）
            else
                c.Bindings.Add(new MqttBinding { TopicName = topicName, TagName = tagName, FieldName = fieldName });
            return CommandResult.Ok(new { connection = c.Name, topic_name = topicName, tag_name = tagName });
        }
    }

    /// <summary>mqtt_remove_binding：移除指定连接内变量↔字段绑定。</summary>
    public class MqttRemoveBindingHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "mqtt_remove_binding", Description = "移除 MQTT 连接内变量↔字段绑定",
            Parameters = new()
            {
                ["connection_name"] = new() { Type = "string", Required = true, Description = "所属连接名" },
                ["topic_name"] = new() { Type = "string", Required = true, Description = "所属主题配置名" },
                ["tag_name"] = new() { Type = "string", Required = true, Description = "变量名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("connection_name") || string.IsNullOrWhiteSpace(p["connection_name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: connection_name");
            if (!p.ContainsKey("topic_name") || !p.ContainsKey("tag_name")) return ValidationResult.Fail("缺少必填参数: topic_name/tag_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var s = project.MqttSettings;
            if (s == null) return CommandResult.Fail("NOT_FOUND", "MQTT 未配置");
            var c = MqttHandlerSupport.FindConnection(s, p, out var err);
            if (c == null) return err!;
            var topicName = p["topic_name"]!.ToString()!;
            var tagName = p["tag_name"]!.ToString()!;
            var binding = c.Bindings.FirstOrDefault(b => b.TopicName == topicName && b.TagName == tagName);
            if (binding == null) return CommandResult.Fail("NOT_FOUND", $"连接 \"{c.Name}\" 内绑定 {topicName}/{tagName} 不存在");
            c.Bindings.Remove(binding);
            return CommandResult.Ok(new { connection = c.Name, bindings = c.Bindings.Count });
        }
    }
}
