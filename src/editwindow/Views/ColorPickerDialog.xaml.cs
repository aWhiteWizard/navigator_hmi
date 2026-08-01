using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 颜色选择对话框：Windows 画图式连续取色（HSV 渐变板：水平=色相，垂直=明暗）+ 透明 + 自定义 hex。
    /// 选择结果通过 <see cref="SelectedColorHex"/> 返回（空串 = 透明）。
    /// </summary>
    public partial class ColorPickerDialog : Window
    {
        /// <summary>选中的颜色（#RRGGBB 或空串=透明）。</summary>
        public string SelectedColorHex { get; private set; } = "";

        private static readonly BrushConverter _brushConverter = new();

        public ColorPickerDialog(string initialHex)
        {
            InitializeComponent();
            SelectedColorHex = string.IsNullOrWhiteSpace(initialHex) ? "" : initialHex;
            if (SelectedColorHex.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                SelectedColorHex = "";

            BuildColorBoard();

            // 初始值回填预览（透明 = 留空）
            if (SelectedColorHex.Length > 0)
            {
                HexBox.Text = SelectedColorHex.TrimStart('#');
                UpdatePreview(SelectedColorHex);
            }
            else
            {
                HexBox.Text = "";
                HexPreview.Background = Brushes.Transparent;
                CurrentPreview.Background = Brushes.Transparent;
            }
        }

        /// <summary>绘制 HSV 渐变板（水平色相 0-360°，垂直明暗 上亮下暗，饱和度固定 1）。</summary>
        private void BuildColorBoard()
        {
            int w = 220, h = 140;
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
            var pixels = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
            {
                double v = 1.0 - (double)y / h;   // 顶部亮、底部暗
                for (int x = 0; x < w; x++)
                {
                    double hue = (double)x / w * 360.0;
                    var (r, g, b) = HsvToRgb(hue, 1.0, v);
                    int idx = (y * w + x) * 3;
                    pixels[idx] = b;      // Bgr24: B, G, R
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = r;
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 3, 0);
            ColorBoard.Background = new ImageBrush(bmp) { Stretch = Stretch.Fill };
        }

        /// <summary>HSV → RGB（h 0-360, s/v 0-1）。</summary>
        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double hp = (h % 360) / 60.0;
            double x = c * (1 - Math.Abs(hp % 2 - 1));
            (double r, double g, double b) rgb = hp switch
            {
                < 1 => (c, x, 0),
                < 2 => (x, c, 0),
                < 3 => (0, c, x),
                < 4 => (0, x, c),
                < 5 => (x, 0, c),
                _ => (c, 0, x)
            };
            double m = v - c;
            return ((byte)Math.Clamp((rgb.r + m) * 255, 0, 255),
                    (byte)Math.Clamp((rgb.g + m) * 255, 0, 255),
                    (byte)Math.Clamp((rgb.b + m) * 255, 0, 255));
        }

        private void Board_MouseDown(object sender, MouseButtonEventArgs e)
        {
            PickColor(e.GetPosition(ColorBoard));
            ColorBoard.CaptureMouse();
        }

        private void Board_MouseMove(object sender, MouseEventArgs e)
        {
            // 按住拖动连续取色（画图式）
            if (e.LeftButton == MouseButtonState.Pressed)
                PickColor(e.GetPosition(ColorBoard));
        }

        /// <summary>按坐标取色：x→色相(0-360°)，y→明暗(顶亮底暗)。</summary>
        private void PickColor(Point pos)
        {
            double w = ColorBoard.ActualWidth > 0 ? ColorBoard.ActualWidth : 220;
            double h = ColorBoard.ActualHeight > 0 ? ColorBoard.ActualHeight : 140;
            pos.X = Math.Clamp(pos.X, 0, w);
            pos.Y = Math.Clamp(pos.Y, 0, h);

            double hue = pos.X / w * 360.0;
            double v = 1.0 - pos.Y / h;
            var (r, g, b) = HsvToRgb(hue, 1.0, v);
            var hex = $"#{r:X2}{g:X2}{b:X2}";
            SelectedColorHex = hex;
            HexBox.Text = hex.TrimStart('#');
            UpdatePreview(hex);
        }

        private void TransparentBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedColorHex = "";
            HexBox.Text = "";
            HexPreview.Background = Brushes.Transparent;
            CurrentPreview.Background = Brushes.Transparent;
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = HexBox.Text.Trim().TrimStart('#');
            if (text.Length == 6 && text.All(Uri.IsHexDigit))
            {
                var hex = "#" + text.ToUpperInvariant();
                SelectedColorHex = hex;
                UpdatePreview(hex);
            }
        }

        private void UpdatePreview(string hex)
        {
            try
            {
                var brush = (Brush)_brushConverter.ConvertFrom(hex)!;
                HexPreview.Background = brush;
                CurrentPreview.Background = brush;
            }
            catch (FormatException)
            {
                HexPreview.Background = Brushes.White;
                CurrentPreview.Background = Brushes.White;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
