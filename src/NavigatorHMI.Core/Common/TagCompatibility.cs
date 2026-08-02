namespace NavigatorHMI.Common
{
    /// <summary>控件绑定的变量类型要求（显式三态，避免 bool? 语义模糊）。</summary>
    public enum TagRequirement
    {
        /// <summary>不限类型（文本/交互类控件）。</summary>
        Any,
        /// <summary>数字类型（NumericDisplay/IOField/ProgressBar）。</summary>
        Numeric,
        /// <summary>字符串路径（Image/Frame 背景图）。</summary>
        StringPath
    }

    /// <summary>
    /// 变量类型与控件绑定兼容性规则（GUI 下拉过滤 + CommandLayer 校验共用单一来源）。
    /// </summary>
    public static class TagCompatibility
    {
        /// <summary>数字显示控件（NumericDisplay/IOField/ProgressBar）允许的变量类型。</summary>
        public static bool IsNumericCompatible(TagDataType t)
            => t is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32 or TagDataType.FLOAT or TagDataType.BOOL;

        /// <summary>图片路径控件（Image/Frame）允许的变量类型（字符串路径）。</summary>
        public static bool IsStringPathCompatible(TagDataType t) => t == TagDataType.STRING;

        /// <summary>控件类型 → 变量类型要求。新增控件类型显式声明（默认 Any 属已知放宽，非静默漏配）。</summary>
        public static TagRequirement GetRequirement(Widget widget) => widget switch
        {
            NumericDisplayWidget or IOFieldWidget or ProgressBarWidget => TagRequirement.Numeric,
            ImageWidget or FrameWidget => TagRequirement.StringPath,
            _ => TagRequirement.Any,
        };

        /// <summary>校验控件与变量类型兼容（不兼容返回失败信息，null = 兼容）。</summary>
        public static string? Check(Widget widget, Tag tag)
        {
            var req = GetRequirement(widget);
            if (req == TagRequirement.Any) return null;
            bool ok = req == TagRequirement.Numeric ? IsNumericCompatible(tag.DataType) : IsStringPathCompatible(tag.DataType);
            return ok ? null : (req == TagRequirement.Numeric
                ? $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定数值控件（支持 BOOL/INT16/UINT16/INT32/FLOAT）"
                : $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定图片控件（支持 STRING）");
        }
    }
}
