using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        public DataTemplate EllipseTemplate { get; set; } = null!;
        public DataTemplate IOFieldTemplate { get; set; } = null!;
        public DataTemplate CheckBoxTemplate { get; set; } = null!;
        public DataTemplate TextListTemplate { get; set; } = null!;
        public DataTemplate FrameTemplate { get; set; } = null!;
        public DataTemplate ProgressBarTemplate { get; set; } = null!;
        public DataTemplate DateTimeTemplate { get; set; } = null!;
        public DataTemplate DefaultTemplate { get; set; } = null!;
    public DataTemplate WindowTemplate { get; set; } = null!;   // W4：窗口控件（UserView/AlarmView/RobotList）
    public DataTemplate PolygonTemplate { get; set; } = null!;   // 世界地图批 3：多边形

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
                EllipseWidget => EllipseTemplate,
                IOFieldWidget => IOFieldTemplate,
                CheckBoxWidget => CheckBoxTemplate,
                TextListWidget => TextListTemplate,
                FrameWidget => FrameTemplate,
                ProgressBarWidget => ProgressBarTemplate,
                DateTimeWidget => DateTimeTemplate,
                WindowWidget => WindowTemplate,
                PolygonWidget => PolygonTemplate,
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
                EllipseTemplate = CreateEllipseTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                IOFieldTemplate = CreateIOFieldTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                CheckBoxTemplate = CreateCheckBoxTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                TextListTemplate = CreateTextListTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                FrameTemplate = CreateFrameTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                ProgressBarTemplate = CreateProgressBarTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                DateTimeTemplate = CreateDateTimeTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                DefaultTemplate = CreateRectangleTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                WindowTemplate = CreateWindowTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler),
                PolygonTemplate = CreatePolygonTemplate(clickHandler, previewMouseLeftButtonDownHandler, mouseLeftButtonDownHandler, mouseMoveHandler, mouseLeftButtonUpHandler, previewMouseRightButtonDownHandler, mouseRightButtonUpHandler)
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
            bool bindFillBackground = false, bool transparentHitArea = false)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            if (bindFillBackground)
                border.SetBinding(Border.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            if (transparentHitArea)
                border.SetValue(Border.BackgroundProperty, Brushes.Transparent);   // 纯命中不渲染（如 Point 标记边框外空白区可拖拽）
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
            // Content 用 TextBlock 包装：支持字体/加粗/倾斜/下划线完整渲染
            var content = new FrameworkElementFactory(typeof(TextBlock));
            content.SetBinding(TextBlock.TextProperty, new Binding("Text"));
            BindTextFormatting(content);
            content.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            btn.AppendChild(content);
            // 背景色（用户可设 BgColor；默认浅灰保证可见可选中）
            btn.SetBinding(Button.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
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
            tb.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));   // 绑定变量 → 基准值；否则自身 Content
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
            tb.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));   // Label 绑定变量后显示基准值（DisplayText 回退 Text，零回归）
            BindTextFormatting(tb);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // LabelTemplate（仅 LabelWidget 使用）
            return dt;
        }

        /// <summary>绑定文本格式属性（TextBlock 类，Foreground 用 SafeTextColorConverter：文本色透明/同背景回退黑）。</summary>
        private static void BindTextFormatting(FrameworkElementFactory tb, bool centerAlign = false)
        {
            tb.SetBinding(TextBlock.FontFamilyProperty, new Binding("FontFamily"));
            tb.SetBinding(TextBlock.FontSizeProperty, new Binding("FontSize"));
            tb.SetBinding(TextBlock.FontWeightProperty, new Binding("FontWeight"));
            tb.SetBinding(TextBlock.FontStyleProperty, new Binding("FontStyle"));
            tb.SetBinding(TextBlock.TextDecorationsProperty, new Binding("TextDecoration") { Converter = new TextDecorationConverter() });
            var fg = new MultiBinding { Converter = new SafeTextColorConverter() };
            fg.Bindings.Add(new Binding("TextColor"));
            fg.Bindings.Add(new Binding("FillColor"));
            tb.SetBinding(TextBlock.ForegroundProperty, fg);
            if (centerAlign)
                tb.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            else
                tb.SetBinding(TextBlock.TextAlignmentProperty, new Binding("HAlign") { Converter = new HAlignToTextAlignmentConverter() });
        }

        /// <summary>绑定原生控件（TextBox/GroupBox）的字体属性（Control 类无 TextDecorations，下划线由模板单独处理）。</summary>
        private static void BindFontControl(FrameworkElementFactory fe)
        {
            fe.SetBinding(Control.FontFamilyProperty, new Binding("FontFamily"));
            fe.SetBinding(Control.FontSizeProperty, new Binding("FontSize"));
            fe.SetBinding(Control.FontWeightProperty, new Binding("FontWeight"));
            fe.SetBinding(Control.FontStyleProperty, new Binding("FontStyle"));
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
            img.SetBinding(Image.SourceProperty, new Binding("DisplayPath"));   // 绑定变量 → 基准值路径；否则自身 ImagePath
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
            tb.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));   // 绑定变量 → 基准值；否则自身 Value
            BindTextFormatting(tb, centerAlign: true);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // NumericDisplayTemplate
            return dt;
        }

        private static DataTemplate CreateSwitchTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var btn = new FrameworkElementFactory(typeof(Button));
            // Content 用 TextBlock：显示 OnText/OffText（按 IsOn 切换）+ 字体完整渲染
            var content = new FrameworkElementFactory(typeof(TextBlock));
            var sw = new MultiBinding { Converter = new SwitchTextConverter() };
            sw.Bindings.Add(new Binding("DisplayIsOn"));   // 绑定变量 → 基准值状态；否则自身 IsOn
            sw.Bindings.Add(new Binding("OnText"));
            sw.Bindings.Add(new Binding("OffText"));
            content.SetBinding(TextBlock.TextProperty, sw);
            BindTextFormatting(content);
            content.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            btn.AppendChild(content);
            // 背景色（用户可设 BgColor）
            btn.SetBinding(Button.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
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


        private static DataTemplate CreateEllipseTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
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
            tb.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));   // 绑定变量 → 基准值；否则自身 Content
            BindTextFormatting(tb);
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
            // Content 用 TextBlock 包装：支持字体/加粗/倾斜/下划线完整渲染
            var content = new FrameworkElementFactory(typeof(TextBlock));
            content.SetBinding(TextBlock.TextProperty, new Binding("Text"));
            BindTextFormatting(content);
            content.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            cb.AppendChild(content);
            // 背景色（用户可设 BgColor）
            cb.SetBinding(CheckBox.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            cb.SetBinding(CheckBox.IsCheckedProperty, new Binding("DisplayIsChecked"));   // 绑定变量 → 基准值勾选状态；否则自身 IsChecked
            cb.SetBinding(CheckBox.WidthProperty, new Binding("Width"));
            cb.SetBinding(CheckBox.HeightProperty, new Binding("Height"));
            cb.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(cb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = cb;
            return dt;
        }

        private static DataTemplate CreateTextListTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            // 显示列表项文本（绑列表 → 第 N 项；未绑 → 空白），设计态只读
            tb.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));
            BindTextFormatting(tb, centerAlign: true);
            tb.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU, bindFillBackground: true);   // TextListTemplate
            return dt;
        }

        private static DataTemplate CreateFrameTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var gb = new FrameworkElementFactory(typeof(GroupBox));
            // 标题用 HeaderProperty 绑定（AppendChild 会设置 Content 而非 Header，GroupBox 仅允许单 Content）
            gb.SetBinding(GroupBox.HeaderProperty, new Binding("Title"));
            // 标题字体继承 GroupBox 的 Control 字体属性（FontFamily/Size/Weight/Style；下划线不支持，面板已隐藏 Undr）
            BindFontControl(gb);
            gb.SetBinding(GroupBox.WidthProperty, new Binding("Width"));
            gb.SetBinding(GroupBox.HeightProperty, new Binding("Height"));
            // 背景色 + 背景图片（ImagePath 为空时仅显示背景色与标题框）
            gb.SetBinding(GroupBox.BackgroundProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            var img = new FrameworkElementFactory(typeof(Image));
            img.SetBinding(Image.SourceProperty, new Binding("DisplayPath"));   // 绑定变量 → 基准值路径；否则自身 ImagePath
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
            pb.SetBinding(ProgressBar.ValueProperty, new Binding("DisplayProgressValue") { Mode = BindingMode.OneWay });   // 只读计算属性必须 OneWay（ProgressBar.Value 默认 TwoWay）
            pb.SetBinding(ProgressBar.MinimumProperty, new Binding("Min"));
            pb.SetBinding(ProgressBar.MaximumProperty, new Binding("Max"));
            // 填充 = Foreground（WPF ProgressBar Indicator 默认绑 Foreground）→ PatternFillConverter 生成花纹刷
            var fill = new MultiBinding { Converter = new PatternFillConverter() };
            fill.Bindings.Add(new Binding("FillStyle"));
            fill.Bindings.Add(new Binding("FillColor"));
            pb.SetBinding(ProgressBar.ForegroundProperty, fill);
            pb.SetBinding(ProgressBar.WidthProperty, new Binding("Width"));
            pb.SetBinding(ProgressBar.HeightProperty, new Binding("Height"));
            pb.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            AddInteractionHandlers(pb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            dt.VisualTree = pb;
            return dt;
        }

        /// <summary>C13 日期时间控件渲染：TextBlock 显示 Text（边框包裹）。</summary>
        /// <summary>W4 窗口控件渲染：按 WindowType 三模板设计态预览（UserView 未登录示例 / AlarmView 2-3 条示例 / RobotList 3 卡片示例）。</summary>
        private static DataTemplate CreateWindowTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var ww = new FrameworkElementFactory(typeof(WindowPreview));
            ww.SetBinding(WindowPreview.WindowWidgetProperty, new Binding("."));
            ww.SetBinding(WindowPreview.WidthProperty, new Binding("Width"));
            ww.SetBinding(WindowPreview.HeightProperty, new Binding("Height"));
            ww.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });   // 选中视觉 + ResizeAdorner
            ww.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, pmLBD);
            ww.AddHandler(UIElement.MouseLeftButtonDownEvent, mLBD);
            ww.AddHandler(UIElement.MouseMoveEvent, mMove);
            ww.AddHandler(UIElement.MouseLeftButtonUpEvent, mLBU);
            ww.AddHandler(UIElement.PreviewMouseRightButtonDownEvent, pmRBD);
            ww.AddHandler(UIElement.MouseRightButtonUpEvent, mRBU);
            dt.VisualTree = ww;
            return dt;
        }

        private static DataTemplate CreateDateTimeTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));   // D6：未绑定实时时钟/绑定基准值
            var fg = new MultiBinding { Converter = new SafeTextColorConverter() };   // TextColor + FillColor（与其他文本控件一致）
            fg.Bindings.Add(new Binding("TextColor"));
            fg.Bindings.Add(new Binding("FillColor"));
            tb.SetBinding(TextBlock.ForegroundProperty, fg);
            tb.SetValue(TextBlock.BackgroundProperty, System.Windows.Media.Brushes.White);   // D8：白底可读（与画布区分）
            tb.SetValue(TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            tb.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            dt.VisualTree = WrapWithBorder(tb, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
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

        /// <summary>世界地图批 3：多边形模板——Polygon 形状绑定顶点/颜色（P7：Points 为画布绝对坐标，
        /// converter 转相对包围盒偏移；Border W/H 命中框 = 包围盒）。</summary>
        private static DataTemplate CreatePolygonTemplate(RoutedEventHandler click, MouseButtonEventHandler pmLBD, MouseButtonEventHandler mLBD,
            MouseEventHandler mMove, MouseButtonEventHandler mLBU, MouseButtonEventHandler pmRBD, MouseButtonEventHandler mRBU)
        {
            var dt = new DataTemplate();
            var canvas = new FrameworkElementFactory(typeof(Canvas));
            var poly = new FrameworkElementFactory(typeof(Polygon));
            poly.SetBinding(Polygon.PointsProperty, new Binding("Points") { Converter = new PointCollectionConverter() });
            poly.SetBinding(Polygon.FillProperty, new Binding("FillColor") { Converter = new ColorStringToBrushConverter() });
            poly.SetBinding(Polygon.StrokeProperty, new Binding("StrokeColor") { Converter = new ColorStringToBrushConverter() });
            poly.SetBinding(Polygon.StrokeThicknessProperty, new Binding("StrokeThickness"));
            canvas.AppendChild(poly);
            dt.VisualTree = WrapWithBorder(canvas, click, pmLBD, mLBD, mMove, mLBU, pmRBD, mRBU);
            return dt;
        }
    }

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

    /// <summary>布尔取反转换器（IsEnabled = !IsAiThinking 等反向绑定用）。</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b ? !b : value;
    }

    /// <summary>始终返回 0 的转换器（用于 Line 的 X1/Y1 起点归零）。</summary>
    public class ZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => 0.0;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>List&lt;PointD&gt; → PointCollection（多边形顶点渲染）。P7：顶点为画布绝对坐标，
    /// 输出转相对包围盒偏移（减去 min）——ItemContainerStyle 的 Canvas.Left=X（包围盒左上）+ 相对点 = 绝对位置，防双加。</summary>
    public class PointCollectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var pts = new PointCollection();
            if (value is System.Collections.IEnumerable list)
            {
                var items = list.Cast<object>().ToList();
                if (items.Count > 0 && items[0] is PointD)
                {
                    double minX = items.Cast<PointD>().Min(p => p.X);
                    double minY = items.Cast<PointD>().Min(p => p.Y);
                    foreach (PointD pd in items)
                        pts.Add(new Point(pd.X - minX, pd.Y - minY));
                    return pts;
                }
            }
            return pts;
        }
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

    /// <summary>
    /// 安全文本色转换器（MultiBinding: TextColor + FillColor）。
    /// 文本色为空/透明/与背景色相同 → 回退黑色，保证文字始终可见。
    /// </summary>
    public class SafeTextColorConverter : IMultiValueConverter
    {
        private static readonly BrushConverter _brushConverter = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string textColor = values.Length > 0 ? values[0]?.ToString() ?? "" : "";
            string fillColor = values.Length > 1 ? values[1]?.ToString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(textColor)
             || textColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase)
             || textColor.Equals(fillColor, StringComparison.OrdinalIgnoreCase))
                return Brushes.Black;

            try { return (Brush)_brushConverter.ConvertFrom(textColor)!; }
            catch (FormatException) { return Brushes.Black; }
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>TextDecoration 字符串（None/Underline）→ TextDecorationCollection 转换器。</summary>
    public class TextDecorationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value?.ToString().Equals("Underline", StringComparison.OrdinalIgnoreCase) == true
                ? TextDecorations.Underline : null;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Switch 显示文本：DisplayIsOn → OnText/OffText（MultiBinding；绑定变量时状态取基准值判定）。</summary>
    public class SwitchTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            bool isOn = values.Length > 0 && values[0] is bool b && b;
            return isOn ? (values.Length > 1 ? values[1]?.ToString() ?? "ON" : "ON")
                        : (values.Length > 2 ? values[2]?.ToString() ?? "OFF" : "OFF");
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 进度条填充样式转换器（MultiBinding: FillStyle + FillColor）。
    /// Solid=实心；Diagonal=斜线花纹；Grid=方格花纹（DrawingBrush 平铺，TileMode.Tile）。
    /// </summary>
    public class PatternFillConverter : IMultiValueConverter
    {
        private static readonly BrushConverter _brushConverter = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string style = values.Length > 0 ? values[0]?.ToString() ?? "Solid" : "Solid";
            string color = values.Length > 1 ? values[1]?.ToString() ?? "#3399FF" : "#3399FF";
            Brush? colorBrush = null;
            try { colorBrush = (Brush)_brushConverter.ConvertFrom(color)!; }
            catch { colorBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)); }

            if (style == "Diagonal" || style == "Grid")
            {
                var group = new DrawingGroup();
                // 底色：浅色底（颜色淡化版）
                var baseColor = colorBrush is SolidColorBrush scb ? scb.Color
                    : Color.FromRgb(0x33, 0x99, 0xFF);
                var lightColor = Color.FromArgb(
                    baseColor.A,
                    (byte)(baseColor.R + (255 - baseColor.R) / 2),
                    (byte)(baseColor.G + (255 - baseColor.G) / 2),
                    (byte)(baseColor.B + (255 - baseColor.B) / 2));
                group.Children.Add(new GeometryDrawing(
                    new SolidColorBrush(lightColor),
                    null,
                    new RectangleGeometry(new Rect(0, 0, 14, 14))));

                var geo = new GeometryGroup();
                if (style == "Diagonal")
                {
                    geo.Children.Add(new LineGeometry(new Point(0, 14), new Point(14, 0)));
                    geo.Children.Add(new LineGeometry(new Point(0, 0), new Point(14, 14)));   // 交叉斜线
                }
                else
                {
                    geo.Children.Add(new RectangleGeometry(new Rect(0, 0, 14, 14)));          // 方格边框
                    geo.Children.Add(new RectangleGeometry(new Rect(7, 7, 14, 14)));          // 格线（7px 均匀方格网）
                }
                group.Children.Add(new GeometryDrawing(null, new Pen(colorBrush, 2.0), geo));

                var pattern = new DrawingBrush(group)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 14, 14),
                    ViewportUnits = BrushMappingMode.Absolute
                };
                pattern.Freeze();
                return pattern;
            }

            // Solid：直接返回颜色画刷（冻结防共享风险）
            colorBrush.Freeze();
            return colorBrush;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 完整路径 → ImageSource（列表管理面板图片预览用）。
    /// 仅接受已解析的完整文件路径（ListItemVM.PreviewImagePath 已做存在性校验）；
    /// StreamSource 读盘后立即释放文件句柄（用户可能随后重命名/删除文件）。加载失败返回 null（占位）。
    /// 不用 new Uri(path)：含 '#' 的文件名会被解析为 URI fragment 导致加载失败。
    /// </summary>
    public class PathToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not string path || path.Length == 0) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // 立即读盘，不占用文件句柄
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                bmp.StreamSource = fs;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // 文件损坏/非图片格式/无权限 → 降级返回 null，由 XAML 占位兜底（列表项仍可编辑路径）
                return null;
            }
        }

        public object? ConvertBack(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
