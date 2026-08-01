using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

            LoadRecentColors();
        }

        /// <summary>加载最近使用颜色到快捷栏（无记录时隐藏；非法色值跳过防构造崩溃）。</summary>
        private void LoadRecentColors()
        {
            var recent = RecentColorStore.Load();
            if (recent.Count == 0) return;
            RecentPanel.Visibility = Visibility.Visible;

            var panel = new ItemsPanelTemplate();
            var factory = new FrameworkElementFactory(typeof(UniformGrid));
            // 10 列 × 2 行 = 20 个最近色一行显示 10 个、两行全部放下（窗口适度拉长）
            factory.SetValue(UniformGrid.ColumnsProperty, 10);
            panel.VisualTree = factory;
            RecentGrid.ItemsPanel = panel;

            foreach (var hex in recent)
            {
                // 校验 #RRGGBB 格式（手改 JSON 可能含非法值，跳过避免 BrushConverter 抛异常）
                if (hex.Length != 7 || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit))
                    continue;
                var border = new Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(2),
                    Background = (Brush)_brushConverter.ConvertFrom(hex)!,
                    BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, Tag = hex
                };
                border.MouseLeftButtonDown += Swatch_MouseLeftButtonDown;
                RecentGrid.Items.Add(border);
            }
        }

        /// <summary>点击色块（最近使用栏）选择颜色。</summary>
        private void Swatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string hex)
            {
                SelectedColorHex = hex;
                HexBox.Text = hex.TrimStart('#');
                UpdatePreview(hex);
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

        /// <summary>松开鼠标释放捕获（否则后续按钮点击被取色板拦截）。</summary>
        private void Board_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ColorBoard.ReleaseMouseCapture();
        }

        /// <summary>鼠标移出取色板且未按住时释放捕获（防残留拦截）。</summary>
        private void Board_MouseLeave(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                ColorBoard.ReleaseMouseCapture();
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
            // 记住选中的颜色（非透明）到最近使用栏
            if (SelectedColorHex.Length > 0)
                RecentColorStore.Add(SelectedColorHex);
            DialogResult = true;
        }
    }
}
