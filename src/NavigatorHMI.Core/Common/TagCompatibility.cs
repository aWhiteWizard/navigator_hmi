namespace NavigatorHMI.Common
{
    /// <summary>控件绑定的变量类型要求（显式三态，避免 bool? 语义模糊）。</summary>
    public enum TagRequirement
    {
        /// <summary>不限类型（文本/交互类控件）。</summary>
        Any,
        /// <summary>数字类型（NumericDisplay/IOField/ProgressBar + 列表索引控件 Image/Frame/TextList）。</summary>
        Numeric
    }

    /// <summary>
    /// 变量类型与控件绑定兼容性规则（GUI 下拉过滤 + CommandLayer 校验共用单一来源）。
    /// </summary>
    public static class TagCompatibility
    {
        /// <summary>数值/索引绑定控件允许的变量类型（BOOL/INT16/UINT16/INT32/FLOAT）。</summary>
        public static bool IsNumericCompatible(TagDataType t)
            => t is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32 or TagDataType.FLOAT or TagDataType.BOOL;

        /// <summary>控件类型 → 变量类型要求。新增控件类型显式声明（默认 Any 属已知放宽，非静默漏配）。</summary>
        public static TagRequirement GetRequirement(Widget widget) => widget switch
        {
            NumericDisplayWidget or IOFieldWidget or ProgressBarWidget => TagRequirement.Numeric,
            // 列表消费控件（Image/Frame/TextList）：绑定数值变量 = 显示索引（0=第1项）
            ImageWidget or FrameWidget or TextListWidget => TagRequirement.Numeric,
            _ => TagRequirement.Any,
        };

        /// <summary>校验控件与变量类型兼容（不兼容返回失败信息，null = 兼容）。</summary>
        public static string? Check(Widget widget, Tag tag)
        {
            if (GetRequirement(widget) == TagRequirement.Numeric && !IsNumericCompatible(tag.DataType))
                return $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定该控件（索引需 BOOL/INT16/UINT16/INT32/FLOAT）";
            return null;
        }
    }
}
