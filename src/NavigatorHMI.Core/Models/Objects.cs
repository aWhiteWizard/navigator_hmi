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

} // namespace NavigatorHMI.Common
