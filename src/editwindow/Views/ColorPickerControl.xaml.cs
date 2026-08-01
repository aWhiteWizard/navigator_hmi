using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 颜色选择控件：显示当前颜色色块，点击弹出 <see cref="ColorPickerDialog"/>（画图式 HSV 连续取色 + 透明 + 自定义 hex）。
    /// 通过 <see cref="Value"/> 依赖属性（TwoWay）与 ViewModel 颜色属性绑定。
    /// </summary>
    public partial class ColorPickerControl : UserControl
    {
        private static readonly BrushConverter _brushConverter = new();

        /// <summary>颜色值（#RRGGBB 或空串 = 透明）。默认 TwoWay 绑定。</summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value), typeof(string), typeof(ColorPickerControl),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public ColorPickerControl()
        {
            InitializeComponent();
            RefreshSwatch(Value);
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ColorPickerControl)d).RefreshSwatch(e.NewValue as string);

        /// <summary>刷新色块背景与 hex 文本（空串/透明显示棋盘格样式的浅色 + 文本"透明"）。</summary>
        private void RefreshSwatch(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Equals("Transparent", System.StringComparison.OrdinalIgnoreCase))
            {
                Swatch.Background = Brushes.Transparent;
                HexLabel.Text = "(透明)";
                HexLabel.Foreground = Brushes.Gray;
            }
            else
            {
                try
                {
                    Swatch.Background = (Brush)_brushConverter.ConvertFrom(hex)!;
                    HexLabel.Text = hex.ToUpperInvariant();
                    HexLabel.Foreground = Brushes.Black;
                }
                catch (FormatException)
                {
                    Swatch.Background = Brushes.White;
                    HexLabel.Text = hex;
                    HexLabel.Foreground = Brushes.Black;
                }
            }
        }

        private void Swatch_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var dialog = new ColorPickerDialog(Value) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true)
            {
                Value = dialog.SelectedColorHex;
            }
        }
    }
}
