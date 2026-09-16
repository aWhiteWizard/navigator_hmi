using System.Globalization;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// Modbus 变量「数据来源」的解析 / 归一化 / 校验（PC 端单一来源，命令层 + 编译门禁 + GUI 共用）。
    ///
    /// 背景（2026-09-16 缺陷实录）：变量来源此前是**自由文本、零校验**——用户在 CLI/控制台把来源写成
    /// `4x:0001`，而 FW 只认 `modbus://&lt;从站&gt;/&lt;寄存器&gt;`（`acquisition.cpp` 里 `protocolForTag()` 对
    /// 非 `mqtt://`/`modbus://` 的来源返回空 ⇒ **直接跳过、不采集、不报错**）⇒ 变量恒 0、无任何日志。
    /// 本类把「哪些写法合法、怎么写才被 FW 采集」收敛成一处置。
    ///
    /// **寄存器地址三种写法（务必区分，别混）**：
    /// <list type="table">
    /// <item><term>规范形式 `modbus://&lt;从站&gt;/&lt;PDU&gt;`</term><description>第二段是 **PDU 地址**（FW 实际下发值）。如 `modbus://100/1`。</description></item>
    /// <item><term>裸数字 `1` / `0001`</term><description>按 **PDU 直接地址**理解（与设备手册「寄存器 01」口径一致）。</description></item>
    /// <item><term>4x 记法 `4x:0002` / `4x0002` / `40002`</term><description>按 **保持寄存器编号**理解，1 起编号：`PDU = 编号 − 1`（`40001` ↔ PDU 0）。</description></item>
    /// </list>
    /// ⚠️ 同一串 `0001`：裸数字 = PDU 1；`4x:0001` = 保持寄存器第 1 个 = PDU 0 —— 这正是行业里最经典的错一位来源。
    ///
    /// FW 当前只实现**保持寄存器（FC3 读 / FC6 写）**，故 `3x/1x/0x`（输入寄存器/离散量/线圈）**明确拒绝**并给出原因，
    /// 不做「静默接受、运行期无效」。
    /// </summary>
    public static class ModbusTagSource
    {
        /// <summary>规范前缀（FW 判据）。</summary>
        public const string Prefix = "modbus://";

        /// <summary>MQTT 前缀（FW 判据；Z 循环起 tag 侧 mqtt:// 语义已废弃，但为兼容旧工程仍放行）。</summary>
        public const string MqttPrefix = "mqtt://";

        /// <summary>从站地址范围（与 FW `kModbusSlaveMax` 及设备侧校验一致）。</summary>
        public const int SlaveMin = 1;
        public const int SlaveMax = 247;

        /// <summary>寄存器 PDU 地址范围（16 位地址空间）。</summary>
        public const int RegisterMin = 0;
        public const int RegisterMax = 65535;

        /// <summary>构造规范来源串。</summary>
        public static string Build(int slave, int register)
            => $"{Prefix}{slave}/{register}";

        /// <summary>是否 MODBUS 来源（FW 判据同款：仅看前缀）。</summary>
        public static bool IsModbusSource(string? source)
            => !string.IsNullOrEmpty(source) && source.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>是否 MQTT 来源。</summary>
        public static bool IsMqttSource(string? source)
            => !string.IsNullOrEmpty(source) && source.StartsWith(MqttPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 严格解析**规范形式** `modbus://&lt;从站&gt;/&lt;PDU&gt;`（不做记法换算；用于校验已归一化的值）。
        /// </summary>
        public static bool TryParse(string? source, out int slave, out int register, out string error)
        {
            slave = 0;
            register = 0;
            error = "";
            if (!IsModbusSource(source))
            {
                error = $"不是 Modbus 来源（必须以 {Prefix} 开头，或填写寄存器地址由系统归一化）";
                return false;
            }

            var rest = source!.Substring(Prefix.Length).Trim();
            var parts = rest.Split('/');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out slave)
                || !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out register))
            {
                error = $"格式应为 {Prefix}<从站地址>/{RegisterMin}~{RegisterMax} 的寄存器 PDU 地址，如 {Prefix}100/1（实际 \"{source}\"）";
                return false;
            }
            if (slave < SlaveMin || slave > SlaveMax)
            {
                error = $"从站地址必须在 {SlaveMin}~{SlaveMax}（实际 {slave}）";
                return false;
            }
            if (register < RegisterMin || register > RegisterMax)
            {
                error = $"寄存器地址必须在 {RegisterMin}~{RegisterMax}（实际 {register}）";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 解析**寄存器地址输入**（支持裸数字 / 4x 记法 / 40001 式 / 完整 modbus:// 串）→ 规范二段。
        /// 完整 modbus:// 串时 <paramref name="slave"/> 由串内给出（调用方若已定设备，需自行比对）。
        /// </summary>
        public static bool TryParseRegisterInput(string? input, out int slave, out int register, out string error)
        {
            slave = 0;
            register = 0;
            error = "";
            var text = (input ?? "").Trim();
            if (text.Length == 0)
            {
                error = "寄存器地址为空";
                return false;
            }

            // ① 完整规范串：modbus://<slave>/<reg>
            if (IsModbusSource(text))
                return TryParse(text, out slave, out register, out error);

            var lower = text.ToLowerInvariant().Replace(" ", "");
            if (lower.StartsWith(MqttPrefix))
            {
                error = "这是 MQTT 来源，不是 Modbus 寄存器地址（MQTT 变量请在 MQTT 页配置映射）";
                return false;
            }

            // ② 记法前缀：4x / 3x / 1x / 0x（可带冒号）
            var body = lower;
            string? area = null;
            if (lower.Length >= 2 && (lower[1] == 'x'))
            {
                area = lower.Substring(0, 2);
                body = lower.Substring(2).TrimStart(':', '：');
            }

            if (!long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                error = $"无法识别的寄存器地址 \"{input}\"（可用：裸数字如 1、4x 记法如 4x:0002、PLC 式如 40002、或完整 {Prefix}100/1）";
                return false;
            }

            // ⚠️ 全程用 long 计算、**先判范围再转型**：早期版本直接 `(int)n` 会在未检查上下文截断
            // （如裸 4294967296 → 截成 0 被当成合法 PDU 0，属"静默读错寄存器"，比报错更糟）。
            long resolved;
            if (area == null)
            {
                // 裸数字：≤9999 = PDU 直接地址（与设备手册「寄存器 01」口径一致）；
                // 5/6 位 PLC 式数字按区号判型（1xxxx/3xxxx 是未实现的区，明确拒绝而不是当成超大 PDU 地址）
                if (IsPlcArea(n, 1) || IsPlcArea(n, 3))
                {
                    error = $"\"{input}\" 按 PLC 记法属 {(IsPlcArea(n, 1) ? "1x 离散量" : "3x 输入寄存器")} 区，" +
                            "FW 目前只读**保持寄存器 4x**（FC3 读 / FC6 写）——请写 4xxxx 或直接写 PDU 地址（如 1）";
                    return false;
                }
                resolved = IsPlcArea(n, 4)
                    ? n - (n > 99999 ? 400001 : 40001)   // 5 位 40001 起 / 6 位 400001 起
                    : n;                                  // 其余（0~9999 / 20000~29999 / 50000+）按 PDU 直接地址
            }
            else if (area == "4x")
            {
                // PLC 式：5 位 40001 起 / 6 位 400001 起 ⇒ PDU = n − 基址；简写 4x:0002 ⇒ PDU = 2 − 1
                var pdu = n > 99999 ? n - 400001 : n >= 40001 ? n - 40001 : n - 1;
                if (pdu < 0)
                {
                    error = $"4x 记法的寄存器编号从 1 起（4x:0001 / 40001 = PDU 0）；\"{input}\" 解析为负数";
                    return false;
                }
                resolved = pdu;
            }
            else
            {
                error = $"暂不支持 {area} 区（FW 目前只读**保持寄存器 4x**：FC3 读 / FC6 写）；" +
                        $"输入寄存器 3x、线圈 0x、离散量 1x 均未实现，请改用 4x 区";
                return false;
            }

            if (resolved < RegisterMin || resolved > RegisterMax)
            {
                error = $"解析出的寄存器地址 {resolved} 超出 {RegisterMin}~{RegisterMax}（输入 \"{input}\"）";
                return false;
            }
            register = (int)resolved;
            return true;
        }

        /// <summary>PLC 5/6 位区号判定：`&lt;areaDigit&gt;0001` ~ `&lt;areaDigit&gt;9999`（5 位）或 `&lt;areaDigit&gt;00001` ~ `&lt;areaDigit&gt;65535`（6 位）。</summary>
        private static bool IsPlcArea(long value, int areaDigit)
        {
            var five = areaDigit * 10000L;
            var six = areaDigit * 100000L;
            return (value >= five + 1 && value <= five + 9999)
                || (value >= six + 1 && value <= six + 65536);   // 6 位 400001..465536（含 PDU 65535）
        }

        /// <summary>
        /// 归一化为规范来源串（命令层 / GUI 共用入口）。
        /// </summary>
        /// <param name="input">用户输入（裸数字 / 4x 记法 / 40001 式 / 完整 modbus:// 串）。</param>
        /// <param name="deviceSlaveId">所属设备的从站地址（0 = 未知/无设备，此时必须写全 modbus:// 串）。</param>
        /// <param name="normalized">归一化结果（成功时非空）。</param>
        /// <param name="error">失败原因（可直接展示给用户）。</param>
        public static bool TryNormalize(string? input, int deviceSlaveId, out string normalized, out string error)
        {
            normalized = "";
            input = input?.Trim();   // 自 trim（不依赖调用方）——避免 " modbus://1/1 " 这类带空白输入被判非法
            if (!TryParseRegisterInput(input, out var slave, out var register, out error))
                return false;

            if (IsModbusSource(input))
            {
                // 完整串自带从站地址：与设备从站不一致时明确报错（FW 按串内从站下发，写错=读到别的从站/读不到）
                if (deviceSlaveId > 0 && slave != deviceSlaveId)
                {
                    error = $"来源里的从站地址 {slave} 与所属设备的从站地址 {deviceSlaveId} 不一致——" +
                            $"FW 按来源里的从站下发请求，请改成 {Build(deviceSlaveId, register)} 或改设备配置";
                    return false;
                }
                normalized = Build(slave, register);
                return true;
            }

            if (deviceSlaveId <= 0)
            {
                error = $"只填寄存器地址时无法确定从站地址——请先指定所属设备，或写全 {Prefix}<从站>/<寄存器>（如 {Prefix}100/1）";
                return false;
            }
            normalized = Build(deviceSlaveId, register);
            return true;
        }
    }
}
