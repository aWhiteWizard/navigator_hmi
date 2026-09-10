using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AB-4（2026-09-11）：变量所属设备关联（Tag.DeviceName / proto tag.device_name=9）。
    /// 背景（Do 期发现的关键缺口）：PC 侧此前从不写 DeviceName——采集变量在设备端拿不到连接参数，
    /// 且 AB-2/AB-3 的「按设备协议选 Modbus RTU/TCP + 每（协议,设备）一实例」完全依赖它。
    /// 本测试锁定：命令层可写入/清除/校验归属；编译产物携带 device_name（proto 契约真源）。
    /// </summary>
    public class TagDeviceRefTests
    {
        private static CommandService NewService(out HMIProject project)
        {
            project = new HMIProject { Name = "设备归属测试工程" };
            project.Devices.Add(new DeviceConfig
            {
                Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":1}",
            });
            project.Devices.Add(new DeviceConfig
            {
                Name = "PLC-A", Protocol = ProtocolType.ModbusTCP,
                ConnectionInfo = "{\"ip\":\"192.168.1.50\",\"port\":502,\"slaveId\":1}",
            });
            return new CommandService(project);
        }

        [Fact]
        public void createTag_指定设备_写入DeviceName()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "PT100",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("PT100", p.Tags.Single().DeviceName);
        }

        [Fact]
        public void createTag_设备不存在_拒绝()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "不存在的设备",
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("不存在", r.ErrorMessage);
            Assert.Empty(p.Tags);   // 原子性
        }

        [Fact]
        public void createTag_未提供设备_DeviceName为空_兼容旧行为()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "内部量", ["data_type"] = "FLOAT",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("", p.Tags.Single().DeviceName);
        }

        [Fact]
        public void updateTag_改设备_生效()
        {
            var svc = NewService(out var p);
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "PT100",
            });
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["device"] = "PLC-A", ["source"] = "modbus://1/1",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("PLC-A", p.Tags.Single().DeviceName);
        }

        [Fact]
        public void updateTag_未提供设备_保留原归属()
        {
            var svc = NewService(out var p);
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "PT100",
            });
            var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "温度", ["unit"] = "℃" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("PT100", p.Tags.Single().DeviceName);   // OptIfProvided 语义：未提供不改
        }

        [Fact]
        public void updateTag_显式空串_清除归属()
        {
            var svc = NewService(out var p);
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "PT100",
            });
            var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "温度", ["device"] = "" });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("", p.Tags.Single().DeviceName);
        }

        [Fact]
        public void updateTag_设备不存在_拒绝且不改动()
        {
            var svc = NewService(out var p);
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "PT100",
            });
            var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "温度", ["device"] = "幽灵设备" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Equal("PT100", p.Tags.Single().DeviceName);
        }

        [Fact]
        public void 编译产物携带device_name_设备端路由依赖()
        {
            var svc = NewService(out var p);
            p.ProjectFilePath = Path.Combine(Path.GetTempPath(), "ab4_devref", "设备归属.hmiproj");
            Directory.CreateDirectory(Path.GetDirectoryName(p.ProjectFilePath)!);
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["source"] = "modbus://1/0", ["device"] = "PT100",
            });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                // proto tag.device_name = 9：设备端 projectparser 消费它定位连接参数与传输协议
                Assert.Equal("PT100", nav.Tags.Single().DeviceName);
            }
            finally
            {
                var dir = Path.GetDirectoryName(p.ProjectFilePath)!;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void createTag_设备参数进compact白名单_AI可建带设备变量()
        {
            // AB-4：device 须 KeepInCompact，否则 AI 走 compact schema 时看不到该参数（同 A4 group_name 教训）
            var def = new NavigatorHMI.CommandLayer.Handlers.CreateTagHandler().Definition;
            Assert.True(def.Parameters["device"].KeepInCompact);
            var upd = new NavigatorHMI.CommandLayer.Handlers.UpdateTagHandler().Definition;
            Assert.True(upd.Parameters["device"].KeepInCompact);
        }
    }
}
