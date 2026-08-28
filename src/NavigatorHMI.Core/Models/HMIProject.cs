using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

        /// <summary>
        /// 工程脏标记（[ProtoIgnore] 不落盘）——**模型级唯一真源**（2026-08-30 K 循环 K-1a/d 迁移完成，EditWindow 窗口级已删）。
        /// 标量 setter 修改、Screens/Tags 集合增删改自动置脏；元素级修改经 <see cref="MarkDirty"/> 显式置脏；
        /// 保存成功/加载完成后 <see cref="ClearDirty"/> 清脏。运行时状态字段（CurrentScreenName）不置脏。
        /// </summary>
        [ProtoIgnore]
        public bool IsDirty { get; private set; }

        /// <summary>置脏（元素级修改/命令层修改入口；模型内部 setter 与集合事件自动调用）。</summary>
        public void MarkDirty() => IsDirty = true;

        /// <summary>清脏（保存成功 / 加载完成后调用）。</summary>
        public void ClearDirty() => IsDirty = false;

        /// <summary>集合变化 → 置脏（Screens/Tags 的 CollectionChanged 钩子）。</summary>
        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => IsDirty = true;

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
            set { if (_name != value) { _name = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private DateTime _createTime;

        /// <summary>工程创建时间</summary>
        [ProtoMember(2)]
        public DateTime CreateTime
        {
            get => _createTime;
            set { if (_createTime != value) { _createTime = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private DateTime _lastModifiedTime;

        /// <summary>最后修改时间</summary>
        [ProtoMember(3)]
        public DateTime LastModifiedTime
        {
            get => _lastModifiedTime;
            set { if (_lastModifiedTime != value) { _lastModifiedTime = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private string _version = "1.0";

        /// <summary>工程格式版本号</summary>
        [ProtoMember(4)]
        public string Version
        {
            get => _version;
            set { if (_version != value) { _version = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private ObservableCollection<Screen> _screens = new();

        /// <summary>画面列表（包含 Template/WorldMap/Custom 三种类型）。ObservableCollection + CollectionChanged 置脏；setter 替换时重挂钩子（防整集合赋值丢事件，2026-08-30 K 循环）。</summary>
        [ProtoMember(5)]
        public ObservableCollection<Screen> Screens
        {
            get => _screens;
            set
            {
                if (!ReferenceEquals(_screens, value))
                {
                    if (_screens != null) _screens.CollectionChanged -= OnCollectionChanged;
                    _screens = value ?? new ObservableCollection<Screen>();
                    _screens.CollectionChanged += OnCollectionChanged;
                    OnPropertyChanged();
                    MarkDirty();
                }
            }
        }

        /// <summary>工程文件路径（ProtoBuf 持久化，绝对或相对路径）</summary>
        private string _projectFilePath = "";

        [ProtoMember(6)]
        public string ProjectFilePath
        {
            get => _projectFilePath;
            set { if (_projectFilePath != value) { _projectFilePath = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private int _deviceWidth = 800;

        /// <summary>目标设备屏幕宽度（像素）</summary>
        [ProtoMember(7)]
        public int DeviceWidth
        {
            get => _deviceWidth;
            set { if (_deviceWidth != value) { _deviceWidth = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private int _deviceHeight = 480;

        /// <summary>目标设备屏幕高度（像素）</summary>
        [ProtoMember(8)]
        public int DeviceHeight
        {
            get => _deviceHeight;
            set { if (_deviceHeight != value) { _deviceHeight = value; OnPropertyChanged(); MarkDirty(); } }
        }

        // ═══════════════════════════════════════════
        // v2.0 新增字段 (ProtoMember 9+)
        // ═══════════════════════════════════════════

        private ObservableCollection<Tag> _tags = new();

        /// <summary>工程中所有变量定义（控件绑定 + 数据采集的数据源）。ObservableCollection + CollectionChanged 置脏；setter 替换时重挂钩子（2026-08-30 K 循环）。</summary>
        [ProtoMember(9)]
        public ObservableCollection<Tag> Tags
        {
            get => _tags;
            set
            {
                if (!ReferenceEquals(_tags, value))
                {
                    if (_tags != null) _tags.CollectionChanged -= OnCollectionChanged;
                    _tags = value ?? new ObservableCollection<Tag>();
                    _tags.CollectionChanged += OnCollectionChanged;
                    OnPropertyChanged();
                    MarkDirty();
                }
            }
        }

        /// <summary>工程中所有报警规则（设备端 AlarmEngine 按此检测触发）</summary>
        [ProtoMember(10)]
        public List<AlarmRule> Alarms { get; set; } = new();

        /// <summary>工程中所有设备通信配置（ModbusRTU/ModbusTCP/MQTT）</summary>
        [ProtoMember(11)]
        public List<DeviceConfig> Devices { get; set; } = new();

        /// <summary>世界地图配置（仅 ScreenType.WorldMap 画面使用），null 表示未配置</summary>
        private WorldMapConfig? _worldMap;

        [ProtoMember(12)]
        public WorldMapConfig? WorldMap
        {
            get => _worldMap;
            set { if (!ReferenceEquals(_worldMap, value)) { _worldMap = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private bool _showNavigationBar = true;

        /// <summary>设备端是否显示底部/顶部导航栏（默认开启）。默认 true——IsRequired 强制写（protobuf-net 省略 false 丢值）。</summary>
        [ProtoMember(13, IsRequired = true)]
        public bool ShowNavigationBar
        {
            get => _showNavigationBar;
            set { if (_showNavigationBar != value) { _showNavigationBar = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private NavPosition _navigationPosition = NavPosition.Top;

        /// <summary>导航栏位置（Top / Bottom）</summary>
        [ProtoMember(14)]
        public NavPosition NavigationPosition
        {
            get => _navigationPosition;
            set { if (_navigationPosition != value) { _navigationPosition = value; OnPropertyChanged(); MarkDirty(); } }
        }

        private string _startScreen = "";

        /// <summary>设备启动后默认显示的画面名称。空字符串表示使用第一个画面。</summary>
        [ProtoMember(15)]
        public string StartScreen
        {
            get => _startScreen;
            set { if (_startScreen != value) { _startScreen = value; OnPropertyChanged(); MarkDirty(); } }
        }

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
        private SecuritySettings _security = new();

        [ProtoMember(19)]
        public SecuritySettings Security
        {
            get => _security;
            set { if (!ReferenceEquals(_security, value)) { _security = value; OnPropertyChanged(); MarkDirty(); } }
        }

        /// <summary>构造：Screens/Tags 集合增删改自动置脏（K 循环 IsDirty 单点化）。</summary>
        public HMIProject()
        {
            Screens.CollectionChanged += OnCollectionChanged;
            Tags.CollectionChanged += OnCollectionChanged;
        }
    }
}
