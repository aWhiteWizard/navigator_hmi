using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-3b（2026-09-10 ④通信批）+ Z 循环多连接重构（2026-09-11）：MQTT 命令层测试——
    /// 连接 CRUD（mqtt_add/update/delete_connection）/ EnableMqtt 开关 / 映射命令带 connection_name（Topic 增删查重收窄连接内、
    /// Binding 设置归属本连接）/ 连接参数校验（broker/port/password dpapi）。
    /// 契约：MqttSettings.Connections（HMIProject 24）+ MqttConnection/Topic/Binding 字段号 = proto（Y-2/Z-1 审计已锁）。
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

        /// <summary>建连接（命令层 mqtt_add_connection——测试走命令真实路径）；返回连接名。</summary>
        private static string AddConn(CommandService svc, string name = "broker-A", string broker = "192.168.1.14")
        {
            var r = svc.Execute("mqtt_add_connection", new Dictionary<string, object?>
            {
                ["name"] = name, ["broker"] = broker, ["port"] = 1883,
            });
            Assert.True(r.Success, r.ErrorMessage);
            return name;
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

        // ═══ Z 循环连接 CRUD ═══

        [Fact]
        public void 新增连接_成功_落库Connections()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("mqtt_add_connection", new Dictionary<string, object?>
            {
                ["name"] = "broker-A", ["broker"] = "192.168.1.14", ["port"] = 1883,
                ["client_id"] = "hmi-a", ["keep_alive_sec"] = 60, ["status_tag"] = "conn_a",
            });
            Assert.True(r.Success, r.ErrorMessage);
            var c = p.MqttSettings!.Connections.Single();
            Assert.Equal("broker-A", c.Name);
            Assert.Equal("192.168.1.14", c.Config.Broker);
            Assert.Equal(1883, c.Config.Port);
            Assert.Equal("hmi-a", c.Config.ClientId);
            Assert.Equal("conn_a", c.Config.StatusTag);
        }

        [Fact]
        public void 新增连接_重名_拒绝()
        {
            var svc = NewService(out _);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_connection", new Dictionary<string, object?> { ["name"] = "broker-A" });
            Assert.False(r.Success);
            Assert.Equal("DUPLICATE", r.ErrorCode);
        }

        [Fact]
        public void 新增连接_连接参数校验_拒绝()
        {
            var svc = NewService(out _);
            var bad = svc.Execute("mqtt_add_connection", new Dictionary<string, object?>
            { ["name"] = "bad", ["broker"] = "mqtt://x", ["port"] = 70000, ["password"] = "明文密码" });
            Assert.False(bad.Success);
            Assert.Contains("前缀", bad.ErrorMessage);
        }

        [Fact]
        public void 更新连接参数_成功()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            var r = svc.Execute("mqtt_update_connection", new Dictionary<string, object?>
            {
                ["name"] = "broker-A", ["broker"] = "192.168.1.15", ["port"] = 1884, ["status_tag"] = "conn_new",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("192.168.1.15", p.MqttSettings!.Connections.Single().Config.Broker);
            Assert.Equal(1884, p.MqttSettings!.Connections.Single().Config.Port);
            Assert.Equal("conn_new", p.MqttSettings!.Connections.Single().Config.StatusTag);
        }

        [Fact]
        public void 删除连接_连带Topic与Binding()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "t", ["tag_name"] = "温度", ["field_name"] = "temp" });
            var r = svc.Execute("mqtt_delete_connection", new Dictionary<string, object?> { ["name"] = "broker-A" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Empty(p.MqttSettings!.Connections);
        }

        // ═══ Topic（带 connection_name；查重收窄连接内）═══

        [Fact]
        public void 添加发布Topic_成功_落库()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish",
                ["qos"] = 0, ["retain"] = true, ["publish_interval_ms"] = 5000, ["json_template"] = 1,
            });
            Assert.True(r.Success, r.ErrorMessage);
            var t = p.MqttSettings!.Connections.Single().Topics.Single();
            Assert.Equal("温度上报", t.Name);
            Assert.Equal("plant/temp", t.Topic);
            Assert.Equal(MqttTopicDirection.Publish, t.Direction);
            Assert.True(t.Retain);
            Assert.Equal(5000, t.PublishIntervalMs);
            Assert.Equal(MqttJsonTemplate.KvWithTimestamp, t.JsonTemplate);
        }

        [Fact]
        public void 添加Topic_缺connection_name_拒绝()
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("connection_name", r.ErrorMessage);
        }

        [Fact]
        public void 添加Topic_连接不存在_拒绝()
        {
            var svc = NewService(out _);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "无此连接", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            Assert.False(r.Success);
            Assert.Equal("NOT_FOUND", r.ErrorCode);
        }

        [Theory]
        [InlineData("plant/+/temp", "发布通配符拒绝")]
        [InlineData("plant/#", "发布通配符拒绝")]
        public void 添加发布Topic_非法路径_拒绝(string topic, string label)
        {
            var svc = NewService(out _);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = topic, ["direction"] = "publish",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void 添加发布Topic_超长_拒绝()
        {
            var svc = NewService(out _);
            AddConn(svc);
            var longTopic = "plant/" + new string('a', 600);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = longTopic, ["direction"] = "publish",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("512", r.ErrorMessage);
        }

        [Fact]
        public void 添加订阅Topic_wildcard_合法()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "泵站", ["topic"] = "plant/+/status", ["direction"] = "subscribe",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Single(p.MqttSettings!.Connections.Single().Topics);
        }

        [Theory]
        [InlineData("plant/b#", "# 未独占整级")]
        [InlineData("#x/y", "# 未独占整级")]
        [InlineData("plant/#/temp", "# 不在末级")]
        public void 添加订阅Topic_hash非独占末级_拒绝(string topic, string label)
        {
            var svc = NewService(out _);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = topic, ["direction"] = "subscribe",
            });
            Assert.False(r.Success, $"应拒绝 {topic}: {label}");
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void 添加订阅Topic_合法hash末级_通过()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "all", ["topic"] = "plant/#", ["direction"] = "subscribe",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Single(p.MqttSettings!.Connections.Single().Topics);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(99)]
        public void 添加Topic_QoS越界_拒绝(int qos)
        {
            var svc = NewService(out _);
            AddConn(svc);
            var r = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish", ["qos"] = qos,
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("QoS", r.ErrorMessage);
        }

        [Fact]
        public void 更新Topic_QoS与周期_成功()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish", ["qos"] = 0 });
            var r = svc.Execute("mqtt_update_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = "broker-A", ["name"] = "温度上报", ["qos"] = 1, ["retain"] = true,
                ["publish_interval_ms"] = 10000, ["json_template"] = 1,
            });
            Assert.True(r.Success, r.ErrorMessage);
            var t = p.MqttSettings!.Connections.Single().Topics.Single();
            Assert.Equal(1, t.Qos);
            Assert.True(t.Retain);
            Assert.Equal(10000, t.PublishIntervalMs);
            Assert.Equal(MqttJsonTemplate.KvWithTimestamp, t.JsonTemplate);
        }

        [Fact]
        public void 更新Topic_QoS越界_拒绝且不改动()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            var r = svc.Execute("mqtt_update_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "t", ["qos"] = 5 });
            Assert.False(r.Success);
            Assert.Equal(0, p.MqttSettings!.Connections.Single().Topics.Single().Qos);   // 原子性：失败不改动
        }

        [Fact]
        public void 同方向同Topic路径_连接内重复_拒绝()
        {
            var svc = NewService(out _);
            AddConn(svc);
            var ok = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "a", ["topic"] = "plant/temp", ["direction"] = "publish" });
            Assert.True(ok.Success, ok.ErrorMessage);
            var dup = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "b", ["topic"] = "plant/temp", ["direction"] = "publish" });
            Assert.False(dup.Success);
            Assert.Contains("同父节点重复", dup.ErrorMessage);
        }

        [Fact]
        public void 跨连接同名Topic_合法()
        {
            var svc = NewService(out var p);
            AddConn(svc, "broker-A");
            AddConn(svc, "broker-B", "192.168.1.15");
            // 两个连接各加同名「温度上报」——不同 broker 命名空间，互不干扰（Z 循环收窄查重到连接内）
            var r1 = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish" });
            var r2 = svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-B", ["name"] = "温度上报", ["topic"] = "sec/temp", ["direction"] = "publish" });
            Assert.True(r1.Success && r2.Success, $"{r1.ErrorMessage} / {r2.ErrorMessage}");
            Assert.Equal(2, p.MqttSettings!.Connections.Count);
            Assert.All(p.MqttSettings.Connections, c => Assert.Single(c.Topics));
        }

        // ═══ Binding（归属本连接）═══

        [Fact]
        public void 设置Binding_成功与覆盖()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish" });
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "温度上报", ["tag_name"] = "温度", ["field_name"] = "temp" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("temp", p.MqttSettings!.Connections.Single().Bindings.Single().FieldName);
            // 覆盖更新（幂等）
            var upd = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "温度上报", ["tag_name"] = "温度", ["field_name"] = "temperature" });
            Assert.True(upd.Success, upd.ErrorMessage);
            Assert.Single(p.MqttSettings!.Connections.Single().Bindings);
            Assert.Equal("temperature", p.MqttSettings!.Connections.Single().Bindings.Single().FieldName);
        }

        [Fact]
        public void 设置Binding_变量不存在_拒绝()
        {
            var svc = NewService(out _);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "t", ["tag_name"] = "不存在变量", ["field_name"] = "f" });
            Assert.False(r.Success);
            Assert.Contains("变量", r.ErrorMessage);
        }

        [Fact]
        public void 设置Binding_主题不存在_拒绝()
        {
            var svc = NewService(out _);
            AddConn(svc);
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "无此主题", ["tag_name"] = "温度", ["field_name"] = "f" });
            Assert.False(r.Success);
            Assert.Contains("主题", r.ErrorMessage);
        }

        [Fact]
        public void 设置Binding_主题属于他连接_拒绝()
        {
            // Z 循环：Binding 挂哪棵 Topic 树即属哪个连接——引用他连接 Topic 拒绝（西门子同构归属）
            var svc = NewService(out _);
            AddConn(svc, "broker-A");
            AddConn(svc, "broker-B", "192.168.1.15");
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            var r = svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-B", ["topic_name"] = "t", ["tag_name"] = "温度", ["field_name"] = "f" });
            Assert.False(r.Success);
            Assert.Contains("不存在", r.ErrorMessage);
        }

        [Fact]
        public void 删除Topic_连带删除Binding()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "温度上报", ["topic"] = "plant/temp", ["direction"] = "publish" });
            svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "温度上报", ["tag_name"] = "温度", ["field_name"] = "temp" });
            var r = svc.Execute("mqtt_delete_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "温度上报" });
            Assert.True(r.Success, r.ErrorMessage);
            var c = p.MqttSettings!.Connections.Single();
            Assert.Empty(c.Topics);
            Assert.Empty(c.Bindings);   // 级联删除（本连接内）
        }

        [Fact]
        public void 移除Binding_成功()
        {
            var svc = NewService(out var p);
            AddConn(svc);
            svc.Execute("mqtt_add_topic", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["name"] = "t", ["topic"] = "a/b", ["direction"] = "publish" });
            svc.Execute("mqtt_set_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "t", ["tag_name"] = "温度", ["field_name"] = "temp" });
            var r = svc.Execute("mqtt_remove_binding", new Dictionary<string, object?>
            { ["connection_name"] = "broker-A", ["topic_name"] = "t", ["tag_name"] = "温度" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Empty(p.MqttSettings!.Connections.Single().Bindings);
        }
    }
}
