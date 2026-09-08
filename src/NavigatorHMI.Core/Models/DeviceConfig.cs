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
        /// ModbusRTU: {"port":"/dev/ttyS1","baud":9600,"slaveId":1}
        /// ModbusTCP: {"ip":"192.168.1.50","port":502,"slaveId":1}
        /// MQTT (Y-3a 2026-09-10 全字段): {"broker":"192.168.1.1","port":1883,"version":0,"clientId":"hmi-01",
        ///   "username":"","password":"<加密包>","keepAlive":60,"enableTls":false,"statusTag":""}
        ///   —— broker 不含协议前缀；password 存 DPAPI 加密包（绝不明文进 .navihmi）；
        ///   version 0=3.1.1/1=5.0；缺省字段取默认（port 1883/keepAlive 60）
        /// </summary>
        [ProtoMember(3)]
        public string ConnectionInfo { get; set; } = "";
    }
}
