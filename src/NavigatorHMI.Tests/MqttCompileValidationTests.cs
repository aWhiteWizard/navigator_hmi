using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-3b（2026-09-10 ④通信批）+ Z 循环多连接重构（2026-09-11）：MQTT 编译校验测试（ProjectGenerator 3h 段）——
    /// EnableMqtt 开启无连接 / 连接未填 broker / Binding 引用悬空（本连接主题不存在/变量不存在）/ StatusTag 变量不存在 →
    /// COMPILE_FAILED；EnableMqtt 禁用 → 编译 Nullify（产物 MqttSettings null，FW 不建连接对象）。
    /// 旧单份 DeviceName 设备语义废弃（校验连接级；Z-4 迁移归 Connections[0]）。
    /// </summary>
    public class MqttCompileValidationTests
    {
        private static HMIProject ProjectWithMqtt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_mqtt_compile");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "MQTT 编译校验",
                ProjectFilePath = Path.Combine(dir, "mqtt-compile.hmiproj"),
            };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template });
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            return p;
        }

        /// <summary>建连接（直接模型——编译校验测数据构造；EnableMqtt 默认置 true）。</summary>
        private static MqttConnection AddConn(HMIProject p, string name = "broker-A", string broker = "192.168.1.1")
        {
            p.MqttSettings ??= new MqttSettings { SchemaVersion = 1 };
            p.MqttSettings.EnableMqtt = true;
            var c = new MqttConnection
            {
                Name = name,
                Config = new MqttConfig { Broker = broker, Port = 1883, KeepAliveSec = 60 },
            };
            p.MqttSettings.Connections.Add(c);
            return c;
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void EnableMqtt开启_无连接_编译报错()
        {
            var p = ProjectWithMqtt();
            p.MqttSettings = new MqttSettings { EnableMqtt = true };
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("未配置任何连接"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void EnableMqtt开启_有连接无映射_编译通过()
        {
            var p = ProjectWithMqtt();
            AddConn(p);
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.False(r.HasErrors, string.Join("; ", r.Errors));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 连接未填broker_编译报错()
        {
            var p = ProjectWithMqtt();
            AddConn(p, broker: "");   // broker 空——编译级兜底（命令层已拦，防绕过）
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("broker"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void Binding引用主题不存在_编译报错()
        {
            var p = ProjectWithMqtt();
            AddConn(p).Bindings.Add(new MqttBinding { TopicName = "无此主题", TagName = "温度", FieldName = "t" });
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("主题配置"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void Binding引用变量不存在_编译报错()
        {
            var p = ProjectWithMqtt();
            var c = AddConn(p);
            c.Topics.Add(new MqttTopic { Name = "t1", Direction = MqttTopicDirection.Publish, Topic = "a/b" });
            c.Bindings.Add(new MqttBinding { TopicName = "t1", TagName = "不存在变量", FieldName = "t" });
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("变量"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void StatusTag变量不存在_编译报错()
        {
            var p = ProjectWithMqtt();
            AddConn(p).Config.StatusTag = "无此状态变量";
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("StatusTag"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void EnableMqtt禁用_编译Nullify_产物无MqttSettings()
        {
            // Z 循环设计②：EnableMqtt=false → 编译 Nullify（FW 不建任何连接对象——产物 MqttSettings null；PC 编辑态数据保留）
            var p = ProjectWithMqtt();
            var c = AddConn(p);
            c.Topics.Add(new MqttTopic { Name = "温度上报", Direction = MqttTopicDirection.Publish, Topic = "plant/temp" });
            c.Bindings.Add(new MqttBinding { TopicName = "温度上报", TagName = "温度", FieldName = "temp" });
            p.MqttSettings!.EnableMqtt = false;
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.False(r.HasErrors, string.Join("; ", r.Errors));   // 禁用不校验连接（编辑期半成品允许）
                using var fs = File.OpenRead(r.OutputPath);
                var nav = ProtoBuf.Serializer.Deserialize<NavihmiProject>(fs);
                Assert.Null(nav.MqttSettings);   // Nullify：产物无 MQTT 段
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 完整MQTT配置_编译通过_产物含MqttSettings()
        {
            var p = ProjectWithMqtt();
            var c = AddConn(p);
            c.Config.StatusTag = "状态";
            p.Tags.Add(new Tag { Name = "状态", DataType = TagDataType.FLOAT });
            c.Topics.Add(new MqttTopic { Name = "温度上报", Direction = MqttTopicDirection.Publish, Topic = "plant/temp", PublishIntervalMs = 5000 });
            c.Topics.Add(new MqttTopic { Name = "泵站", Direction = MqttTopicDirection.Subscribe, Topic = "plant/+/status" });
            c.Bindings.Add(new MqttBinding { TopicName = "温度上报", TagName = "温度", FieldName = "temp" });
            c.Bindings.Add(new MqttBinding { TopicName = "泵站", TagName = "温度", FieldName = "pumpTemp" });
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.False(r.HasErrors, string.Join("; ", r.Errors));
                using var fs = File.OpenRead(r.OutputPath);
                var nav = ProtoBuf.Serializer.Deserialize<NavihmiProject>(fs);
                Assert.NotNull(nav.MqttSettings);
                Assert.True(nav.MqttSettings!.EnableMqtt);
                var conn = Assert.Single(nav.MqttSettings.Connections);
                Assert.Equal("broker-A", conn.Name);
                Assert.Equal(2, conn.Topics.Count);
                Assert.Equal(2, conn.Bindings.Count);
            }
            finally { CleanOutput(p); }
        }
    }
}
