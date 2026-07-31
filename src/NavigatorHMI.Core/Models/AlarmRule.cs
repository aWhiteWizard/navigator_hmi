using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>报警类型</summary>
    public enum AlarmType
    {
        /// <summary>高报：值超过阈值时触发</summary>
        High,
        /// <summary>低报：值低于阈值时触发</summary>
        Low,
        /// <summary>变化率报警：值变化速率超过阈值时触发</summary>
        RateChange,
        /// <summary>偏差报警：值偏离设定值超过阈值时触发</summary>
        Deviation
    }

    /// <summary>报警严重等级（从高到低）</summary>
    public enum Severity
    {
        /// <summary>紧急：需要立即处理，可能造成设备损坏或安全事故</summary>
        Emergency,
        /// <summary>重要：需要尽快处理，影响生产质量</summary>
        Important,
        /// <summary>警告：需要关注，暂不影响生产</summary>
        Warning,
        /// <summary>提示：信息性通知</summary>
        Info
    }

    /// <summary>
    /// 报警规则定义。编译到 .navihmi 下发到设备端，由 AlarmEngine 按规则检测、触发、通知。
    /// </summary>
    [ProtoContract]
    public class AlarmRule
    {
        /// <summary>报警名称（工程内唯一，如 "温度过高"）</summary>
        [ProtoMember(1)]
        public string Name { get; set; } = "";

        /// <summary>关联变量名（必须存在于 HMIProject.Tags 中）</summary>
        [ProtoMember(2)]
        public string TagName { get; set; } = "";

        /// <summary>报警类型</summary>
        [ProtoMember(3)]
        public AlarmType Type { get; set; }

        /// <summary>触发阈值（High/Low 类型的比较基准值）</summary>
        [ProtoMember(4)]
        public double Threshold { get; set; }

        /// <summary>回差值（避免阈值附近抖动触发/恢复循环）</summary>
        [ProtoMember(5)]
        public double Deadband { get; set; }

        /// <summary>防抖动延迟时间（毫秒）。条件持续满足超过此时间才触发报警。</summary>
        [ProtoMember(6)]
        public int DelayMs { get; set; }

        /// <summary>严重等级</summary>
        [ProtoMember(7)]
        public Severity Level { get; set; }

        /// <summary>报警描述文本（设备端显示用）</summary>
        [ProtoMember(8)]
        public string Message { get; set; } = "";
    }
}
