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
                    _selectedWidget = value;
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
