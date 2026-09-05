using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>编译产物控件类型判别（对应 navihmi.proto WidgetType）。</summary>
    public enum NavihmiWidgetType
    {
        Button = 0, Text = 1, Label = 2, Rectangle = 3, Image = 4, NumericDisplay = 5,
        Switch = 6, Line = 7, Circle = 8, Ellipse = 9, IOField = 10,
        CheckBox = 11, TextList = 12, Frame = 13, ProgressBar = 14, DateTime = 15,
        Window = 16,   // W1：窗口控件（UserView/AlarmView/RobotList）
        Polygon = 17,   // 多边形
        TrendView = 18,   // P-4：趋势图控件（2026-09-02）
        HistoryView = 19   // P-5：历史记录控件（2026-09-02，枚举一次到位）
    }

    /// <summary>
    /// 编译产物工程 DTO（.navihmi 契约，对应 navihmi.proto HMIProject）。
    /// 字段号与 proto 严格对齐；Tag/AlarmRule/DeviceConfig/WorldMapConfig/ListDef 字段号与 proto 一致，直接复用原模型。
    /// Screen/Widget 因 PC 端用 protobuf-net 继承（标准 proto 无法表达），编译时扁平化为本 DTO。
    /// </summary>
    [ProtoContract]
    public class NavihmiProject
    {
        [ProtoMember(1)] public string Name { get; set; } = "";
        [ProtoMember(2)] public long CreateTime { get; set; }
        [ProtoMember(3)] public long LastModifiedTime { get; set; }
        [ProtoMember(4)] public string Version { get; set; } = "";
        [ProtoMember(5)] public List<NavihmiScreen> Screens { get; set; } = new();
        [ProtoMember(6)] public string ProjectFilePath { get; set; } = "";
        [ProtoMember(7)] public int DeviceWidth { get; set; }
        [ProtoMember(8)] public int DeviceHeight { get; set; }
        [ProtoMember(9)] public List<Tag> Tags { get; set; } = new();
        [ProtoMember(10)] public List<AlarmRule> Alarms { get; set; } = new();
        [ProtoMember(11)] public List<DeviceConfig> Devices { get; set; } = new();
        [ProtoMember(12)] public WorldMapConfig? WorldMap { get; set; }
        [ProtoMember(13, IsRequired = true)] public bool ShowNavigationBar { get; set; } = true;
        [ProtoMember(14)] public NavPosition NavigationPosition { get; set; }
        [ProtoMember(15)] public string StartScreen { get; set; } = "";
        [ProtoMember(16)] public List<ListDef> Lists { get; set; } = new();
        [ProtoMember(17)] public int FormatVersion { get; set; } = 1;   // 契约版本（FW 端解析后校验，防旧产物静默错读）；1 = 世界地图重构（P1：PointWidget/geo_* 移除，新增 work_range_points/events/view_locked）
        // 注：世界地图重构时曾误升 2（P11 改回 1）——契约仍在演进且 FW 端尚未按 2 适配，版本号以本端为准；FW 适配时如需区分再升
        // W1 用户系统（18-20，与 proto users=18/groups=19/security=20 对齐）
        [ProtoMember(18)] public List<UserAccount> Users { get; set; } = new();
        [ProtoMember(19)] public List<UserGroup> Groups { get; set; } = new();
        [ProtoMember(20)] public SecuritySettings Security { get; set; } = new();
        [ProtoMember(21)] public bool EnableVnc { get; set; }
        [ProtoMember(22)] public string DeviceModel { get; set; } = "";   // 工程目标设备型号（device-profile 写入；设备身份由设备自身配置决定，与工程无关——2026-08-30 用户 Check 指正）
        [ProtoMember(23, IsRequired = true)] public bool IncludeWorldMapOnDevice { get; set; } = true;   // P-3：设备端是否显示世界地图画面（编译产物过滤 WorldMap Screen；默认 true 兼容旧工程）
    }

    /// <summary>编译产物画面（navihmi.proto Screen）。</summary>
    [ProtoContract]
    public class NavihmiScreen
    {
        [ProtoMember(1)] public string Name { get; set; } = "";
        [ProtoMember(2)] public double Width { get; set; }
        [ProtoMember(3)] public double Height { get; set; }
        [ProtoMember(4)] public ScreenType Type { get; set; }
        [ProtoMember(5)] public List<NavihmiWidget> Widgets { get; set; } = new();
        [ProtoMember(6)] public bool IsGlobal { get; set; }
        [ProtoMember(7, IsRequired = true)] public bool ShowInNav { get; set; } = true;
        [ProtoMember(8)] public int NavOrder { get; set; }
    }

    /// <summary>
    /// 编译产物控件（navihmi.proto Widget，扁平结构）。
    /// 基类字段 1-7 + 类型判别 8 + 各类型字段 9-36（未用字段保持默认值）。
    /// </summary>
    [ProtoContract]
    public class NavihmiWidget
    {
        // ── 基类（1-7） ──
        [ProtoMember(1)] public double X { get; set; }
        [ProtoMember(2)] public double Y { get; set; }
        [ProtoMember(3)] public double Width { get; set; }
        [ProtoMember(4)] public double Height { get; set; }
        [ProtoMember(5)] public string ObjectName { get; set; } = "";
        [ProtoMember(6)] public string BoundTag { get; set; } = "";
        [ProtoMember(7)] public List<WidgetEvent> Events { get; set; } = new();
        // ── 类型判别（8） ──
        [ProtoMember(8)] public NavihmiWidgetType Type { get; set; }
        // ── 各类型字段（9-36） ──
        [ProtoMember(9)] public string Text { get; set; } = "";
        [ProtoMember(10)] public string Content { get; set; } = "";
        [ProtoMember(11)] public string HAlign { get; set; } = "";
        [ProtoMember(12)] public string FontFamily { get; set; } = "";
        [ProtoMember(13)] public double FontSize { get; set; }
        [ProtoMember(14)] public string FontWeight { get; set; } = "";
        [ProtoMember(15)] public string FontStyle { get; set; } = "";
        [ProtoMember(16)] public string TextDecoration { get; set; } = "";
        [ProtoMember(17)] public string TextColor { get; set; } = "";
        [ProtoMember(18)] public string FillColor { get; set; } = "";
        [ProtoMember(19)] public string StrokeColor { get; set; } = "";
        [ProtoMember(20)] public double StrokeThickness { get; set; }
        [ProtoMember(21)] public string ImagePath { get; set; } = "";
        [ProtoMember(22)] public string StretchMode { get; set; } = "";
        [ProtoMember(23)] public string ListRef { get; set; } = "";
        [ProtoMember(24)] public int DefaultIndex { get; set; }
        [ProtoMember(25)] public double Value { get; set; }
        [ProtoMember(26)] public double Min { get; set; }
        [ProtoMember(27)] public double Max { get; set; }
        [ProtoMember(28)] public string FillStyle { get; set; } = "";
        [ProtoMember(29)] public bool IsOn { get; set; }
        [ProtoMember(30)] public string OnText { get; set; } = "";
        [ProtoMember(31)] public string OffText { get; set; } = "";
        [ProtoMember(32)] public bool IsChecked { get; set; }
        [ProtoMember(33)] public bool IsReadOnly { get; set; }
        [ProtoMember(34)] public double X2 { get; set; }
        [ProtoMember(35)] public double Y2 { get; set; }
        [ProtoMember(36)] public string Title { get; set; } = "";
        [ProtoMember(37)] public string DtText { get; set; } = "";
        [ProtoMember(38)] public string DtFormat { get; set; } = "";
        // ── W1 窗口控件（39+，W5 细化 RobotSlots） ──
        [ProtoMember(39)] public int WindowType { get; set; }
        [ProtoMember(40)] public string WinTitle { get; set; } = "";
        [ProtoMember(41, IsRequired = true)] public bool ShowTitleBar { get; set; } = true;
        [ProtoMember(42)] public bool ShowHistory { get; set; }
        [ProtoMember(43)] public string SelectedTag { get; set; } = "";
        [ProtoMember(44)] public double CardWidth { get; set; }
        [ProtoMember(45)] public double CardHeight { get; set; }
        // ── W5 窗口控件扩展（46-53，与 proto 对齐） ──
        [ProtoMember(46)] public bool ShowUserName { get; set; }
        [ProtoMember(47)] public bool ShowRole { get; set; }
        [ProtoMember(48)] public bool ShowMode { get; set; }
        [ProtoMember(49)] public bool CardShowNumber { get; set; }
        [ProtoMember(50)] public bool CardShowStatus { get; set; }
        [ProtoMember(51)] public bool CardShowLocation { get; set; }
        [ProtoMember(52)] public string BoundDevice { get; set; } = "";
        [ProtoMember(53)] public List<RobotSlotBinding> RobotSlots { get; set; } = new();
        // ── P-5 AlarmView 显示模式（54，窗口区空洞；2026-09-02）──
        [ProtoMember(54)] public int DisplayMode { get; set; }                    // WindowWidget DisplayMode：0=当前报警 1=报警缓冲区
        // ── 多边形（64）──
        [ProtoMember(64)] public List<PointD> Points { get; set; } = new();       // Polygon 顶点画面坐标
        // ── P-4 趋势图（65-72，与 proto 对齐；2026-09-02）──
        [ProtoMember(65)] public int TrendMode { get; set; }                     // TrendView：0=时间-数据 1=变量A-B（零值=0）
        [ProtoMember(66)] public string TrendTagA { get; set; } = "";            // 趋势变量 A
        [ProtoMember(67)] public string TrendTagB { get; set; } = "";            // 趋势变量 B（变量A-B模式）
        [ProtoMember(68)] public int SampleIntervalMs { get; set; }              // 采样间隔 ms（默认 1000——零值由 FW 兜底默认）
        [ProtoMember(69)] public int TimeWindowSeconds { get; set; }             // 时间窗 s（默认 60）
        [ProtoMember(70)] public string LineColor { get; set; } = "";            // 曲线颜色
        [ProtoMember(71)] public double LineWidth { get; set; }                  // 曲线粗细
        [ProtoMember(72)] public int RefreshRateMs { get; set; }                 // 刷新率 ms（默认 500）
        // ── P-5 历史记录（73-74；2026-09-02）──
        [ProtoMember(73)] public List<string> HistoryTags { get; set; } = new();  // HistoryView 变量列表（多变量）
        [ProtoMember(74)] public string HistoryDbPath { get; set; } = "";         // HistoryView 数据库路径（空=设备端默认 navihmi_history.db）
        // ── P-6 Frame 视频（75-76；2026-09-02）──
        [ProtoMember(75, IsRequired = true)] public bool ShowVideo { get; set; }  // Frame 视频模式（checkbox；false=普通 Frame）
        [ProtoMember(76)] public string VideoSource { get; set; } = "";           // Frame 视频源（本地路径打包/RTSP URL 不入包）
        // ── Q-6 HistoryView 列显示名（77；2026-09-04 用户 Check：变量配置表格化 tag+title）──
        [ProtoMember(77)] public List<string> HistoryTagTitles { get; set; } = new();   // 平行 HistoryTags[i] 的列显示名（空/缺省=显示变量名——老工程兼容）
        // ── R-4 Frame 视频播放控制（78；2026-09-05 用户 Check）──
        [ProtoMember(78)] public string PlayTag { get; set; } = "";   // Frame 播放控制布尔变量（true=播放 false=暂停；空=未绑定）
        // ── S-4/S-5 Frame 视频源列表（79-80；2026-09-05 用户拍板）──
        [ProtoMember(79)] public string VideoListRef { get; set; } = "";    // Frame 视频源列表名（Video 型列表引用；空=未选走单源）
        [ProtoMember(80)] public string VideoIndexTag { get; set; } = "";   // Frame 视频源选择变量（整型非负索引——变量值取列表项切源）
    }
}
