using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>变量数据类型，与 Modbus 寄存器类型对应</summary>
    public enum TagDataType
    {
        /// <summary>布尔量（线圈/离散输入）</summary>
        BOOL,
        /// <summary>有符号 16 位整数</summary>
        INT16,
        /// <summary>无符号 16 位整数</summary>
        UINT16,
        /// <summary>有符号 32 位整数</summary>
        INT32,
        /// <summary>32 位浮点数</summary>
        FLOAT,
        /// <summary>字符串（MQTT 消息体）</summary>
        STRING,
        /// <summary>日期时间（显示/编辑控件用；格式 yyyy-MM-dd HH:mm:ss）</summary>
        DATETIME
    }

    /// <summary>
    /// 工程变量定义。变量是控件与数据采集之间的桥梁：
    /// 控件通过 BoundTag 绑定变量名来显示实时值，数据采集引擎按变量配置去 Modbus/MQTT 拉取数据。
    /// </summary>
    [ProtoContract]
    public class Tag
    {
        /// <summary>变量名（工程内唯一，如 "Tank1_Temp"）</summary>
        [ProtoMember(1)]
        public string Name { get; set; } = "";

        /// <summary>数据类型</summary>
        [ProtoMember(2)]
        public TagDataType DataType { get; set; }

        /// <summary>工程单位（如 "°C" / "rpm" / "m"），供 IOField/NumericDisplay 等数值控件显示单位</summary>
        [ProtoMember(3)]
        public string Unit { get; set; } = "";

        /// <summary>数据来源地址，URI 格式：
        /// Modbus: "modbus://{从站地址}/{寄存器类型}{地址}"，如 "modbus://1/40001"
        /// MQTT:   "mqtt://{主题路径}"，如 "mqtt://robot/pose/x"</summary>
        [ProtoMember(4)]
        public string Source { get; set; } = "";

        /// <summary>采集周期（毫秒），默认 100ms</summary>
        [ProtoMember(5)]
        public int ScanIntervalMs { get; set; } = 100;

        /// <summary>变化死区。值变化小于此阈值时不触发画面刷新，避免高频抖动。</summary>
        [ProtoMember(6)]
        public double Deadband { get; set; }

        /// <summary>变量描述（给用户看的注释，不参与运行时逻辑）</summary>
        [ProtoMember(7)]
        public string Description { get; set; } = "";

        /// <summary>基准值（设计态预览值，字符串；绑定该变量的控件在设计态显示此值，运行时由实时值覆盖）。
        /// 数字变量存数字文本（如 "25.5"），字符串变量存原值（如图片路径/文本）。</summary>
        [ProtoMember(8)]
        public string BaseValue { get; set; } = "";
    }
}
