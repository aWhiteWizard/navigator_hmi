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
        #region 原 未分区块（第 60-2175 行）

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
        private void ValidateDuplicateNames()
        {
            if (Project?.WorldMap == null) return;
            var dupNames = WorkPointRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var r in WorkPointRows) r.IsDuplicateName = dupNames.Contains(r.Name);
        }

        /// <summary>延迟移除误触空行：在 CollectionChanged 处理器内同步 Remove 同一集合会触发 CheckReentrancy 崩溃，
        /// 改经 Dispatcher 在事件周期结束后移除；移除前复查行仍在集合中（防重复/已删）。</summary>
        private static void RemoveRowDeferred<T>(ObservableCollection<T> rows, T row)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (rows.Contains(row)) rows.Remove(row);
                });
            }
            else
            {
                // 无 WPF Application（如单测环境）：集合修改不会伴随 DataGrid 重入，直接移除安全
                if (rows.Contains(row)) rows.Remove(row);
            }
        }

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

        // ── 子类特有属性 ──

        private string _buttonText = "";
        public string ButtonText
        {
            get => _buttonText;
            set
            {
                if (_buttonText != value)
                {
                    _buttonText = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget btn) btn.Text = value;
                }
            }
        }

        private string _textContent = "";
        public string TextContent
        {
            get => _textContent;
            set
            {
                if (_textContent != value)
                {
                    _textContent = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.Content = value;
                }
            }
        }
        /// <summary>Text 水平对齐。</summary>
        public string TextHAlign { get => _textHAlign; set { if (_textHAlign != value) { _textHAlign = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.HAlign = value; } } }

        private string _labelText = "";

        public string LabelText { get => _labelText; set { if (_labelText != value) { _labelText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.Text = value; } } }

        private string _imagePath = "";
        public string ImagePath { get => _imagePath; set { if (_imagePath != value) { _imagePath = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.ImagePath = value; } } }
        private string _imageFillColor = "#EEEEEE";

        // ── 列表绑定（Image/Frame/TextList 共用模式：ListRef 下拉 + DefaultIndex 缺省值） ──

        /// <summary>图片列表集合（选中 Image/Frame 时的下拉数据源，首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> ImageListOptions { get; } = new();

        /// <summary>文本列表集合（选中 TextList 时的下拉数据源，首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> TextListOptions { get; } = new();

        /// <summary>重建列表下拉（首项空串=不绑列表 + 工程对应类型列表名 + 选中控件已绑定列表占位），选中控件时调用。
        /// 用增量同步（不 Clear 重建）——Clear+Add 重建会丢 ComboBox 选中且 null 短路后无法自动恢复（ListManager 同款坑）。</summary>
        private void RefreshListOptions(Widget? w)
        {
            BeginSuppressListRefWrites();   // 增量同步移除占位项时 ComboBox 失配回写 """ 会真解绑——窗口期拦截
            var imgNames = Project?.Lists.Where(l => l.Type == ListType.Image).Select(l => l.Name) ?? Enumerable.Empty<string>();
            var textNames = Project?.Lists.Where(l => l.Type == ListType.Text).Select(l => l.Name) ?? Enumerable.Empty<string>();   // S-4：仅文本型（原 != Image 会混入 Video 型）
            var videoNames = Project?.Lists.Where(l => l.Type == ListType.Video).Select(l => l.Name) ?? Enumerable.Empty<string>();   // S-5：视频列表下拉选项（Check 2026-09-05 收紧：仅 Video 型——Text 型列表给 TextList 控件，不混入 Frame 视频列表下拉；Check ② 曾放开 Text 已收回）
            SyncOptions(ImageListOptions, imgNames);
            SyncOptions(TextListOptions, textNames);
            SyncOptions(VideoListOptions, videoNames);
            // 占位：选中控件已绑定的列表名（即使类型不符/列表已删）加入对应 Options，
            // 防 ComboBox SelectedItem 失配导致属性面板显示空（AI/CLI 建的列表 type 可能与控件期望不符，但功能正常）
            switch (w)
            {
                case ImageWidget img when !string.IsNullOrEmpty(img.ListRef) && !ImageListOptions.Contains(img.ListRef):
                    ImageListOptions.Add(img.ListRef); break;
                case FrameWidget f:
                    // 审查 🟡-2：Frame 可能同时持 ListRef（图模式残留）与 VideoListRef——占位须两集合都补（互斥 case 会漏后者）
                    if (!string.IsNullOrEmpty(f.ListRef) && !ImageListOptions.Contains(f.ListRef))
                        ImageListOptions.Add(f.ListRef);
                    if (!string.IsNullOrEmpty(f.VideoListRef) && !VideoListOptions.Contains(f.VideoListRef))
                        VideoListOptions.Add(f.VideoListRef);
                    break;
                case TextListWidget tl when !string.IsNullOrEmpty(tl.ListRef) && !TextListOptions.Contains(tl.ListRef):
                    TextListOptions.Add(tl.ListRef); break;
            }
        }

        /// <summary>增量同步选项集合：首项 "" 恒保持，后续按名称尾部增删/对齐——不 Clear 重建，保 ComboBox 选中（wpf-combobox-style 集合重建陷阱）。</summary>
        private static void SyncOptions(System.Collections.ObjectModel.ObservableCollection<string> options, IEnumerable<string> names)
        {
            if (options.Count == 0 || options[0] != "") options.Insert(0, "");   // 哨兵首项
            var keep = new HashSet<string>(names);
            for (int i = options.Count - 1; i >= 1; i--)
                if (!keep.Contains(options[i])) options.RemoveAt(i);
            var pos = 1;
            foreach (var n in names)
            {
                while (pos < options.Count && options[pos] != n) pos++;
                if (pos == options.Count && !options.Contains(n)) options.Add(n);   // 去重兜底（防乱序输入重复添加）
                pos++;
            }
        }

        private string? _imageListRef = "";
        /// <summary>ImageWidget 绑定的图片列表名（null=无）。</summary>
        public string? ImageListRef
        {
            get => _imageListRef;
            set
            {
                // null = 切换选中控件时 RefreshListOptions 重建下拉的瞬态（ComboBox SelectedItem 在空选项时变 null）——忽略防误解绑；
                // "" = 用户显式选「（无绑定）」解绑，保留
                if (value == null) return;
                if (_imageListRef != value)
                {
                    _imageListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is ImageWidget img)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（防哨兵 "" 真解绑）
                        if (value == img.ListRef) return;     // 模型同值跳过（防同步回写副作用）
                        if (!_syncingFromModel) BeforeModify?.Invoke();   // 检查全过才推快照（防失配回写推空撤销快照污染 Undo 栈）
                        img.ListRef = value;
                        if (!string.IsNullOrEmpty(value)) WidgetDesignValue.Clear(img);
                    }
                }
            }
        }
        private int _imageDefaultIndex = 0;
        /// <summary>ImageWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int ImageDefaultIndex { get => _imageDefaultIndex; set { var v = Math.Max(0, value); if (_imageDefaultIndex != v) { _imageDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.DefaultIndex = v; } } }

        private string? _frameListRef = "";
        /// <summary>FrameWidget 绑定的图片列表名（null=无）。</summary>
        public string? FrameListRef
        {
            get => _frameListRef;
            set
            {
                if (value == null) return;   // RefreshListOptions 重建瞬态，忽略防误解绑（同 ImageListRef）
                if (_frameListRef != value)
                {
                    _frameListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is FrameWidget f)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（防哨兵 "" 真解绑）
                        if (value == f.ListRef) return;       // 模型同值跳过
                        if (!_syncingFromModel) BeforeModify?.Invoke();
                        f.ListRef = value;
                        if (!string.IsNullOrEmpty(value)) WidgetDesignValue.Clear(f);
                    }
                }
            }
        }
        private int _frameDefaultIndex = 0;
        /// <summary>FrameWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int FrameDefaultIndex { get => _frameDefaultIndex; set { var v = Math.Max(0, value); if (_frameDefaultIndex != v) { _frameDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.DefaultIndex = v; } } }

        private string? _textListListRef = "";
        /// <summary>TextListWidget 绑定的文本列表名（null=无）。</summary>
        public string? TextListListRef
        {
            get => _textListListRef;
            set
            {
                if (value == null) return;   // RefreshListOptions 重建瞬态，忽略防误解绑（同 ImageListRef）
                if (_textListListRef != value)
                {
                    _textListListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is TextListWidget tl)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（防哨兵 "" 真解绑）
                        if (value == tl.ListRef) return;      // 模型同值跳过
                        if (!_syncingFromModel) BeforeModify?.Invoke();
                        tl.ListRef = value;
                    }
                }
            }
        }
        private int _textListDefaultIndex = 0;
        /// <summary>TextListWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int TextListDefaultIndex { get => _textListDefaultIndex; set { var v = Math.Max(0, value); if (_textListDefaultIndex != v) { _textListDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget tl) tl.DefaultIndex = v; } } }
        /// <summary>NumericDisplay 设计态数值预览。</summary>
        public double NumericValue { get => _numericValue; set { if (Math.Abs(_numericValue - value) > 0.001) { _numericValue = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget nd) nd.Value = value; } } }

        private bool _switchIsOn = false;
        public bool SwitchIsOn { get => _switchIsOn; set { if (_switchIsOn != value) { _switchIsOn = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.IsOn = value; } } }
        private string _switchOnText = "ON";
        public string SwitchOnText { get => _switchOnText; set { if (_switchOnText != value) { _switchOnText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.OnText = value; } } }
        private string _switchOffText = "OFF";
        public string SwitchOffText { get => _switchOffText; set { if (_switchOffText != value) { _switchOffText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.OffText = value; } } }

        private double _lineX2 = 100;
        public double LineX2 { get => _lineX2; set { if (!double.IsFinite(value)) value = _lineX2; if (_selectedWidget is LineWidget ln) value = Math.Min(Math.Max(0, value), ln.Width); value = Math.Round(value, 3, MidpointRounding.AwayFromZero); if (Math.Abs(_lineX2 - value) > 0.001) { if (!_syncingFromModel) BeforeModify?.Invoke(); _lineX2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln2) ln2.X2 = value; } } }   // C12-11：端点存储统一 3 位
        private double _lineY2 = 0;
        public double LineY2 { get => _lineY2; set { if (!double.IsFinite(value)) value = _lineY2; if (_selectedWidget is LineWidget ln) value = Math.Min(Math.Max(0, value), ln.Height); value = Math.Round(value, 3, MidpointRounding.AwayFromZero); if (Math.Abs(_lineY2 - value) > 0.001) { if (!_syncingFromModel) BeforeModify?.Invoke(); _lineY2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln3) ln3.Y2 = value; } } }   // C12-11：端点存储统一 3 位
        private string _lineStrokeColor = "#000000";
        public string LineStrokeColor { get => _lineStrokeColor; set { if (_lineStrokeColor != value) { _lineStrokeColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LineWidget ln) ln.StrokeColor = value; } } }

        private double _lineStrokeThickness = 1;
        public double LineStrokeThickness { get => _lineStrokeThickness; set { if (Math.Abs(_lineStrokeThickness - value) > 0.001) { _lineStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LineWidget ln) ln.StrokeThickness = value; } } }
        /// <summary>Ellipse 边框粗细。</summary>
        public double EllipseStrokeThickness { get => _ellipseStrokeThickness; set { if (Math.Abs(_ellipseStrokeThickness - value) > 0.001) { _ellipseStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is EllipseWidget w) w.StrokeThickness = value; } } }


        private string _ioFieldContent = "";
        public string IOFieldContent { get => _ioFieldContent; set { if (_ioFieldContent != value) { _ioFieldContent = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.Content = value; } } }
        private bool _ioFieldIsReadOnly = false;
        public bool IOFieldIsReadOnly { get => _ioFieldIsReadOnly; set { if (_ioFieldIsReadOnly != value) { _ioFieldIsReadOnly = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.IsReadOnly = value; } } }
        private string _ioFieldFillColor = "#EEEEEE";

        private string _checkBoxText = "CheckBox";
        public string CheckBoxText { get => _checkBoxText; set { if (_checkBoxText != value) { _checkBoxText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget cb) cb.Text = value; } } }
        private bool _checkBoxIsChecked = false;
        public bool CheckBoxIsChecked { get => _checkBoxIsChecked; set { if (_checkBoxIsChecked != value) { _checkBoxIsChecked = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget cb) cb.IsChecked = value; } } }

        private string _frameTitle = "Frame";   // R-6：同步 FrameWidget 新默认（老工程值由装载覆盖）
        public string FrameTitle { get => _frameTitle; set { if (_frameTitle != value) { _frameTitle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.Title = value; } } }
        private string _frameFillColor = "#EEEEEE";
        public string FrameFillColor { get => _frameFillColor; set { if (_frameFillColor != value) { _frameFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.FillColor = value; } } }
        private string _frameImagePath = "";
        public string FrameImagePath { get => _frameImagePath; set { if (_frameImagePath != value) { _frameImagePath = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.ImagePath = value; } } }

        private bool _frameShowVideo = false;
        /// <summary>P-6（2026-09-02）：Frame 视频模式（checkbox——勾选且有视频源=视频；未勾选或无源=普通 Frame）。</summary>
        public bool FrameShowVideo { get => _frameShowVideo; set { if (_frameShowVideo != value) { _frameShowVideo = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVideoMode)); if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.ShowVideo = value; } } } }

        private string _frameVideoSource = "";
        /// <summary>P-6（2026-09-02）：Frame 视频源（模型字段）。Check ①（2026-09-05）删「视频源」单源输入框 UI——
        /// 新配置源只走 VideoListRef 列表；本字段保留：遗留 P-6 工程加载/老包兼容 + set_property 契约 + 361 同步链需要。</summary>
        public string FrameVideoSource { get => _frameVideoSource; set { if (_frameVideoSource != value) { _frameVideoSource = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.VideoSource = value; } } }

        private string _framePlayTag = "";
        /// <summary>R-4（2026-09-05 用户 Check）：播放控制变量（布尔变量驱动播放/暂停；空=未绑定点击直接控制）。</summary>
        public string FramePlayTag
        {
            get => _framePlayTag;
            set
            {
                if (value == null) return;   // 审查 🟡：ComboBox SelectedValue 失配回写 null 拦截（先例 HistoryTagRowVM.Tag/RobotSlotRowVM.SelectedTag——否则模型 PlayTag 被静默清空）
                if (_framePlayTag != value) { _framePlayTag = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.PlayTag = value; }
            }
        }

        // ── S-4/S-5（2026-09-05 用户拍板）：视频源列表 + 视频源选择索引变量 ──
        private string _frameVideoListRef = "";
        /// <summary>S-5：Frame 视频列表名（Video 型列表——Check 2026-09-05 收紧仅 Video，Text 型收回；空=未选列表。
        /// 注：Check ①删单源 UI——遗留工程 VideoSource 模型保留（老 P-6 工程 FW 兜底播放），面板不再引导单源。
        /// 防御同 FrameListRef 先例（审查 🟡-3：null 拦截 + suppress 失配窗口 + 同值跳过——防哨兵回写清空 f.VideoListRef）。</summary>
        public string FrameVideoListRef
        {
            get => _frameVideoListRef;
            set
            {
                if (value == null) return;
                if (_frameVideoListRef != value)
                {
                    _frameVideoListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is FrameWidget f)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（RefreshListOptions 重建瞬态——防哨兵 "" 真解绑）
                        if (value == f.VideoListRef) return;   // 模型同值跳过
                        if (!_syncingFromModel) BeforeModify?.Invoke();
                        f.VideoListRef = value;
                    }
                }
            }
        }

        private string _frameVideoIndexTag = "";
        /// <summary>S-5：视频源选择变量（索引非负——UINT16/INT32，Check ③收紧去 INT16；变量值取视频源列表对应项；空=未绑定播列表首项）。</summary>
        public string FrameVideoIndexTag
        {
            get => _frameVideoIndexTag;
            set
            {
                if (value == null) return;
                if (_frameVideoIndexTag != value) { _frameVideoIndexTag = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.VideoIndexTag = value; }
            }
        }

        /// <summary>S-5：视频列表名选项（工程 Video 型列表——Check 2026-09-05 收紧仅 Video；选中 Frame 时刷新——首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> VideoListOptions { get; } = new();

        /// <summary>P-6：是否视频模式（勾选显示视频——源来自下方「视频源列表」，Check ①删单源框后不再有独立视频源行）。</summary>
        public bool IsVideoMode => _frameShowVideo;

        private double _progressValue = 0;
        public double ProgressValue { get => _progressValue; set { if (Math.Abs(_progressValue - value) > 0.001) { _progressValue = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Value = value; } } }
        private double _progressMin = 0;
        public double ProgressMin { get => _progressMin; set { if (Math.Abs(_progressMin - value) > 0.001) { _progressMin = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Min = value; } } }
        private double _progressMax = 100;
        public double ProgressMax { get => _progressMax; set { if (Math.Abs(_progressMax - value) > 0.001) { _progressMax = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Max = value; } } }
        private string _progressFillColor = "#3399FF";
        public string ProgressFillColor { get => _progressFillColor; set { if (_progressFillColor != value) { _progressFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.FillColor = value; } } }

        private string _dateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        // ═══ W4 窗口控件属性 ═══
        private string _windowTitle = "用户";
        public string WindowTitle { get => _windowTitle; set { if (_windowTitle != value) { _windowTitle = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.Title = value; } } } }
        private bool _windowShowTitleBar = true;
        public bool WindowShowTitleBar { get => _windowShowTitleBar; set { if (_windowShowTitleBar != value) { _windowShowTitleBar = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowTitleBar = value; } } } }
        private string _windowFillColor = "#EEEEEE";
        public string WindowFillColor { get => _windowFillColor; set { if (_windowFillColor != value) { _windowFillColor = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.FillColor = value; } } } }
        private string _windowBorderColor = "#888888";
        public string WindowBorderColor { get => _windowBorderColor; set { if (_windowBorderColor != value) { _windowBorderColor = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.BorderColor = value; } } } }
        private bool _windowShowUserName = true;
        public bool WindowShowUserName { get => _windowShowUserName; set { if (_windowShowUserName != value) { _windowShowUserName = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowUserName = value; } } } }
        private bool _windowShowRole = true;
        public bool WindowShowRole { get => _windowShowRole; set { if (_windowShowRole != value) { _windowShowRole = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowRole = value; } } } }
        private bool _windowShowMode = true;
        public bool WindowShowMode { get => _windowShowMode; set { if (_windowShowMode != value) { _windowShowMode = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowMode = value; } } } }
        private bool _windowShowHistory;
        public bool WindowShowHistory { get => _windowShowHistory; set { if (_windowShowHistory != value) { _windowShowHistory = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowHistory = value; } } } }
        private AlarmDisplayMode _windowDisplayMode = AlarmDisplayMode.Current;
        /// <summary>AlarmView 显示模式（P-5，2026-09-02：当前报警=0 默认 / 报警缓冲区=1；设计态固定——要两模式放两个 AlarmView）。</summary>
        public AlarmDisplayMode WindowDisplayMode { get => _windowDisplayMode; set { if (_windowDisplayMode != value) { _windowDisplayMode = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.DisplayMode = value; } } } }
        public Array WindowDisplayModeOptions => Enum.GetValues(typeof(AlarmDisplayMode));
        private double _windowCardWidth = 120;
        public double WindowCardWidth { get => _windowCardWidth; set { if (_windowCardWidth != value) { _windowCardWidth = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardWidth = value; } } } }
        private double _windowCardHeight = 110;
        public double WindowCardHeight { get => _windowCardHeight; set { if (_windowCardHeight != value) { _windowCardHeight = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardHeight = value; } } } }
        private bool _windowCardShowNumber = true;
        public bool WindowCardShowNumber { get => _windowCardShowNumber; set { if (_windowCardShowNumber != value) { _windowCardShowNumber = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardShowNumber = value; } } } }
        private bool _windowCardShowStatus = true;
        public bool WindowCardShowStatus { get => _windowCardShowStatus; set { if (_windowCardShowStatus != value) { _windowCardShowStatus = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardShowStatus = value; } } } }
        private bool _windowCardShowLocation = true;
        public bool WindowCardShowLocation { get => _windowCardShowLocation; set { if (_windowCardShowLocation != value) { _windowCardShowLocation = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardShowLocation = value; } } } }
        private string _windowSelectedTag = "";
        public string WindowSelectedTag { get => _windowSelectedTag; set { if (value == null) return; if (_windowSelectedTag != value) { _windowSelectedTag = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.SelectedTag = value; } } } }
        private string _windowBoundDevice = "";
        public string WindowBoundDevice { get => _windowBoundDevice; set { if (value == null) return; var mapped = value == "（仅内部变量）" ? "" : value; if (_windowBoundDevice != mapped) { _windowBoundDevice = mapped; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.BoundDevice = mapped; } RefreshWindowDeviceTags(); } } }

        /// <summary>W4 设备列表（RobotList BoundDevice 两态过滤用）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> WindowDevices { get; } = new();
        // ── Q-7（2026-09-04 用户 Check）：Window 属性组按 windowType 显隐（PropWindow 分组 Visibility 绑）──
        public bool IsAlarmView => _selectedWidget is WindowWidget w && w.Type == WindowType.AlarmView;
        public bool IsUserView => _selectedWidget is WindowWidget w && w.Type == WindowType.UserView;
        public bool IsRobotList => _selectedWidget is WindowWidget w && w.Type == WindowType.RobotList;
        private void NotifyWindowTypeFlags()
        {
            OnPropertyChanged(nameof(IsAlarmView));
            OnPropertyChanged(nameof(IsUserView));
            OnPropertyChanged(nameof(IsRobotList));
        }

        public void RefreshWindowDevices()
        {
            WindowDevices.Clear();
            WindowDevices.Add("（仅内部变量）");
            if (Project != null)
                foreach (var d in Project.Devices) WindowDevices.Add(d.Name);
        }

        /// <summary>W4 RobotList 可用变量（按 BoundDevice 两态过滤：指定设备 → 该设备变量（Tag.DeviceName）+ 内部变量；空 → 仅内部变量）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<Tag> WindowDeviceTags { get; } = new();
        public void RefreshWindowDeviceTags()
        {
            WindowDeviceTags.Clear();
            if (Project == null) return;
            foreach (var t in Project.Tags)
            {
                if (_windowBoundDevice.Length == 0 && string.IsNullOrEmpty(t.DeviceName)) WindowDeviceTags.Add(t);
                else if (_windowBoundDevice.Length > 0 && (t.DeviceName == _windowBoundDevice || string.IsNullOrEmpty(t.DeviceName))) WindowDeviceTags.Add(t);
            }
        }

        /// <summary>W4 RobotSlots 绑定表行集合（5 槽位：编号/状态/位置/详细信息/操作）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<RobotSlotRowVM> RobotSlotRows { get; } = new();
        public void RefreshRobotSlots()
        {
            RobotSlotRows.Clear();
            if (_selectedWidget is not WindowWidget ww) return;
            var labels = new[] { "编号变量", "状态变量", "位置变量", "详细信息JSON", "操作变量" };
            var getters = new Func<RobotSlotBinding, string>[] { b => b.IdTag, b => b.StatusTag, b => b.LocationTag, b => b.DetailTag, b => b.OperTag };
            for (int i = 0; i < 5; i++)
            {
                var b = i < ww.RobotSlots.Count ? ww.RobotSlots[i] : null;   // 无槽位不补模型（行 VM 空值展示）
                RobotSlotRows.Add(new RobotSlotRowVM(this, i, labels[i], b == null ? "" : getters[i](b)));
            }
        }

        /// <summary>RobotSlotRowVM 写回：槽位 i 的绑定变量 → ww.RobotSlots[i] 对应字段。
        /// Q-2（2026-09-04 Check 审查修复）：RobotSlotBinding 为独立 INPC 对象（Widget 订阅链不覆盖）+ List<> 增补无通知
        /// → 必须显式 DirtyRequested（此前仅 BeforeModify 快照，IsDirty 不置 → 关闭无提示改动静默丢失）。</summary>
        public void SetRobotSlotTag(int index, string tag)
        {
            if (_selectedWidget is not WindowWidget ww || index < 0) return;
            BeforeModify?.Invoke();   // 快照先推（补槽/赋值前——撤销可回滚）
            while (ww.RobotSlots.Count <= index) ww.RobotSlots.Add(new RobotSlotBinding());   // 首次配置才补槽位
            var b = ww.RobotSlots[index];
            switch (index)
            {
                case 0: b.IdTag = tag; break;
                case 1: b.StatusTag = tag; break;
                case 2: b.LocationTag = tag; break;
                case 3: b.DetailTag = tag; break;
                default: b.OperTag = tag; break;
            }
            DirtyRequested?.Invoke();   // Q-2：显式标脏（POCO/嵌套对象组纪律——对齐作业点行 OnWorkPointRowChanged）
        }
        public string DateTimeFormat { get => _dateTimeFormat; set { if (_dateTimeFormat != value) { _dateTimeFormat = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is DateTimeWidget dt) { dt.Format = value; } } } }   // 任务 7：改格式不写 Text——DisplayText 绑定分支按 Format 格式化基准值、未绑定分支 FormatNow 实时（两分支自动联动）

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