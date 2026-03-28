using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NavigatorHMI.Common
{
    [ProtoContract]
    public class Screen : INotifyPropertyChanged
    {
        private string _name;
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private double _width;
        [ProtoMember(2)]
        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        private double _height;
        [ProtoMember(3)]
        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        // 画面上的控件列表
        [ProtoMember(4)]
        public List<Widget> Widgets { get; set; } = new List<Widget>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    }
}

[ProtoContract]
[ProtoInclude(100, typeof(ButtonWidget))]
[ProtoInclude(101, typeof(TextWidget))]
[ProtoInclude(102, typeof(RectangleWidget))]
public abstract class Widget : INotifyPropertyChanged
{
    private double _x;
    [ProtoMember(1)]
    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    private double _y;
    [ProtoMember(2)]
    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    private double _width;
    [ProtoMember(3)]
    public double Width
    {
        get => _width;
        set { _width = value; OnPropertyChanged(); }
    }

    private double _height;
    [ProtoMember(4)]
    public double Height
    {
        get => _height;
        set { _height = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

[ProtoContract]
public class ButtonWidget : Widget
{
    private string _text;
    [ProtoMember(1)]
    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }
}

[ProtoContract]
public class TextWidget : Widget
{
    private string _content;
    [ProtoMember(1)]
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }
}

[ProtoContract]
public class RectangleWidget : Widget
{
    private string _fillColor;
    [ProtoMember(1)]
    public string FillColor
    {
        get => _fillColor;
        set { _fillColor = value; OnPropertyChanged(); }
    }
}