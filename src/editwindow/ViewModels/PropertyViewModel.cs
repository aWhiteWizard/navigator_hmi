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
        /// <summary>未选中任何对象。</summary>
        None,
        /// <summary>选中了画面 (Screen)。</summary>
        Screen,
        /// <summary>选中了按钮控件 (ButtonWidget)。</summary>
        ButtonWidget,
        /// <summary>选中了文本控件 (TextWidget)。</summary>
        TextWidget,
        /// <summary>选中了矩形控件 (RectangleWidget)。</summary>
        RectangleWidget
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
                        // 切换选中控件后刷新重复检测
                        RefreshDuplicateCheck();

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
        /// <summary>是否为 ButtonWidget。</summary>
        public bool IsButtonWidget => _selectedWidget is ButtonWidget;
        /// <summary>是否为 TextWidget。</summary>
        public bool IsTextWidget => _selectedWidget is TextWidget;
        /// <summary>是否为 RectangleWidget。</summary>
        public bool IsRectangleWidget => _selectedWidget is RectangleWidget;
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
