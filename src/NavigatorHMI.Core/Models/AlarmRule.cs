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

    /// <summary>报警触发模式（W1 补充：BOOL 变量位沿触发；模拟量仍用 High/Low 阈值）。</summary>
    public enum AlarmTriggerMode
    {
        /// <summary>阈值比较（默认：High/Low/Deviation/RateChange 按 Threshold）</summary>
        Threshold,
        /// <summary>BOOL 上升沿触发（false→true）</summary>
        OnRising,
        /// <summary>BOOL 下降沿触发（true→false）</summary>
        OnFalling,
        /// <summary>BOOL 值变化即触发</summary>
        OnChange
    }

    /// <summary>报警类别（W1 补充：与级别分离——类别管确认策略，级别管显示语义）。</summary>
    public enum AlarmCategory
    {
        /// <summary>系统报警（设备系统，默认按系统策略）</summary>
        System,
        /// <summary>用户自定义报警（组态配置）</summary>
        User,
        /// <summary>错误类（异常/故障语义）</summary>
        Error
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

        /// <summary>W1 触发模式（BOOL 位沿/阈值比较）。</summary>
        [ProtoMember(9)]
        public AlarmTriggerMode TriggerMode { get; set; } = AlarmTriggerMode.Threshold;

        /// <summary>W1 报警类别（System/User/Error——管确认策略）。</summary>
        [ProtoMember(10)]
        public AlarmCategory Category { get; set; } = AlarmCategory.User;

        /// <summary>W1 排序优先级（数值越大越靠前；同级按时间倒序）。</summary>
        [ProtoMember(11)]
        public int Priority { get; set; }

        /// <summary>W1 需要确认（false=自动恢复无需确认）。默认 true——IsRequired 强制写（protobuf-net 省略 false 丢值）。</summary>
        [ProtoMember(12, IsRequired = true)]   // 默认 true——IsRequired 强制写（protobuf-net 省略 false 会丢值）
        public bool AckRequired { get; set; } = true;

        /// <summary>W1 确认组（联动确认：同组报警一起确认；空=单独确认）。</summary>
        [ProtoMember(13)]
        public string AckGroup { get; set; } = "";

        /// <summary>W1 颜色图标覆盖（空=跟随级别系统语义色；十六进制如 "#FF0000"）。</summary>
        [ProtoMember(14)]
        public string ColorOverride { get; set; } = "";
    }
}
