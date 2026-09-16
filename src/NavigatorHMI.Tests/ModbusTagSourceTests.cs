using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// 2026-09-16：Modbus 变量「数据来源」解析 / 归一化 / 校验（<see cref="ModbusTagSource"/>）纯逻辑测试。
    ///
    /// 缺陷背景：来源此前自由文本零校验，用户在 CLI 写 `4x:0001` 也能落库，而 FW 只认
    /// `modbus://&lt;从站&gt;/&lt;寄存器&gt;` ⇒ 该变量被静默跳过（不采集、无日志、值恒 0）。
    /// 本测试锁定三种写法的换算口径（这是行业里最经典的"错一位"来源）与拒绝口径。
    /// </summary>
    public class ModbusTagSourceTests
    {
        // ── 规范串解析 ──────────────────────────────────────────────

        [Theory]
        [InlineData("modbus://100/1", 100, 1)]
        [InlineData("MODBUS://7/0", 7, 0)]
        [InlineData("modbus:// 100 / 12 ", 100, 12)]
        public void 规范串解析(string src, int slave, int reg)
        {
            Assert.True(ModbusTagSource.TryParse(src, out var s, out var r, out var err), err);
            Assert.Equal(slave, s);
            Assert.Equal(reg, r);
        }

        [Theory]
        [InlineData("modbus://0/1", "从站地址")]      // 从站下界
        [InlineData("modbus://248/1", "从站地址")]    // 从站上界（FW kModbusSlaveMax=247）
        [InlineData("modbus://100/70000", "寄存器地址")]
        [InlineData("modbus://100", "格式")]
        [InlineData("modbus://100/1/2", "格式")]
        [InlineData("modbus://abc/1", "格式")]
        [InlineData("mqtt://", "modbus://")]
        [InlineData("4x:0001", "modbus://")]
        public void 规范串非法_解析失败(string src, string keyword)
        {
            Assert.False(ModbusTagSource.TryParse(src, out _, out _, out var err));
            Assert.Contains(keyword, err);
        }

        // ── 寄存器地址换算（三种写法）────────────────────────────────
        // 同一串 "0001"：裸数字 = PDU 1；4x:0001 = 保持寄存器第 1 个 = PDU 0 —— 错一位的经典位置。

        [Theory]
        [InlineData("1", 1)]          // 裸数字 = PDU 直接地址
        [InlineData("0001", 1)]
        [InlineData("0", 0)]
        [InlineData("4x:0001", 0)]    // 4x 记法从 1 编号 → PDU = 1-1
        [InlineData("4x:0002", 1)]    // ← PT100 温度值1路（手册"寄存器 01" = PLC 40002）
        [InlineData("4x0002", 1)]     // 无冒号写法
        [InlineData("40001", 0)]      // PLC 式 4xxxx → PDU = n-40001
        [InlineData("40002", 1)]
        [InlineData("465536", 65535)] // 上界
        [InlineData("modbus://100/5", 5)]
        public void 寄存器记法换算(string input, int expectPdu)
        {
            Assert.True(ModbusTagSource.TryParseRegisterInput(input, out _, out var pdu, out var err), err);
            Assert.Equal(expectPdu, pdu);
        }

        [Theory]
        [InlineData("3x:0001", "保持寄存器")]   // 输入寄存器未实现
        [InlineData("0x:0001", "保持寄存器")]   // 线圈未实现
        [InlineData("1x:0001", "保持寄存器")]   // 离散量未实现
        [InlineData("30001", "保持寄存器")]     // 裸 5 位按 PLC 记法识别为 3x 区（未实现）
        [InlineData("abc", "无法识别")]
        [InlineData("", "为空")]
        [InlineData("mqtt://plant/temp", "MQTT")]
        public void 寄存器记法非法_拒绝(string input, string keyword)
        {
            Assert.False(ModbusTagSource.TryParseRegisterInput(input, out _, out _, out var err));
            Assert.Contains(keyword, err);
        }

        // ── 归一化（命令层 / GUI 共用入口）───────────────────────────

        [Theory]
        [InlineData("1", 100, "modbus://100/1")]
        [InlineData("4x:0002", 100, "modbus://100/1")]
        [InlineData("40002", 100, "modbus://100/1")]
        [InlineData("4x:0001", 100, "modbus://100/0")]
        [InlineData("modbus://100/1", 100, "modbus://100/1")]   // 已规范：原样
        [InlineData("modbus://7/3", 0, "modbus://7/3")]         // 无设备也放行（完整串自带从站）
        public void 归一化_成功(string input, int devSlave, string expect)
        {
            Assert.True(ModbusTagSource.TryNormalize(input, devSlave, out var norm, out var err), err);
            Assert.Equal(expect, norm);
        }

        [Fact]
        public void 归一化_从站与设备不一致_拒绝并给出建议值()
        {
            Assert.False(ModbusTagSource.TryNormalize("modbus://99/1", 100, out _, out var err));
            Assert.Contains("99", err);
            Assert.Contains("modbus://100/1", err);   // 建议值可直接抄
        }

        [Fact]
        public void 归一化_只填裸数字但无设备_拒绝()
        {
            Assert.False(ModbusTagSource.TryNormalize("1", 0, out _, out var err));
            Assert.Contains("所属设备", err);
        }

        [Fact]
        public void 前缀判定()
        {
            Assert.True(ModbusTagSource.IsModbusSource("modbus://1/1"));
            Assert.False(ModbusTagSource.IsModbusSource("4x:0001"));
            Assert.True(ModbusTagSource.IsMqttSource("mqtt://plant/temp"));
            Assert.False(ModbusTagSource.IsMqttSource("modbus://1/1"));
        }

        // ── 复审补（2026-09-16）：溢出与裸数字边界 ──────────────────────
        // 早期版本直接 `(int)n` 会在未检查上下文截断：裸 4294967296(=2^32) 会被截成 PDU 0 而"合法通过"
        // ⇒ 静默读错寄存器（比报错更糟）。

        [Theory]
        [InlineData("4294967296")]        // 2^32 → 曾截断成 0
        [InlineData("4295032831")]        // 2^32+65535 → 曾截断成 65535
        [InlineData("4x4295367297")]      // 4x 记法 + 2^32+40001 → 曾截断成 PDU 0
        [InlineData("99999999999999999999")]   // 超 long
        [InlineData("465537")]            // 6 位 4x 上界 +1
        [InlineData("-1")]
        [InlineData("+1")]
        [InlineData("1e3")]
        public void 溢出与非法写法_一律拒绝(string input)
        {
            Assert.False(ModbusTagSource.TryParseRegisterInput(input, out _, out _, out var err), "不应接受：" + input);
            Assert.NotEqual("", err);
        }

        [Theory]
        [InlineData("10000", 10000)]   // 非标准区间的 5 位数字按 PDU 直通（与"判型只认 1x/3x/4x"口径一致）
        [InlineData("20000", 20000)]
        [InlineData("40000", 40000)]
        [InlineData("50000", 50000)]
        [InlineData("65535", 65535)]   // PDU 上界
        public void 裸数字边界_PDU直通(string input, int expectPdu)
        {
            Assert.True(ModbusTagSource.TryParseRegisterInput(input, out _, out var pdu, out var err), err);
            Assert.Equal(expectPdu, pdu);
        }

        [Theory]
        [InlineData("10001", "1x 离散量")]     // 5 位 1xxxx 判型拒绝
        [InlineData("100001", "1x 离散量")]    // 6 位同上
        [InlineData("30001", "3x 输入寄存器")]
        [InlineData("300001", "3x 输入寄存器")]
        public void 裸5_6位在未实现区_拒绝并说明(string input, string keyword)
        {
            Assert.False(ModbusTagSource.TryParseRegisterInput(input, out _, out _, out var err));
            Assert.Contains(keyword, err);
        }

        [Fact]
        public void 大小写_前缀不敏感但归一出小写()
        {
            Assert.True(ModbusTagSource.TryNormalize("MODBUS://100/1", 100, out var norm, out var err), err);
            Assert.Equal("modbus://100/1", norm);   // FW 判据大小写敏感 ⇒ 落库必须小写
            Assert.True(ModbusTagSource.TryNormalize(" modbus://100/1 ", 100, out var n2, out _));
            Assert.Equal("modbus://100/1", n2);     // 自带 trim（不依赖调用方）
        }
    }
}
