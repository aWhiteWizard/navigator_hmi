using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 颜色选择对话框：预设色板 + 透明选项 + 自定义 hex 输入。
    /// 选择结果通过 <see cref="SelectedColorHex"/> 返回（空串 = 透明）。
    /// </summary>
    public partial class ColorPickerDialog : Window
    {
        /// <summary>选中的颜色（#RRGGBB 或空串=透明）。</summary>
        public string SelectedColorHex { get; private set; } = "";

        private static readonly string[] PresetColors =
        {
            "#000000", "#404040", "#808080", "#C0C0C0", "#FFFFFF", "#EEEEEE",
            "#FF0000", "#8B0000", "#FFA500", "#FFFF00", "#808000", "#FFC0CB",
            "#008000", "#90EE90", "#00FFFF", "#0000FF", "#00008B", "#800080",
            "#A52A2A", "#000080", "#008080", "#FFD700", "#4682B4", "#D3D3D3"
        };

        private static readonly BrushConverter _brushConverter = new();

        public ColorPickerDialog(string initialHex)
        {
            InitializeComponent();
            SelectedColorHex = string.IsNullOrWhiteSpace(initialHex) ? "" : initialHex;

            // ItemsPanel = 8 列 UniformGrid
            var panel = new ItemsPanelTemplate();
            var factory = new FrameworkElementFactory(typeof(UniformGrid));
            factory.SetValue(UniformGrid.ColumnsProperty, 8);
            panel.VisualTree = factory;
            SwatchGrid.ItemsPanel = panel;

            // 生成色块
            foreach (var hex in PresetColors)
            {
                var border = new Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(2),
                    Background = (Brush)_brushConverter.ConvertFrom(hex)!,
                    BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Tag = hex
                };
                border.MouseLeftButtonDown += Swatch_MouseLeftButtonDown;
                SwatchGrid.Items.Add(border);
            }

            // 初始值回填预览（Transparent 与空串同分支：留空 + 透明预览）
            if (SelectedColorHex.Length > 0
             && !SelectedColorHex.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
            {
                HexBox.Text = SelectedColorHex.TrimStart('#');
                UpdatePreview(SelectedColorHex);
            }
            else
            {
                SelectedColorHex = "";
                HexBox.Text = "";
                HexPreview.Background = Brushes.Transparent;
            }
        }

        private void Swatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string hex)
            {
                SelectColor(hex);
            }
        }

        private void SelectColor(string hex)
        {
            SelectedColorHex = hex;
            UpdatePreview(hex);
        }

        private void TransparentBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectedColorHex = "";
            HexBox.Text = "";
            HexPreview.Background = Brushes.Transparent;
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
                HexPreview.Background = (Brush)_brushConverter.ConvertFrom(hex)!;
            }
            catch (FormatException)
            {
                HexPreview.Background = Brushes.White;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
