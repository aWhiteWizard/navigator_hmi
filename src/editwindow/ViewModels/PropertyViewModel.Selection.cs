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
    public partial class PropertyViewModel : INotifyPropertyChanged
    {

#region 原 未分区块（72daf0e^ 第 52-755 行；18 个成员块）
        /// <summary>事件栏：当前选中控件可用事件行（DESIGN-EVENTS.md §4）。</summary>
        public ObservableCollection<EventRowVM> EventRows { get; } = new();
        private bool _eventBarVisible;
        public bool EventBarVisible
        {
            get => _eventBarVisible;
            set { _eventBarVisible = value; OnPropertyChanged(); }
        }

        /// <summary>当前选中的多个控件（框选多选模式，仅 MultiSelect 时非空）。</summary>
        public List<Widget> SelectedWidgets { get; } = new();

        /// <summary>是否多选批量编辑模式（框选 ≥2 个控件）。</summary>
        public bool IsMultiSelect => SelectedWidgets.Count > 1;

        /// <summary>
        /// 设置选中集合（框选收尾调用）。
        /// 1 个 → 走单选逻辑；≥2 个 → MultiSelect 批量编辑模式。
        /// </summary>
        public void SelectWidgets(IEnumerable<Widget> widgets)
        {
            SelectedWidgets.Clear();
            SelectedWidgets.AddRange(widgets);

            if (SelectedWidgets.Count == 1)
            {
                SelectedWidget = SelectedWidgets[0];   // 单选走原逻辑
                NotifySelectionChanged();
                return;
            }
            if (SelectedWidgets.Count > 1)
            {
                // 多选：解订阅并置空单选/画面状态（防状态三元组残留），显示批量编辑面板
                _syncingFromModel = true;
                try
                {
                    if (_selectedWidget != null)
                        _selectedWidget.PropertyChanged -= OnSelectedWidgetPropertyChanged;
                    _selectedWidget = null;
                    _selectedScreen = null;
                    SelectedObjectType = PropertyTargetType.MultiSelect;
                    var first = SelectedWidgets[0];
                    BatchFontFamily = GetProperty<string>(first, "FontFamily") ?? WidgetFontDefaults.FontFamily;
                    BatchFontSize = GetProperty<double?>(first, "FontSize") ?? WidgetFontDefaults.FontSize;
                    BatchFontWeight = GetProperty<string>(first, "FontWeight") ?? "Normal";
                    BatchFontStyle = GetProperty<string>(first, "FontStyle") ?? "Normal";
                    BatchTextDecoration = GetProperty<string>(first, "TextDecoration") ?? "None";
                    BatchTextColor = GetProperty<string>(first, "TextColor") ?? "#000000";
                    BatchFillColor = GetProperty<string>(first, "FillColor") ?? "#EEEEEE";
                }
                finally { _syncingFromModel = false; }
                NotifySelectionChanged();
            }
        }

        /// <summary>退出多选批量编辑（清空选中三元组，面板随 IsPropertyVisible 隐藏）。</summary>
        public void ClearMultiSelection()
        {
            SelectedWidgets.Clear();
            _syncingFromModel = true;
            try
            {
                if (_selectedWidget != null)
                    _selectedWidget.PropertyChanged -= OnSelectedWidgetPropertyChanged;
                _selectedWidget = null;
                _selectedScreen = null;
                SelectedObjectType = PropertyTargetType.None;
            }
            finally { _syncingFromModel = false; }
            NotifySelectionChanged();
        }

        /// <summary>通知选中状态相关的计算属性（XAML Visibility/模板切换依赖）。</summary>
        private void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedWidgets));   // "已选 N 个控件"数量刷新
            OnPropertyChanged(nameof(IsMultiSelect));
            OnPropertyChanged(nameof(IsPropertyVisible));
            OnPropertyChanged(nameof(WidgetTypeName));
            NotifyWindowTypeFlags();   // Q-7：切走/清空/多选路径统一复位（防残留旧值；选中 window 时 case 分支已刷过）
        }

        public Screen? SelectedScreen
        {
            get => _selectedScreen;
            set
            {
                if (_selectedScreen != value)
                {
                    // 对称退订旧 widget 的 PropertyChanged（防泄漏）
                    if (_selectedWidget != null)
                        _selectedWidget.PropertyChanged -= OnSelectedWidgetPropertyChanged;
                    _selectedWidget = null;
                    SelectedWidgets.Clear();   // 选画面时退出多选
                    _selectedScreen = value;
                    if (value != null)
                    {
                        _syncingFromModel = true;
                        try
                        {
                        ScreenName = value.Name;
                        ScreenWidth = value.Width;
                        ScreenHeight = value.Height;
                        WorldMapShowGlobalOverlay = value.Type == ScreenType.WorldMap && Project?.WorldMap?.ShowGlobalOverlay == true;
                        IncludeWorldMapOnDevice = Project?.IncludeWorldMapOnDevice != false;   // P-3：设备端地图开关同步（缺省 true 兼容旧工程）
                        WorldMapViewLocked = Project?.WorldMap?.ViewLocked == true;   // P5：锁定预览勾选同步
                        RefreshWorldMapWorkPoints();   // P4：作业点/作业范围点表格同步
                        }
                        finally { _syncingFromModel = false; }
                    }
                    // 事件栏：世界地图画面显示地图级 onClick 事件（属性面板入口）；普通画面选中时不显示事件栏
                    if (value != null && value.Type == ScreenType.WorldMap) RefreshWorldMapEventBar();
                    else { EventBarVisible = false; EventRows.Clear(); }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsScreenSelected));
                    OnPropertyChanged(nameof(IsWidgetSelected));
                    OnPropertyChanged(nameof(IsPropertyVisible));
                    OnPropertyChanged(nameof(WidgetTypeName));
                    OnPropertyChanged(nameof(IsButtonWidget));
                    OnPropertyChanged(nameof(IsTextWidget));
                    OnPropertyChanged(nameof(IsRectangleWidget));
                    OnPropertyChanged(nameof(IsWorldMapScreen));
                    NotifyWindowTypeFlags();   // Q-7 审查 🟡 S1：选画面时 _selectedWidget 置空 → 分组 flag 复位（闭合「任何选中变化都复位」）
                     SelectedObjectType = value != null ? PropertyTargetType.Screen : PropertyTargetType.None;
                     OnPropertyChanged(nameof(SelectedObjectType));
                }
            }
        }

        /// <summary>当前选中的 Widget，null 表示无选中。</summary>
        public Widget? SelectedWidget
        {
            get => _selectedWidget;
            set
            {
                if (_selectedWidget != value)
                {
                    // 选中控件切换：抑制同步/失配回写窗口——RefreshBindableTags 的 Clear+Add 使 ComboBox
                    // SelectedItem 失配回写 null 可能延迟到 Dispatcher 块外，BoundTag setter 会发 bind_tag 空解绑
                    BeginSuppressBindTagCommands();
                    // 取消订阅旧 widget 的 PropertyChanged
                    if (_selectedWidget != null)
                        _selectedWidget.PropertyChanged -= OnSelectedWidgetPropertyChanged;
                    _selectedScreen = null;
                    SelectedWidgets.Clear();   // 单选时退出多选
                    _selectedWidget = value;
                    // 订阅新 widget 的 PropertyChanged（实时同步 ResizeAdorner 的修改）
                    if (value != null)
                        value.PropertyChanged += OnSelectedWidgetPropertyChanged;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPropertyVisible));
                    OnPropertyChanged(nameof(IsButtonWidget));
                    OnPropertyChanged(nameof(IsTextWidget));
                    OnPropertyChanged(nameof(IsRectangleWidget));
                    OnPropertyChanged(nameof(IsWidgetSelected));
                    OnPropertyChanged(nameof(IsScreenSelected));
                    OnPropertyChanged(nameof(WidgetTypeName));
                    NotifyWindowTypeFlags();   // Q-7：任何控件切换都刷分组 flag（选中 window 时按类型显隐，切走复位 false）
                    RefreshEventBar(value);

                     SelectedObjectType = value switch
                     {
                         ButtonWidget => PropertyTargetType.ButtonWidget,
                         TextWidget => PropertyTargetType.TextWidget,
                         RectangleWidget => PropertyTargetType.RectangleWidget,
                         LabelWidget => PropertyTargetType.LabelWidget,
                         ImageWidget => PropertyTargetType.ImageWidget,
                         NumericDisplayWidget => PropertyTargetType.NumericDisplayWidget,
                         SwitchWidget => PropertyTargetType.SwitchWidget,
                         LineWidget => PropertyTargetType.LineWidget,
                         CircleWidget => PropertyTargetType.CircleWidget,
                         EllipseWidget => PropertyTargetType.EllipseWidget,
                         IOFieldWidget => PropertyTargetType.IOFieldWidget,
                         CheckBoxWidget => PropertyTargetType.CheckBoxWidget,
                         TextListWidget => PropertyTargetType.TextListWidget,
                         FrameWidget => PropertyTargetType.FrameWidget,
                         ProgressBarWidget => PropertyTargetType.ProgressBarWidget,
                         DateTimeWidget => PropertyTargetType.DateTimeWidget,
                         WindowWidget => PropertyTargetType.WindowWidget,
                         PolygonWidget => PropertyTargetType.PolygonWidget,   // 世界地图批 3
                         TrendViewWidget => PropertyTargetType.TrendViewWidget,   // P-4
                         HistoryViewWidget => PropertyTargetType.HistoryViewWidget,   // P-5
                         _ => PropertyTargetType.None
                     };
                     OnPropertyChanged(nameof(SelectedObjectType));

                    // 切换选中控件时，同步 ViewModel 的属性值（同步过程不触发 BeforeModify）
                    if (value != null)
                    {
                        _syncingFromModel = true;
                        try
                        {
                        X = value.X;
                        Y = value.Y;
                        Width = value.Width;
                        Height = value.Height;
                        ObjectName = value.ObjectName;
                        RefreshDuplicateCheck();

                        switch (value)
                        {
                            case ButtonWidget btn: ButtonText = btn.Text; ButtonFontFamily = btn.FontFamily; ButtonFontSize = btn.FontSize; ButtonFontWeight = btn.FontWeight; ButtonFontStyle = btn.FontStyle; ButtonTextDecoration = btn.TextDecoration; ButtonTextColor = btn.TextColor; ButtonFillColor = btn.FillColor; break;
                            case TextWidget txt: TextContent = txt.Content; TextFillColor = txt.FillColor; TextFontSize = txt.FontSize; TextFontWeight = txt.FontWeight; TextFontStyle = txt.FontStyle; TextTextColor = txt.TextColor; TextHAlign = txt.HAlign; TextFontFamily = txt.FontFamily; TextTextDecoration = txt.TextDecoration; break;
                            case RectangleWidget rect: RectFillColor = rect.FillColor; break;
                            case LabelWidget lbl: LabelText = lbl.Text; LabelFontSize = lbl.FontSize; LabelFontWeight = lbl.FontWeight; LabelTextColor = lbl.TextColor; LabelFillColor = lbl.FillColor; LabelFontFamily = lbl.FontFamily; LabelFontStyle = lbl.FontStyle; LabelTextDecoration = lbl.TextDecoration; break;
                            case ImageWidget img: ImagePath = img.ImagePath; ImageFillColor = img.FillColor; ImageListRef = img.ListRef; ImageDefaultIndex = img.DefaultIndex; break;
                            case NumericDisplayWidget nd: NumericFontSize = nd.FontSize; NumericTextColor = nd.TextColor; NumericFillColor = nd.FillColor; NumericValue = nd.Value; NumericFontFamily = nd.FontFamily; NumericFontWeight = nd.FontWeight; NumericFontStyle = nd.FontStyle; NumericTextDecoration = nd.TextDecoration; break;
                            case SwitchWidget sw: SwitchIsOn = sw.IsOn; SwitchOnText = sw.OnText; SwitchOffText = sw.OffText; SwitchFontFamily = sw.FontFamily; SwitchFontSize = sw.FontSize; SwitchFontWeight = sw.FontWeight; SwitchFontStyle = sw.FontStyle; SwitchTextDecoration = sw.TextDecoration; SwitchTextColor = sw.TextColor; SwitchFillColor = sw.FillColor; break;
                            case LineWidget line: LineX2 = Math.Round(line.X2, 3); LineY2 = Math.Round(line.Y2, 3); LineStrokeColor = line.StrokeColor; LineStrokeThickness = line.StrokeThickness; break;   // C12-11：直线端点 3 位小数
                            case CircleWidget c: CircleFillColor = c.FillColor; CircleStrokeColor = c.StrokeColor; CircleStrokeThickness = c.StrokeThickness; break;
                            case EllipseWidget el: EllipseFillColor = el.FillColor; EllipseStrokeColor = el.StrokeColor; EllipseStrokeThickness = el.StrokeThickness; break;
                            case IOFieldWidget io: IOFieldContent = io.Content; IOFieldIsReadOnly = io.IsReadOnly; IOFieldFillColor = io.FillColor; IOFieldTextColor = io.TextColor; IOFieldFontFamily = io.FontFamily; IOFieldFontSize = io.FontSize; IOFieldFontWeight = io.FontWeight; IOFieldFontStyle = io.FontStyle; IOFieldTextDecoration = io.TextDecoration; break;
                            case CheckBoxWidget cb: CheckBoxText = cb.Text; CheckBoxIsChecked = cb.IsChecked; CheckBoxFontFamily = cb.FontFamily; CheckBoxFontSize = cb.FontSize; CheckBoxFontWeight = cb.FontWeight; CheckBoxFontStyle = cb.FontStyle; CheckBoxTextDecoration = cb.TextDecoration; CheckBoxTextColor = cb.TextColor; CheckBoxFillColor = cb.FillColor; break;
                            case TextListWidget tl: TextListFontFamily = tl.FontFamily; TextListFontSize = tl.FontSize; TextListFontWeight = tl.FontWeight; TextListFontStyle = tl.FontStyle; TextListTextDecoration = tl.TextDecoration; TextListTextColor = tl.TextColor; TextListFillColor = tl.FillColor; TextListListRef = tl.ListRef; TextListDefaultIndex = tl.DefaultIndex; break;
                            case FrameWidget f: FrameTitle = f.Title; FrameFillColor = f.FillColor; FrameImagePath = f.ImagePath; FrameFontFamily = f.FontFamily; FrameFontSize = f.FontSize; FrameFontWeight = f.FontWeight; FrameFontStyle = f.FontStyle; FrameTextDecoration = f.TextDecoration; FrameListRef = f.ListRef; FrameDefaultIndex = f.DefaultIndex; FrameShowVideo = f.ShowVideo; FrameVideoSource = f.VideoSource; FramePlayTag = f.PlayTag; FrameVideoListRef = f.VideoListRef; FrameVideoIndexTag = f.VideoIndexTag; break;   // P-6 视频 / R-4 播放控制 / S-5 视频源列表
                            case ProgressBarWidget pb: ProgressValue = pb.Value; ProgressMin = pb.Min; ProgressMax = pb.Max; ProgressFillColor = pb.FillColor; ProgressFillStyle = pb.FillStyle; break;
                case DateTimeWidget dt: DateTimeFormat = dt.Format; break;
                case WindowWidget ww:
                    WindowTitle = ww.Title;
                    WindowShowTitleBar = ww.ShowTitleBar;
                    WindowFillColor = ww.FillColor;
                    WindowBorderColor = ww.BorderColor;
                    WindowShowUserName = ww.ShowUserName; WindowShowRole = ww.ShowRole; WindowShowMode = ww.ShowMode;
                    WindowShowHistory = ww.ShowHistory;
                    WindowDisplayMode = ww.DisplayMode;   // P-5：AlarmView 显示模式
                    WindowCardWidth = ww.CardWidth; WindowCardHeight = ww.CardHeight;
                    WindowCardShowNumber = ww.CardShowNumber; WindowCardShowStatus = ww.CardShowStatus; WindowCardShowLocation = ww.CardShowLocation;
                    WindowSelectedTag = ww.SelectedTag;
                    WindowBoundDevice = ww.BoundDevice;
                    RefreshWindowDevices();
                    RefreshWindowDeviceTags();   // 显式刷（模型默认 BoundDevice="" == VM 默认短路——setter 不触发，首选中不空）
                    RefreshRobotSlots();   // WindowBoundDevice setter 内已刷新变量列表（去重）
                    break;
                case PolygonWidget pg: PolygonFillColor = pg.FillColor; PolygonStrokeColor = pg.StrokeColor; PolygonStrokeThickness = pg.StrokeThickness; break;
                case TrendViewWidget tc: LoadTrendViewProperties(tc); break;   // P-4：属性在 partial 文件（PropertyViewModel.TrendView.cs）
                case HistoryViewWidget hv: LoadHistoryViewProperties(hv); break;   // P-5：属性在 partial 文件（PropertyViewModel.HistoryView.cs）
                        }

                        // 绑定变量（基类通用属性，选中控件时同步下拉 + 刷新变量列表）
                        RefreshBindableTags();
                        BoundTag = ResolveBoundTarget(value.BoundTag);
                        // 列表下拉数据源（Image/Frame/TextList 选中时同步）
                        RefreshListOptions(value);
                        // 无条件通知（切换选中控件时 setter 可能值相等短路——不得依赖其副作用）
                        OnPropertyChanged(nameof(IsValueEditable));
                        RefreshPolygonPointRows();   // P7：多边形顶点可编辑表格刷新
                        RefreshRectanglePointRows();   // V-6b：矩形 4 顶点表格刷新（选中矩形时）
                        }
                        finally { _syncingFromModel = false; }
                    }
                }

            }
        }

        /// <summary>
        /// 当选中 Widget 的属性（如 X/Y/Width/Height）发生改变时，
        /// 同步更新 PropertyViewModel 对应的属性，使属性窗口 UI 实时刷新。
        /// </summary>
        private void OnSelectedWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender != _selectedWidget) return;

            // AI 后台线程（update_list 级联改控件 ListRef）触发时跨线程改 ObservableCollection 会被吞 → 封送回 UI 线程（与 OnTagCommandExecuted 同模式）；无 UI 环境（测试/headless）直接走同步分支
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(
                    new Action(() => OnSelectedWidgetPropertyChanged(sender, e)));
                return;
            }

            // 模型→VM 反向同步（拖拽/缩放每帧触发）：不触发 BeforeModify，防每帧 Push 撤销快照
            _syncingFromModel = true;
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(Widget.X):
                        X = _selectedWidget.X;
                        break;
                    case nameof(PolygonWidget.StrokeColor): PolygonStrokeColor = ((PolygonWidget)_selectedWidget).StrokeColor; break;
                    case nameof(PolygonWidget.StrokeThickness): PolygonStrokeThickness = ((PolygonWidget)_selectedWidget).StrokeThickness; break;
                    case nameof(Widget.BoundTag):
                        // CLI/Undo 等外部改模型 BoundTag → 面板实时同步（不触发命令）
                        _boundTag = ResolveBoundTarget(_selectedWidget.BoundTag);
                        OnPropertyChanged(nameof(BoundTag));
                        OnPropertyChanged(nameof(IsValueEditable));
                       
                        break;
                    case "ListRef":
                        // CLI/Undo 改模型列表绑定 → 面板下拉实时同步（不触发命令）；
                        // 同步后补 Options 占位：外部命令（set_property）改绑的列表名可能不在对应类型 Options（如 Text 类型列表绑 Image 控件）→ 防 SelectedItem 失配显示空
                        switch (_selectedWidget)
                        {
                            case ImageWidget img:
                                ImageListRef = img.ListRef;
                                if (!string.IsNullOrEmpty(img.ListRef) && !ImageListOptions.Contains(img.ListRef)) ImageListOptions.Add(img.ListRef);
                                break;
                            case FrameWidget f:
                                FrameListRef = f.ListRef;
                                if (!string.IsNullOrEmpty(f.ListRef) && !ImageListOptions.Contains(f.ListRef)) ImageListOptions.Add(f.ListRef);
                                break;
                            case TextListWidget tl:
                                TextListListRef = tl.ListRef;
                                if (!string.IsNullOrEmpty(tl.ListRef) && !TextListOptions.Contains(tl.ListRef)) TextListOptions.Add(tl.ListRef);
                                break;
                        }
                        break;
                    case "DefaultIndex":
                        // CLI/Undo 改模型缺省值 → 面板实时同步（不触发命令）
                        switch (_selectedWidget)
                        {
                            case ImageWidget img: ImageDefaultIndex = img.DefaultIndex; break;
                            case FrameWidget f: FrameDefaultIndex = f.DefaultIndex; break;
                            case TextListWidget tl: TextListDefaultIndex = tl.DefaultIndex; break;
                        }
                        break;
                    case "ImagePath":
                        // CLI/Undo/清值改模型路径 → 面板实时同步（Image/Frame 共用属性名，按类型分支）
                        if (_selectedWidget is ImageWidget i) ImagePath = i.ImagePath;
                        else if (_selectedWidget is FrameWidget f) FrameImagePath = f.ImagePath;
                        break;
                    case nameof(Widget.Y):
                    Y = _selectedWidget.Y;
                    break;
                case nameof(Widget.Width):
                    Width = _selectedWidget.Width;
                    break;
                case nameof(Widget.Height):
                    Height = _selectedWidget.Height;
                    break;
                case nameof(Widget.ObjectName):
                    ObjectName = _selectedWidget.ObjectName;
                    // ObjectName 实时变更后刷新重复检测
                    RefreshDuplicateCheck();
                    break;
                case nameof(LineWidget.X2):
                    if (_selectedWidget is LineWidget line1) LineX2 = Math.Round(line1.X2, 3);   // C12-11：3 位小数
                    break;
                case nameof(LineWidget.Y2):
                    if (_selectedWidget is LineWidget line2) LineY2 = Math.Round(line2.Y2, 3);   // C12-11：3 位小数
                    break;
                // W4：WindowWidget 外部改模型 → 面板实时同步（CLI/Undo）
                case nameof(WindowWidget.Title):
                    if (_selectedWidget is WindowWidget wt) WindowTitle = wt.Title;
                    break;
                case nameof(WindowWidget.ShowTitleBar):
                    if (_selectedWidget is WindowWidget wsb) WindowShowTitleBar = wsb.ShowTitleBar;
                    break;
                case nameof(WindowWidget.FillColor):
                    if (_selectedWidget is PolygonWidget pgF) PolygonFillColor = pgF.FillColor;
                    else if (_selectedWidget is WindowWidget wfc) WindowFillColor = wfc.FillColor;
                    break;
                case nameof(WindowWidget.BorderColor):
                    if (_selectedWidget is WindowWidget wbc) WindowBorderColor = wbc.BorderColor;
                    break;
                case nameof(WindowWidget.ShowUserName):
                    if (_selectedWidget is WindowWidget wun) WindowShowUserName = wun.ShowUserName;
                    break;
                case nameof(WindowWidget.ShowRole):
                    if (_selectedWidget is WindowWidget wro) WindowShowRole = wro.ShowRole;
                    break;
                case nameof(WindowWidget.ShowMode):
                    if (_selectedWidget is WindowWidget wmo) WindowShowMode = wmo.ShowMode;
                    break;
                case nameof(WindowWidget.ShowHistory):
                    if (_selectedWidget is WindowWidget wh) WindowShowHistory = wh.ShowHistory;
                    break;
                case nameof(WindowWidget.DisplayMode):
                    if (_selectedWidget is WindowWidget wdm) WindowDisplayMode = wdm.DisplayMode;   // P-5
                    break;
                case nameof(WindowWidget.CardWidth):
                    if (_selectedWidget is WindowWidget wcw) WindowCardWidth = wcw.CardWidth;
                    break;
                case nameof(WindowWidget.CardHeight):
                    if (_selectedWidget is WindowWidget wch) WindowCardHeight = wch.CardHeight;
                    break;
                case nameof(WindowWidget.CardShowNumber):
                    if (_selectedWidget is WindowWidget wcn) WindowCardShowNumber = wcn.CardShowNumber;
                    break;
                case nameof(WindowWidget.CardShowStatus):
                    if (_selectedWidget is WindowWidget wcs) WindowCardShowStatus = wcs.CardShowStatus;
                    break;
                case nameof(WindowWidget.CardShowLocation):
                    if (_selectedWidget is WindowWidget wcl) WindowCardShowLocation = wcl.CardShowLocation;
                    break;
                case nameof(WindowWidget.BoundDevice):
                    if (_selectedWidget is WindowWidget wb) WindowBoundDevice = wb.BoundDevice;
                    break;
                case nameof(WindowWidget.SelectedTag):
                    if (_selectedWidget is WindowWidget ws) WindowSelectedTag = ws.SelectedTag;
                    break;
                case nameof(WindowWidget.Type):
                    // Q-7 审查 🔴 P2：set_property windowType（CLI/AI 运行期改窗口类型）→ 分组 flag 实时刷新
                    NotifyWindowTypeFlags();
                    break;
            }
            }
            finally { _syncingFromModel = false; }
        }
        /// <summary>批量字体族（应用到所有选中控件）。</summary>
        public string BatchFontFamily { get => _batchFontFamily; set { if (_batchFontFamily != value) { _batchFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "FontFamily", value); } } }
        private double _batchFontSize = WidgetFontDefaults.FontSize;
        /// <summary>批量字号（应用到所有选中控件；钳制 1-200）。</summary>
        public double BatchFontSize { get => _batchFontSize; set { if (!double.IsFinite(value)) value = _batchFontSize; value = Math.Clamp(value, 1, 200); if (Math.Abs(_batchFontSize - value) > 0.001) { _batchFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "FontSize", value); } } }
        private string _batchFontWeight = "Normal";
        /// <summary>批量字重（应用到所有选中控件）。</summary>
        public string BatchFontWeight { get => _batchFontWeight; set { if (_batchFontWeight != value) { _batchFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "FontWeight", value); } } }
        private string _batchFontStyle = "Normal";
        /// <summary>批量字型（应用到所有选中控件）。</summary>
        public string BatchFontStyle { get => _batchFontStyle; set { if (_batchFontStyle != value) { _batchFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "FontStyle", value); } } }
        private string _batchTextDecoration = "None";
        /// <summary>批量下划线（应用到所有选中控件）。</summary>
        public string BatchTextDecoration { get => _batchTextDecoration; set { if (_batchTextDecoration != value) { _batchTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "TextDecoration", value); } } }
        private string _batchTextColor = "#000000";
        /// <summary>批量文本色（应用到所有选中控件）。</summary>
        public string BatchTextColor { get => _batchTextColor; set { if (_batchTextColor != value) { _batchTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "TextColor", value); } } }
        private string _batchFillColor = "#EEEEEE";
        /// <summary>批量背景色（应用到所有选中控件）。</summary>
        public string BatchFillColor { get => _batchFillColor; set { if (_batchFillColor != value) { _batchFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (!_syncingFromModel) foreach (var w in SelectedWidgets) ApplyProperty(w, "FillColor", value); } } }

        private PropertyTargetType _selectedObjectType;
        /// <summary>当前选中对象的类型，供 XAML DataTemplate 切换使用。</summary>
        public PropertyTargetType SelectedObjectType
        {
            get => _selectedObjectType;
            private set
            {
                if (_selectedObjectType != value)
                {
                    _selectedObjectType = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

    }
}