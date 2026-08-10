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
        Polygon = 17   // 多边形
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
        [ProtoMember(13)] public bool ShowNavigationBar { get; set; } = true;
        [ProtoMember(14)] public NavPosition NavigationPosition { get; set; }
        [ProtoMember(15)] public string StartScreen { get; set; } = "";
        [ProtoMember(16)] public List<ListDef> Lists { get; set; } = new();
        [ProtoMember(17)] public int FormatVersion { get; set; } = 2;   // 契约版本（FW 端解析后校验，防旧产物静默错读）；2 = 世界地图重构（P1：PointWidget/geo_* 移除，新增 work_range_points/events/view_locked）
        // W1 用户系统（18-20，与 proto users=18/groups=19/security=20 对齐）
        [ProtoMember(18)] public List<UserAccount> Users { get; set; } = new();
        [ProtoMember(19)] public List<UserGroup> Groups { get; set; } = new();
        [ProtoMember(20)] public SecuritySettings Security { get; set; } = new();
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
        [ProtoMember(7)] public bool ShowInNav { get; set; } = true;
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
        [ProtoMember(41)] public bool ShowTitleBar { get; set; } = true;
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
        // ── 多边形（64）──
        [ProtoMember(64)] public List<PointD> Points { get; set; } = new();       // Polygon 顶点画面坐标
    }
}
