using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// HMI 工程根对象。包含工程所有配置数据，ProtoBuf 序列化为 .hmiproj 源文件和 .navihmi 编译产物。
    /// </summary>
    /// <remarks>
    /// ProtoMember 编号兼容性约束：
    /// 编号 1-8 来自 v1.0 格式，不可重排或删除。新增字段从 9 开始追加。
    /// 旧 .hmiproj 文件反序列化时，缺失的新字段取默认值。
    /// </remarks>
    [ProtoContract]
    public class HMIProject : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        /// <param name="propertyName">属性名（自动填充）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>E11 运行时当前画面名（[ProtoIgnore] 不落盘：GUI 画面切换时维护；CLI/AI 的 current_screen 命令读取）。</summary>
        [ProtoIgnore]
        public string? CurrentScreenName { get; set; }

        // ═══════════════════════════════════════════
        // v1.0 字段 (ProtoMember 1-8, 不可变)
        // ═══════════════════════════════════════════

        private string _name = "";

        /// <summary>工程名称</summary>
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private DateTime _createTime;

        /// <summary>工程创建时间</summary>
        [ProtoMember(2)]
        public DateTime CreateTime
        {
            get => _createTime;
            set { if (_createTime != value) { _createTime = value; OnPropertyChanged(); } }
        }

        private DateTime _lastModifiedTime;

        /// <summary>最后修改时间</summary>
        [ProtoMember(3)]
        public DateTime LastModifiedTime
        {
            get => _lastModifiedTime;
            set { if (_lastModifiedTime != value) { _lastModifiedTime = value; OnPropertyChanged(); } }
        }

        private string _version = "1.0";

        /// <summary>工程格式版本号</summary>
        [ProtoMember(4)]
        public string Version
        {
            get => _version;
            set { if (_version != value) { _version = value; OnPropertyChanged(); } }
        }

        /// <summary>画面列表（包含 Template/WorldMap/Custom 三种类型）</summary>
        [ProtoMember(5)]
        public List<Screen> Screens { get; set; } = new();

        /// <summary>工程文件路径（ProtoBuf 持久化，绝对或相对路径）</summary>
        private string _projectFilePath = "";

        [ProtoMember(6)]
        public string ProjectFilePath
        {
            get => _projectFilePath;
            set { if (_projectFilePath != value) { _projectFilePath = value; OnPropertyChanged(); } }
        }

        private int _deviceWidth = 800;

        /// <summary>目标设备屏幕宽度（像素）</summary>
        [ProtoMember(7)]
        public int DeviceWidth
        {
            get => _deviceWidth;
            set { if (_deviceWidth != value) { _deviceWidth = value; OnPropertyChanged(); } }
        }

        private int _deviceHeight = 480;

        /// <summary>目标设备屏幕高度（像素）</summary>
        [ProtoMember(8)]
        public int DeviceHeight
        {
            get => _deviceHeight;
            set { if (_deviceHeight != value) { _deviceHeight = value; OnPropertyChanged(); } }
        }

        // ═══════════════════════════════════════════
        // v2.0 新增字段 (ProtoMember 9+)
        // ═══════════════════════════════════════════

        /// <summary>工程中所有变量定义（控件绑定 + 数据采集的数据源）</summary>
        [ProtoMember(9)]
        public List<Tag> Tags { get; set; } = new();

        /// <summary>工程中所有报警规则（设备端 AlarmEngine 按此检测触发）</summary>
        [ProtoMember(10)]
        public List<AlarmRule> Alarms { get; set; } = new();

        /// <summary>工程中所有设备通信配置（ModbusRTU/ModbusTCP/MQTT）</summary>
        [ProtoMember(11)]
        public List<DeviceConfig> Devices { get; set; } = new();

        /// <summary>世界地图配置（仅 ScreenType.WorldMap 画面使用），null 表示未配置</summary>
        [ProtoMember(12)]
        public WorldMapConfig? WorldMap { get; set; }

        /// <summary>设备端是否显示底部/顶部导航栏（默认开启）。默认 true——IsRequired 强制写（protobuf-net 省略 false 丢值）。</summary>
        [ProtoMember(13, IsRequired = true)]
        public bool ShowNavigationBar { get; set; } = true;

        /// <summary>导航栏位置（Top / Bottom）</summary>
        [ProtoMember(14)]
        public NavPosition NavigationPosition { get; set; } = NavPosition.Top;

        /// <summary>设备启动后默认显示的画面名称。空字符串表示使用第一个画面。</summary>
        [ProtoMember(15)]
        public string StartScreen { get; set; } = "";

        /// <summary>工程中所有列表定义（文本列表/图片列表，控件按数值变量索引显示对应项）</summary>
        [ProtoMember(16)]
        public List<ListDef> Lists { get; set; } = new();

        // ═══════════════════════════════════════════
        // W1 新增（ProtoMember 17+）——注意：.hmiproj 源模型编号独立于 .navihmi 编译产物（proto 17=format_version，源模型无）；用户系统源 17-19 ↔ 编译产物 proto 18-20（ProjectGenerator.ToDto 映射）
        // ═══════════════════════════════════════════

        /// <summary>W1 用户账户列表（运行时用户系统；FW 初始管理员兜底）。</summary>
        [ProtoMember(17)]
        public List<UserAccount> Users { get; set; } = new();

        /// <summary>W1 用户组（预置管理员/操作员/访客 + 自定义）。</summary>
        [ProtoMember(18)]
        public List<UserGroup> Groups { get; set; } = new();

        /// <summary>W1 安全设置（密码策略——用户安全设置面板）。</summary>
        [ProtoMember(19)]
        public SecuritySettings Security { get; set; } = new();
    }
}
