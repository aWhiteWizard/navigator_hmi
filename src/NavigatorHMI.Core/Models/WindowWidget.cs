using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>窗口控件类型（DESIGN-WINDOWS.md §2 定稿：三预置窗口）。</summary>
    public enum WindowType
    {
        /// <summary>用户视图：头像+用户名/角色/运行模式+登录/注销+⚙ 管理</summary>
        UserView,
        /// <summary>报警视图：活动报警列表（级别色标/确认/历史开关）</summary>
        AlarmView,
        /// <summary>机器人列表：卡片网格（绑定变量驱动/详情/操作）</summary>
        RobotList
    }

    /// <summary>RobotList 机器人绑定组（DESIGN-WINDOWS.md §5：每台机器人五变量；INPC——绑定表编辑标脏）。</summary>
    [ProtoContract]
    public class RobotSlotBinding : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        private string _idTag = "";
        /// <summary>编号变量（STRING：机器人编号，如 R01）</summary>
        [ProtoMember(1)]
        public string IdTag { get => _idTag; set { if (_idTag != value) { _idTag = value; Notify(nameof(IdTag)); } } }

        private string _statusTag = "";
        /// <summary>状态变量（STRING：运行/空闲/故障/离线）</summary>
        [ProtoMember(2)]
        public string StatusTag { get => _statusTag; set { if (_statusTag != value) { _statusTag = value; Notify(nameof(StatusTag)); } } }

        private string _locationTag = "";
        /// <summary>位置变量（STRING：如 A区-3号位）</summary>
        [ProtoMember(3)]
        public string LocationTag { get => _locationTag; set { if (_locationTag != value) { _locationTag = value; Notify(nameof(LocationTag)); } } }

        private string _detailTag = "";
        /// <summary>详细信息 JSON 变量（STRING：配置/当前任务等扩展字段）</summary>
        [ProtoMember(4)]
        public string DetailTag { get => _detailTag; set { if (_detailTag != value) { _detailTag = value; Notify(nameof(DetailTag)); } } }

        private string _operTag = "";
        /// <summary>操作变量（STRING：写入操作指令——下线/上线/删除）</summary>
        [ProtoMember(5)]
        public string OperTag { get => _operTag; set { if (_operTag != value) { _operTag = value; Notify(nameof(OperTag)); } } }
    }

    /// <summary>W1 窗口控件（DESIGN-WINDOWS.md §2 定稿）：统一模型，类型枚举区分三窗口。
    /// 属性 setter 全部带 OnPropertyChanged（W4 审查：画布标脏/预览刷新依赖 INPC 链——自动属性会导致修改不标脏/预览不刷新）。</summary>
    [ProtoContract]
    public class WindowWidget : Widget
    {
        private WindowType _type = WindowType.UserView;
        /// <summary>窗口类型（UserView/AlarmView/RobotList）。</summary>
        [ProtoMember(1)]
        public WindowType Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(); } }
        }

        private string _title = "用户";
        /// <summary>标题文本。</summary>
        [ProtoMember(2)]
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        private bool _showTitleBar = true;
        /// <summary>是否显示标题栏。</summary>
        [ProtoMember(3, IsRequired = true)]   // 默认 true——IsRequired 强制写（protobuf-net 省略 false 会丢值）
        public bool ShowTitleBar
        {
            get => _showTitleBar;
            set { if (_showTitleBar != value) { _showTitleBar = value; OnPropertyChanged(); } }
        }

        private string _fillColor = "#EEEEEE";
        /// <summary>背景色。</summary>
        [ProtoMember(4)]
        public string FillColor
        {
            get => _fillColor;
            set { if (_fillColor != value) { _fillColor = value; OnPropertyChanged(); } }
        }

        private string _borderColor = "#888888";
        /// <summary>边框色。</summary>
        [ProtoMember(5)]
        public string BorderColor
        {
            get => _borderColor;
            set { if (_borderColor != value) { _borderColor = value; OnPropertyChanged(); } }
        }

        // ── UserView 显示开关 ──
        private bool _showUserName = true;
        [ProtoMember(6, IsRequired = true)] public bool ShowUserName { get => _showUserName; set { if (_showUserName != value) { _showUserName = value; OnPropertyChanged(); } } }
        private bool _showRole = true;
        [ProtoMember(7, IsRequired = true)] public bool ShowRole { get => _showRole; set { if (_showRole != value) { _showRole = value; OnPropertyChanged(); } } }
        private bool _showMode = true;
        [ProtoMember(8, IsRequired = true)] public bool ShowMode { get => _showMode; set { if (_showMode != value) { _showMode = value; OnPropertyChanged(); } } }

        private bool _showHistory;
        /// <summary>AlarmView 是否含历史报警（false=仅活动）。</summary>
        [ProtoMember(9)]
        public bool ShowHistory
        {
            get => _showHistory;
            set { if (_showHistory != value) { _showHistory = value; OnPropertyChanged(); } }
        }

        // ── RobotList 卡片 ──
        private double _cardWidth = 120;
        [ProtoMember(10)] public double CardWidth { get => _cardWidth; set { if (_cardWidth != value) { _cardWidth = value; OnPropertyChanged(); } } }
        private double _cardHeight = 110;
        [ProtoMember(11)] public double CardHeight { get => _cardHeight; set { if (_cardHeight != value) { _cardHeight = value; OnPropertyChanged(); } } }
        private bool _cardShowNumber = true;
        [ProtoMember(12, IsRequired = true)] public bool CardShowNumber { get => _cardShowNumber; set { if (_cardShowNumber != value) { _cardShowNumber = value; OnPropertyChanged(); } } }
        private bool _cardShowStatus = true;
        [ProtoMember(13, IsRequired = true)] public bool CardShowStatus { get => _cardShowStatus; set { if (_cardShowStatus != value) { _cardShowStatus = value; OnPropertyChanged(); } } }
        private bool _cardShowLocation = true;
        [ProtoMember(14, IsRequired = true)] public bool CardShowLocation { get => _cardShowLocation; set { if (_cardShowLocation != value) { _cardShowLocation = value; OnPropertyChanged(); } } }

        private string _selectedTag = "";
        /// <summary>RobotList 选中编号写入变量（STRING，空=不写）。</summary>
        [ProtoMember(15)]
        public string SelectedTag
        {
            get => _selectedTag;
            set { if (_selectedTag != value) { _selectedTag = value; OnPropertyChanged(); } }
        }

        /// <summary>RobotList 机器人绑定组（每台五变量）。</summary>
        [ProtoMember(16)]
        private List<RobotSlotBinding> _robotSlots = new();
        public List<RobotSlotBinding> RobotSlots { get => _robotSlots; set { if (_robotSlots != value) { _robotSlots = value; OnPropertyChanged(); } } }

        private string _boundDevice = "";
        /// <summary>RobotList 绑定的通信设备（控制器）——变量下拉仅该设备变量（空=仅内部变量）。</summary>
        [ProtoMember(17)]
        public string BoundDevice
        {
            get => _boundDevice;
            set { if (_boundDevice != value) { _boundDevice = value; OnPropertyChanged(); } }
        }
    }
}