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

        #region 原 未分区块（72daf0e^ 第 558-2063 行，×93 段）

        // ═══ 批量编辑属性（多选模式，反射应用到所有选中控件） ═══

        private string _batchFontFamily = WidgetFontDefaults.FontFamily;
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

        private string _rectFillColor = "#EEEEEE";
        public string RectFillColor
        {
            get => _rectFillColor;
            set { if (_rectFillColor != value) { _rectFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is RectangleWidget rect) rect.FillColor = value; } }
        }

        private double _labelFontSize = 14;
        public double LabelFontSize { get => _labelFontSize; set { if (Math.Abs(_labelFontSize - value) > 0.001) { _labelFontSize = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.FontSize = value; } } }

        private string _labelTextColor = "#000000";
        public string LabelTextColor { get => _labelTextColor; set { if (_labelTextColor != value) { _labelTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.TextColor = value; } } }
        private string _labelFillColor = "#EEEEEE";
        /// <summary>Label 背景色（CSS 格式）。</summary>
        public string LabelFillColor { get => _labelFillColor; set { if (_labelFillColor != value) { _labelFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.FillColor = value; } } }
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

        // ── Polygon 属性（世界地图批 3）──
        private string _polygonFillColor = "#30FFFFFF";

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
        /// <summary>IOField 背景色（CSS 格式）。</summary>
        public string IOFieldFillColor { get => _ioFieldFillColor; set { if (_ioFieldFillColor != value) { _ioFieldFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.FillColor = value; } } }
        private string _ioFieldTextColor = "#000000";
        /// <summary>IOField 文本色。</summary>
        public string IOFieldTextColor { get => _ioFieldTextColor; set { if (_ioFieldTextColor != value) { _ioFieldTextColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.TextColor = value; } } }

        private string _progressFillStyle = "Solid";
        /// <summary>ProgressBar 填充样式（Solid/Diagonal/Grid）。</summary>
        public string ProgressFillStyle { get => _progressFillStyle; set { if (_progressFillStyle != value) { _progressFillStyle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.FillStyle = value; } } }
        #endregion

    }
}