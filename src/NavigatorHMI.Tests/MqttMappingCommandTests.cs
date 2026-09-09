using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-3b（2026-09-10 ④通信批）：MQTT 三层映射命令层测试——
    /// EnableMqtt 开关 / Topic 增删（发布禁 +/#、订阅 wildcard、同父重复拒绝）/ Binding 设置（变量存在性/主题存在性/覆盖更新）。
    /// 契约：MqttSettings（HMIProject 24）+ Topic/Binding 字段号 = proto（Y-2 审计已锁）。
    /// </summary>
    public class MqttMappingCommandTests
    {
        private static CommandService NewService(out HMIProject project)
        {
            project = new HMIProject { Name = "MQTT 映射测试" };
            project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            project.Tags.Add(new Tag { Name = "开关", DataType = TagDataType.BOOL });
            return new CommandService(project);
        }

        [Fact]
        public void 未配置MQTT_开关置true_自动建MqttSettings()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("mqtt_set_enabled", new Dictionary<string, object?> { ["enabled"] = true });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.NotNull(p.MqttSettings);
            Assert.True(p.MqttSettings!.EnableMqtt);
        }

        [Fact]
        public void 添加发布Topic_成功_落库()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish",
                ["qos"] = 0, ["retain"] = true, ["publish_interval_ms"] = 5000,
                ["json_template"] = 1,
            });
            Assert.True(r.Success, r.ErrorMessage);
            var t = p.MqttSettings!.Topics.Single();
            Assert.Equal("温度上报", t.Name);
            Assert.Equal("plant/temp", t.Topic);
            Assert.Equal(MqttTopicDirection.Publish, t.Direction);
            Assert.True(t.Retain);
            Assert.Equal(5000, t.PublishIntervalMs);
            Assert.Equal(MqttJsonTemplate.KvWithTimestamp, t.JsonTemplate);
        }

        [Theory]
        [InlineData("plant/+/temp", "发布通配符拒绝")]
        [InlineData("plant/#", "发布通配符拒绝")]
        public void 添加发布Topic_非法路径_拒绝(string topic, string label)
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = topic, ["direction"] = "publish",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void 添加发布Topic_超长_拒绝()
        {
            var svc = NewService(out _);
            var longTopic = "plant/" + new string('a', 600);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = longTopic, ["direction"] = "publish",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("512", r.ErrorMessage);
        }

        [Fact]
        public void 添加订阅Topic_wildcard_合法()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "泵站", ["topic"] = "plant/+/status", ["direction"] = "subscribe",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Single(p.MqttSettings!.Topics);
        }

        // ── reviewer 🟡1/🟡5 闭环：订阅 # 边界负例 + QoS 越界 + update_topic（2026-09-10）──

        [Theory]
        [InlineData("plant/b#", "# 未独占整级")]
        [InlineData("#x/y", "# 未独占整级")]
        [InlineData("plant/#/temp", "# 不在末级")]
        public void 添加订阅Topic_hash非独占末级_拒绝(string topic, string label)
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = topic, ["direction"] = "subscribe",
            });
            Assert.False(r.Success, $"应拒绝 {topic}: {label}");
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void 添加订阅Topic_合法hash末级_通过()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "all", ["topic"] = "plant/#", ["direction"] = "subscribe",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Single(p.MqttSettings!.Topics);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(99)]
        public void 添加Topic_QoS越界_拒绝(int qos)
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish", ["qos"] = qos,
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("QoS", r.ErrorMessage);
        }

        [Fact]
        public void 更新Topic_QoS与周期_成功()
        {
            var svc = NewService(out var p);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish", ["qos"] = 0,
            });
            var r = svc.Execute("mqtt_update_topic", new Dictionary<string, object?>
            {
                ["name"] = "温度上报", ["qos"] = 1, ["retain"] = true, ["publish_interval_ms"] = 10000,
                ["json_template"] = 1,
            });
            Assert.True(r.Success, r.ErrorMessage);
            var t = p.MqttSettings!.Topics.Single();
            Assert.Equal(1, t.Qos);
            Assert.True(t.Retain);
            Assert.Equal(10000, t.PublishIntervalMs);
            Assert.Equal(MqttJsonTemplate.KvWithTimestamp, t.JsonTemplate);
        }

        [Fact]
        public void 更新Topic_QoS越界_拒绝且不改动()
        {
            var svc = NewService(out var p);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish",
            });
            var r = svc.Execute("mqtt_update_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["qos"] = 5,
            });
            Assert.False(r.Success);
            Assert.Equal(0, p.MqttSettings!.Topics.Single().Qos);   // 原子性：失败不改动
        }

        [Fact]
        public void 同方向同Topic路径_重复_拒绝()
        {
            var svc = NewService(out _);
            var ok = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "a", ["topic"] = "plant/temp", ["direction"] = "publish",
            });
            Assert.True(ok.Success, ok.ErrorMessage);
            var dup = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "b", ["topic"] = "plant/temp", ["direction"] = "publish",
            });
            Assert.False(dup.Success);
            Assert.Contains("同父节点重复", dup.ErrorMessage);
        }

        [Fact]
        public void 设置Binding_成功与覆盖()
        {
            var svc = NewService(out var p);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish",
            });
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "温度上报", ["tag_name"] = "温度", ["field_name"] = "temp",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("temp", p.MqttSettings!.Bindings.Single().FieldName);
            // 覆盖更新（幂等）
            var upd = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "温度上报", ["tag_name"] = "温度", ["field_name"] = "temperature",
            });
            Assert.True(upd.Success, upd.ErrorMessage);
            Assert.Single(p.MqttSettings!.Bindings);
            Assert.Equal("temperature", p.MqttSettings!.Bindings.Single().FieldName);
        }

        [Fact]
        public void 设置Binding_变量不存在_拒绝()
        {
            var svc = NewService(out _);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish",
            });
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "t", ["tag_name"] = "不存在变量", ["field_name"] = "f",
            });
            Assert.False(r.Success);
            Assert.Contains("变量", r.ErrorMessage);
        }

        [Fact]
        public void 设置Binding_主题不存在_拒绝()
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "无此主题", ["tag_name"] = "温度", ["field_name"] = "f",
            });
            Assert.False(r.Success);
            Assert.Contains("主题", r.ErrorMessage);
        }

        [Fact]
        public void 删除Topic_连带删除Binding()
        {
            var svc = NewService(out var p);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish",
            });
            svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "温度上报", ["tag_name"] = "温度", ["field_name"] = "temp",
            });
            var r = svc.Execute("mqtt_delete_topic", new Dictionary<string, object?> { ["name"] = "温度上报" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Empty(p.MqttSettings!.Topics);
            Assert.Empty(p.MqttSettings!.Bindings);   // 级联删除
        }

        [Fact]
        public void 移除Binding_成功()
        {
            var svc = NewService(out var p);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish",
            });
            svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "t", ["tag_name"] = "温度", ["field_name"] = "temp",
            });
            var r = svc.Execute("mqtt_remove_binding", new Dictionary<string, object?>
            {
                ["topic_name"] = "t", ["tag_name"] = "温度",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Empty(p.MqttSettings!.Bindings);
        }

        // ═══ Y Check 裁决（2026-09-11）：mqtt_set_device——选定 MQTT 连接设备（通讯页创建后引用）═══
        [Fact]
        public void 选定MQTT设备_成功_落库DeviceName()
        {
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig { Name = "MQTT-Broker", Protocol = ProtocolType.MQTT, ConnectionInfo = "{\"broker\":\"192.168.1.1\"}" });
            var r = svc.Execute("mqtt_set_device", new Dictionary<string, object?> { ["device_name"] = "MQTT-Broker" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("MQTT-Broker", p.MqttSettings!.DeviceName);
        }

        [Fact]
        public void 清除选定MQTT设备_空串_成功()
        {
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig { Name = "MQTT-Broker", Protocol = ProtocolType.MQTT });
            svc.Execute("mqtt_set_device", new Dictionary<string, object?> { ["device_name"] = "MQTT-Broker" });
            var r = svc.Execute("mqtt_set_device", new Dictionary<string, object?> { ["device_name"] = "" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("", p.MqttSettings!.DeviceName);
        }

        [Fact]
        public void 选定MQTT设备_不存在_拒绝()
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_set_device", new Dictionary<string, object?> { ["device_name"] = "无此设备" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void 选定MQTT设备_协议非MQTT_拒绝()
        {
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig { Name = "ModbusTCP-1", Protocol = ProtocolType.ModbusTCP });
            var r = svc.Execute("mqtt_set_device", new Dictionary<string, object?> { ["device_name"] = "ModbusTCP-1" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }
    }
}
