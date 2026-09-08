using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-3b（2026-09-10 ④通信批）：MQTT 编译校验测试（ProjectGenerator 3h 段）——
    /// EnableMqtt 开启无 MQTT 设备 / Binding 引用悬空（主题不存在/变量不存在）/ StatusTag 变量不存在 → COMPILE_FAILED。
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

        private static void AddMqttDevice(HMIProject p, string name = "MQTT-Broker")
        {
            p.Devices.Add(new DeviceConfig
            {
                Name = name,
                Protocol = ProtocolType.MQTT,
                ConnectionInfo = "{\"broker\":\"192.168.1.1\",\"port\":1883}",
            });
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void EnableMqtt开启_无MQTT设备_编译报错()
        {
            var p = ProjectWithMqtt();
            p.MqttSettings = new MqttSettings { EnableMqtt = true };
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("MQTT 设备"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void EnableMqtt开启_有MQTT设备_无映射_编译通过()
        {
            var p = ProjectWithMqtt();
            AddMqttDevice(p);
            p.MqttSettings = new MqttSettings { EnableMqtt = true };
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.False(r.HasErrors, string.Join("; ", r.Errors));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void Binding引用主题不存在_编译报错()
        {
            var p = ProjectWithMqtt();
            AddMqttDevice(p);
            p.MqttSettings = new MqttSettings
            {
                EnableMqtt = true,
                Bindings = { new MqttBinding { TopicName = "无此主题", TagName = "温度", FieldName = "t" } },
            };
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
            AddMqttDevice(p);
            p.MqttSettings = new MqttSettings
            {
                EnableMqtt = true,
                Topics = { new MqttTopic { Name = "t1", Direction = MqttTopicDirection.Publish, Topic = "a/b" } },
                Bindings = { new MqttBinding { TopicName = "t1", TagName = "不存在变量", FieldName = "t" } },
            };
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
            AddMqttDevice(p);
            p.MqttSettings = new MqttSettings
            {
                EnableMqtt = true,
                Config = new MqttConfig { Broker = "192.168.1.1", StatusTag = "无此状态变量" },
            };
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.True(r.HasErrors);
                Assert.Contains(r.Errors, e => e.Contains("StatusTag"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 完整MQTT配置_编译通过_产物含MqttSettings()
        {
            var p = ProjectWithMqtt();
            AddMqttDevice(p);
            p.MqttSettings = new MqttSettings
            {
                EnableMqtt = true,
                SchemaVersion = 1,
                Config = new MqttConfig { Broker = "192.168.1.1", Port = 1883, KeepAliveSec = 60 },
                Topics =
                {
                    new MqttTopic { Name = "温度上报", Direction = MqttTopicDirection.Publish, Topic = "plant/temp", PublishIntervalMs = 5000 },
                    new MqttTopic { Name = "泵站", Direction = MqttTopicDirection.Subscribe, Topic = "plant/+/status" },
                },
                Bindings =
                {
                    new MqttBinding { TopicName = "温度上报", TagName = "温度", FieldName = "temp" },
                    new MqttBinding { TopicName = "泵站", TagName = "温度", FieldName = "pumpTemp" },
                },
            };
            try
            {
                var r = ProjectGenerator.Compile(p);
                Assert.False(r.HasErrors, string.Join("; ", r.Errors));
                using var fs = File.OpenRead(r.OutputPath);
                var nav = ProtoBuf.Serializer.Deserialize<NavihmiProject>(fs);
                Assert.NotNull(nav.MqttSettings);
                Assert.True(nav.MqttSettings!.EnableMqtt);
                Assert.Equal(2, nav.MqttSettings.Topics.Count);
                Assert.Equal(2, nav.MqttSettings.Bindings.Count);
            }
            finally { CleanOutput(p); }
        }
    }
}
