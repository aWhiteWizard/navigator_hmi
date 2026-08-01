using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// Widget 类型的 DataTemplate 选择器。根据 Widget 子类类型返回不同的视觉模板。
    /// TextWidget 用 Content 绑定模板（无 Text 属性）；LabelWidget 用 Text 绑定模板——
    /// 二者视觉结构一致（TextBlock + 背景 Border）。
    /// RectangleWidget 使用默认矩形模板。
    /// </summary>
    public class WidgetTemplateSelector : DataTemplateSelector
    {
        public DataTemplate ButtonTemplate { get; set; } = null!;
        public DataTemplate LabelTemplate { get; set; } = null!;
        public DataTemplate TextContentTemplate { get; set; } = null!;
        public DataTemplate ImageTemplate { get; set; } = null!;
        public DataTemplate NumericDisplayTemplate { get; set; } = null!;
        public DataTemplate SwitchTemplate { get; set; } = null!;
        public DataTemplate LineTemplate { get; set; } = null!;
        public DataTemplate CircleTemplate { get; set; } = null!;
        public DataTemplate IOFieldTemplate { get; set; } = null!;
        public DataTemplate CheckBoxTemplate { get; set; } = null!;
        public DataTemplate TextBoxTemplate { get; set; } = null!;
        public DataTemplate FrameTemplate { get; set; } = null!;
        public DataTemplate ProgressBarTemplate { get; set; } = null!;
        public DataTemplate DefaultTemplate { get; set; } = null!;

        /// <summary>
        /// 根据 item 运行时类型选择对应的 DataTemplate。
        /// 未匹配的类型回退到 DefaultTemplate（矩形填充色渲染）。
        /// TextWidget 用 Content 绑定模板（无 Text 属性）；LabelWidget 用 Text 绑定模板。
        /// </summary>
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                ButtonWidget => ButtonTemplate,
                TextWidget => TextContentTemplate,
                LabelWidget => LabelTemplate,
                ImageWidget => ImageTemplate,
                NumericDisplayWidget => NumericDisplayTemplate,
                SwitchWidget => SwitchTemplate,
                LineWidget => LineTemplate,
                CircleWidget => CircleTemplate,
                IOFieldWidget => IOFieldTemplate,
                CheckBoxWidget => CheckBoxTemplate,
                TextBoxWidget => TextBoxTemplate,
                FrameWidget => FrameTemplate,
                ProgressBarWidget => ProgressBarTemplate,
                RectangleWidget => DefaultTemplate,
                _ => DefaultTemplate
            };
        }
    }

    /// <summary>
    /// Widget 控件列表的 ItemsControl 工厂。
    /// 负责创建并配置用于在画布上渲染 Widget 集合的 ItemsControl 实例。
    /// 纯 UI 构建逻辑，无状态依赖，所有方法均为静态。
    /// </summary>
    /// <remarks>
    /// 事件处理器通过 <see cref="FrameworkElementFactory.AddHandler"/> 
    /// 在 DataTemplate 级别绑定，确保所有 Widget 模板自动获得拖拽/点击/选中能力。
    /// </remarks>
    public static class WidgetItemsControlFactory
    {
        /// <summary>
        /// 根据指定画面和事件委托创建 ItemsControl。
        /// 配置 Canvas 面板布局、双向位置绑定（X/Y）、控件模板选择器、
        /// 选中状态绑定及交互事件处理器。
        /// </summary>
        public static ItemsControl Create(
            Screen screen,
            RoutedEventHandler clickHandler,
            MouseButtonEventHandler previewMouseLeftButtonDownHandler,
            MouseButtonEventHandler mouseLeftButtonDownHandler,
            MouseEventHandler mouseMoveHandler,
            MouseButtonEventHandler mouseLeftButtonUpHandler,
            MouseButtonEventHandler previewMouseRightButtonDownHandler,
            MouseButtonEventHandler mouseRightButtonUpHandler)
        {
            var itemsControl = new ItemsControl();
            itemsControl.Name = "MyItemsControl";

            // 设置 ItemsPanel — 使用 Canvas 作为子元素的布局面板
            var panelTemplate = new ItemsPanelTemplate();
            var panelFactory = new FrameworkElementFactory(typeof(Canvas));
            panelFactory.SetValue(Canvas.BackgroundProperty, Brushes.Transparent);
            panelTemplate.VisualTree = panelFactory;
            itemsControl.ItemsPanel = panelTemplate;

            // 设置 ItemContainerStyle — 将数据模型的 X/Y 绑定到 Canvas.Left/Top
            var style = new Style(typeof(ContentPresenter));
            style.Setters.Add(new Setter(Canvas.LeftProperty, new Binding("X") { Mode = BindingMode.OneWay }));
            style.Setters.Add(new Setter(Canvas.TopProperty, new Binding("Y") { Mode = BindingMode.OneWay }));
            itemsControl.ItemContainerStyle = style;

            // 创建各类型模板
            var selector = new WidgetTemplateSelector
            {
                ButtonTemplate = CreateButtonTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                LabelTemplate = CreateLabelTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                TextContentTemplate = CreateTextContentTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                ImageTemplate = CreateImageTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                NumericDisplayTemplate = CreateNumericDisplayTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                SwitchTemplate = CreateSwitchTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                LineTemplate = CreateLineTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                CircleTemplate = CreateCircleTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                IOFieldTemplate = CreateIOFieldTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                CheckBoxTemplate = CreateCheckBoxTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                TextBoxTemplate = CreateTextBoxTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                FrameTemplate = CreateFrameTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                ProgressBarTemplate = CreateProgressBarTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                DefaultTemplate = CreateRectangleTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler)
            };

            itemsControl.ItemTemplateSelector = selector;
            return itemsControl;
        }

        // ═══════ 交互事件附加辅助 ═══════
        private static void AddInteractionHandlers(FrameworkElementFactory factory,
            RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU,
            MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            factory.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, pmLBD);
            factory.AddHandler(UIElement.MouseLeftButtonDownEvent, mLBD, handledEventsToo: true);
            factory.AddHandler(UIElement.MouseMoveEvent, mMove, handledEventsToo: true);
            factory.AddHandler(UIElement.MouseLeftButtonUpEvent, mLBU, handledEventsToo: true);
            factory.AddHandler(UIElement.PreviewMouseRightButtonDownEvent, pmRBD, true);
            factory.AddHandler(UIElement.MouseRightButtonUpEvent, mRBU, handledEventsToo: true);
        }

        /// <summary>用 Border 包裹元素，设置 Width/Height/IsSelected 绑定 + 交互事件。</summary>
        /// <param name="bindFillBackground">是否绑定 Border.Background = FillColor（Label/NumericDisplay/IOField 等
        /// 无自带填充的控件需要背景色保证可见可选中；Circle/Rectangle 已绑定自身 Fill，传 false 避免双重绑定）。</param>
        private static FrameworkElementFactory WrapWithBorder(FrameworkElementFactory inner,
            RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU,
            MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU,
            bool bindFillBackground = false)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            if (bindFillBackground)
                border.SetBinding(Border.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            border.SetBinding(Border.WidthProperty, new Binding("Width"));
            border.SetBinding(Border.HeightProperty, new Binding("Height"));
            border.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(border, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            border.AppendChild(inner);
            return border;
        }

        // ═══════ 各类型模板 ═══════

        private static DataTemplate CreateButtonTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var btn = new FrameworkElementFactory(typeof(Button));
            btn.SetBinding(Button.ContentProperty, new Binding("Text"));
            btn.SetBinding(Button.WidthProperty, new Binding("Width"));
            btn.SetBinding(Button.HeightProperty, new Binding("Height"));
            btn.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(btn, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = btn;
            return dt;
        }

        /// <summary>TextWidget 专属模板：绑定 Content（TextWidget 无 Text 属性，原共享 LabelTemplate 导致文字不显示）。</summary>
        private static DataTemplate CreateTextContentTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding("Content"));
            BindTextFormatting(tb);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // TextContentTemplate
            return dt;
        }

        private static DataTemplate CreateLabelTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding("Text"));
            BindTextFormatting(tb);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // LabelTemplate（仅 LabelWidget 使用）
            return dt;
        }

        /// <summary>绑定文本格式属性（FontSize/FontWeight/Foreground/TextAlignment），Text/Label 共享。</summary>
        private static void BindTextFormatting(FrameworkElementFactory tb)
        {
            tb.SetBinding(TextBlock.FontSizeProperty, new Binding("FontSize"));
            tb.SetBinding(TextBlock.FontWeightProperty, new Binding("FontWeight"));
            tb.SetBinding(TextBlock.ForegroundProperty, new Binding("TextColor") { Converter = new ColorStringToBrushConverter() });
            tb.SetBinding(TextBlock.TextAlignmentProperty, new Binding("HAlign") { Converter = new HAlignToTextAlignmentConverter() });
        }

        private static DataTemplate CreateImageTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            // Border 背景绑定 FillColor：保证图片路径为空/图片透明时控件仍可见、可选中
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            border.SetBinding(Border.WidthProperty, new Binding("Width"));
            border.SetBinding(Border.HeightProperty, new Binding("Height"));
            border.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(border, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("ImagePath"));
            img.SetValue(Image.StretchProperty, System.Windows.Media.Stretch.Uniform);
            border.AppendChild(img);
            dt.VisualTree = border;
            return dt;
        }

        private static DataTemplate CreateNumericDisplayTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            // 设计态显示 Value 数值（运行时由 BoundTag 变量实时值覆盖）
            tb.SetBinding(TextBlock.TextProperty, new Binding("Value"));
            tb.SetBinding(TextBlock.FontSizeProperty, new Binding("FontSize"));
            tb.SetBinding(TextBlock.ForegroundProperty, new Binding("TextColor") { Converter = new ColorStringToBrushConverter() });
            tb.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // NumericDisplayTemplate
            return dt;
        }

        private static DataTemplate CreateSwitchTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var btn = new FrameworkElementFactory(typeof(Button));
            btn.SetBinding(Button.ContentProperty, new Binding("IsOn"));
            btn.SetBinding(Button.WidthProperty, new Binding("Width"));
            btn.SetBinding(Button.HeightProperty, new Binding("Height"));
            btn.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(btn, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = btn;
            return dt;
        }

        private static DataTemplate CreateLineTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var sp = new FrameworkElementFactory(typeof(Canvas));
            var line = new FrameworkElementFactory(typeof(Line));
            line.SetBinding(Line.X1Property, new Binding("X") { Converter = new ZeroConverter() });
            line.SetBinding(Line.Y1Property, new Binding("Y") { Converter = new ZeroConverter() });
            line.SetBinding(Line.X2Property, new Binding("X2"));
            line.SetBinding(Line.Y2Property, new Binding("Y2"));
            line.SetBinding(Line.StrokeProperty, new Binding("StrokeColor") { Converter = new ColorStringToBrushConverter() });
            line.SetBinding(Line.StrokeThicknessProperty, new Binding("StrokeThickness"));
            sp.AppendChild(line);
            dt.VisualTree = WrapWithBorder(sp, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            return dt;
        }

        private static DataTemplate CreateCircleTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var ellipse = new FrameworkElementFactory(typeof(Ellipse));
            ellipse.SetBinding(Ellipse.FillProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            ellipse.SetBinding(Ellipse.StrokeProperty, new Binding("StrokeColor") { Converter = new ColorStringToBrushConverter() });
            ellipse.SetBinding(Ellipse.StrokeThicknessProperty, new Binding("StrokeThickness"));
            dt.VisualTree = WrapWithBorder(ellipse, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            return dt;
        }

        private static DataTemplate CreateIOFieldTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BorderBrushProperty, Brushes.Gray);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding("Content"));
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            tb.SetValue(TextBlock.MarginProperty, new Thickness(2));
            border.AppendChild(tb);
            dt.VisualTree = WrapWithBorder(border, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // IOFieldTemplate
            return dt;
        }

        private static DataTemplate CreateCheckBoxTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var cb = new FrameworkElementFactory(typeof(CheckBox));
            cb.SetBinding(CheckBox.ContentProperty, new Binding("Text"));
            cb.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsChecked"));
            cb.SetBinding(CheckBox.WidthProperty, new Binding("Width"));
            cb.SetBinding(CheckBox.HeightProperty, new Binding("Height"));
            cb.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(cb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = cb;
            return dt;
        }

        private static DataTemplate CreateTextBoxTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBox));
            tb.SetBinding(TextBox.TextProperty, new Binding("Content"));
            tb.SetBinding(TextBox.WidthProperty, new Binding("Width"));
            tb.SetBinding(TextBox.HeightProperty, new Binding("Height"));
            // 设计态只读：值通过属性面板写入（Content），画布上不可编辑（保证按下即可拖拽）
            tb.SetValue(TextBox.IsReadOnlyProperty, true);
            tb.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = tb;
            return dt;
        }

        private static DataTemplate CreateFrameTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var gb = new FrameworkElementFactory(typeof(GroupBox));
            gb.SetBinding(GroupBox.HeaderProperty, new Binding("Title"));
            gb.SetBinding(GroupBox.WidthProperty, new Binding("Width"));
            gb.SetBinding(GroupBox.HeightProperty, new Binding("Height"));
            // 背景色 + 背景图片（ImagePath 为空时仅显示背景色与标题框）
            gb.SetBinding(GroupBox.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("ImagePath"));
            img.SetValue(Image.StretchProperty, System.Windows.Media.Stretch.Fill);
            gb.AppendChild(img);
            gb.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(gb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = gb;
            return dt;
        }

        private static DataTemplate CreateProgressBarTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var pb = new FrameworkElementFactory(typeof(ProgressBar));
            pb.SetBinding(ProgressBar.ValueProperty, new Binding("Value"));
            pb.SetBinding(ProgressBar.MinimumProperty, new Binding("Min"));
            pb.SetBinding(ProgressBar.MaximumProperty, new Binding("Max"));
            pb.SetBinding(ProgressBar.WidthProperty, new Binding("Width"));
            pb.SetBinding(ProgressBar.HeightProperty, new Binding("Height"));
            pb.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(pb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = pb;
            return dt;
        }

        private static DataTemplate CreateRectangleTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var rect = new FrameworkElementFactory(typeof(Rectangle));
            rect.SetBinding(Rectangle.FillProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            dt.VisualTree = WrapWithBorder(rect, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            return dt;
        }
    }

    /// <summary>颜色字符串到 Brush 的转换器（用于 DataTemplate 绑定）。</summary>
    /// <summary>
    /// 颜色字符串到 Brush 的转换器（用于 DataTemplate 绑定）。
    /// 空串/留空 → 透明画刷（不显示背景，但 Transparent 非 null 仍参与命中测试，控件可选中）。
    /// "Transparent"/#00000000 由 BrushConverter 原生支持；非法值回退 Gray。
    /// </summary>
    public class ColorStringToBrushConverter : IValueConverter
    {
        private static readonly BrushConverter _brushConverter = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try { return (Brush)_brushConverter.ConvertFrom(s)!; }
                catch { return Brushes.Gray; }
            }
            // null/留空 = 不显示背景（Transparent 非 null 画刷仍参与命中测试，控件保持可选中）
            return Brushes.Transparent;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>始终返回 0 的转换器（用于 Line 的 X1/Y1 起点归零）。</summary>
    public class ZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => 0.0;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>HAlign 字符串（Left/Center/Right）→ TextAlignment 转换器（Text/Label 模板绑定）。</summary>
    public class HAlignToTextAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value?.ToString() switch
            {
                "Center" => TextAlignment.Center,
                "Right" => TextAlignment.Right,
                _ => TextAlignment.Left
            };
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
