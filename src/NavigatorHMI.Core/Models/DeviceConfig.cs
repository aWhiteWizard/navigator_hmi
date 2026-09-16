using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>通信协议类型</summary>
    public enum ProtocolType
    {
        /// <summary>Modbus RTU (RS-485 串口)</summary>
        ModbusRTU,
        /// <summary>Modbus TCP (以太网)</summary>
        ModbusTCP,
        /// <summary>MQTT (消息队列遥测传输)</summary>
        MQTT
    }

    /// <summary>
    /// 设备通信配置。定义 HMI 设备连接的现场设备通信参数（PLC/传感器/MQTT Broker）。
    /// </summary>
    [ProtoContract]
    public class DeviceConfig
    {
        /// <summary>设备名称（如 "主PLC" / "温度模块"）</summary>
        [ProtoMember(1)]
        public string Name { get; set; } = "";

        /// <summary>通信协议</summary>
        [ProtoMember(2)]
        public ProtocolType Protocol { get; set; }

        /// <summary>
        /// 连接信息，JSON 字符串格式。
        /// ModbusRTU: {"port":"/dev/ttyUSB0","baud":9600,"slaveId":1}
        /// ModbusTCP: {"ip":"192.168.1.50","port":502,"slaveId":1}
        /// MQTT (Y-3a 2026-09-10 全字段): {"broker":"192.168.1.1","port":1883,"version":0,"clientId":"hmi-01",
        ///   "username":"","password":"<加密包>","keepAlive":60,"enableTls":false,"statusTag":""}
        ///   —— broker 不含协议前缀；password 存 DPAPI 加密包（绝不明文进 .navihmi）；
        ///   version 0=3.1.1/1=5.0；缺省字段取默认（port 1883/keepAlive 60）
        /// </summary>
        [ProtoMember(3)]
        public string ConnectionInfo { get; set; } = "";

        /// <summary>
        /// 从 connection_info 里取 Modbus 从站地址（ModbusRTU/ModbusTCP 的 `slaveId`）。
        /// 返回 0 表示无法解析（JSON 非法/字段缺失/协议非 Modbus）——调用方据此决定"未知从站"分支。
        /// 用途（2026-09-16）：变量来源归一化（<see cref="ModbusTagSource.TryNormalize"/>）与编译门禁需要它做一致性校验。
        /// </summary>
        public int TryGetSlaveId()
        {
            if (Protocol is not (ProtocolType.ModbusRTU or ProtocolType.ModbusTCP))
                return 0;
            if (string.IsNullOrWhiteSpace(ConnectionInfo))
                return 0;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(ConnectionInfo);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return 0;
                if (!doc.RootElement.TryGetProperty("slaveId", out var sid)
                 || sid.ValueKind != System.Text.Json.JsonValueKind.Number)
                    return 0;
                var v = sid.GetInt32();
                return v is >= ModbusTagSource.SlaveMin and <= ModbusTagSource.SlaveMax ? v : 0;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Modbus RTU 串口参数约束（AB-4 2026-09-11 用户裁决：八档白名单 + port 必须 /dev/ 前缀）。
    /// PC 侧 GUI（DeviceEditDialog）与命令层（DeviceHandlers）共用同一份口径；
    /// 设备端 FW `modbus_rtu_driver.cpp` 的波特率白名单与此保持一致——改一处须同步三处。
    /// </summary>
    public static class ModbusRtuRules
    {
        /// <summary>支持的波特率档位（含既往 GUI 下拉的 38400/57600，避免旧工程被拒）。</summary>
        public static readonly IReadOnlyList<int> AllowedBaudRates = new[] { 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200 };

        /// <summary>默认波特率（PT100 模块出厂默认 9600 8N1）。</summary>
        public const int DefaultBaudRate = 9600;

        /// <summary>默认串口路径（RK3562 设备端 UART 未启用，RS-485 走 USB 适配器）。</summary>
        public const string DefaultPort = "/dev/ttyUSB0";

        /// <summary>波特率是否在支持档位内。</summary>
        public static bool IsAllowedBaudRate(int baudRate) => AllowedBaudRates.Contains(baudRate);

        /// <summary>串口路径是否为设备端绝对路径（必须以 /dev/ 开头——板端只认设备文件）。</summary>
        public static bool IsValidPort(string? port)
            => !string.IsNullOrWhiteSpace(port) && port.Trim().StartsWith("/dev/", StringComparison.Ordinal);
    }
}
