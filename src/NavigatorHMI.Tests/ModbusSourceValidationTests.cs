using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// 2026-09-16：变量来源「能落库 + 能编译 = 设备端真的会采集」的三层保证。
    ///
    /// 实录缺陷：用户在 CLI/控制台把变量来源写成 `4x:0001`（现用 PT100 模块该值既非 FW 认的
    /// `modbus://…`，也不是任何可采集来源）⇒ 设备端 `protocolForTag()` 直接跳过该变量，
    /// **不建驱动、不轮询、无任何日志、值恒 0**，用户只能看到"读不到值"。
    /// 本测试锁定三处防线：① 命令层落库即归一化 ② 命令层拒绝不可采集写法 ③ 编译期门禁兜住绕过命令层的路径。
    /// </summary>
    public class ModbusSourceValidationTests
    {
        private static HMIProject Project(out CommandService svc, int deviceSlaveId = 100)
        {
            var p = new HMIProject { Name = "Modbus 来源校验" };
            p.Devices.Add(new DeviceConfig
            {
                Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = $"{{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":{deviceSlaveId}}}",
            });
            p.Devices.Add(new DeviceConfig
            {
                Name = "网关", Protocol = ProtocolType.ModbusTCP,
                ConnectionInfo = $"{{\"ip\":\"192.168.1.12\",\"port\":502,\"slaveId\":{deviceSlaveId}}}",
            });
            svc = new CommandService(p);
            return p;
        }

        private static CommandResult CreateTag(CommandService svc, string name, string source, string device)
            => svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = name, ["data_type"] = "UINT16", ["source"] = source, ["device"] = device,
            });

        // ── ① 命令层：落库即归一化（用户写 4x 记法/裸数字/40002 都能落成规范串）──────

        [Theory]
        [InlineData("1", "modbus://100/1")]          // 裸数字 = PDU
        [InlineData("4x:0002", "modbus://100/1")]    // 4x 记法 = 保持寄存器编号（PT100 温度值1路）
        [InlineData("40002", "modbus://100/1")]      // PLC 式
        [InlineData("4x:0001", "modbus://100/0")]    // 用户实录写法：归一化后是 PDU 0（≠ 他想要的温度寄存器 1）
        [InlineData("modbus://100/1", "modbus://100/1")]
        public void createTag_来源归一化为规范串(string input, string expect)
        {
            var p = Project(out var svc);
            var r = CreateTag(svc, "温度", input, "PT100");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(expect, p.Tags.Single().Source);
        }

        [Fact]
        public void createTag_只填寄存器地址_用设备从站号补全()
        {
            var p = Project(out var svc, deviceSlaveId: 7);
            var r = CreateTag(svc, "温度", "4x:0002", "网关");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("modbus://7/1", p.Tags.Single().Source);   // 从站号取自设备，而非硬编码 1
        }

        // ── ② 命令层：拒绝不可采集写法（防静默失效）────────────────────────

        [Fact]
        public void createTag_来源不可识别_拒绝()
        {
            var p = Project(out var svc);
            var r = CreateTag(svc, "温度", "4x0002x", "PT100");
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("无法识别", r.ErrorMessage);
            Assert.Empty(p.Tags);
        }

        [Fact]
        public void createTag_只读输入寄存器3x_拒绝并说明只支持4x()
        {
            Project(out var svc);
            var r = CreateTag(svc, "温度", "3x:0001", "PT100");
            Assert.False(r.Success);
            Assert.Contains("保持寄存器", r.ErrorMessage);
        }

        [Fact]
        public void createTag_从站与设备不一致_拒绝并给出建议()
        {
            Project(out var svc);
            var r = CreateTag(svc, "温度", "modbus://99/1", "PT100");
            Assert.False(r.Success);
            Assert.Contains("modbus://100/1", r.ErrorMessage);
        }

        [Fact]
        public void createTag_关联设备但来源为空_拒绝()
        {
            Project(out var svc);
            var r = CreateTag(svc, "温度", "", "PT100");
            Assert.False(r.Success);
            Assert.Contains("数据来源为空", r.ErrorMessage);
        }

        [Fact]
        public void createTag_裸数字但无设备_拒绝()
        {
            Project(out var svc);
            var r = CreateTag(svc, "温度", "1", "");
            Assert.False(r.Success);
            Assert.Contains("所属设备", r.ErrorMessage);
        }

        [Fact]
        public void createTag_内部变量_仍放行()
        {
            var p = Project(out var svc);
            var r = CreateTag(svc, "计数", "", "");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("", p.Tags.Single().Source);
        }

        [Fact]
        public void updateTag_改来源_归一化()
        {
            var p = Project(out var svc);
            Assert.True(CreateTag(svc, "温度", "", "").Success);
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["source"] = "40002", ["device"] = "PT100",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("modbus://100/1", p.Tags.Single().Source);
        }

        [Fact]
        public void updateTag_只改设备致来源不一致_拒绝且不改模型()
        {
            var p = Project(out var svc, deviceSlaveId: 100);
            Assert.True(CreateTag(svc, "温度", "modbus://100/1", "PT100").Success);
            p.Devices.Add(new DeviceConfig
            {
                Name = "PT100-B", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":7}",
            });
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["device"] = "PT100-B",
            });
            Assert.False(r.Success);
            Assert.Contains("不一致", r.ErrorMessage);
            Assert.Equal("PT100", p.Tags.Single().DeviceName);        // 原子性：归属未被改动
            Assert.Equal("modbus://100/1", p.Tags.Single().Source);
        }

        // ── ③ 编译期门禁（兜住直接构造模型 / 旧工程文件等绕过命令层的路径）──────

        private static HMIProject CompilableProject()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_modbus_source");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject { Name = "来源门禁", ProjectFilePath = Path.Combine(dir, "src-gate.hmiproj") };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Devices.Add(new DeviceConfig
            {
                Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":100}",
            });
            return p;
        }

        [Fact]
        public void 编译_来源非modbus写法_报错()
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "RTU_0001", DataType = TagDataType.UINT16, Source = "4x:0001", DeviceName = "PT100" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("RTU_0001") && e.Contains("无法识别"));
            Assert.Null(r.OutputPath);   // 有错不出产物
        }

        [Fact]
        public void 编译_从站与设备不一致_报错()
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.UINT16, Source = "modbus://1/1", DeviceName = "PT100" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("不一致") && e.Contains("modbus://100/1"));
        }

        [Fact]
        public void 编译_关联设备但来源为空_报错()
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.UINT16, Source = "", DeviceName = "PT100" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("数据来源为空"));
        }

        [Fact]
        public void 编译_归属设备不存在_报错()
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.UINT16, Source = "modbus://100/1", DeviceName = "已删除的设备" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("悬空归属"));
        }

        [Fact]
        public void 编译_规范来源_通过()
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "RTU_温度", DataType = TagDataType.UINT16, Source = "modbus://100/1", DeviceName = "PT100" });
            p.Tags.Add(new Tag { Name = "内部计数", DataType = TagDataType.UINT16, Source = "" });
            p.Tags.Add(new Tag { Name = "温度上报", DataType = TagDataType.FLOAT, Source = "mqtt://" });
            var r = ProjectGenerator.Compile(p);
            Assert.False(r.HasErrors, string.Join("; ", r.Errors));
        }

        // ── 复审补（2026-09-16）：审阅指出的未覆盖分支 ─────────────────────

        [Fact]
        public void updateTag_给内部变量补设备但不给来源_拒绝()
        {
            // 复审驳回项 1：create 拒、门禁拒、update 却放行 ⇒ 会落库"有设备没来源"的变量（设备端不采集）
            var p = Project(out var svc);
            Assert.True(CreateTag(svc, "计数", "", "").Success);
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "计数", ["device"] = "PT100",
            });
            Assert.False(r.Success);
            Assert.Contains("数据来源为空", r.ErrorMessage);
            Assert.Equal("", p.Tags.Single().DeviceName);   // 原子性：归属未写入
        }

        [Theory]
        [InlineData("4x:0001x")]
        [InlineData("3x:0001")]
        public void updateTag_改设备时原来源不可采集_拒绝(string badSource)
        {
            var p = Project(out var svc);
            p.Tags.Add(new Tag { Name = "坏来源", DataType = TagDataType.UINT16, Source = badSource });   // 绕过命令层直接造存量坏数据
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "坏来源", ["device"] = "PT100",
            });
            Assert.False(r.Success);
            Assert.Equal("", p.Tags.Single().DeviceName);
            Assert.Equal(badSource, p.Tags.Single().Source);
        }

        [Fact]
        public void createTag_MQTT来源_归一化scheme小写()
        {
            var p = Project(out var svc);
            var r = CreateTag(svc, "上报", "MQTT://plant/temp", "");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("mqtt://plant/temp", p.Tags.Single().Source);   // FW 判据大小写敏感 ⇒ scheme 必须小写
        }

        [Fact]
        public void createTag_Modbus设备配MQTT来源_拒绝()
        {
            Project(out var svc);
            var r = CreateTag(svc, "上报", "mqtt://plant/temp", "PT100");
            Assert.False(r.Success);
            Assert.Contains("Modbus 设备", r.ErrorMessage);
        }

        [Fact]
        public void createTag_回显归一化来源()
        {
            Project(out var svc);
            var r = CreateTag(svc, "温度", "4x:0002", "PT100");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Contains("modbus://100/1", r.Data?.ToString() ?? "");
        }

        [Fact]
        public void 编译_无设备归属的modbus变量_豁免不报错()
        {
            // B4_测试工程形态（modbus://1/0 无归属）：设备端回落 TCP 默认连接并打 warning ⇒ 属已知遗留，不在编译期拦
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "mb_temp", DataType = TagDataType.INT16, Source = "modbus://1/0" });
            var r = ProjectGenerator.Compile(p);
            Assert.False(r.HasErrors, string.Join("; ", r.Errors));
        }

        [Fact]
        public void 编译_设备缺slaveId_报错()
        {
            var p = CompilableProject();
            p.Devices[0].ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600}";   // 无 slaveId
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.UINT16, Source = "modbus://100/1", DeviceName = "PT100" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("slaveId"));
        }

        [Fact]
        public void 编译_Modbus设备配mqtt来源_报错()
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "上报", DataType = TagDataType.FLOAT, Source = "mqtt://plant/temp", DeviceName = "PT100" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("Modbus 设备") && e.Contains("值恒 0"));
        }

        // ── 复审 R2/R4 补：规范串等价性 + 空串清空语义 ──────────────────────

        [Theory]
        [InlineData("MODBUS://100/1")]   // 前缀大小写（FW startsWith 大小写敏感）
        [InlineData("modbus://100/01")]  // PDU 前导零（非规范写法）
        public void 编译_来源非规范形式_报错(string src)
        {
            var p = CompilableProject();
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.UINT16, Source = src, DeviceName = "PT100" });
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("不是规范形式") && e.Contains("modbus://100/1"));
        }

        [Fact]
        public void updateTag_清空来源_须同时清设备()
        {
            // ① 只清 source（设备仍在）→ 拒绝，并指向 --device ""
            var p = Project(out var svc);
            Assert.True(CreateTag(svc, "温度", "modbus://100/1", "PT100").Success);
            var r1 = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "温度", ["source"] = "" });
            Assert.False(r1.Success);
            Assert.Contains("数据来源为空", r1.ErrorMessage);
            Assert.Equal("modbus://100/1", p.Tags.Single().Source);   // 原子性：来源未清
            Assert.Equal("PT100", p.Tags.Single().DeviceName);

            // ② 同时清 source + device → 成功变内部变量
            var r2 = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["source"] = "", ["device"] = "",
            });
            Assert.True(r2.Success, r2.ErrorMessage);
            Assert.Equal("", p.Tags.Single().Source);
            Assert.Equal("", p.Tags.Single().DeviceName);
        }
    }
}
