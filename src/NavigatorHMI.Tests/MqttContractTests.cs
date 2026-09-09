using System;
using System.Linq;
using System.Reflection;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-2 MQTT 三层映射契约测试（2026-09-10 ④通信批）+ Z 循环多连接管理器（2026-09-11）。
    /// 1) protobuf round-trip：MqttSettings 三层（Config/Topic/Binding）序列化回读一致；
    ///    （Z：Connections 多连接每项 name+config+topics+bindings round-trip；旧单份字段 3/4/5/6 deprecated 保留可序列化——迁移读取依赖）
    /// 2) 编译 DTO 透传：HMIProject.MqttSettings → NavihmiProject.MqttSettings（ProjectGenerator ToDto）；
    /// 3) Tag.Group 字段 round-trip（Y-5 变量分组契约先行）；
    /// 4) 字段号审计：C# ProtoMember ↔ proto 字段号对拍（Mqtt* 五类型含 MqttConnection + 锚点 24/10/4——
    ///    锁两端同步，防 N+9「PC proto 漏同步」型错位，2026-09-10 reviewer 🟡 闭环）；
    /// 5) KeepAliveSec=0（MQTT 禁用心跳合法值）IsRequired 恒写回读保真。
    /// 字段号与 proto（fw/proto/navihmi.proto）逐字段对齐——改契约须两端同步（N+9 教训）。
    /// </summary>
    public class MqttContractTests
    {
        private static byte[] Serialize<T>(T obj)
        {
            using var ms = new System.IO.MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, obj);
            return ms.ToArray();
        }

        private static T Deserialize<T>(byte[] bytes)
        {
            return ProtoBuf.Serializer.Deserialize<T>(new System.IO.MemoryStream(bytes));
        }

        [Fact]
        public void MqttSettings_三层_序列化round_trip_完整()
        {
            var s = new MqttSettings
            {
                EnableMqtt = true,
                SchemaVersion = 1,
                Config = new MqttConfig
                {
                    Broker = "127.0.0.1",
                    Port = 1883,
                    Version = MqttVersion.V3_1_1,
                    ClientId = "hmi-y2-test",
                    Username = "admin",
                    Password = "AES-ENC-BLOB",
                    KeepAliveSec = 60,
                    EnableTls = false,
                    StatusTag = "mqtt_conn_state",
                },
                Topics =
                {
                    new MqttTopic
                    {
                        Name = "泵站状态",
                        Direction = MqttTopicDirection.Subscribe,
                        Topic = "plant/pump/status",
                        Qos = 1,
                        Retain = false,
                        PublishIntervalMs = 0,
                        JsonTemplate = MqttJsonTemplate.Kv,
                        ResponseTopic = "",
                    },
                    new MqttTopic
                    {
                        Name = "温度上报",
                        Direction = MqttTopicDirection.Publish,
                        Topic = "plant/temp",
                        Qos = 0,
                        Retain = true,
                        PublishIntervalMs = 5000,
                        JsonTemplate = MqttJsonTemplate.KvWithTimestamp,
                        ResponseTopic = "plant/temp/ack",
                    },
                },
                Bindings =
                {
                    new MqttBinding { TopicName = "泵站状态", TagName = "Pump_Run", FieldName = "run" },
                    new MqttBinding { TopicName = "泵站状态", TagName = "Pump_Speed", FieldName = "speed" },
                    new MqttBinding { TopicName = "温度上报", TagName = "Temp_1", FieldName = "temp" },
                },
            };
            var back = Deserialize<MqttSettings>(Serialize(s));

            Assert.True(back.EnableMqtt);
            Assert.Equal(1, back.SchemaVersion);
            // Config 层
            Assert.Equal("127.0.0.1", back.Config.Broker);
            Assert.Equal(1883, back.Config.Port);
            Assert.Equal(MqttVersion.V3_1_1, back.Config.Version);
            Assert.Equal("hmi-y2-test", back.Config.ClientId);
            Assert.Equal("admin", back.Config.Username);
            Assert.Equal("AES-ENC-BLOB", back.Config.Password);   // 加密包字段 round-trip 保真（FW 端解密依赖）
            Assert.Equal(60, back.Config.KeepAliveSec);
            Assert.False(back.Config.EnableTls);
            Assert.Equal("mqtt_conn_state", back.Config.StatusTag);
            // Topic 层（2 条 + 顺序 + 全字段）
            Assert.Equal(2, back.Topics.Count);
            Assert.Equal("泵站状态", back.Topics[0].Name);
            Assert.Equal(MqttTopicDirection.Subscribe, back.Topics[0].Direction);
            Assert.Equal("plant/pump/status", back.Topics[0].Topic);
            Assert.Equal(1, back.Topics[0].Qos);
            Assert.False(back.Topics[0].Retain);
            Assert.Equal(0, back.Topics[0].PublishIntervalMs);
            Assert.Equal(MqttJsonTemplate.Kv, back.Topics[0].JsonTemplate);
            Assert.Equal("", back.Topics[0].ResponseTopic);
            Assert.Equal("温度上报", back.Topics[1].Name);
            Assert.Equal(MqttTopicDirection.Publish, back.Topics[1].Direction);
            Assert.Equal(5000, back.Topics[1].PublishIntervalMs);
            Assert.Equal(MqttJsonTemplate.KvWithTimestamp, back.Topics[1].JsonTemplate);
            Assert.Equal("plant/temp/ack", back.Topics[1].ResponseTopic);
            Assert.True(back.Topics[1].Retain);   // true 侧写入路径验证（reviewer 🟡：topic0 false 测不出字段错位丢值）
            // Binding 层（3 条 + 引用锚）
            Assert.Equal(3, back.Bindings.Count);
            Assert.Equal("泵站状态", back.Bindings[0].TopicName);
            Assert.Equal("Pump_Run", back.Bindings[0].TagName);
            Assert.Equal("run", back.Bindings[0].FieldName);
            Assert.Equal("温度上报", back.Bindings[2].TopicName);
            Assert.Equal("Temp_1", back.Bindings[2].TagName);
            Assert.Equal("temp", back.Bindings[2].FieldName);
        }

        [Fact]
        public void MqttSettings_多连接_round_trip_每连接独立三层()
        {
            // Z 循环（2026-09-11）：MqttSettings.Connections 多连接——每连接 name+config+topics+bindings
            // 独立 round-trip（西门子同构归属：Binding 挂该连接 Topic 树；同名 Topic 跨连接互不干扰）
            var s = new MqttSettings
            {
                EnableMqtt = true,
                SchemaVersion = 1,
                Connections =
                {
                    new MqttConnection
                    {
                        Name = "broker-A",
                        Config = new MqttConfig { Broker = "192.168.1.14", Port = 1883, ClientId = "hmi-A", StatusTag = "conn_a" },
                        Topics =
                        {
                            new MqttTopic { Name = "温度上报", Direction = MqttTopicDirection.Publish, Topic = "plant/temp", Qos = 0, Retain = true, PublishIntervalMs = 5000, JsonTemplate = MqttJsonTemplate.KvWithTimestamp },
                            new MqttTopic { Name = "泵站", Direction = MqttTopicDirection.Subscribe, Topic = "plant/+/status", Qos = 1 },
                        },
                        Bindings =
                        {
                            new MqttBinding { TopicName = "温度上报", TagName = "Temp_1", FieldName = "temp" },
                            new MqttBinding { TopicName = "泵站", TagName = "Pump_Run", FieldName = "run" },
                        },
                    },
                    new MqttConnection
                    {
                        Name = "broker-B",
                        Config = new MqttConfig { Broker = "192.168.1.14", Port = 1884, StatusTag = "conn_b" },
                        Topics =
                        {
                            // 与 broker-A 同名 Topic 合法（不同 broker 命名空间）
                            new MqttTopic { Name = "温度上报", Direction = MqttTopicDirection.Publish, Topic = "sec/temp", JsonTemplate = MqttJsonTemplate.Kv },
                        },
                        Bindings =
                        {
                            new MqttBinding { TopicName = "温度上报", TagName = "Temp_2", FieldName = "t" },
                        },
                    },
                },
            };
            var back = Deserialize<MqttSettings>(Serialize(s));

            Assert.True(back.EnableMqtt);
            Assert.Equal(2, back.Connections.Count);
            // broker-A 全字段
            Assert.Equal("broker-A", back.Connections[0].Name);
            Assert.Equal("192.168.1.14", back.Connections[0].Config.Broker);
            Assert.Equal(1883, back.Connections[0].Config.Port);
            Assert.Equal("hmi-A", back.Connections[0].Config.ClientId);
            Assert.Equal("conn_a", back.Connections[0].Config.StatusTag);
            Assert.Equal(2, back.Connections[0].Topics.Count);
            Assert.Equal("plant/temp", back.Connections[0].Topics[0].Topic);
            Assert.Equal(5000, back.Connections[0].Topics[0].PublishIntervalMs);
            Assert.Equal("plant/+/status", back.Connections[0].Topics[1].Topic);
            Assert.Equal(2, back.Connections[0].Bindings.Count);
            Assert.Equal("Temp_1", back.Connections[0].Bindings[0].TagName);
            Assert.Equal("run", back.Connections[0].Bindings[1].FieldName);
            // broker-B 独立三层（同名 Topic 归属各自连接）
            Assert.Equal("broker-B", back.Connections[1].Name);
            Assert.Equal(1884, back.Connections[1].Config.Port);
            Assert.Equal("sec/temp", back.Connections[1].Topics[0].Topic);
            Assert.Single(back.Connections[1].Bindings);
            Assert.Equal("Temp_2", back.Connections[1].Bindings[0].TagName);
        }

        [Fact]
        public void MqttSettings_未配置_null_不序列化()
        {
            var p = new HMIProject { Name = "无MQTT工程" };
            var back = Deserialize<HMIProject>(Serialize(p));
            Assert.Null(back.MqttSettings);   // 缺省 null → FW 不建连接对象（旧工程无兼容包袱）
        }

        [Fact]
        public void HMIProject_MqttSettings_编译DTO透传_round_trip()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_y2_mqtt");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "MQTT工程",
                ProjectFilePath = Path.Combine(dir, "y2-mqtt.hmiproj"),
            };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            // Z 循环编译校验：EnableMqtt 开启需连接（broker 非空）+ Binding 变量存在——测试工程补足（连接级；旧 DeviceName 设备语义废弃）
            p.Tags.Add(new Tag { Name = "V1", DataType = TagDataType.FLOAT });
            p.MqttSettings = new MqttSettings
            {
                EnableMqtt = true,
                Connections =
                {
                    new MqttConnection
                    {
                        Name = "broker-A",
                        Config = new MqttConfig { Broker = "192.168.1.100", Port = 1883 },
                        Topics = { new MqttTopic { Name = "t1", Direction = MqttTopicDirection.Publish, Topic = "a/b" } },
                        Bindings = { new MqttBinding { TopicName = "t1", TagName = "V1", FieldName = "v1" } },
                    }
                },
            };
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = ProtoBuf.Serializer.Deserialize<NavihmiProject>(fs);
                Assert.NotNull(nav.MqttSettings);
                Assert.True(nav.MqttSettings!.EnableMqtt);
                var conn = Assert.Single(nav.MqttSettings.Connections);   // Z 循环多连接透传（西门子同构归属）
                Assert.Equal("broker-A", conn.Name);
                Assert.Equal("192.168.1.100", conn.Config.Broker);
                Assert.Equal("a/b", conn.Topics[0].Topic);
                Assert.Equal("V1", conn.Bindings[0].TagName);
            }
            finally
            {
                var output = Path.Combine(dir, "output");
                if (Directory.Exists(output)) Directory.Delete(output, true);
            }
        }

        [Fact]
        public void Tag_Group_round_trip_默认空与自定义()
        {
            var t1 = new Tag { Name = "V1", Group = "" };   // 未分组
            Assert.Equal("", Deserialize<Tag>(Serialize(t1)).Group);
            var t2 = new Tag { Name = "V2", Group = "电机组" };
            var back = Deserialize<Tag>(Serialize(t2));
            Assert.Equal("电机组", back.Group);
        }

        [Fact]
        public void MqttConfig_KeepAliveSec零值_回读保持零()
        {
            // reviewer 🟡（2026-09-10）：KeepAliveSec 初始值 60 ≠ CLR 默认 0，显式设 0（MQTT 禁用心跳合法值）
            // 须恒写——IsRequired=true 防 protobuf-net 省略回读失真（4_bugs bool-default-loss 同族 int 延伸）
            var c = new MqttConfig { Broker = "127.0.0.1", KeepAliveSec = 0 };
            var back = Deserialize<MqttConfig>(Serialize(c));
            Assert.Equal(0, back.KeepAliveSec);
        }

        // ═══ Y-2 字段号审计（reviewer 🟡 闭环：锁 C# ProtoMember ↔ proto 字段号，防 N+9「PC proto 漏同步」型错位）═══

        private static Dictionary<string, int> FieldNumbers(Type t) => t.GetProperties()
            .Select(pi => (pi, attr: pi.GetCustomAttribute<ProtoBuf.ProtoMemberAttribute>()))
            .Where(x => x.attr != null)
            .ToDictionary(x => x.pi.Name, x => x.attr!.Tag);

        [Fact]
        public void MqttConfig_字段号与proto对齐()
        {
            var f = FieldNumbers(typeof(MqttConfig));
            // proto MqttConfig: broker=1 port=2 version=3 client_id=4 username=5 password=6 keep_alive_sec=7 enable_tls=8 status_tag=9
            Assert.Equal(1, f[nameof(MqttConfig.Broker)]);
            Assert.Equal(2, f[nameof(MqttConfig.Port)]);
            Assert.Equal(3, f[nameof(MqttConfig.Version)]);
            Assert.Equal(4, f[nameof(MqttConfig.ClientId)]);
            Assert.Equal(5, f[nameof(MqttConfig.Username)]);
            Assert.Equal(6, f[nameof(MqttConfig.Password)]);
            Assert.Equal(7, f[nameof(MqttConfig.KeepAliveSec)]);
            Assert.Equal(8, f[nameof(MqttConfig.EnableTls)]);
            Assert.Equal(9, f[nameof(MqttConfig.StatusTag)]);
            Assert.Equal(9, f.Count);   // 无多无少
        }

        [Fact]
        public void MqttTopic_字段号与proto对齐()
        {
            var f = FieldNumbers(typeof(MqttTopic));
            // proto MqttTopic: name=1 direction=2 topic=3 qos=4 retain=5 publish_interval_ms=6 json_template=7 response_topic=8
            Assert.Equal(1, f[nameof(MqttTopic.Name)]);
            Assert.Equal(2, f[nameof(MqttTopic.Direction)]);
            Assert.Equal(3, f[nameof(MqttTopic.Topic)]);
            Assert.Equal(4, f[nameof(MqttTopic.Qos)]);
            Assert.Equal(5, f[nameof(MqttTopic.Retain)]);
            Assert.Equal(6, f[nameof(MqttTopic.PublishIntervalMs)]);
            Assert.Equal(7, f[nameof(MqttTopic.JsonTemplate)]);
            Assert.Equal(8, f[nameof(MqttTopic.ResponseTopic)]);
            Assert.Equal(8, f.Count);
        }

        [Fact]
        public void MqttBinding_字段号与proto对齐()
        {
            var f = FieldNumbers(typeof(MqttBinding));
            // proto MqttBinding: topic_name=1 tag_name=2 field_name=3
            Assert.Equal(1, f[nameof(MqttBinding.TopicName)]);
            Assert.Equal(2, f[nameof(MqttBinding.TagName)]);
            Assert.Equal(3, f[nameof(MqttBinding.FieldName)]);
            Assert.Equal(3, f.Count);
        }

        [Fact]
        public void MqttSettings_字段号与proto对齐()
        {
            var f = FieldNumbers(typeof(MqttSettings));
            // proto MqttSettings: enable_mqtt=1 schema_version=2 config=3(deprecated) topics=4(deprecated) bindings=5(deprecated) device_name=6(deprecated) connections=7(Z 循环)
            Assert.Equal(1, f[nameof(MqttSettings.EnableMqtt)]);
            Assert.Equal(2, f[nameof(MqttSettings.SchemaVersion)]);
            Assert.Equal(3, f[nameof(MqttSettings.Config)]);
            Assert.Equal(4, f[nameof(MqttSettings.Topics)]);
            Assert.Equal(5, f[nameof(MqttSettings.Bindings)]);
            Assert.Equal(6, f[nameof(MqttSettings.DeviceName)]);
            Assert.Equal(7, f[nameof(MqttSettings.Connections)]);   // Z 循环多连接管理器（2026-09-11）
            Assert.Equal(f.Count, f.Values.Distinct().Count());   // 🟡3（reviewer Z-1）：防两属性同 Tag 漏检
            Assert.Equal(7, f.Count);
        }

        [Fact]
        public void MqttConnection_字段号与proto对齐()
        {
            var f = FieldNumbers(typeof(MqttConnection));
            // proto MqttConnection: name=1 config=2 topics=3 bindings=4
            Assert.Equal(1, f[nameof(MqttConnection.Name)]);
            Assert.Equal(2, f[nameof(MqttConnection.Config)]);
            Assert.Equal(3, f[nameof(MqttConnection.Topics)]);
            Assert.Equal(4, f[nameof(MqttConnection.Bindings)]);
            Assert.Equal(f.Count, f.Values.Distinct().Count());   // 🟡3（reviewer Z-1）：防两属性同 Tag 漏检
            Assert.Equal(4, f.Count);
        }

        [Fact]
        public void MqttSettings_空Connections_round_trip()
        {
            // 🟡4（reviewer Z-1）：Connections 空表（EnableMqtt 开但未建连接）序列化回读——空表保真、Config 恒非空兜底
            var s = new MqttSettings { EnableMqtt = true, SchemaVersion = 1 };
            var back = Deserialize<MqttSettings>(Serialize(s));
            Assert.True(back.EnableMqtt);
            Assert.Empty(back.Connections);
            Assert.NotNull(back.Connections);
            Assert.NotNull(back.Config);   // new() 兜底恒非空（判"未配置"须用 EnableMqtt/Connections 空，非 null）
        }

        [Fact]
        public void 锚点字段号_HMIProject24_Tag10_WidgetEvent4()
        {
            // 锚点 = proto 顶层引用（HMIProject.mqtt_settings=24 / Tag.group=10 / WidgetEvent.priority=4）
            Assert.Equal(24, FieldNumbers(typeof(HMIProject))[nameof(HMIProject.MqttSettings)]);
            Assert.Equal(10, FieldNumbers(typeof(Tag))[nameof(Tag.Group)]);
            Assert.Equal(4, FieldNumbers(typeof(WidgetEvent))[nameof(WidgetEvent.Priority)]);
            // 编译契约 DTO 同样 24（.navihmi 产物与源模型双端锚定）
            Assert.Equal(24, FieldNumbers(typeof(NavihmiProject))[nameof(NavihmiProject.MqttSettings)]);
        }
    }
}
