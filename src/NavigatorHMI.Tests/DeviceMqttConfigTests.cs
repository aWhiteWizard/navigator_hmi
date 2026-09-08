using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-3a（2026-09-10 ④通信批）：MQTT 设备连接全字段校验 + list-devices CLI 查询口测试。
    /// 真源 = DeviceConfig.connection_info JSON（FW connInfoForDevice 现有机制消费）；
    /// 校验规则：broker 必填且无协议前缀/路径、port 1-65535、version 0|1、clientId ≤64 无空格、
    /// keepAlive ≥0（0=禁用心跳）、enableTls 布尔。
    /// </summary>
    public class DeviceMqttConfigTests
    {
        private static CommandService NewService()
        {
            var p = new HMIProject { Name = "MQTT 测试工程" };
            return new CommandService(p);
        }

        private static string MqttJson(string broker = "192.168.1.1", int? port = 1883,
            int? version = null, string clientId = "hmi-01", string password = "",
            int? keepAlive = null, bool? enableTls = null)
        {
            var parts = new System.Collections.Generic.List<string>
            {
                $"\"broker\":\"{broker}\""
            };
            if (port != null) parts.Add($"\"port\":{port}");
            if (version != null) parts.Add($"\"version\":{version}");
            if (clientId != null) parts.Add($"\"clientId\":\"{clientId}\"");
            if (password != null) parts.Add($"\"password\":\"{password}\"");
            if (keepAlive != null) parts.Add($"\"keepAlive\":{keepAlive}");
            if (enableTls != null) parts.Add($"\"enableTls\":{(enableTls.Value ? "true" : "false")}");
            return "{" + string.Join(",", parts) + "}";
        }

        [Fact]
        public void configureDevice_MQTT全字段_成功入库()
        {
            var svc = NewService();
            var json = MqttJson(broker: "192.168.1.50", port: 1883, version: 0, clientId: "pump-01",
                keepAlive: 30, enableTls: false);
            var r = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "MQTT-Broker", ["protocol"] = "MQTT", ["connection_info"] = json,
            });
            Assert.True(r.Success, r.ErrorMessage);
            var text = System.Text.Json.JsonSerializer.Serialize(r.Data);
            Assert.Contains("MQTT-Broker", text);
        }

        [Theory]
        [InlineData("mqtt://192.168.1.1", "不应带协议前缀")]
        [InlineData("", "非空字符串 broker")]
        [InlineData("192.168.1.1/x", "不应含路径")]
        public void configureDevice_MQTT_broker非法_拒绝(string broker, string reason)
        {
            var svc = NewService();
            var r = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "bad", ["protocol"] = "MQTT", ["connection_info"] = MqttJson(broker: broker),
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains(reason, r.ErrorMessage);
        }

        [Theory]
        [InlineData(0, "port")]
        [InlineData(65536, "port")]
        [InlineData(2, "version")]
        public void configureDevice_MQTT_数值越界_拒绝(int bad, string field)
        {
            var svc = NewService();
            var json = field == "port"
                ? MqttJson(port: bad)
                : MqttJson(version: bad);
            var r = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "bad", ["protocol"] = "MQTT", ["connection_info"] = json,
            });
            Assert.False(r.Success);
            Assert.Contains(field == "port" ? "port" : "version", r.ErrorMessage);
        }

        [Fact]
        public void configureDevice_MQTT_keepAlive零值_合法()
        {
            // 0 = MQTT 协议禁用心跳（合法值——防校验误拒）
            var svc = NewService();
            var r = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "keep0", ["protocol"] = "MQTT", ["connection_info"] = MqttJson(keepAlive: 0),
            });
            Assert.True(r.Success, r.ErrorMessage);
        }

        [Fact]
        public void configureDevice_MQTT_clientId超长或含空格_拒绝()
        {
            var svc = NewService();
            var r1 = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "c1", ["protocol"] = "MQTT",
                ["connection_info"] = MqttJson(clientId: new string('a', 65)),
            });
            Assert.False(r1.Success);
            Assert.Contains("clientId", r1.ErrorMessage);

            var r2 = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "c2", ["protocol"] = "MQTT",
                ["connection_info"] = MqttJson(clientId: "has space"),
            });
            Assert.False(r2.Success);
        }

        [Fact]
        public void listDevices_MQTT凭据掩码_不泄露明文密码()
        {
            var svc = NewService();
            // 密文凭据入库（真实链路：GUI CredentialStore.Encrypt 后 dpapi: 前缀——此处模拟密文形态）
            svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "MQTT-1", ["protocol"] = "MQTT",
                ["connection_info"] = MqttJson(broker: "192.168.1.1", password: "dpapi:ENCRYPTEDBLOB"),
            });
            svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "PLC-1", ["protocol"] = "ModbusTCP",
                ["connection_info"] = "{\"ip\":\"192.168.1.10\",\"port\":502,\"slaveId\":1}",
            });

            var r = svc.Execute("list_devices", new Dictionary<string, object?>());
            Assert.True(r.Success, r.ErrorMessage);
            // Data 匿名对象 → JSON 断言 count
            var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
            Assert.Contains("\"count\":2", json);
            Assert.DoesNotContain("ENCRYPTEDBLOB", json);   // 掩码——密文内容也不回显
            Assert.Contains("[encrypted]", json);
            Assert.Contains("PLC-1", json);
        }

        [Fact]
        public void configureDevice_MQTT_明文密码_拒绝()
        {
            // Y-3a reviewer 🟡1：非空 password 必须带 dpapi: 前缀——CLI/直传明文一律拒绝（绝不明文入库）
            var svc = NewService();
            var r = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "leak", ["protocol"] = "MQTT",
                ["connection_info"] = MqttJson(broker: "192.168.1.1", password: "Secret123"),
            });
            Assert.False(r.Success);
            Assert.Contains("dpapi", r.ErrorMessage);
        }

        [Fact]
        public void updateDevice_MQTT_补全字段校验_生效()
        {
            var svc = NewService();
            svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "MQTT-1", ["protocol"] = "MQTT",
                ["connection_info"] = MqttJson(broker: "192.168.1.1"),
            });
            // 更新为非法 broker → 拒绝（校验用目标协议）
            var bad = svc.Execute("update_device", new Dictionary<string, object?>
            {
                ["name"] = "MQTT-1", ["connection_info"] = MqttJson(broker: "mqtt://bad"),
            });
            Assert.False(bad.Success);
            Assert.Contains("协议前缀", bad.ErrorMessage);
            // 更新为合法 → 成功
            var ok = svc.Execute("update_device", new Dictionary<string, object?>
            {
                ["name"] = "MQTT-1", ["connection_info"] = MqttJson(broker: "192.168.1.99", port: 8883),
            });
            Assert.True(ok.Success, ok.ErrorMessage);
        }

        [Fact]
        public void listDevices_空工程_返回零设备()
        {
            var svc = NewService();
            var r = svc.Execute("list_devices", new Dictionary<string, object?>());
            Assert.True(r.Success);
            var text = System.Text.Json.JsonSerializer.Serialize(r.Data);
            Assert.Contains("\"count\":0", text);
        }
    }
}
