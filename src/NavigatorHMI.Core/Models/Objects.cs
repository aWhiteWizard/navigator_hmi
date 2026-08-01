using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>画面类型</summary>
    public enum ScreenType
    {
        /// <summary>全局画面（固定存在，不可删除，叠加在所有画面上层）</summary>
        Template,
        /// <summary>世界地图（固定存在，不可删除，特殊地图渲染）</summary>
        WorldMap,
        /// <summary>自定义画面（用户创建，可增删）</summary>
        Custom
    }

    /// <summary>导航栏位置</summary>
    public enum NavPosition
    {
        /// <summary>顶部</summary>
        Top,
        /// <summary>底部</summary>
        Bottom
    }

    /// <summary>
    /// 画面定义。每个画面包含一组控件，按 Z-Order 排列（Widgets[0]=底层，末尾=顶层）。
    /// </summary>
    [ProtoContract]
    public class Screen : INotifyPropertyChanged
    {
        private string _name = "";

        /// <summary>画面名称（同一工程内唯一）</summary>
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        private double _width;

        /// <summary>画面宽度（像素）</summary>
        [ProtoMember(2)]
        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        private double _height;

        /// <summary>画面高度（像素）</summary>
        [ProtoMember(3)]
        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        /// <summary>画面类型</summary>
        [ProtoMember(4)]
        public ScreenType Type { get; set; }

        /// <summary>画面上的控件列表（Widgets[0] 最底层，末尾最顶层）</summary>
        [ProtoMember(5)]
        public ObservableCollection<Widget> Widgets { get; set; } = new();

        /// <summary>是否全局画面（工程中最多一个，全局控件叠加在所有画面上层）</summary>
        [ProtoMember(6)]
        public bool IsGlobal { get; set; } = false;

        /// <summary>是否在导航栏显示</summary>
        [ProtoMember(7)]
        public bool ShowInNav { get; set; } = true;

        /// <summary>导航栏排序权重（值越小越靠前）</summary>
        [ProtoMember(8)]
        public int NavOrder { get; set; }

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// ═══════════════════════════════════════════
// Widget 继承体系
// ═══════════════════════════════════════════

namespace NavigatorHMI.Common
{

/// <summary>
/// 控件抽象基类。定义所有可视控件的共同属性（位置、尺寸、名称、变量绑定、事件绑定）。
/// </summary>
/// <remarks>
/// ProtoInclude 编号从 100 开始，与 Widget 自身 ProtoMember(1-7) 不冲突。
/// 新增控件子类时必须追加 ProtoInclude 注册，否则 ProtoBuf 反序列化失败。
/// </remarks>
[ProtoContract]
[ProtoInclude(100, typeof(ButtonWidget))]
[ProtoInclude(101, typeof(TextWidget))]
[ProtoInclude(102, typeof(RectangleWidget))]
[ProtoInclude(103, typeof(LabelWidget))]
[ProtoInclude(104, typeof(ImageWidget))]
[ProtoInclude(105, typeof(NumericDisplayWidget))]
[ProtoInclude(106, typeof(SwitchWidget))]
[ProtoInclude(107, typeof(LineWidget))]
[ProtoInclude(108, typeof(CircleWidget))]
[ProtoInclude(109, typeof(IOFieldWidget))]
[ProtoInclude(110, typeof(CheckBoxWidget))]
[ProtoInclude(111, typeof(TextBoxWidget))]
[ProtoInclude(112, typeof(FrameWidget))]
[ProtoInclude(113, typeof(ProgressBarWidget))]
public abstract class Widget : INotifyPropertyChanged
{
    private double _x;

    /// <summary>控件左上角 X 坐标（相对画面左上角，像素）</summary>
    [ProtoMember(1)]
    public double X
    {
        get => _x;
        set { _x = Math.Round(value, 3); OnPropertyChanged(); }
    }

    private double _y;

    /// <summary>控件左上角 Y 坐标（相对画面左上角，像素）</summary>
    [ProtoMember(2)]
    public double Y
    {
        get => _y;
        set { _y = Math.Round(value, 3); OnPropertyChanged(); }
    }

    private double _width;

    /// <summary>控件宽度（像素）</summary>
    [ProtoMember(3)]
    public double Width
    {
        get => _width;
        set { _width = Math.Round(value, 3); OnPropertyChanged(); }
    }

    private double _height;

    /// <summary>控件高度（像素）</summary>
    [ProtoMember(4)]
    public double Height
    {
        get => _height;
        set { _height = Math.Round(value, 3); OnPropertyChanged(); }
    }

    private string _objectName = "";

    /// <summary>
    /// 控件 ObjectName（画面内唯一标识，用于 CLI/AI Agent 引用）。
    /// 编译时校验同画面内不可重复。
    /// </summary>
    [ProtoMember(5)]
    public string ObjectName
    {
        get => _objectName;
        set { _objectName = value; OnPropertyChanged(); }
    }

    /// <summary>绑定的变量名（可选，控件显示该变量的实时值）</summary>
    [ProtoMember(6)]
    public string BoundTag { get; set; } = "";

    /// <summary>事件-动作绑定列表。每个条目定义"什么事件→执行什么动作"。</summary>
    [ProtoMember(7)]
    public List<WidgetEvent> Events { get; set; } = new();

    private bool _isSelected;

    /// <summary>UI 选中状态（瞬态，不持久化）</summary>
    [ProtoIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>触发 PropertyChanged 事件</summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// 按钮控件。支持文本显示 + 点击事件绑定。
/// </summary>
[ProtoContract]
public class ButtonWidget : Widget
{
    private string _text = "";

    /// <summary>按钮显示文本</summary>
    [ProtoMember(1)]
    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// 文本标签控件。可绑定变量显示实时值，或静态显示固定文本。
/// </summary>
[ProtoContract]
public class TextWidget : Widget
{
    private string _content = "";

    /// <summary>文本内容（可被 BoundTag 覆盖为实时值）</summary>
    [ProtoMember(1)]
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// 矩形控件。用于装饰/背景，支持填充色。
/// </summary>
[ProtoContract]
public class RectangleWidget : Widget
{
    private string _fillColor = "#FFFFFF";

    /// <summary>填充颜色（CSS 格式，如 "#FF0000"）</summary>
    [ProtoMember(1)]
    public string FillColor
    {
        get => _fillColor;
        set { _fillColor = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// 标签控件。静态文本标签，支持字体大小/颜色/对齐方式。
/// 不绑定变量，仅用于静态标注。
/// </summary>
[ProtoContract]
public class LabelWidget : Widget
{
    private string _text = "Label";
    /// <summary>显示文本</summary>
    [ProtoMember(1)]
    public string Text { get => _text; set { _text = value; OnPropertyChanged(); } }

    private double _fontSize = 14;
    /// <summary>字体大小（像素）</summary>
    [ProtoMember(2)]
    public double FontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }

    private string _fontWeight = "Normal";
    /// <summary>字重：Normal / Bold</summary>
    [ProtoMember(3)]
    public string FontWeight { get => _fontWeight; set { _fontWeight = value; OnPropertyChanged(); } }

    private string _textColor = "#000000";
    /// <summary>文本颜色（CSS 格式，如 "#FF0000"）</summary>
    [ProtoMember(4)]
    public string TextColor { get => _textColor; set { _textColor = value; OnPropertyChanged(); } }

    private string _hAlign = "Left";
    /// <summary>水平对齐：Left / Center / Right</summary>
    [ProtoMember(5)]
    public string HAlign { get => _hAlign; set { _hAlign = value; OnPropertyChanged(); } }
}

/// <summary>
/// 图片控件。显示静态图片（PNG/JPEG/BMP），支持拉伸模式。
/// </summary>
[ProtoContract]
public class ImageWidget : Widget
{
    private string _imagePath = "";
    /// <summary>图片文件路径（相对工程目录或绝对路径）</summary>
    [ProtoMember(1)]
    public string ImagePath { get => _imagePath; set { _imagePath = value; OnPropertyChanged(); } }

    private string _stretchMode = "Uniform";
    /// <summary>拉伸模式：None / Fill / Uniform / UniformToFill</summary>
    [ProtoMember(2)]
    public string StretchMode { get => _stretchMode; set { _stretchMode = value; OnPropertyChanged(); } }
}

/// <summary>
/// 数值显示控件。绑定变量显示实时数值，支持前缀/后缀/小数位/字体/颜色。
/// </summary>
[ProtoContract]
public class NumericDisplayWidget : Widget
{
    private int _decimalPlaces = 1;
    /// <summary>小数位数</summary>
    [ProtoMember(1)]
    public int DecimalPlaces { get => _decimalPlaces; set { _decimalPlaces = value; OnPropertyChanged(); } }

    private string _prefix = "";
    /// <summary>数值前缀（如 "$" / "温度: "）</summary>
    [ProtoMember(2)]
    public string Prefix { get => _prefix; set { _prefix = value; OnPropertyChanged(); } }

    private string _suffix = "";
    /// <summary>数值后缀（如 "℃" / "rpm" / "%"）</summary>
    [ProtoMember(3)]
    public string Suffix { get => _suffix; set { _suffix = value; OnPropertyChanged(); } }

    private double _fontSize = 16;
    /// <summary>字体大小（像素）</summary>
    [ProtoMember(4)]
    public double FontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }

    private string _textColor = "#000000";
    /// <summary>文本颜色（CSS 格式）</summary>
    [ProtoMember(5)]
    public string TextColor { get => _textColor; set { _textColor = value; OnPropertyChanged(); } }
}

/// <summary>
/// 开关控件。双状态切换，支持自定义 ON/OFF 文本标签。
/// </summary>
[ProtoContract]
public class SwitchWidget : Widget
{
    private bool _isOn = false;
    /// <summary>当前开关状态（true=ON，false=OFF）</summary>
    [ProtoMember(1)]
    public bool IsOn { get => _isOn; set { _isOn = value; OnPropertyChanged(); } }

    private string _onText = "ON";
    /// <summary>ON 状态显示的文本</summary>
    [ProtoMember(2)]
    public string OnText { get => _onText; set { _onText = value; OnPropertyChanged(); } }

    private string _offText = "OFF";
    /// <summary>OFF 状态显示的文本</summary>
    [ProtoMember(3)]
    public string OffText { get => _offText; set { _offText = value; OnPropertyChanged(); } }
}

/// <summary>
/// 直线控件。用于画面装饰和分隔。
/// </summary>
/// <remarks>
/// X2/Y2 是直线终点相对于 Widget 左上角 (X,Y) 的偏移量，即绝对终点 = (X+X2, Y+Y2)。
/// Width/Height 定义控件边界框，仅用于点击检测和选中高亮，不影响直线渲染。
/// </remarks>
[ProtoContract]
public class LineWidget : Widget
{
    private double _x2 = 100;
    /// <summary>终点 X 偏移（相对 Widget.X，像素）</summary>
    [ProtoMember(1)]
    public double X2 { get => _x2; set { _x2 = Math.Round(value, 3); OnPropertyChanged(); } }

    private double _y2 = 0;
    /// <summary>终点 Y 偏移（相对 Widget.Y，像素）</summary>
    [ProtoMember(2)]
    public double Y2 { get => _y2; set { _y2 = Math.Round(value, 3); OnPropertyChanged(); } }

    private string _strokeColor = "#000000";
    /// <summary>线条颜色（CSS 格式）</summary>
    [ProtoMember(3)]
    public string StrokeColor { get => _strokeColor; set { _strokeColor = value; OnPropertyChanged(); } }

    private double _strokeThickness = 1;
    /// <summary>线条粗细（像素）</summary>
    [ProtoMember(4)]
    public double StrokeThickness { get => _strokeThickness; set { _strokeThickness = Math.Round(value, 3); OnPropertyChanged(); } }
}

/// <summary>
/// 圆形/椭圆控件。用于装饰或状态指示，支持填充色和边框。
/// 当 Width == Height 时为正圆，否则为椭圆。
/// </summary>
[ProtoContract]
public class CircleWidget : Widget
{
    private string _fillColor = "#FFFFFF";
    /// <summary>填充颜色（CSS 格式）</summary>
    [ProtoMember(1)]
    public string FillColor { get => _fillColor; set { _fillColor = value; OnPropertyChanged(); } }

    private string _strokeColor = "#000000";
    /// <summary>边框颜色（CSS 格式）</summary>
    [ProtoMember(2)]
    public string StrokeColor { get => _strokeColor; set { _strokeColor = value; OnPropertyChanged(); } }

    private double _strokeThickness = 1;
    /// <summary>边框粗细（像素）</summary>
    [ProtoMember(3)]
    public double StrokeThickness { get => _strokeThickness; set { _strokeThickness = Math.Round(value, 3); OnPropertyChanged(); } }
}

/// <summary>
/// IO 域控件。双向数据绑定输入输出框，支持只读模式切换。
/// 运行时：只读模式仅显示，可写模式允许用户输入。
/// </summary>
[ProtoContract]
public class IOFieldWidget : Widget
{
    private string _content = "";
    /// <summary>显示/输入内容</summary>
    [ProtoMember(1)]
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }

    private bool _isReadOnly = false;
    /// <summary>是否只读（true=仅显示，false=可编辑）</summary>
    [ProtoMember(2)]
    public bool IsReadOnly { get => _isReadOnly; set { _isReadOnly = value; OnPropertyChanged(); } }
}

/// <summary>
/// 复选框控件。支持选中/未选中状态及文本标签，可绑定布尔变量。
/// </summary>
[ProtoContract]
public class CheckBoxWidget : Widget
{
    private bool _isChecked = false;
    /// <summary>选中状态</summary>
    [ProtoMember(1)]
    public bool IsChecked { get => _isChecked; set { _isChecked = value; OnPropertyChanged(); } }

    private string _text = "CheckBox";
    /// <summary>复选框旁显示的文本标签</summary>
    [ProtoMember(2)]
    public string Text { get => _text; set { _text = value; OnPropertyChanged(); } }
}

/// <summary>
/// 文本框控件。用户输入文本，支持密码模式（内容以 * 显示）。
/// </summary>
[ProtoContract]
public class TextBoxWidget : Widget
{
    private string _content = "";
    /// <summary>文本框内容</summary>
    [ProtoMember(1)]
    public string Content { get => _content; set { _content = value; OnPropertyChanged(); } }

    private bool _isPassword = false;
    /// <summary>是否密码模式（true=显示掩码字符）</summary>
    [ProtoMember(2)]
    public bool IsPassword { get => _isPassword; set { _isPassword = value; OnPropertyChanged(); } }
}

/// <summary>
/// 框架控件。带标题的容器框，用于视觉分组其它控件，支持背景图片。
/// </summary>
[ProtoContract]
public class FrameWidget : Widget
{
    private string _title = "Group";
    /// <summary>框架标题文本</summary>
    [ProtoMember(1)]
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    private string _fillColor = "#FFFFFF";
    /// <summary>框架背景色（CSS 格式）</summary>
    [ProtoMember(2)]
    public string FillColor { get => _fillColor; set { _fillColor = value; OnPropertyChanged(); } }

    private string _imagePath = "";
    /// <summary>框架背景图片路径（相对工程目录或绝对路径，空则仅显示标题框）</summary>
    [ProtoMember(3)]
    public string ImagePath { get => _imagePath; set { _imagePath = value; OnPropertyChanged(); } }
}

/// <summary>
/// 进度条控件。绑定变量显示进度百分比或数值，支持自定义范围和填充色。
/// </summary>
[ProtoContract]
public class ProgressBarWidget : Widget
{
    private double _value = 0;
    /// <summary>当前进度值（在 Min-Max 范围内）</summary>
    [ProtoMember(1)]
    public double Value { get => _value; set { _value = value; OnPropertyChanged(); } }

    private double _min = 0;
    /// <summary>最小值</summary>
    [ProtoMember(2)]
    public double Min { get => _min; set { _min = value; OnPropertyChanged(); } }

    private double _max = 100;
    /// <summary>最大值</summary>
    [ProtoMember(3)]
    public double Max { get => _max; set { _max = value; OnPropertyChanged(); } }

    private string _fillColor = "#3399FF";
    /// <summary>进度条填充色（CSS 格式）</summary>
    [ProtoMember(4)]
    public string FillColor { get => _fillColor; set { _fillColor = value; OnPropertyChanged(); } }
}

} // namespace NavigatorHMI.Common
