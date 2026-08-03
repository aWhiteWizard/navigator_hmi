using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

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
        ProgressBarWidget
    }

    /// <summary>
    /// 属性窗口的 ViewModel，持有当前选中的 Widget 或 Screen 并暴露其属性供绑定。
    /// </summary>
    public class PropertyViewModel : INotifyPropertyChanged
    {
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
            if (result.Success && cmdName is "create_tag" or "update_tag" or "delete_tag" or "create_list" or "update_list" or "delete_list")
            {
                RefreshBindableTags();
                RefreshListOptions(_selectedWidget);
                // 选中控件仍有效时，按模型 BoundTag 重同步下拉选中（变量可能被删/重命名；
                // 同步性质直接赋值，不发 bind_tag 命令——避免 update_tag 改名后的冗余绑定+重复快照）
                if (_selectedWidget != null)
                {
                    var tag = Project?.Tags.FirstOrDefault(t => t.Name == _selectedWidget.BoundTag);
                    _syncingFromModel = true;
                    try { _boundTag = tag ?? NoBindingSentinel; OnPropertyChanged(nameof(BoundTag)); OnPropertyChanged(nameof(IsValueEditable)); }
                    finally { _syncingFromModel = false; }
                }
            }
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
                        }
                        finally { _syncingFromModel = false; }
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsScreenSelected));
                    OnPropertyChanged(nameof(IsWidgetSelected));
                    OnPropertyChanged(nameof(IsPropertyVisible));
                    OnPropertyChanged(nameof(WidgetTypeName));
                    OnPropertyChanged(nameof(IsButtonWidget));
                    OnPropertyChanged(nameof(IsTextWidget));
                    OnPropertyChanged(nameof(IsRectangleWidget));
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
                            case LineWidget line: LineX2 = line.X2; LineY2 = line.Y2; LineStrokeColor = line.StrokeColor; LineStrokeThickness = line.StrokeThickness; break;
                            case CircleWidget c: CircleFillColor = c.FillColor; CircleStrokeColor = c.StrokeColor; CircleStrokeThickness = c.StrokeThickness; break;
                            case EllipseWidget el: EllipseFillColor = el.FillColor; EllipseStrokeColor = el.StrokeColor; EllipseStrokeThickness = el.StrokeThickness; break;
                            case IOFieldWidget io: IOFieldContent = io.Content; IOFieldIsReadOnly = io.IsReadOnly; IOFieldFillColor = io.FillColor; IOFieldTextColor = io.TextColor; IOFieldFontFamily = io.FontFamily; IOFieldFontSize = io.FontSize; IOFieldFontWeight = io.FontWeight; IOFieldFontStyle = io.FontStyle; IOFieldTextDecoration = io.TextDecoration; break;
                            case CheckBoxWidget cb: CheckBoxText = cb.Text; CheckBoxIsChecked = cb.IsChecked; CheckBoxFontFamily = cb.FontFamily; CheckBoxFontSize = cb.FontSize; CheckBoxFontWeight = cb.FontWeight; CheckBoxFontStyle = cb.FontStyle; CheckBoxTextDecoration = cb.TextDecoration; CheckBoxTextColor = cb.TextColor; CheckBoxFillColor = cb.FillColor; break;
                            case TextListWidget tl: TextListFontFamily = tl.FontFamily; TextListFontSize = tl.FontSize; TextListFontWeight = tl.FontWeight; TextListFontStyle = tl.FontStyle; TextListTextDecoration = tl.TextDecoration; TextListTextColor = tl.TextColor; TextListFillColor = tl.FillColor; TextListListRef = tl.ListRef; TextListDefaultIndex = tl.DefaultIndex; break;
                            case FrameWidget f: FrameTitle = f.Title; FrameFillColor = f.FillColor; FrameImagePath = f.ImagePath; FrameFontFamily = f.FontFamily; FrameFontSize = f.FontSize; FrameFontWeight = f.FontWeight; FrameFontStyle = f.FontStyle; FrameTextDecoration = f.TextDecoration; FrameListRef = f.ListRef; FrameDefaultIndex = f.DefaultIndex; break;
                            case ProgressBarWidget pb: ProgressValue = pb.Value; ProgressMin = pb.Min; ProgressMax = pb.Max; ProgressFillColor = pb.FillColor; ProgressFillStyle = pb.FillStyle; break;
                        }

                        // 绑定变量（基类通用属性，选中控件时同步下拉 + 刷新变量列表）
                        RefreshBindableTags();
                        BoundTag = Project?.Tags.FirstOrDefault(t => t.Name == value.BoundTag) ?? NoBindingSentinel;
                        // 列表下拉数据源（Image/Frame/TextList 选中时同步）
                        RefreshListOptions(value);
                        // 无条件通知（切换选中控件时 setter 可能值相等短路——不得依赖其副作用，见 wpf-interactions §12）
                        OnPropertyChanged(nameof(IsValueEditable));
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

            // 模型→VM 反向同步（拖拽/缩放每帧触发）：不触发 BeforeModify，防每帧 Push 撤销快照
            _syncingFromModel = true;
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(Widget.X):
                        X = _selectedWidget.X;
                        break;
                    case nameof(Widget.BoundTag):
                        // CLI/Undo 等外部改模型 BoundTag → 面板实时同步（不触发命令）
                        _boundTag = Project?.Tags.FirstOrDefault(t => t.Name == _selectedWidget.BoundTag) ?? NoBindingSentinel;
                        OnPropertyChanged(nameof(BoundTag));
                        OnPropertyChanged(nameof(IsValueEditable));
                        break;
                    case "ListRef":
                        // CLI/Undo 改模型列表绑定 → 面板下拉实时同步（不触发命令）
                        switch (_selectedWidget)
                        {
                            case ImageWidget img: ImageListRef = img.ListRef; break;
                            case FrameWidget f: FrameListRef = f.ListRef; break;
                            case TextListWidget tl: TextListListRef = tl.ListRef; break;
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
                    if (_selectedWidget is LineWidget line1) LineX2 = line1.X2;
                    break;
                case nameof(LineWidget.Y2):
                    if (_selectedWidget is LineWidget line2) LineY2 = line2.Y2;
                    break;
            }
            }
            finally { _syncingFromModel = false; }
        }


        /// <summary>是否有 Widget 被选中（属性面板可见性控制）。</summary>
        public bool IsPropertyVisible => _selectedWidget != null || _selectedScreen != null || IsMultiSelect;
        /// <summary>当前选中的是画面。</summary>
        public bool IsScreenSelected => _selectedScreen != null;
        /// <summary>当前选中的是控件（单选）。</summary>
        public bool IsWidgetSelected => _selectedWidget != null;
        /// <summary>控件类型名称（选中画面显示"画面"，多选显示"批量编辑"）。</summary>
        public string WidgetTypeName => IsMultiSelect ? "批量编辑" : _selectedScreen != null ? "画面" : _selectedWidget?.GetType().Name ?? "";

        // ═══ 批量编辑属性（多选模式，反射应用到所有选中控件） ═══

        private string _batchFontFamily = WidgetFontDefaults.FontFamily;
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

        // ── 画面属性字段 ──
 
        private string _screenName = "";
        /// <summary>画面名称。</summary>
        public string ScreenName
        {
            get => _screenName;
            set
            {
                if (_screenName != value)
                {
                    _screenName = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedScreen != null) _selectedScreen.Name = value;
                }
            }
        }
 
        private double _screenWidth;
        /// <summary>画面宽度。</summary>
        public double ScreenWidth
        {
            get => _screenWidth;
            set
            {
                var rounded = Math.Round(value, 3);
                if (Math.Abs(_screenWidth - rounded) > 0.0001)
                {
                    _screenWidth = rounded;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedScreen != null)
                    {
                        _selectedScreen.Width = rounded;
                        // 同步刷新钳制上限（画面尺寸变更后立即生效）
                        CanvasWidth = rounded;
                    }
                }
            }
        }
 
        private double _screenHeight;
        /// <summary>画面高度。</summary>
        public double ScreenHeight
        {
            get => _screenHeight;
            set
            {
                var rounded = Math.Round(value, 3);
                if (Math.Abs(_screenHeight - rounded) > 0.0001)
                {
                    _screenHeight = rounded;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke();
                    if (_selectedScreen != null)
                    {
                        _selectedScreen.Height = rounded;
                        CanvasHeight = rounded;
                    }
                }
            }
        }

        // 图片列表下拉相关（ImageListOptions 等）
        public string ScreenTypeText => _selectedScreen?.Type switch
        {
            ScreenType.Template => "全局画面",
            ScreenType.WorldMap => "地图画面",
            ScreenType.Custom => "自定义画面",
            _ => ""
        };

        /// <summary>画面名称是否只读（Template 和 WorldMap 不可修改名称）。</summary>
        public bool IsScreenNameReadOnly => _selectedScreen?.Type switch
        {
            ScreenType.Template => true,
            ScreenType.WorldMap => true,
            ScreenType.Custom => false,
            _ => true
        };

        // ── 控件绑定的属性字段 ──

        /// <summary>画布尺寸（由 EditWindow.LoadCanvas 注入，用于属性面板坐标/尺寸钳制）。</summary>
        public double CanvasWidth { get; set; } = double.MaxValue;
        /// <summary>用户修改模型前的回调（EditWindow 注入 → 撤销快照；选中同步初始化不触发）。</summary>
        public Action? BeforeModify { get; set; }
        /// <summary>选中控件/画面时同步 VM 属性的标志（该过程不触发 BeforeModify，防误 Push 快照）。</summary>
        private bool _syncingFromModel;
        /// <summary>画布尺寸（由 EditWindow.LoadCanvas 注入）。</summary>
        public double CanvasHeight { get; set; } = double.MaxValue;

        private double _x;
        public double X
        {
            get => _x;
            set
            {
                if (_selectedWidget != null)
                {
                    if (!double.IsFinite(value)) value = _x;   // 防 NaN/Infinity
                    value = Math.Max(0, Math.Min(value, CanvasWidth - _selectedWidget.Width));   // 0 ≤ X ≤ 画布宽-控件宽
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
            get => _y;
            set
            {
                if (_selectedWidget != null)
                {
                    if (!double.IsFinite(value)) value = _y;
                    value = Math.Max(0, Math.Min(value, CanvasHeight - _selectedWidget.Height));
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
        private string _textFillColor = "#EEEEEE";
        /// <summary>Text 背景色（CSS 格式）。</summary>
        public string TextFillColor { get => _textFillColor; set { if (_textFillColor != value) { _textFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.FillColor = value; } } }
        private double _textFontSize = 14;
        /// <summary>Text 字体大小。</summary>
        public double TextFontSize { get => _textFontSize; set { if (Math.Abs(_textFontSize - value) > 0.001) { _textFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.FontSize = value; } } }
        private string _textFontWeight = "Normal";
        /// <summary>Text 字重。</summary>
        public string TextFontWeight { get => _textFontWeight; set { if (_textFontWeight != value) { _textFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.FontWeight = value; } } }
        private string _textTextColor = "#000000";
        /// <summary>Text 文本色。</summary>
        public string TextTextColor { get => _textTextColor; set { if (_textTextColor != value) { _textTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.TextColor = value; } } }
        private string _textHAlign = "Left";
        /// <summary>Text 水平对齐。</summary>
        public string TextHAlign { get => _textHAlign; set { if (_textHAlign != value) { _textHAlign = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.HAlign = value; } } }

        private string _rectFillColor = "#EEEEEE";
        public string RectFillColor
        {
            get => _rectFillColor;
            set { if (_rectFillColor != value) { _rectFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is RectangleWidget rect) rect.FillColor = value; } }
        }

        private string _labelText = "";
        #region 绑定变量

        /// <summary>「无绑定」哨兵项（非 null，Name=""）。用哨兵替代 null 作下拉首项：
        /// WPF ComboBox 的 ItemsSource 含 null 项时，点击 null 项可能不触发 SelectedItem 回写
        /// （null 与"未选中"状态无法区分）→ 解绑无效果。哨兵是真实对象，点击必然触发绑定。</summary>
        private static readonly Tag NoBindingSentinel = new() { Name = "" };

        /// <summary>绑定下拉数据源：第一项哨兵 = 无绑定，其后为按控件类型过滤的工程变量。</summary>
        public System.Collections.ObjectModel.ObservableCollection<Tag> BindableTags { get; } = new();

        /// <summary>重建绑定下拉（哨兵 + 按控件类型过滤的工程变量），选中控件时调用。</summary>
        public void RefreshBindableTags()
        {
            BindableTags.Clear();
            BindableTags.Add(NoBindingSentinel);   // 无绑定哨兵（非 null）
            if (Project == null) return;
            // 类型过滤：数值/索引控件只显示数字变量（BOOL/INT16/UINT16/INT32/FLOAT），其他不限
            var req = _selectedWidget != null ? TagCompatibility.GetRequirement(_selectedWidget) : TagRequirement.Any;
            var current = _selectedWidget?.BoundTag ?? "";
            bool currentIncluded = false;
            foreach (var t in Project.Tags)
            {
                bool ok = req switch
                {
                    TagRequirement.Numeric => TagCompatibility.IsNumericCompatible(t.DataType),
                    _ => true,
                };
                if (ok)
                {
                    BindableTags.Add(t);
                    if (t.Name == current) currentIncluded = true;
                }
            }
            // 当前绑定变量被类型过滤掉（历史/CLI 不兼容绑定）：保留该项
            // → SelectedItem 不失配 → 不回写 null → 不静默解绑（wpf-combobox-style §3 场景 B 防护）
            if (!currentIncluded && current.Length > 0)
            {
                var cur = Project.Tags.FirstOrDefault(t => t.Name == current);
                if (cur != null) BindableTags.Add(cur);
            }
        }

        /// <summary>绑定变量后设计态 value 字段（NumericValue/IOFieldContent/ProgressValue 等）不可编辑（只显示变量值）。</summary>
        /// <summary>设计态值字段（NumericValue/IOFieldContent/ImagePath/FrameImagePath/ProgressValue）是否可编辑/显示：
        /// 绑变量（BoundTag）或绑列表（Image/Frame 的 ListRef）后由变量/列表控制，隐藏不显示。统一读模型（与切换同步路径同源）。</summary>
        public bool IsValueEditable
        {
            get
            {
                if (_selectedWidget == null) return true;
                if (!string.IsNullOrEmpty(_selectedWidget.BoundTag)) return false;
                return _selectedWidget switch
                {
                    ImageWidget img => string.IsNullOrEmpty(img.ListRef),
                    FrameWidget f => string.IsNullOrEmpty(f.ListRef),
                    _ => true,
                };
            }
        }

        private Tag? _boundTag;

        /// <summary>选中控件绑定的变量（哨兵 = 未绑定）；变更走 CommandService.bind_tag（空 tag_name = 解绑）。</summary>
        public Tag? BoundTag
        {
            get => _boundTag;
            set
            {
                if (_boundTag == value) return;
                _boundTag = value ?? NoBindingSentinel;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValueEditable));   // 绑定状态 → 设计态 value 字段可编辑性
                if (!_syncingFromModel && _selectedWidget != null && CurrentScreen != null && CommandService != null)
                {
                    BeforeModify?.Invoke();
                    // 哨兵/空名 → 空 tag_name = 解绑
                    var tagName = (value == null || string.IsNullOrEmpty(value.Name)) ? "" : value.Name;
                    var result = CommandService.Execute("bind_tag", new Dictionary<string, object?>
                    {
                        ["screen_name"] = CurrentScreen.Name,
                        ["widget_name"] = _selectedWidget.ObjectName,
                        ["tag_name"] = tagName,
                    });
                    // 失败（如变量已被删）：回滚 UI 选中态并提示，防静默不一致
                    if (!result.Success)
                    {
                        _boundTag = Project?.Tags.FirstOrDefault(t => t.Name == _selectedWidget.BoundTag) ?? NoBindingSentinel;
                        OnPropertyChanged(nameof(BoundTag));
                        OnPropertyChanged(nameof(IsValueEditable));
                        System.Windows.MessageBox.Show(result.ErrorMessage ?? "绑定变量失败", "绑定",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
        }
        #endregion

        public string LabelText { get => _labelText; set { if (_labelText != value) { _labelText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.Text = value; } } }

        private double _labelFontSize = 14;
        public double LabelFontSize { get => _labelFontSize; set { if (Math.Abs(_labelFontSize - value) > 0.001) { _labelFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.FontSize = value; } } }

        private string _labelTextColor = "#000000";
        public string LabelTextColor { get => _labelTextColor; set { if (_labelTextColor != value) { _labelTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.TextColor = value; } } }
        private string _labelFillColor = "#EEEEEE";
        /// <summary>Label 背景色（CSS 格式）。</summary>
        public string LabelFillColor { get => _labelFillColor; set { if (_labelFillColor != value) { _labelFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.FillColor = value; } } }

        private string _imagePath = "";
        public string ImagePath { get => _imagePath; set { if (_imagePath != value) { _imagePath = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.ImagePath = value; } } }
        private string _imageFillColor = "#EEEEEE";
        /// <summary>图片背景色（CSS 格式，默认浅灰；图片透明区域/无图片时可见）。</summary>
        public string ImageFillColor { get => _imageFillColor; set { if (_imageFillColor != value) { _imageFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.FillColor = value; } } }

        private string _buttonFontFamily = "Microsoft YaHei UI";
        /// <summary>ButtonWidget 字体族。</summary>
        public string ButtonFontFamily { get => _buttonFontFamily; set { if (_buttonFontFamily != value) { _buttonFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.FontFamily = value; } } }
        private double _buttonFontSize = 14;
        /// <summary>ButtonWidget 字体大小。</summary>
        public double ButtonFontSize { get => _buttonFontSize; set { if (Math.Abs(_buttonFontSize - value) > 0.001) { _buttonFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.FontSize = value; } } }
        private string _buttonFontWeight = "Normal";
        /// <summary>ButtonWidget 字重。</summary>
        public string ButtonFontWeight { get => _buttonFontWeight; set { if (_buttonFontWeight != value) { _buttonFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.FontWeight = value; } } }
        private string _buttonFontStyle = "Normal";
        /// <summary>ButtonWidget 字型。</summary>
        public string ButtonFontStyle { get => _buttonFontStyle; set { if (_buttonFontStyle != value) { _buttonFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.FontStyle = value; } } }

        private string _buttonTextDecoration = "None";
        /// <summary>ButtonWidget 下划线。</summary>
        public string ButtonTextDecoration { get => _buttonTextDecoration; set { if (_buttonTextDecoration != value) { _buttonTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.TextDecoration = value; } } }

        private string _textFontFamily = "Microsoft YaHei UI";
        /// <summary>TextWidget 字体族。</summary>
        public string TextFontFamily { get => _textFontFamily; set { if (_textFontFamily != value) { _textFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget w) w.FontFamily = value; } } }
        private string _textFontStyle = "Normal";
        /// <summary>TextWidget 字型。</summary>
        public string TextFontStyle { get => _textFontStyle; set { if (_textFontStyle != value) { _textFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget w) w.FontStyle = value; } } }

        private string _textTextDecoration = "None";
        /// <summary>TextWidget 下划线。</summary>
        public string TextTextDecoration { get => _textTextDecoration; set { if (_textTextDecoration != value) { _textTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget w) w.TextDecoration = value; } } }

        private string _labelFontFamily = "Microsoft YaHei UI";
        /// <summary>LabelWidget 字体族。</summary>
        public string LabelFontFamily { get => _labelFontFamily; set { if (_labelFontFamily != value) { _labelFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget w) w.FontFamily = value; } } }
        private string _labelFontWeight = "Normal";
        /// <summary>LabelWidget 字重。</summary>
        public string LabelFontWeight { get => _labelFontWeight; set { if (_labelFontWeight != value) { _labelFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget w) w.FontWeight = value; } } }
        private string _labelFontStyle = "Normal";
        /// <summary>LabelWidget 字型。</summary>
        public string LabelFontStyle { get => _labelFontStyle; set { if (_labelFontStyle != value) { _labelFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget w) w.FontStyle = value; } } }

        private string _labelTextDecoration = "None";
        /// <summary>LabelWidget 下划线。</summary>
        public string LabelTextDecoration { get => _labelTextDecoration; set { if (_labelTextDecoration != value) { _labelTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget w) w.TextDecoration = value; } } }

        private string _switchFontFamily = "Microsoft YaHei UI";
        /// <summary>SwitchWidget 字体族。</summary>
        public string SwitchFontFamily { get => _switchFontFamily; set { if (_switchFontFamily != value) { _switchFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.FontFamily = value; } } }
        private double _switchFontSize = 14;
        /// <summary>SwitchWidget 字体大小。</summary>
        public double SwitchFontSize { get => _switchFontSize; set { if (Math.Abs(_switchFontSize - value) > 0.001) { _switchFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.FontSize = value; } } }
        private string _switchFontWeight = "Normal";
        /// <summary>SwitchWidget 字重。</summary>
        public string SwitchFontWeight { get => _switchFontWeight; set { if (_switchFontWeight != value) { _switchFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.FontWeight = value; } } }
        private string _switchFontStyle = "Normal";
        /// <summary>SwitchWidget 字型。</summary>
        public string SwitchFontStyle { get => _switchFontStyle; set { if (_switchFontStyle != value) { _switchFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.FontStyle = value; } } }

        private string _switchTextDecoration = "None";
        /// <summary>SwitchWidget 下划线。</summary>
        public string SwitchTextDecoration { get => _switchTextDecoration; set { if (_switchTextDecoration != value) { _switchTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.TextDecoration = value; } } }

        private string _checkBoxFontFamily = "Microsoft YaHei UI";
        /// <summary>CheckBoxWidget 字体族。</summary>
        public string CheckBoxFontFamily { get => _checkBoxFontFamily; set { if (_checkBoxFontFamily != value) { _checkBoxFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.FontFamily = value; } } }
        private double _checkBoxFontSize = 14;
        /// <summary>CheckBoxWidget 字体大小。</summary>
        public double CheckBoxFontSize { get => _checkBoxFontSize; set { if (Math.Abs(_checkBoxFontSize - value) > 0.001) { _checkBoxFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.FontSize = value; } } }
        private string _checkBoxFontWeight = "Normal";
        /// <summary>CheckBoxWidget 字重。</summary>
        public string CheckBoxFontWeight { get => _checkBoxFontWeight; set { if (_checkBoxFontWeight != value) { _checkBoxFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.FontWeight = value; } } }
        private string _checkBoxFontStyle = "Normal";
        /// <summary>CheckBoxWidget 字型。</summary>
        public string CheckBoxFontStyle { get => _checkBoxFontStyle; set { if (_checkBoxFontStyle != value) { _checkBoxFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.FontStyle = value; } } }

        private string _checkBoxTextDecoration = "None";
        /// <summary>CheckBoxWidget 下划线。</summary>
        public string CheckBoxTextDecoration { get => _checkBoxTextDecoration; set { if (_checkBoxTextDecoration != value) { _checkBoxTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.TextDecoration = value; } } }

        private string _ioFieldFontFamily = "Microsoft YaHei UI";
        /// <summary>IOFieldWidget 字体族。</summary>
        public string IOFieldFontFamily { get => _ioFieldFontFamily; set { if (_ioFieldFontFamily != value) { _ioFieldFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget w) w.FontFamily = value; } } }
        private double _ioFieldFontSize = 14;
        /// <summary>IOFieldWidget 字体大小。</summary>
        public double IOFieldFontSize { get => _ioFieldFontSize; set { if (Math.Abs(_ioFieldFontSize - value) > 0.001) { _ioFieldFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget w) w.FontSize = value; } } }
        private string _ioFieldFontWeight = "Normal";
        /// <summary>IOFieldWidget 字重。</summary>
        public string IOFieldFontWeight { get => _ioFieldFontWeight; set { if (_ioFieldFontWeight != value) { _ioFieldFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget w) w.FontWeight = value; } } }
        private string _ioFieldFontStyle = "Normal";
        /// <summary>IOFieldWidget 字型。</summary>
        public string IOFieldFontStyle { get => _ioFieldFontStyle; set { if (_ioFieldFontStyle != value) { _ioFieldFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget w) w.FontStyle = value; } } }

        private string _ioFieldTextDecoration = "None";
        /// <summary>IOFieldWidget 下划线。</summary>
        public string IOFieldTextDecoration { get => _ioFieldTextDecoration; set { if (_ioFieldTextDecoration != value) { _ioFieldTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget w) w.TextDecoration = value; } } }

        private string _textListFontFamily = "Microsoft YaHei UI";
        /// <summary>TextListWidget 字体族。</summary>
        public string TextListFontFamily { get => _textListFontFamily; set { if (_textListFontFamily != value) { _textListFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.FontFamily = value; } } }
        private double _textListFontSize = 14;
        /// <summary>TextListWidget 字体大小。</summary>
        public double TextListFontSize { get => _textListFontSize; set { if (Math.Abs(_textListFontSize - value) > 0.001) { _textListFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.FontSize = value; } } }
        private string _textListFontWeight = "Normal";
        /// <summary>TextListWidget 字重。</summary>
        public string TextListFontWeight { get => _textListFontWeight; set { if (_textListFontWeight != value) { _textListFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.FontWeight = value; } } }
        private string _textListFontStyle = "Normal";
        /// <summary>TextListWidget 字型。</summary>
        public string TextListFontStyle { get => _textListFontStyle; set { if (_textListFontStyle != value) { _textListFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.FontStyle = value; } } }

        private string _textListTextDecoration = "None";
        /// <summary>TextListWidget 下划线。</summary>
        public string TextListTextDecoration { get => _textListTextDecoration; set { if (_textListTextDecoration != value) { _textListTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.TextDecoration = value; } } }

        private string _textListTextColor = "#000000";
        /// <summary>TextList 文本色。</summary>
        public string TextListTextColor { get => _textListTextColor; set { if (_textListTextColor != value) { _textListTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.TextColor = value; } } }
        private string _textListFillColor = "#EEEEEE";
        /// <summary>TextList 背景色。</summary>
        public string TextListFillColor { get => _textListFillColor; set { if (_textListFillColor != value) { _textListFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget w) w.FillColor = value; } } }

        // ── 列表绑定（Image/Frame/TextList 共用模式：ListRef 下拉 + DefaultIndex 缺省值） ──

        /// <summary>图片列表集合（选中 Image/Frame 时的下拉数据源，首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> ImageListOptions { get; } = new();

        /// <summary>文本列表集合（选中 TextList 时的下拉数据源，首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> TextListOptions { get; } = new();

        /// <summary>重建列表下拉（首项空串=不绑列表 + 工程对应类型列表名），选中控件时调用。</summary>
        private void RefreshListOptions(Widget? w)
        {
            ImageListOptions.Clear();
            ImageListOptions.Add("");
            TextListOptions.Clear();
            TextListOptions.Add("");
            if (Project == null) return;
            foreach (var l in Project.Lists)
            {
                if (l.Type == ListType.Image) ImageListOptions.Add(l.Name);
                else TextListOptions.Add(l.Name);
            }
        }

        private string? _imageListRef = "";
        /// <summary>ImageWidget 绑定的图片列表名（null=无）。</summary>
        public string? ImageListRef { get => _imageListRef; set { if (_imageListRef != value) { _imageListRef = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsValueEditable)); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) { img.ListRef = value ?? ""; if (!string.IsNullOrEmpty(value)) WidgetDesignValue.Clear(img); } } } }   // Clear 在同步路径也执行是有意兜底：CLI 纯模型改 ListRef 也需清路径
        private int _imageDefaultIndex = 0;
        /// <summary>ImageWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int ImageDefaultIndex { get => _imageDefaultIndex; set { var v = Math.Max(0, value); if (_imageDefaultIndex != v) { _imageDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.DefaultIndex = v; } } }

        private string? _frameListRef = "";
        /// <summary>FrameWidget 绑定的图片列表名（null=无）。</summary>
        public string? FrameListRef { get => _frameListRef; set { if (_frameListRef != value) { _frameListRef = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsValueEditable)); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) { f.ListRef = value ?? ""; if (!string.IsNullOrEmpty(value)) WidgetDesignValue.Clear(f); } } } }   // Clear 在同步路径也执行是有意兜底：CLI 纯模型改 ListRef 也需清路径
        private int _frameDefaultIndex = 0;
        /// <summary>FrameWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int FrameDefaultIndex { get => _frameDefaultIndex; set { var v = Math.Max(0, value); if (_frameDefaultIndex != v) { _frameDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.DefaultIndex = v; } } }

        private string? _textListListRef = "";
        /// <summary>TextListWidget 绑定的文本列表名（null=无）。</summary>
        public string? TextListListRef { get => _textListListRef; set { if (_textListListRef != value) { _textListListRef = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget tl) tl.ListRef = value ?? ""; } } }
        private int _textListDefaultIndex = 0;
        /// <summary>TextListWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int TextListDefaultIndex { get => _textListDefaultIndex; set { var v = Math.Max(0, value); if (_textListDefaultIndex != v) { _textListDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget tl) tl.DefaultIndex = v; } } }

        private string _buttonTextColor = "#000000";
        /// <summary>Button 文本色。</summary>
        public string ButtonTextColor { get => _buttonTextColor; set { if (_buttonTextColor != value) { _buttonTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.TextColor = value; } } }
        private string _buttonFillColor = "#EEEEEE";
        /// <summary>Button 背景色。</summary>
        public string ButtonFillColor { get => _buttonFillColor; set { if (_buttonFillColor != value) { _buttonFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget w) w.FillColor = value; } } }

        private string _switchTextColor = "#000000";
        /// <summary>Switch 文本色。</summary>
        public string SwitchTextColor { get => _switchTextColor; set { if (_switchTextColor != value) { _switchTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.TextColor = value; } } }
        private string _switchFillColor = "#EEEEEE";
        /// <summary>Switch 背景色。</summary>
        public string SwitchFillColor { get => _switchFillColor; set { if (_switchFillColor != value) { _switchFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget w) w.FillColor = value; } } }

        private string _checkBoxTextColor = "#000000";
        /// <summary>CheckBox 文本色。</summary>
        public string CheckBoxTextColor { get => _checkBoxTextColor; set { if (_checkBoxTextColor != value) { _checkBoxTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.TextColor = value; } } }
        private string _checkBoxFillColor = "#EEEEEE";
        /// <summary>CheckBox 背景色。</summary>
        public string CheckBoxFillColor { get => _checkBoxFillColor; set { if (_checkBoxFillColor != value) { _checkBoxFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget w) w.FillColor = value; } } }


        private string _numericFontFamily = "Microsoft YaHei UI";
        /// <summary>NumericDisplayWidget 字体族。</summary>
        public string NumericFontFamily { get => _numericFontFamily; set { if (_numericFontFamily != value) { _numericFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget w) w.FontFamily = value; } } }
        private string _numericFontWeight = "Normal";
        /// <summary>NumericDisplayWidget 字重。</summary>
        public string NumericFontWeight { get => _numericFontWeight; set { if (_numericFontWeight != value) { _numericFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget w) w.FontWeight = value; } } }
        private string _numericFontStyle = "Normal";
        /// <summary>NumericDisplayWidget 字型。</summary>
        public string NumericFontStyle { get => _numericFontStyle; set { if (_numericFontStyle != value) { _numericFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget w) w.FontStyle = value; } } }

        private string _numericTextDecoration = "None";
        /// <summary>NumericDisplayWidget 下划线。</summary>
        public string NumericTextDecoration { get => _numericTextDecoration; set { if (_numericTextDecoration != value) { _numericTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget w) w.TextDecoration = value; } } }

        private string _frameFontFamily = "Microsoft YaHei UI";
        /// <summary>FrameWidget 字体族。</summary>
        public string FrameFontFamily { get => _frameFontFamily; set { if (_frameFontFamily != value) { _frameFontFamily = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget w) w.FontFamily = value; } } }
        private double _frameFontSize = 14;
        /// <summary>FrameWidget 字体大小。</summary>
        public double FrameFontSize { get => _frameFontSize; set { if (Math.Abs(_frameFontSize - value) > 0.001) { _frameFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget w) w.FontSize = value; } } }
        private string _frameFontWeight = "Normal";
        /// <summary>FrameWidget 字重。</summary>
        public string FrameFontWeight { get => _frameFontWeight; set { if (_frameFontWeight != value) { _frameFontWeight = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget w) w.FontWeight = value; } } }
        private string _frameFontStyle = "Normal";
        /// <summary>FrameWidget 字型。</summary>
        public string FrameFontStyle { get => _frameFontStyle; set { if (_frameFontStyle != value) { _frameFontStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget w) w.FontStyle = value; } } }

        private string _frameTextDecoration = "None";
        /// <summary>FrameWidget 下划线。</summary>
        public string FrameTextDecoration { get => _frameTextDecoration; set { if (_frameTextDecoration != value) { _frameTextDecoration = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget w) w.TextDecoration = value; } } }

        private double _numericFontSize = 16;
        public double NumericFontSize { get => _numericFontSize; set { if (Math.Abs(_numericFontSize - value) > 0.001) { _numericFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget nd) nd.FontSize = value; } } }
        private string _numericTextColor = "#000000";
        public string NumericTextColor { get => _numericTextColor; set { if (_numericTextColor != value) { _numericTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget nd) nd.TextColor = value; } } }
        private string _numericFillColor = "#EEEEEE";
        /// <summary>NumericDisplay 背景色（CSS 格式）。</summary>
        public string NumericFillColor { get => _numericFillColor; set { if (_numericFillColor != value) { _numericFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget nd) nd.FillColor = value; } } }
        private double _numericValue = 0;
        /// <summary>NumericDisplay 设计态数值预览。</summary>
        public double NumericValue { get => _numericValue; set { if (Math.Abs(_numericValue - value) > 0.001) { _numericValue = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget nd) nd.Value = value; } } }

        private bool _switchIsOn = false;
        public bool SwitchIsOn { get => _switchIsOn; set { if (_switchIsOn != value) { _switchIsOn = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.IsOn = value; } } }
        private string _switchOnText = "ON";
        public string SwitchOnText { get => _switchOnText; set { if (_switchOnText != value) { _switchOnText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.OnText = value; } } }
        private string _switchOffText = "OFF";
        public string SwitchOffText { get => _switchOffText; set { if (_switchOffText != value) { _switchOffText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.OffText = value; } } }

        private double _lineX2 = 100;
        public double LineX2 { get => _lineX2; set { if (!double.IsFinite(value)) value = _lineX2; if (_selectedWidget is LineWidget ln) value = Math.Min(Math.Max(0, value), ln.Width); if (Math.Abs(_lineX2 - value) > 0.001) { if (!_syncingFromModel) BeforeModify?.Invoke(); _lineX2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln2) ln2.X2 = value; } } }
        private double _lineY2 = 0;
        public double LineY2 { get => _lineY2; set { if (!double.IsFinite(value)) value = _lineY2; if (_selectedWidget is LineWidget ln) value = Math.Min(Math.Max(0, value), ln.Height); if (Math.Abs(_lineY2 - value) > 0.001) { if (!_syncingFromModel) BeforeModify?.Invoke(); _lineY2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln3) ln3.Y2 = value; } } }
        private string _lineStrokeColor = "#000000";
        public string LineStrokeColor { get => _lineStrokeColor; set { if (_lineStrokeColor != value) { _lineStrokeColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LineWidget ln) ln.StrokeColor = value; } } }
        private double _lineStrokeThickness = 1;
        public double LineStrokeThickness { get => _lineStrokeThickness; set { if (Math.Abs(_lineStrokeThickness - value) > 0.001) { _lineStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LineWidget ln) ln.StrokeThickness = value; } } }

        private string _circleFillColor = "#EEEEEE";
        public string CircleFillColor { get => _circleFillColor; set { if (_circleFillColor != value) { _circleFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CircleWidget c) c.FillColor = value; } } }
        private string _circleStrokeColor = "#000000";
        public string CircleStrokeColor { get => _circleStrokeColor; set { if (_circleStrokeColor != value) { _circleStrokeColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CircleWidget c) c.StrokeColor = value; } } }
        private double _circleStrokeThickness = 1;
        public double CircleStrokeThickness { get => _circleStrokeThickness; set { if (Math.Abs(_circleStrokeThickness - value) > 0.001) { _circleStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CircleWidget c) c.StrokeThickness = value; } } }

        private string _ellipseFillColor = "#EEEEEE";
        /// <summary>Ellipse 填充色。</summary>
        public string EllipseFillColor { get => _ellipseFillColor; set { if (_ellipseFillColor != value) { _ellipseFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is EllipseWidget w) w.FillColor = value; } } }
        private string _ellipseStrokeColor = "#000000";
        /// <summary>Ellipse 边框色。</summary>
        public string EllipseStrokeColor { get => _ellipseStrokeColor; set { if (_ellipseStrokeColor != value) { _ellipseStrokeColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is EllipseWidget w) w.StrokeColor = value; } } }
        private double _ellipseStrokeThickness = 1;
        /// <summary>Ellipse 边框粗细。</summary>
        public double EllipseStrokeThickness { get => _ellipseStrokeThickness; set { if (Math.Abs(_ellipseStrokeThickness - value) > 0.001) { _ellipseStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is EllipseWidget w) w.StrokeThickness = value; } } }


        private string _ioFieldContent = "";
        public string IOFieldContent { get => _ioFieldContent; set { if (_ioFieldContent != value) { _ioFieldContent = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.Content = value; } } }
        private bool _ioFieldIsReadOnly = false;
        public bool IOFieldIsReadOnly { get => _ioFieldIsReadOnly; set { if (_ioFieldIsReadOnly != value) { _ioFieldIsReadOnly = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.IsReadOnly = value; } } }
        private string _ioFieldFillColor = "#EEEEEE";
        /// <summary>IOField 背景色（CSS 格式）。</summary>
        public string IOFieldFillColor { get => _ioFieldFillColor; set { if (_ioFieldFillColor != value) { _ioFieldFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.FillColor = value; } } }
        private string _ioFieldTextColor = "#000000";
        /// <summary>IOField 文本色。</summary>
        public string IOFieldTextColor { get => _ioFieldTextColor; set { if (_ioFieldTextColor != value) { _ioFieldTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.TextColor = value; } } }

        private string _checkBoxText = "CheckBox";
        public string CheckBoxText { get => _checkBoxText; set { if (_checkBoxText != value) { _checkBoxText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget cb) cb.Text = value; } } }
        private bool _checkBoxIsChecked = false;
        public bool CheckBoxIsChecked { get => _checkBoxIsChecked; set { if (_checkBoxIsChecked != value) { _checkBoxIsChecked = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget cb) cb.IsChecked = value; } } }

        private string _frameTitle = "Group";
        public string FrameTitle { get => _frameTitle; set { if (_frameTitle != value) { _frameTitle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.Title = value; } } }
        private string _frameFillColor = "#EEEEEE";
        public string FrameFillColor { get => _frameFillColor; set { if (_frameFillColor != value) { _frameFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.FillColor = value; } } }
        private string _frameImagePath = "";
        public string FrameImagePath { get => _frameImagePath; set { if (_frameImagePath != value) { _frameImagePath = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.ImagePath = value; } } }

        private double _progressValue = 0;
        public double ProgressValue { get => _progressValue; set { if (Math.Abs(_progressValue - value) > 0.001) { _progressValue = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Value = value; } } }
        private double _progressMin = 0;
        public double ProgressMin { get => _progressMin; set { if (Math.Abs(_progressMin - value) > 0.001) { _progressMin = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Min = value; } } }
        private double _progressMax = 100;
        public double ProgressMax { get => _progressMax; set { if (Math.Abs(_progressMax - value) > 0.001) { _progressMax = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Max = value; } } }
        private string _progressFillColor = "#3399FF";
        public string ProgressFillColor { get => _progressFillColor; set { if (_progressFillColor != value) { _progressFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.FillColor = value; } } }

        private string _progressFillStyle = "Solid";
        /// <summary>ProgressBar 填充样式（Solid/Diagonal/Grid）。</summary>
        public string ProgressFillStyle { get => _progressFillStyle; set { if (_progressFillStyle != value) { _progressFillStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.FillStyle = value; } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
