using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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
        ButtonWidget,
        TextWidget,
        RectangleWidget,
        LabelWidget,
        ImageWidget,
        NumericDisplayWidget,
        SwitchWidget,
        LineWidget,
        CircleWidget,
        IOFieldWidget,
        CheckBoxWidget,
        TextBoxWidget,
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

        /// <summary>当前选中的画面，null 表示无选中。</summary>
        public Screen? SelectedScreen
        {
            get => _selectedScreen;
            set
            {
                if (_selectedScreen != value)
                {
                    _selectedWidget = null;
                    _selectedScreen = value;
                    if (value != null)
                    {
                        ScreenName = value.Name;
                        ScreenWidth = value.Width;
                        ScreenHeight = value.Height;
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
                         IOFieldWidget => PropertyTargetType.IOFieldWidget,
                         CheckBoxWidget => PropertyTargetType.CheckBoxWidget,
                         TextBoxWidget => PropertyTargetType.TextBoxWidget,
                         FrameWidget => PropertyTargetType.FrameWidget,
                         ProgressBarWidget => PropertyTargetType.ProgressBarWidget,
                         _ => PropertyTargetType.None
                     };
                     OnPropertyChanged(nameof(SelectedObjectType));

                    // 切换选中控件时，同步 ViewModel 的属性值
                    if (value != null)
                    {
                        X = value.X;
                        Y = value.Y;
                        Width = value.Width;
                        Height = value.Height;
                        ObjectName = value.ObjectName;
                        RefreshDuplicateCheck();

                        switch (value)
                        {
                            case ButtonWidget btn: ButtonText = btn.Text; break;
                            case TextWidget txt: TextContent = txt.Content; TextFillColor = txt.FillColor; break;
                            case RectangleWidget rect: RectFillColor = rect.FillColor; break;
                            case LabelWidget lbl: LabelText = lbl.Text; LabelFontSize = lbl.FontSize; LabelTextColor = lbl.TextColor; LabelFillColor = lbl.FillColor; break;
                            case ImageWidget img: ImagePath = img.ImagePath; ImageFillColor = img.FillColor; break;
                            case NumericDisplayWidget nd: NumericPrefix = nd.Prefix; NumericSuffix = nd.Suffix; NumericDecimalPlaces = nd.DecimalPlaces; NumericFontSize = nd.FontSize; NumericTextColor = nd.TextColor; NumericFillColor = nd.FillColor; break;
                            case SwitchWidget sw: SwitchIsOn = sw.IsOn; SwitchOnText = sw.OnText; SwitchOffText = sw.OffText; break;
                            case LineWidget line: LineX2 = line.X2; LineY2 = line.Y2; LineStrokeColor = line.StrokeColor; LineStrokeThickness = line.StrokeThickness; break;
                            case CircleWidget c: CircleFillColor = c.FillColor; CircleStrokeColor = c.StrokeColor; CircleStrokeThickness = c.StrokeThickness; break;
                            case IOFieldWidget io: IOFieldContent = io.Content; IOFieldIsReadOnly = io.IsReadOnly; IOFieldFillColor = io.FillColor; break;
                            case CheckBoxWidget cb: CheckBoxText = cb.Text; CheckBoxIsChecked = cb.IsChecked; break;
                            case TextBoxWidget tbx: TextBoxContent = tbx.Content; TextBoxIsPassword = tbx.IsPassword; break;
                            case FrameWidget f: FrameTitle = f.Title; FrameFillColor = f.FillColor; FrameImagePath = f.ImagePath; break;
                            case ProgressBarWidget pb: ProgressValue = pb.Value; ProgressMin = pb.Min; ProgressMax = pb.Max; ProgressFillColor = pb.FillColor; break;
                        }
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

            switch (e.PropertyName)
            {
                case nameof(Widget.X):
                    X = _selectedWidget.X;
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
            }
        }


        /// <summary>是否有 Widget 被选中（属性面板可见性控制）。</summary>
        public bool IsPropertyVisible => _selectedWidget != null || _selectedScreen != null;
        /// <summary>当前选中的是画面。</summary>
        public bool IsScreenSelected => _selectedScreen != null;
         /// <summary>当前选中的是控件。</summary>
         public bool IsWidgetSelected => _selectedWidget != null;
         /// <summary>控件类型名称（选中画面时显示"画面"）。</summary>
         public string WidgetTypeName => _selectedScreen != null ? "画面" : _selectedWidget?.GetType().Name ?? "";
        public bool IsButtonWidget => _selectedWidget is ButtonWidget;
        public bool IsTextWidget => _selectedWidget is TextWidget;
        public bool IsRectangleWidget => _selectedWidget is RectangleWidget;
        public bool IsLabelWidget => _selectedWidget is LabelWidget;
        public bool IsImageWidget => _selectedWidget is ImageWidget;
        public bool IsNumericDisplayWidget => _selectedWidget is NumericDisplayWidget;
        public bool IsSwitchWidget => _selectedWidget is SwitchWidget;
        public bool IsLineWidget => _selectedWidget is LineWidget;
        public bool IsCircleWidget => _selectedWidget is CircleWidget;
        public bool IsIOFieldWidget => _selectedWidget is IOFieldWidget;
        public bool IsCheckBoxWidget => _selectedWidget is CheckBoxWidget;
        public bool IsTextBoxWidget => _selectedWidget is TextBoxWidget;
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
                    if (_selectedScreen != null) _selectedScreen.Width = rounded;
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
                    if (_selectedScreen != null) _selectedScreen.Height = rounded;
                }
            }
        }

        // 第 228-235 行：ScreenTypeText 属性（已有代码）
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

        private double _x;
        public double X
        {
            get => _x;
            set
            {
                if (Math.Abs(_x - value) > 0.001)
                {
                    _x = value;
                    OnPropertyChanged();
                    // 实时回写到数据模型
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
                if (Math.Abs(_y - value) > 0.001)
                {
                    _y = value;
                    OnPropertyChanged();
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
                if (Math.Abs(_width - value) > 0.001)
                {
                    _width = value;
                    OnPropertyChanged();
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
                if (Math.Abs(_height - value) > 0.001)
                {
                    _height = value;
                    OnPropertyChanged();
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
                    if (_selectedWidget is ButtonWidget btn) btn.Text = value;
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
                    if (_selectedWidget is TextWidget txt) txt.Content = value;
                }
            }
        }
        private string _textFillColor = "#EEEEEE";
        /// <summary>Text 背景色（CSS 格式）。</summary>
        public string TextFillColor { get => _textFillColor; set { if (_textFillColor != value) { _textFillColor = value; OnPropertyChanged(); if (_selectedWidget is TextWidget txt) txt.FillColor = value; } } }

        private string _rectFillColor = "#EEEEEE";
        public string RectFillColor
        {
            get => _rectFillColor;
            set { if (_rectFillColor != value) { _rectFillColor = value; OnPropertyChanged(); if (_selectedWidget is RectangleWidget rect) rect.FillColor = value; } }
        }

        private string _labelText = "";
        public string LabelText { get => _labelText; set { if (_labelText != value) { _labelText = value; OnPropertyChanged(); if (_selectedWidget is LabelWidget lbl) lbl.Text = value; } } }

        private double _labelFontSize = 14;
        public double LabelFontSize { get => _labelFontSize; set { if (Math.Abs(_labelFontSize - value) > 0.001) { _labelFontSize = value; OnPropertyChanged(); if (_selectedWidget is LabelWidget lbl) lbl.FontSize = value; } } }

        private string _labelTextColor = "#000000";
        public string LabelTextColor { get => _labelTextColor; set { if (_labelTextColor != value) { _labelTextColor = value; OnPropertyChanged(); if (_selectedWidget is LabelWidget lbl) lbl.TextColor = value; } } }
        private string _labelFillColor = "#EEEEEE";
        /// <summary>Label 背景色（CSS 格式）。</summary>
        public string LabelFillColor { get => _labelFillColor; set { if (_labelFillColor != value) { _labelFillColor = value; OnPropertyChanged(); if (_selectedWidget is LabelWidget lbl) lbl.FillColor = value; } } }

        private string _imagePath = "";
        public string ImagePath { get => _imagePath; set { if (_imagePath != value) { _imagePath = value; OnPropertyChanged(); if (_selectedWidget is ImageWidget img) img.ImagePath = value; } } }
        private string _imageFillColor = "#EEEEEE";
        /// <summary>图片背景色（CSS 格式，默认浅灰；图片透明区域/无图片时可见）。</summary>
        public string ImageFillColor { get => _imageFillColor; set { if (_imageFillColor != value) { _imageFillColor = value; OnPropertyChanged(); if (_selectedWidget is ImageWidget img) img.FillColor = value; } } }

        private string _numericPrefix = "";
        public string NumericPrefix { get => _numericPrefix; set { if (_numericPrefix != value) { _numericPrefix = value; OnPropertyChanged(); if (_selectedWidget is NumericDisplayWidget nd) nd.Prefix = value; } } }
        private string _numericSuffix = "";
        public string NumericSuffix { get => _numericSuffix; set { if (_numericSuffix != value) { _numericSuffix = value; OnPropertyChanged(); if (_selectedWidget is NumericDisplayWidget nd) nd.Suffix = value; } } }
        private int _numericDecimalPlaces = 1;
        public int NumericDecimalPlaces { get => _numericDecimalPlaces; set { if (_numericDecimalPlaces != value) { _numericDecimalPlaces = value; OnPropertyChanged(); if (_selectedWidget is NumericDisplayWidget nd) nd.DecimalPlaces = value; } } }
        private double _numericFontSize = 16;
        public double NumericFontSize { get => _numericFontSize; set { if (Math.Abs(_numericFontSize - value) > 0.001) { _numericFontSize = value; OnPropertyChanged(); if (_selectedWidget is NumericDisplayWidget nd) nd.FontSize = value; } } }
        private string _numericTextColor = "#000000";
        public string NumericTextColor { get => _numericTextColor; set { if (_numericTextColor != value) { _numericTextColor = value; OnPropertyChanged(); if (_selectedWidget is NumericDisplayWidget nd) nd.TextColor = value; } } }
        private string _numericFillColor = "#EEEEEE";
        /// <summary>NumericDisplay 背景色（CSS 格式）。</summary>
        public string NumericFillColor { get => _numericFillColor; set { if (_numericFillColor != value) { _numericFillColor = value; OnPropertyChanged(); if (_selectedWidget is NumericDisplayWidget nd) nd.FillColor = value; } } }

        private bool _switchIsOn = false;
        public bool SwitchIsOn { get => _switchIsOn; set { if (_switchIsOn != value) { _switchIsOn = value; OnPropertyChanged(); if (_selectedWidget is SwitchWidget sw) sw.IsOn = value; } } }
        private string _switchOnText = "ON";
        public string SwitchOnText { get => _switchOnText; set { if (_switchOnText != value) { _switchOnText = value; OnPropertyChanged(); if (_selectedWidget is SwitchWidget sw) sw.OnText = value; } } }
        private string _switchOffText = "OFF";
        public string SwitchOffText { get => _switchOffText; set { if (_switchOffText != value) { _switchOffText = value; OnPropertyChanged(); if (_selectedWidget is SwitchWidget sw) sw.OffText = value; } } }

        private double _lineX2 = 100;
        public double LineX2 { get => _lineX2; set { if (Math.Abs(_lineX2 - value) > 0.001) { _lineX2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln) ln.X2 = value; } } }
        private double _lineY2 = 0;
        public double LineY2 { get => _lineY2; set { if (Math.Abs(_lineY2 - value) > 0.001) { _lineY2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln) ln.Y2 = value; } } }
        private string _lineStrokeColor = "#000000";
        public string LineStrokeColor { get => _lineStrokeColor; set { if (_lineStrokeColor != value) { _lineStrokeColor = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln) ln.StrokeColor = value; } } }
        private double _lineStrokeThickness = 1;
        public double LineStrokeThickness { get => _lineStrokeThickness; set { if (Math.Abs(_lineStrokeThickness - value) > 0.001) { _lineStrokeThickness = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln) ln.StrokeThickness = value; } } }

        private string _circleFillColor = "#EEEEEE";
        public string CircleFillColor { get => _circleFillColor; set { if (_circleFillColor != value) { _circleFillColor = value; OnPropertyChanged(); if (_selectedWidget is CircleWidget c) c.FillColor = value; } } }
        private string _circleStrokeColor = "#000000";
        public string CircleStrokeColor { get => _circleStrokeColor; set { if (_circleStrokeColor != value) { _circleStrokeColor = value; OnPropertyChanged(); if (_selectedWidget is CircleWidget c) c.StrokeColor = value; } } }
        private double _circleStrokeThickness = 1;
        public double CircleStrokeThickness { get => _circleStrokeThickness; set { if (Math.Abs(_circleStrokeThickness - value) > 0.001) { _circleStrokeThickness = value; OnPropertyChanged(); if (_selectedWidget is CircleWidget c) c.StrokeThickness = value; } } }

        private string _ioFieldContent = "";
        public string IOFieldContent { get => _ioFieldContent; set { if (_ioFieldContent != value) { _ioFieldContent = value; OnPropertyChanged(); if (_selectedWidget is IOFieldWidget io) io.Content = value; } } }
        private bool _ioFieldIsReadOnly = false;
        public bool IOFieldIsReadOnly { get => _ioFieldIsReadOnly; set { if (_ioFieldIsReadOnly != value) { _ioFieldIsReadOnly = value; OnPropertyChanged(); if (_selectedWidget is IOFieldWidget io) io.IsReadOnly = value; } } }
        private string _ioFieldFillColor = "#EEEEEE";
        /// <summary>IOField 背景色（CSS 格式）。</summary>
        public string IOFieldFillColor { get => _ioFieldFillColor; set { if (_ioFieldFillColor != value) { _ioFieldFillColor = value; OnPropertyChanged(); if (_selectedWidget is IOFieldWidget io) io.FillColor = value; } } }

        private string _checkBoxText = "CheckBox";
        public string CheckBoxText { get => _checkBoxText; set { if (_checkBoxText != value) { _checkBoxText = value; OnPropertyChanged(); if (_selectedWidget is CheckBoxWidget cb) cb.Text = value; } } }
        private bool _checkBoxIsChecked = false;
        public bool CheckBoxIsChecked { get => _checkBoxIsChecked; set { if (_checkBoxIsChecked != value) { _checkBoxIsChecked = value; OnPropertyChanged(); if (_selectedWidget is CheckBoxWidget cb) cb.IsChecked = value; } } }

        private string _textBoxContent = "";
        public string TextBoxContent { get => _textBoxContent; set { if (_textBoxContent != value) { _textBoxContent = value; OnPropertyChanged(); if (_selectedWidget is TextBoxWidget tbx) tbx.Content = value; } } }
        private bool _textBoxIsPassword = false;
        public bool TextBoxIsPassword { get => _textBoxIsPassword; set { if (_textBoxIsPassword != value) { _textBoxIsPassword = value; OnPropertyChanged(); if (_selectedWidget is TextBoxWidget tbx) tbx.IsPassword = value; } } }

        private string _frameTitle = "Group";
        public string FrameTitle { get => _frameTitle; set { if (_frameTitle != value) { _frameTitle = value; OnPropertyChanged(); if (_selectedWidget is FrameWidget f) f.Title = value; } } }
        private string _frameFillColor = "#EEEEEE";
        public string FrameFillColor { get => _frameFillColor; set { if (_frameFillColor != value) { _frameFillColor = value; OnPropertyChanged(); if (_selectedWidget is FrameWidget f) f.FillColor = value; } } }
        private string _frameImagePath = "";
        public string FrameImagePath { get => _frameImagePath; set { if (_frameImagePath != value) { _frameImagePath = value; OnPropertyChanged(); if (_selectedWidget is FrameWidget f) f.ImagePath = value; } } }

        private double _progressValue = 0;
        public double ProgressValue { get => _progressValue; set { if (Math.Abs(_progressValue - value) > 0.001) { _progressValue = value; OnPropertyChanged(); if (_selectedWidget is ProgressBarWidget pb) pb.Value = value; } } }
        private double _progressMin = 0;
        public double ProgressMin { get => _progressMin; set { if (Math.Abs(_progressMin - value) > 0.001) { _progressMin = value; OnPropertyChanged(); if (_selectedWidget is ProgressBarWidget pb) pb.Min = value; } } }
        private double _progressMax = 100;
        public double ProgressMax { get => _progressMax; set { if (Math.Abs(_progressMax - value) > 0.001) { _progressMax = value; OnPropertyChanged(); if (_selectedWidget is ProgressBarWidget pb) pb.Max = value; } } }
        private string _progressFillColor = "#3399FF";
        public string ProgressFillColor { get => _progressFillColor; set { if (_progressFillColor != value) { _progressFillColor = value; OnPropertyChanged(); if (_selectedWidget is ProgressBarWidget pb) pb.FillColor = value; } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
