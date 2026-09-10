using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AB-4（2026-09-11）：Modbus RTU 串口参数约束测试。
    /// 用户裁决：八档波特率白名单 {1200,2400,4800,9600,19200,38400,57600,115200} + port 必须以 /dev/ 开头。
    /// 口径三处一致：本测试（PC 命令层 ModbusRtuRules）/ PC GUI（DeviceEditDialog）/
    /// FW 设备端（modbus_rtu_driver.cpp kAllowedBaud）——改一处须同步三处。
    /// </summary>
    public class ModbusRtuConfigTests
    {
        private static CommandService NewService(out HMIProject project)
        {
            project = new HMIProject { Name = "RTU 测试工程" };
            return new CommandService(project);
        }

        private static Dictionary<string, object?> ConfigureArgs(string name, string connection)
            => new() { ["name"] = name, ["protocol"] = "ModbusRTU", ["connection_info"] = connection };

        [Theory]
        [InlineData(1200)]
        [InlineData(2400)]
        [InlineData(4800)]
        [InlineData(9600)]
        [InlineData(19200)]
        [InlineData(38400)]
        [InlineData(57600)]
        [InlineData(115200)]
        public void configureDevice_八档波特率_全部接受(int baud)
        {
            var svc = NewService(out var p);
            var r = svc.Execute("configure_device",
                ConfigureArgs("PT100", $"{{\"port\":\"/dev/ttyUSB0\",\"baud\":{baud},\"slaveId\":1}}"));
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Single(p.Devices);
            Assert.Equal(ProtocolType.ModbusRTU, p.Devices[0].Protocol);
        }

        [Theory]
        [InlineData(300)]
        [InlineData(600)]
        [InlineData(14400)]
        [InlineData(28800)]
        [InlineData(128000)]
        [InlineData(0)]
        public void configureDevice_白名单外波特率_拒绝(int baud)
        {
            var svc = NewService(out var p);
            var r = svc.Execute("configure_device",
                ConfigureArgs("PT100", $"{{\"port\":\"/dev/ttyUSB0\",\"baud\":{baud},\"slaveId\":1}}"));
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("baud", r.ErrorMessage);
            Assert.Empty(p.Devices);   // 原子性：拒绝不落库
        }

        [Theory]
        [InlineData("COM1")]            // Windows 串口名（板端无此设备）
        [InlineData("ttyUSB0")]         // 缺 /dev/ 前缀（相对/裸名）
        [InlineData("/dev/ttyUSB0/")]   // 形态可疑路径（仅校验前缀，允许——记录边界）
        [InlineData("")]
        public void configureDevice_非dev路径_拒绝(string port)
        {
            var svc = NewService(out var p);
            var r = svc.Execute("configure_device",
                ConfigureArgs("PT100", $"{{\"port\":\"{port}\",\"baud\":9600,\"slaveId\":1}}"));
            if (port.StartsWith("/dev/", StringComparison.Ordinal))
            {
                Assert.True(r.Success, r.ErrorMessage);   // 前缀合规即接受（`/dev/` 开头的裸名由 FW 打开失败兜底）
            }
            else
            {
                Assert.False(r.Success);
                Assert.Equal("INVALID_PARAM", r.ErrorCode);
                Assert.Contains("/dev/", r.ErrorMessage);
                Assert.Empty(p.Devices);
            }
        }

        [Fact]
        public void configureDevice_缺少port字段_拒绝()
        {
            var svc = NewService(out _);
            var r = svc.Execute("configure_device", ConfigureArgs("PT100", "{\"baud\":9600,\"slaveId\":1}"));
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("port", r.ErrorMessage);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(248)]
        public void configureDevice_从站号越界_拒绝(int slaveId)
        {
            var svc = NewService(out _);
            var r = svc.Execute("configure_device",
                ConfigureArgs("PT100", $"{{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":{slaveId}}}"));
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("slaveId", r.ErrorMessage);
        }

        [Fact]
        public void updateDevice_非法串口参数_拒绝且保留原配置()
        {
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig
            {
                Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":1}",
            });
            var r = svc.Execute("update_device", new Dictionary<string, object?>
            {
                ["name"] = "PT100",
                ["connection_info"] = "{\"port\":\"COM3\",\"baud\":9600,\"slaveId\":1}",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("/dev/", r.ErrorMessage);
            Assert.Contains("/dev/ttyUSB0", p.Devices.Single().ConnectionInfo);   // 原值未变
        }

        [Fact]
        public void updateDevice_合法串口参数_更新成功()
        {
            var svc = NewService(out var p);
            p.Devices.Add(new DeviceConfig
            {
                Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":1}",
            });
            var r = svc.Execute("update_device", new Dictionary<string, object?>
            {
                ["name"] = "PT100",
                ["connection_info"] = "{\"port\":\"/dev/ttyUSB1\",\"baud\":19200,\"slaveId\":2}",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Contains("/dev/ttyUSB1", p.Devices.Single().ConnectionInfo);
            Assert.Contains("19200", p.Devices.Single().ConnectionInfo);
        }

        // ── 规则常量本身（GUI/FW 同口径锚点） ──

        [Fact]
        public void ModbusRtuRules_默认值_与设备端实际环境一致()
        {
            Assert.Equal(9600, ModbusRtuRules.DefaultBaudRate);          // PT100 模块出厂默认 9600 8N1
            Assert.Equal("/dev/ttyUSB0", ModbusRtuRules.DefaultPort);    // 板端 USB-RS485（内核 CH341 → ttyUSB0）
            Assert.Equal(8, ModbusRtuRules.AllowedBaudRates.Count);
        }

        [Theory]
        [InlineData("/dev/ttyUSB0", true)]
        [InlineData("/dev/ttyS3", true)]
        [InlineData("COM1", false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(null, false)]
        public void ModbusRtuRules_端口路径判定(string? port, bool expected)
            => Assert.Equal(expected, ModbusRtuRules.IsValidPort(port));
    }
}
