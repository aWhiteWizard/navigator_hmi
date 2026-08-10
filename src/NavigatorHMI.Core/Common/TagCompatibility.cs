namespace NavigatorHMI.Common
{
    /// <summary>控件绑定的变量类型要求（显式四态）。</summary>
    public enum TagRequirement
    {
        /// <summary>禁止绑定变量（静态文本/交互类控件）。</summary>
        None,
        /// <summary>不限类型（输入输出/通用控件）。</summary>
        Any,
        /// <summary>数字类型（数值/列表索引控件）。</summary>
        Numeric,
        /// <summary>字符串类型（文本控件显示值）。</summary>
        StringPath,
        /// <summary>布尔类型（开关/复选框控件）。</summary>
        Bool,
        /// <summary>日期时间类型（DateTime 控件）。</summary>
        DateTime,
        /// <summary>经纬度类型（线/多边形/点/圆圆心等图形类控件）。</summary>
        Geo
    }

    /// <summary>
    /// 变量类型与控件绑定兼容性规则（GUI 下拉过滤 + CommandLayer 校验共用单一来源）。
    /// </summary>
    public static class TagCompatibility
    {
        /// <summary>数值/索引绑定控件允许的变量类型（BOOL/INT16/UINT16/INT32/FLOAT）。</summary>
        public static bool IsNumericCompatible(TagDataType t)
            => t is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32 or TagDataType.FLOAT or TagDataType.BOOL;

        /// <summary>字符串绑定控件允许的变量类型（STRING：文本内容/图片路径）。</summary>
        public static bool IsStringPathCompatible(TagDataType t) => t == TagDataType.STRING;

        /// <summary>开关/复选框控件允许的变量类型（仅 BOOL）。</summary>
        public static bool IsBoolCompatible(TagDataType t) => t == TagDataType.BOOL;

        /// <summary>DateTime 控件允许的变量类型（仅 DATETIME）。</summary>
        public static bool IsDateTimeCompatible(TagDataType t) => t == TagDataType.DATETIME;

        /// <summary>图形类控件（线/多边形/点/圆圆心）允许的变量类型（仅 GPS 经纬度）。</summary>
        public static bool IsGeoCompatible(TagDataType t) => t == TagDataType.GPS;

        /// <summary>控件类型 → 变量类型要求。新增控件类型显式声明（默认 Any 属已知放宽，非静默漏配）。</summary>
        public static TagRequirement GetRequirement(Widget widget) => widget switch
        {
            // 标签控件：仅静态文本显示，禁止绑定变量
            LabelWidget => TagRequirement.None,
            // 文本控件：绑 STRING 变量显示值
            TextWidget => TagRequirement.StringPath,
            // 输入输出域：数字或字符串均可显示
            IOFieldWidget => TagRequirement.Any,
            // 开关/复选框（B3/B4）：仅绑 BOOL 变量（运行时双向同步）
            SwitchWidget or CheckBoxWidget => TagRequirement.Bool,
            // 数值显示/进度条：数字
            NumericDisplayWidget or ProgressBarWidget => TagRequirement.Numeric,
            // 列表消费控件（Image/Frame/TextList）：绑定数值变量 = 显示索引（0=第1项）
            ImageWidget or FrameWidget or TextListWidget => TagRequirement.Numeric,
            // 日期时间控件：仅绑 DATETIME 变量
            DateTimeWidget => TagRequirement.DateTime,
            // 图形类控件（世界地图批 2）：线/多边形/点/圆圆心——仅绑 GPS 经纬度变量（动态移动），其余控件禁止
            LineWidget or CircleWidget or PolygonWidget or PointWidget => TagRequirement.Geo,
            _ => TagRequirement.Any,
        };

        /// <summary>校验控件与变量类型兼容（不兼容返回失败信息，null = 兼容）。</summary>
        public static string? Check(Widget widget, Tag tag)
        {
            var req = GetRequirement(widget);
            // 需求 C：GPS 变量仅图形类控件（线/多边形/点/圆）可绑，其余控件一律拒绝（含默认 Any 分支）
            if (tag.DataType == TagDataType.GPS && req != TagRequirement.Geo)
                return $"变量 \"{tag.Name}\" 类型 GPS 不能绑定 {widget.GetType().Name}（仅线/多边形/点/圆可绑）";
            return req switch
            {
                TagRequirement.None => $"该控件（{widget.GetType().Name}）不支持绑定变量",
                TagRequirement.Numeric when !IsNumericCompatible(tag.DataType) =>
                    $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定该控件（索引需 BOOL/INT16/UINT16/INT32/FLOAT）",
                TagRequirement.StringPath when !IsStringPathCompatible(tag.DataType) =>
                    $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定文本控件（支持 STRING）",
                TagRequirement.DateTime when !IsDateTimeCompatible(tag.DataType) =>
                    $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定日期时间控件（仅支持 DATETIME）",
                TagRequirement.Bool when !IsBoolCompatible(tag.DataType) =>
                    $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定开关/复选框（需 BOOL）",
                TagRequirement.Geo when !IsGeoCompatible(tag.DataType) =>
                    $"变量 \"{tag.Name}\" 类型 {tag.DataType} 不能绑定图形控件（仅支持 GPS 经纬度）",
                _ => null,
            };
        }
    }
}
