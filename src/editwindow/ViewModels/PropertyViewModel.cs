using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Serilog;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 属性窗口当前选中对象的类型，供 XAML DataTemplate 切换使用。
    /// </summary>
    public enum PropertyTargetType
    {
        None,
        Screen,
        MultiSelect,
        ButtonWidget,
        TextWidget,
        RectangleWidget,
        LabelWidget,
        ImageWidget,
        NumericDisplayWidget,
        SwitchWidget,
        LineWidget,
        CircleWidget,
        EllipseWidget,
        IOFieldWidget,
        CheckBoxWidget,
        TextListWidget,
        FrameWidget,
        ProgressBarWidget,
        DateTimeWidget,
        WindowWidget,
        PolygonWidget,   // 世界地图批 3：多边形
        TrendViewWidget,   // P-4：趋势图控件（2026-09-02）
        HistoryViewWidget   // P-5：历史记录控件（2026-09-02）
    }

    /// <summary>
    /// 属性窗口的 ViewModel，持有当前选中的 Widget 或 Screen 并暴露其属性供绑定。
    /// P-2d（2026-09-02）：改 partial——本批仅开放 partial 能力（存量区段拆分随各批落地），
    /// 新控件属性（TrendView 等）落独立 partial 文件（PropertyViewModel.TrendView.cs），
    /// 不再堆入主文件（v1.1-design §5.3「改什么拆什么」；partial 各部分同 namespace/同访问级别，
    /// INotifyPropertyChanged 在主文件一处声明即对整个类生效）。
    /// </summary>
    public partial class PropertyViewModel : INotifyPropertyChanged
    {
#region 原 未分区块（72daf0e^ 第 60-2175 行；88 个成员块）

        /// <summary>刷新事件栏（选中控件时调用）：按控件类型映射可用事件 + 已配置函数数。</summary>
        public void RefreshEventBar(Widget? w)
        {
            EventRows.Clear();
            if (w == null) { EventBarVisible = false; return; }
            var typeName = w.GetType().Name;
            EventBarVisible = EventMapping.SupportsEvents(typeName);
            if (!EventBarVisible) return;
            foreach (var evt in EventMapping.GetEvents(typeName))
            {
                var existing = w.Events.FirstOrDefault(e => e.Type == evt);
                EventRows.Add(new EventRowVM(evt) { ActionCount = existing?.Actions.Count ?? 0 });
            }
            OnPropertyChanged(nameof(EventRows));
        }
        private Widget? _selectedWidget;
        private Screen? _selectedScreen;
        private Screen? _currentScreen;  // 当前编辑的画面，用于 ObjectName 重复检测

        /// <summary>工程引用（绑定变量下拉数据源，EditWindow 注入）。</summary>
        public HMIProject? Project { get; set; }

        /// <summary>命令服务（bind_tag 统一入口，GUI/CLI/AI 同一路径）。变量增删改成功后自动刷新绑定下拉。</summary>
        public CommandLayer.CommandService? CommandService
        {
            get => _commandService;
            set
            {
                if (_commandService == value) return;
                if (_commandService != null)
                    _commandService.CommandExecuted -= OnTagCommandExecuted;
                _commandService = value;
                if (_commandService != null)
                    _commandService.CommandExecuted += OnTagCommandExecuted;
            }
        }
        private CommandLayer.CommandService? _commandService;

        /// <summary>变量增删改成功后刷新绑定下拉（防下拉残留已删变量导致静默绑定失败）。</summary>
        private void OnTagCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // AI 后台线程触发时跨线程改 ObservableCollection 会被吞 → 封送回 UI 线程（与 EditWindowViewModel 同模式）
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => OnTagCommandExecuted(cmdName, parameters, result)));
                return;
            }
            if (result.Success && cmdName is "create_tag" or "update_tag" or "delete_tag" or "create_list" or "update_list" or "delete_list")
            {
                BeginSuppressBindTagCommands();   // 变量下拉 Clear 重建瞬态回写 null/哨兵会真解绑——窗口期拦截
                RefreshBindableTags();
                RefreshListOptions(_selectedWidget);
                // 选中控件仍有效时，按模型 BoundTag 重同步下拉选中（变量可能被删/重命名；
                // 同步性质直接赋值，不发 bind_tag 命令——避免 update_tag 改名后的冗余绑定+重复快照）
                if (_selectedWidget != null)
                {
                    _syncingFromModel = true;
                    try { _boundTag = ResolveBoundTarget(_selectedWidget.BoundTag); OnPropertyChanged(nameof(BoundTag)); OnPropertyChanged(nameof(IsValueEditable)); }
                    finally { _syncingFromModel = false; }
                }
            }
        }

        /// <summary>反射读取控件属性（无该属性/类型不符返回 null）。</summary>
        private static T? GetProperty<T>(Widget w, string propName)
        {
            var p = w.GetType().GetProperty(propName);
            if (p == null || !p.CanRead) return default;
            var v = p.GetValue(w);
            return v is T t ? t : default;
        }

        /// <summary>反射写入控件属性（仅对具有该属性的控件生效；同步初始化时跳过——防"读值变写回"）。</summary>
        private static void ApplyProperty(Widget w, string propName, object value)
        {
            var p = w.GetType().GetProperty(propName);
            if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value))
                p.SetValue(w, value);
        }


        /// <summary>是否有 Widget 被选中（属性面板可见性控制）。</summary>
        public bool IsPropertyVisible => _selectedWidget != null || _selectedScreen != null || IsMultiSelect;
        /// <summary>当前选中的是控件（单选）。</summary>
        public bool IsWidgetSelected => _selectedWidget != null;
        /// <summary>控件类型名称（选中画面显示"画面"，多选显示"批量编辑"）。</summary>
        public string WidgetTypeName => IsMultiSelect ? "批量编辑" : _selectedScreen != null ? "画面" : _selectedWidget?.GetType().Name ?? "";

        public bool IsButtonWidget => _selectedWidget is ButtonWidget;
        public bool IsTextWidget => _selectedWidget is TextWidget;
        public bool IsRectangleWidget => _selectedWidget is RectangleWidget;
        public bool IsLabelWidget => _selectedWidget is LabelWidget;
        public bool IsImageWidget => _selectedWidget is ImageWidget;
        public bool IsNumericDisplayWidget => _selectedWidget is NumericDisplayWidget;
        public bool IsSwitchWidget => _selectedWidget is SwitchWidget;
        public bool IsLineWidget => _selectedWidget is LineWidget;
        public bool IsCircleWidget => _selectedWidget is CircleWidget;
        public bool IsEllipseWidget => _selectedWidget is EllipseWidget;
        public bool IsIOFieldWidget => _selectedWidget is IOFieldWidget;
        public bool IsCheckBoxWidget => _selectedWidget is CheckBoxWidget;
        public bool IsTextListWidget => _selectedWidget is TextListWidget;
        public bool IsFrameWidget => _selectedWidget is FrameWidget;
        public bool IsProgressBarWidget => _selectedWidget is ProgressBarWidget;
        public bool IsDateTimeWidget => _selectedWidget is DateTimeWidget;
        public bool IsPolygonWidget => _selectedWidget is PolygonWidget;

        // ── 画面属性字段 ──
 
        private string _screenName = "";
 
        private double _screenWidth;
 
        private double _screenHeight;

        /// <summary>从工程 WorldMapConfig 同步作业范围点表格。</summary>
        private void RefreshWorkMapRangePoints()
        {
            RefreshGpsTagNames();   // X-1a：同作业点表格，重建即同步 GPS 选项
            WorkRangeRows.CollectionChanged -= WorkRangeRows_CollectionChanged;
            WorkRangeRows.Clear();
            if (Project?.WorldMap != null)
                foreach (var rp in Project.WorldMap.WorkRangePoints)
                    WorkRangeRows.Add(new WorkRangeRowVM(rp, OnWorkPointRowChanged, IsGpsTag, WorldMapBeforeModify) { GpsTagNames = GpsTagNames });   // W-2b：绑定变量下拉选项
            WorkRangeRows.CollectionChanged += WorkRangeRows_CollectionChanged;
        }

        /// <summary>V-4a：作业点名称重名校验（续28 B 方案）——重名行标 IsDuplicateName（粉红+ToolTip），
        /// 不删行不拦截；空名（placeholder/未输入）不参与。改名/删行后自动恢复。</summary>
        /// <summary>延迟移除误触空行：在 CollectionChanged 处理器内同步 Remove 同一集合会触发 CheckReentrancy 崩溃，
        /// 改经 Dispatcher 在事件周期结束后移除；移除前复查行仍在集合中（防重复/已删）。</summary>
        // ── P5：世界地图锁定预览 + 视口自适应 ──

        private bool _worldMapViewLocked;

        /// <summary>工程标脏回调（EditWindow 注入 → MarkProjectDirty；WorldMapConfig 是 POCO 不在订阅范围，勾选必须显式标脏防关闭静默丢失）。</summary>
        public Action? DirtyRequested { get; set; }

        // ── 控件绑定的属性字段 ──

        /// <summary>画布尺寸（由 EditWindow.LoadCanvas 注入，用于属性面板坐标/尺寸钳制）。</summary>
        public double CanvasWidth { get; set; } = double.MaxValue;
        /// <summary>用户修改模型前的回调（EditWindow 注入 → 撤销快照；选中同步初始化不触发）。</summary>
        public Action? BeforeModify { get; set; }

        /// <summary>命令失败回调（EditWindow 注入 → PopUndoSnapshot，弹出已推但未生效的空快照）。</summary>
        public Action? OnModifyFailed { get; set; }
        /// <summary>选中控件/画面时同步 VM 属性的标志（该过程不触发 BeforeModify，防误 Push 快照）。</summary>
        private bool _syncingFromModel;
        /// <summary>画布尺寸（由 EditWindow.LoadCanvas 注入）。</summary>
        public double CanvasHeight { get; set; } = double.MaxValue;

        private double _x;
        public double X
        {
            get => Math.Round(_x, 3);   // C12-11：点坐标显示统一 3 位小数（圆心/矩形顶点/位置）
            set
            {
                if (_selectedWidget != null)
                {
                    if (!double.IsFinite(value)) value = _x;   // 防 NaN/Infinity
                    value = Math.Max(0, Math.Min(value, CanvasWidth - _selectedWidget.Width));   // 0 ≤ X ≤ 画布宽-控件宽
                    value = Math.Round(value, 3, MidpointRounding.AwayFromZero);   // C12-11：坐标存储统一 3 位（显示与模型一致）
                }
                if (Math.Abs(_x - value) > 0.001)
                {
                    _x = value;
                    OnPropertyChanged();
                    // 实时回写到数据模型
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedWidget != null) _selectedWidget.X = value;
                }
            }
        }

        private double _y;
        public double Y
        {
            get => Math.Round(_y, 3);   // C12-11：点坐标显示统一 3 位小数
            set
            {
                if (_selectedWidget != null)
                {
                    if (!double.IsFinite(value)) value = _y;
                    value = Math.Max(0, Math.Min(value, CanvasHeight - _selectedWidget.Height));
                    value = Math.Round(value, 3, MidpointRounding.AwayFromZero);   // C12-11：坐标存储统一 3 位
                }
                if (Math.Abs(_y - value) > 0.001)
                {
                    _y = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedWidget != null) _selectedWidget.Y = value;
                }
            }
        }

        private double _width;
        public double Width
        {
            get => _width;
            set
            {
                if (_selectedWidget != null)
                {
                    if (!double.IsFinite(value)) value = _width;
                    // 1 ≤ Width ≤ 画布宽 - X（X+W 不超画布）
                    double maxW = Math.Max(1, CanvasWidth - _selectedWidget.X);
                    value = Math.Max(1, Math.Min(value, maxW));
                }
                if (Math.Abs(_width - value) > 0.001)
                {
                    _width = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedWidget != null) _selectedWidget.Width = value;
                }
            }
        }

        private double _height;
        public double Height
        {
            get => _height;
            set
            {
                if (_selectedWidget != null)
                {
                    if (!double.IsFinite(value)) value = _height;
                    double maxH = Math.Max(1, CanvasHeight - _selectedWidget.Y);
                    value = Math.Max(1, Math.Min(value, maxH));
                }
                if (Math.Abs(_height - value) > 0.001)
                {
                    _height = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedWidget != null) _selectedWidget.Height = value;
                }
            }
        }

        private string _objectName = "";
        public string ObjectName
        {
            get => _objectName;
            set
            {
                if (_objectName != value)
                {
                    _objectName = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedWidget != null) _selectedWidget.ObjectName = value;
                    // ObjectName 变更后刷新重复检测
                    RefreshDuplicateCheck();
                }
            }
        }
        /// <summary>当前编辑的画面引用，供 ObjectName 重复检测使用。</summary>
        public Screen? CurrentScreen
        {
            get => _currentScreen;
            set
            {
                if (_currentScreen != value)
                {
                    _currentScreen = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _hasDuplicateObjectName;
        /// <summary>当前 ObjectName 是否与同画面其他 Widget 重复，变红提示。</summary>
        public bool HasDuplicateObjectName
        {
            get => _hasDuplicateObjectName;
            private set
            {
                if (_hasDuplicateObjectName != value)
                {
                    _hasDuplicateObjectName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 扫描当前画面所有 Widget，检查是否有其他 Widget 的 ObjectName 与当前选中 Widget 重复。
        /// </summary>
        private void RefreshDuplicateCheck()
        {
            if (_selectedWidget == null || string.IsNullOrEmpty(_selectedWidget.ObjectName))
            {
                HasDuplicateObjectName = false;
                return;
            }

            // 优先使用 CurrentScreen（由 EditWindow.OnWidgetSelected 设置）
            System.Collections.ObjectModel.ObservableCollection<Widget>? widgets = _currentScreen?.Widgets;
            if (widgets == null)
            {
                HasDuplicateObjectName = false;
                return;
            }

            bool hasDuplicate = false;
            foreach (var w in widgets)
            {
                if (w != _selectedWidget && string.Equals(w.ObjectName, _selectedWidget.ObjectName, StringComparison.Ordinal))
                {
                    hasDuplicate = true;
                    break;
                }
            }

            HasDuplicateObjectName = hasDuplicate;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion

    }

    /// <summary>W4 RobotList 机器人槽位绑定行（属性面板绑定表一行）。</summary>
    public class RobotSlotRowVM : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        private readonly PropertyViewModel _owner;
        private readonly int _index;
        public string Label { get; }
        public System.Collections.ObjectModel.ObservableCollection<Tag> Tags => _owner.WindowDeviceTags;

        private string _selectedTag = "";
        public string SelectedTag
        {
            get => _selectedTag;
            set
            {
                if (value == null) return;   // 集合重建瞬态失配回写 null 拦截（防清空已配置槽位）
                if (_selectedTag != value)
                {
                    _selectedTag = value;
                    Notify(nameof(SelectedTag));
                    _owner.SetRobotSlotTag(_index, value);
                }
            }
        }

        public RobotSlotRowVM(PropertyViewModel owner, int index, string label, string tag)
        {
            _owner = owner; _index = index; Label = label; _selectedTag = tag;
        }
    }

    /// <summary>作业点表格行（P4）：名称 + 经纬度(固定) + 绑定变量；两列互斥（写固定值清绑定变量，反之亦然）。
    /// 行内编辑修改前调 beforeModify（WorldMap 数据快照——C12-2：撤销粒度 = 单格编辑）。</summary>
    public class WorkPointRowVM : INotifyPropertyChanged
    {
        private readonly Action? _onChanged;
        private readonly Func<string, bool>? _tagValidator;
        private readonly Action? _beforeModify;
        public MapWorkPoint Model { get; }
        public WorkPointRowVM(MapWorkPoint model, Action? onChanged, Func<string, bool>? tagValidator, Action? beforeModify)
        { Model = model; _onChanged = onChanged; _tagValidator = tagValidator; _beforeModify = beforeModify; }
        public WorkPointRowVM() : this(new MapWorkPoint(), null, null, null) { }   // DataGrid 新行占位（提交时由 RowEditEnding 挂入模型）

        public string Name
        {
            get => Model.Name;
            set
            {
                var v = value?.Trim() ?? "";
                // Y-6（2026-09-10，X 循环 NEED_RULE 并入）：作业点名禁 , | 分隔符（FW 注入串按 ,| 分隔——防点名碰撞；
                // 含则标 NameError 不落库——对齐重名「标错不拦截」风格）
                if (v.Contains(',') || v.Contains('|'))
                {
                    NameError = "作业点名不能包含 , 或 | 分隔符";
                    return;   // 不落库（模型保持旧值；名称框停留——DataGrid 提交回滚）
                }
                NameError = "";
                if (Model.Name != v) { _beforeModify?.Invoke(); Model.Name = v; _onChanged?.Invoke(); OnPropertyChanged(); }
            }
        }
        /// <summary>Y-6：作业点名非法标记（含 , | 分隔符——气泡/ToolTip 提示；空 = 合法）。</summary>
        private string _nameError = "";
        public string NameError
        {
            get => _nameError;
            set { if (_nameError != value) { _nameError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNameError)); } }
        }
        public bool HasNameError => _nameError.Length > 0;
        /// <summary>V-4a：名称重复标记（续28 B 方案：重名不删行不拦截，名称框标粉红 + ToolTip 提示；改名后自动恢复）。</summary>
        private bool _isDuplicateName;
        public bool IsDuplicateName
        {
            get => _isDuplicateName;
            set { if (_isDuplicateName != value) { _isDuplicateName = value; OnPropertyChanged(); } }
        }
        public string LngLat
        {
            get => Model.FixedPoint?.ToBaseValue() ?? "";
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (!GeoPoint.TryParse(value, out var g) || g == null) return;   // 非法经纬度忽略（静默，不落库）
                _beforeModify?.Invoke();   // 行内编辑前快照
                Model.FixedPoint = g; Model.BoundTag = "";   // 两列互斥：写固定值清绑定变量
                _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag));
            }
        }
        // W-2a：经纬度拆两列——显示/编辑/修改全 DMS（用户 2026-08-12 拍板：小数只是输入方式，确认后一律度分秒，秒 2 位小数）；
        // 输入兼容小数度（正=E/N，负=W/S）与 DMS（含小数秒）；非法 → CoordError（W-3a 气泡 + ToolTip 带示例）
        public string Lng
        {
            get => Model.FixedPoint != null ? GeoPoint.FormatDms(Model.FixedPoint.Longitude, true) : "";
            set
            {
                var v = (value ?? "").Trim();
                if (GeoPoint.TryParseCoord(v, true, out var lng))
                {
                    // W-3a 回归修复：同值短路——DataGrid Commit 会二次 UpdateSource，避免重复 _beforeModify 覆盖正确撤销快照
                    if (Model.FixedPoint != null && Math.Abs(Model.FixedPoint.Longitude - lng) < 1e-9 && string.IsNullOrEmpty(Model.BoundTag)) return;
                    _beforeModify?.Invoke();
                    Model.FixedPoint ??= new GeoPoint(0, 0);
                    Model.FixedPoint.Longitude = lng;
                    Model.BoundTag = "";   // 互斥：写固定值清绑定变量
                    CoordErrorLng = "";   // V-5b：只清本列错误（Lat 错误独立保留）
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag)); OnPropertyChanged(nameof(LngLat));
                }
                else if (!string.IsNullOrWhiteSpace(v))
                    CoordErrorLng = "经度格式：104.06（东经为正，负号=西经）；DMS 如 E104°3'30.25\"";   // V-5b：非法输入带示例
                else CoordErrorLng = "";
            }
        }
        public string Lat
        {
            get => Model.FixedPoint != null ? GeoPoint.FormatDms(Model.FixedPoint.Latitude, false) : "";
            set
            {
                var v = (value ?? "").Trim();
                if (GeoPoint.TryParseCoord(v, false, out var lat))
                {
                    // W-3a 回归修复：同值短路（同 Lng）
                    if (Model.FixedPoint != null && Math.Abs(Model.FixedPoint.Latitude - lat) < 1e-9 && string.IsNullOrEmpty(Model.BoundTag)) return;
                    _beforeModify?.Invoke();
                    Model.FixedPoint ??= new GeoPoint(0, 0);
                    Model.FixedPoint.Latitude = lat;
                    Model.BoundTag = "";
                    CoordErrorLat = "";
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag)); OnPropertyChanged(nameof(LngLat));
                }
                else if (!string.IsNullOrWhiteSpace(v))
                    CoordErrorLat = "纬度格式：30.67（北纬为正，负号=南纬）；DMS 如 N30°40'20.12\"";
                else CoordErrorLat = "";
            }
        }
        /// <summary>V-5b：经度/纬度输入非法提示（含示例；两列独立，合法输入清本列）。</summary>
        private string _coordErrorLng = "";
        public string CoordErrorLng
        {
            get => _coordErrorLng;
            set { if (_coordErrorLng != value) { _coordErrorLng = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCoordError)); OnPropertyChanged(nameof(CoordError)); } }
        }
        private string _coordErrorLat = "";
        public string CoordErrorLat
        {
            get => _coordErrorLat;
            set { if (_coordErrorLat != value) { _coordErrorLat = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCoordError)); OnPropertyChanged(nameof(CoordError)); } }
        }
        public string CoordError => CoordErrorLng.Length > 0 ? CoordErrorLng : CoordErrorLat;
        public bool HasCoordError => CoordErrorLng.Length > 0 || CoordErrorLat.Length > 0;

        public string BoundTag
        {
            get => Model.BoundTag;
            set
            {
                var v = value?.Trim() ?? "";
                if (v.Length > 0 && (_tagValidator == null || !_tagValidator(v))) return;   // 变量必须存在且为 GPS 类型
                if (Model.BoundTag != v)
                {
                    _beforeModify?.Invoke();   // 行内编辑前快照
                    Model.BoundTag = v; Model.FixedPoint = null;   // 两列互斥：写绑定变量清固定值
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(LngLat));
                    OnPropertyChanged(nameof(Lng)); OnPropertyChanged(nameof(Lat));   // V-5a：清固定值 → 两列同步清空显示
                    OnPropertyChanged(nameof(BoundTagDisplay));   // W-2b：下拉显示联动
                }
            }
        }
        /// <summary>W-2b：绑定变量下拉显示——空 = 「无」；选中「无」= 清空（其余同 BoundTag 校验/互斥逻辑）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string>? GpsTagNames { get; set; }
        public string BoundTagDisplay
        {
            get => string.IsNullOrEmpty(Model.BoundTag) ? "无" : Model.BoundTag;
            set
            {
                var v = (value ?? "").Trim();
                if (v == "无") v = "";
                if (v.Length > 0 && (_tagValidator == null || !_tagValidator(v))) return;
                if (Model.BoundTag != v)
                {
                    _beforeModify?.Invoke();
                    Model.BoundTag = v; Model.FixedPoint = null;
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag));
                    OnPropertyChanged(nameof(LngLat)); OnPropertyChanged(nameof(Lng)); OnPropertyChanged(nameof(Lat));
                }
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>作业范围点表格行（P4）：经纬度(固定) + 绑定变量；两列互斥；无名称。
    /// 行内编辑修改前调 beforeModify（WorldMap 数据快照——C12-2：撤销粒度 = 单格编辑）。</summary>
    public class WorkRangeRowVM : INotifyPropertyChanged
    {
        private readonly Action? _onChanged;
        private readonly Func<string, bool>? _tagValidator;
        private readonly Action? _beforeModify;
        public WorkRangePoint Model { get; }
        public WorkRangeRowVM(WorkRangePoint model, Action? onChanged, Func<string, bool>? tagValidator, Action? beforeModify)
        { Model = model; _onChanged = onChanged; _tagValidator = tagValidator; _beforeModify = beforeModify; }
        public WorkRangeRowVM() : this(new WorkRangePoint(), null, null, null) { }   // DataGrid 新行占位

        public string LngLat
        {
            get => Model.FixedPoint?.ToBaseValue() ?? "";
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                if (!GeoPoint.TryParse(value, out var g) || g == null) return;
                _beforeModify?.Invoke();   // 行内编辑前快照
                Model.FixedPoint = g; Model.BoundTag = "";   // 两列互斥
                _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag));
            }
        }
        // W-2a：范围点经纬度拆两列（同 WorkPointRowVM：显示/编辑/修改全 DMS，输入兼容小数与 DMS）
        public string Lng
        {
            get => Model.FixedPoint != null ? GeoPoint.FormatDms(Model.FixedPoint.Longitude, true) : "";
            set
            {
                var v = (value ?? "").Trim();
                if (GeoPoint.TryParseCoord(v, true, out var lng))
                {
                    // W-3a 回归修复：同值短路——DataGrid Commit 会二次 UpdateSource，避免重复 _beforeModify 覆盖正确撤销快照
                    if (Model.FixedPoint != null && Math.Abs(Model.FixedPoint.Longitude - lng) < 1e-9 && string.IsNullOrEmpty(Model.BoundTag)) return;
                    _beforeModify?.Invoke();
                    Model.FixedPoint ??= new GeoPoint(0, 0);
                    Model.FixedPoint.Longitude = lng;
                    Model.BoundTag = "";
                    CoordErrorLng = "";
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag)); OnPropertyChanged(nameof(LngLat));
                }
                else if (!string.IsNullOrWhiteSpace(v))
                    CoordErrorLng = "经度格式：104.06（东经为正，负号=西经）；DMS 如 E104°3'30.25\"";
                else CoordErrorLng = "";
            }
        }
        public string Lat
        {
            get => Model.FixedPoint != null ? GeoPoint.FormatDms(Model.FixedPoint.Latitude, false) : "";
            set
            {
                var v = (value ?? "").Trim();
                if (GeoPoint.TryParseCoord(v, false, out var lat))
                {
                    // W-3a 回归修复：同值短路（同 Lng）
                    if (Model.FixedPoint != null && Math.Abs(Model.FixedPoint.Latitude - lat) < 1e-9 && string.IsNullOrEmpty(Model.BoundTag)) return;
                    _beforeModify?.Invoke();
                    Model.FixedPoint ??= new GeoPoint(0, 0);
                    Model.FixedPoint.Latitude = lat;
                    Model.BoundTag = "";
                    CoordErrorLat = "";
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag)); OnPropertyChanged(nameof(LngLat));
                }
                else if (!string.IsNullOrWhiteSpace(v))
                    CoordErrorLat = "纬度格式：30.67（北纬为正，负号=南纬）；DMS 如 N30°40'20.12\"";
                else CoordErrorLat = "";
            }
        }
        /// <summary>V-5b：经度/纬度输入非法提示（含示例；两列独立）。</summary>
        private string _coordErrorLng = "";
        public string CoordErrorLng
        {
            get => _coordErrorLng;
            set { if (_coordErrorLng != value) { _coordErrorLng = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCoordError)); OnPropertyChanged(nameof(CoordError)); } }
        }
        private string _coordErrorLat = "";
        public string CoordErrorLat
        {
            get => _coordErrorLat;
            set { if (_coordErrorLat != value) { _coordErrorLat = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCoordError)); OnPropertyChanged(nameof(CoordError)); } }
        }
        public string CoordError => CoordErrorLng.Length > 0 ? CoordErrorLng : CoordErrorLat;
        public bool HasCoordError => CoordErrorLng.Length > 0 || CoordErrorLat.Length > 0;
        public string BoundTag
        {
            get => Model.BoundTag;
            set
            {
                var v = value?.Trim() ?? "";
                if (v.Length > 0 && (_tagValidator == null || !_tagValidator(v))) return;
                if (Model.BoundTag != v)
                {
                    _beforeModify?.Invoke();   // 行内编辑前快照
                    Model.BoundTag = v; Model.FixedPoint = null;   // 两列互斥
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(LngLat));
                    OnPropertyChanged(nameof(Lng)); OnPropertyChanged(nameof(Lat));   // V-5a：清固定值 → 两列同步清空显示
                    OnPropertyChanged(nameof(BoundTagDisplay));   // W-2b：下拉显示联动
                }
            }
        }
        /// <summary>W-2b：绑定变量下拉显示——空 = 「无」；选中「无」= 清空（其余同 BoundTag 校验/互斥逻辑）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string>? GpsTagNames { get; set; }
        public string BoundTagDisplay
        {
            get => string.IsNullOrEmpty(Model.BoundTag) ? "无" : Model.BoundTag;
            set
            {
                var v = (value ?? "").Trim();
                if (v == "无") v = "";
                if (v.Length > 0 && (_tagValidator == null || !_tagValidator(v))) return;
                if (Model.BoundTag != v)
                {
                    _beforeModify?.Invoke();
                    Model.BoundTag = v; Model.FixedPoint = null;
                    _onChanged?.Invoke(); OnPropertyChanged(); OnPropertyChanged(nameof(BoundTag));
                    OnPropertyChanged(nameof(LngLat)); OnPropertyChanged(nameof(Lng)); OnPropertyChanged(nameof(Lat));
                }
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>多边形顶点表格行（P7）：X/Y 画布绝对坐标行内编辑；末行自动补行；删除禁 &lt;3 点。
    /// 直写模型 PointD（无 INPC）——变更由 owner 的 OnPolygonPointRowChanged 重赋值 Points 触发渲染刷新。</summary>
    public class PolygonPointRowVM : INotifyPropertyChanged
    {
        private readonly Action? _onChanged;
        private readonly Action? _beforeModify;
        public PointD Model { get; }
        private int _index;
        public int Index { get => _index; set { if (_index != value) { _index = value; OnPropertyChanged(); } } }
        /// <summary>是否被编辑过（DataGrid 新行未输入任何坐标 = 未编辑 → 空行丢弃判定）。</summary>
        public bool Edited { get; private set; }

        public PolygonPointRowVM(PointD model, Action? onChanged, Action? beforeModify)
        { Model = model; _onChanged = onChanged; _beforeModify = beforeModify; }
        public PolygonPointRowVM() : this(new PointD(), null, null) { }   // DataGrid 新行占位（CanUserAddRows 无参构造）

        public double X
        {
            get => Math.Round(Model.X, 3, MidpointRounding.AwayFromZero);   // C12-11：顶点坐标显示统一 3 位小数
            set
            {
                var v = Math.Round(value, 3, MidpointRounding.AwayFromZero);
                if (Model.X != v)
                {
                    _beforeModify?.Invoke();   // 行内编辑前快照（撤销粒度 = 单格编辑；新行未挂入模型时 null 安全）
                    Model.X = v; _onChanged?.Invoke();
                }
                Edited = true;   // 只要用户提交过单元格即视为已编辑（含输入 0）
                OnPropertyChanged();
            }
        }
        public double Y
        {
            get => Math.Round(Model.Y, 3, MidpointRounding.AwayFromZero);   // C12-11：顶点坐标显示统一 3 位小数
            set
            {
                var v = Math.Round(value, 3, MidpointRounding.AwayFromZero);
                if (Model.Y != v)
                {
                    _beforeModify?.Invoke();
                    Model.Y = v; _onChanged?.Invoke();
                }
                Edited = true;
                OnPropertyChanged();
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>C12-12：外部改动模型（拖拽/缩放平移顶点）后刷新显示值（PointD 无 INPC，行 VM 直读模型需显式通知）。</summary>
        public void RefreshValues()
        {
            OnPropertyChanged(nameof(X));
            OnPropertyChanged(nameof(Y));
        }
    }
}