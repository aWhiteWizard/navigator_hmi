using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 属性窗口的 ViewModel，持有当前选中的 Widget 并暴露其属性供绑定。
    /// </summary>
    public class PropertyViewModel : INotifyPropertyChanged
    {
        private Widget? _selectedWidget;

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
                    _selectedWidget = value;
                    // 订阅新 widget 的 PropertyChanged（实时同步 ResizeAdorner 的修改）
                    if (value != null)
                        value.PropertyChanged += OnSelectedWidgetPropertyChanged;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPropertyVisible));
                    OnPropertyChanged(nameof(IsButtonWidget));
                    OnPropertyChanged(nameof(IsTextWidget));
                    OnPropertyChanged(nameof(IsRectangleWidget));
                    OnPropertyChanged(nameof(WidgetTypeName));

                    // 切换选中控件时，同步 ViewModel 的属性值
                    if (value != null)
                    {
                        X = value.X;
                        Y = value.Y;
                        Width = value.Width;
                        Height = value.Height;
                        ObjectName = value.ObjectName;

                        if (value is ButtonWidget btn)
                            ButtonText = btn.Text;
                        else if (value is TextWidget txt)
                            TextContent = txt.Content;
                        else if (value is RectangleWidget rect)
                            FillColor = rect.FillColor;
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
                    break;
            }
        }


        /// <summary>是否有 Widget 被选中（属性面板可见性控制）。</summary>
        public bool IsPropertyVisible => _selectedWidget != null;

        /// <summary>当前 Widget 的类型名称（用于标题显示）。</summary>
        public string WidgetTypeName => _selectedWidget?.GetType().Name ?? "";

        /// <summary>是否为 ButtonWidget。</summary>
        public bool IsButtonWidget => _selectedWidget is ButtonWidget;
        /// <summary>是否为 TextWidget。</summary>
        public bool IsTextWidget => _selectedWidget is TextWidget;
        /// <summary>是否为 RectangleWidget。</summary>
        public bool IsRectangleWidget => _selectedWidget is RectangleWidget;

        // ── 绑定的属性字段 ──

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
                }
            }
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

        private string _fillColor = "";
        public string FillColor
        {
            get => _fillColor;
            set
            {
                if (_fillColor != value)
                {
                    _fillColor = value;
                    OnPropertyChanged();
                    if (_selectedWidget is RectangleWidget rect) rect.FillColor = value;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
