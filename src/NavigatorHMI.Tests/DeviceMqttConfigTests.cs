using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-3a（2026-09-10 ④通信批）+ Z 循环收窄（2026-09-11）：MQTT 设备配置校验测试改造——
    /// MQTT 已移出通讯配置页（专用 MQTT 根多连接管理器——连接参数内联 MqttConnection.Config）：
    /// ① configure/update_device 传 protocol=MQTT → 一律拒绝（提示移出通讯页——防绕过；
    ///    ProtocolType.MQTT 保留 deprecated 仅旧工程反序列化兼容）；② 连接参数校验迁移至连接层
    ///    （mqtt_add/update_connection——Z-2 已测）；③ list_devices 对 deprecated MQTT 设备（旧数据直模）
    ///    摘要仍掩码凭据不泄露（Summarize MQTT case 保留防旧数据泄漏）。
    /// </summary>
    public class DeviceMqttConfigTests
    {
        private static CommandService NewService(out HMIProject project)
        {
            project = new HMIProject { Name = "MQTT 测试工程" };
            return new CommandService(project);
        }

        [Fact]
        public void configureDevice_MQTT_拒绝_提示移出通讯页()
        {
            var svc = NewService(out _);
            // 任意 MQTT 设备配置一律拒绝（含此前合法全字段——校验规则语义废弃）
            var r = svc.Execute("configure_device", new Dictionary<string, object?>
            {
                ["name"] = "MQTT-Broker", ["protocol"] = "MQTT",
                ["connection_info"] = "{\"broker\":\"192.168.1.1\",\"port\":1883}",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("已移出通讯配置页", r.ErrorMessage);
        }

        [Fact]
        public void updateDevice_协议改MQTT_拒绝()
        {
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig
            {
                Name = "PLC-1", Protocol = ProtocolType.ModbusTCP,
                ConnectionInfo = "{\"ip\":\"192.168.1.10\",\"port\":502,\"slaveId\":1}",
            });
            var r = svc.Execute("update_device", new Dictionary<string, object?>
            {
                ["name"] = "PLC-1", ["protocol"] = "MQTT",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("已移出通讯配置页", r.ErrorMessage);
            // 原协议不被改动（原子性）
            Assert.Equal(ProtocolType.ModbusTCP, p.Devices.Single().Protocol);
        }

        [Fact]
        public void listDevices_MQTT凭据掩码_不泄露明文密码()
        {
            // Z 循环：MQTT 设备 deprecated——旧工程可能残留（直模构造）；list_devices 摘要仍掩码防泄漏
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig
            {
                Name = "MQTT-1", Protocol = ProtocolType.MQTT,
                ConnectionInfo = "{\"broker\":\"192.168.1.1\",\"port\":1883,\"password\":\"dpapi:ENCRYPTEDBLOB\"}",
            });
            p.Devices.Add(new DeviceConfig
            {
                Name = "PLC-1", Protocol = ProtocolType.ModbusTCP,
                ConnectionInfo = "{\"ip\":\"192.168.1.10\",\"port\":502,\"slaveId\":1}",
            });

            var r = svc.Execute("list_devices", new Dictionary<string, object?>());
            Assert.True(r.Success, r.ErrorMessage);
            var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
            Assert.Contains("\"count\":2", json);
            Assert.DoesNotContain("ENCRYPTEDBLOB", json);   // 掩码——密文内容也不回显
            Assert.Contains("[encrypted]", json);
            Assert.Contains("PLC-1", json);
        }

        [Fact]
        public void listDevices_空工程_返回零设备()
        {
            var svc = NewService(out _);
            var r = svc.Execute("list_devices", new Dictionary<string, object?>());
            Assert.True(r.Success);
            var text = System.Text.Json.JsonSerializer.Serialize(r.Data);
            Assert.Contains("\"count\":0", text);
        }
    }
}
