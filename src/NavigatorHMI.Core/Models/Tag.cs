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
        DATETIME,
        /// <summary>经纬度（GPS/定位；基准值 DMS 格式 "(E0°0'0\", N0°0'0\")"，见 <see cref="GeoPoint"/>；仅作业点/作业范围点可绑定）</summary>
        GPS
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

        /// <summary>W1 所属设备名（通信设备/控制器——RobotList BoundDevice 两态过滤用；空=内部变量）。
        /// **AB-4（2026-09-11）第二消费方**：设备端采集路由——FW 按 tag.device_name（proto 9）定位
        /// 连接参数与传输协议（Modbus RTU/TCP 分流 + 一协议多设备/多从站实例），空则回落 TCP 默认连接。
        /// 改动本字段语义时须同时核对两处消费方（PC RobotList 过滤 / FW acquisition）。</summary>
        [ProtoMember(9)]
        public string DeviceName { get; set; } = "";

        /// <summary>Y-5 变量分组（2026-09-10 ④通信批）：空 = 未分组；变量管理器按此分组收起展开（UI 会话状态不落盘）。
        /// 运行时仅 PC 组态语义，FW 透传即可（不做分组逻辑）。</summary>
        [ProtoMember(10)]
        public string Group { get; set; } = "";

        /// <summary>分组显示名（Y-5a：空 Group → 「未分组」——UI 分组视图用；非 ProtoMember 不落盘）。</summary>
        public string GroupLabel => string.IsNullOrEmpty(Group) ? UngroupedSentinel : Group;

        /// <summary>「未分组」哨兵（Y-5a reviewer 🟡：保留字——真实组名禁用它，防 list-tags 过滤语义分裂；见 <see cref="GroupLabel"/>）。</summary>
        public const string UngroupedSentinel = "未分组";

        /// <summary>
        /// AH（2026-10-30 用户需求）：**换算系数**——设备端采集到原始值后按
        /// `工程值 = 原始值 × Scale + Offset` 换算再写入（1 位小数的温度传感器常见：原始 199 → 19.9℃ 用 Scale=0.1）。
        /// 默认 1 = 不换算。换算在**采集入口**统一做（Modbus/MQTT 同一条链路），
        /// 因此死区/报警阈值/事件动作/界面显示全部使用换算后的工程值，无需各自处理。
        /// </summary>
        [ProtoMember(11)]
        public double Scale { get; set; } = 1;

        /// <summary>AH：换**偏移**（工程值 = 原始值 × Scale + Offset），默认 0。用于"零漂修正/量程平移"。</summary>
        [ProtoMember(12)]
        public double Offset { get; set; } = 0;

        /// <summary>
        /// AH：对采集原始值做换算（PC 侧设计态预览/FW 设备端采集共用同一公式）。
        /// 结果按 6 位小数四舍五入——避免二进制浮点把 199×0.1 显示成 19.900000000000002。
        /// </summary>
        public double ApplyScale(double raw)
        {
            if (Scale == 1 && Offset == 0) return raw;   // 未配置：完全不变（含类型语义）
            return Math.Round(raw * Scale + Offset, 6);
        }
    }
}
