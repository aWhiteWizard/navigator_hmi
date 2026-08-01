using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 工厂默认字体设置对话框：字体/字号/加粗/倾斜/下划线，实时预览。
    /// 确定时写入 <see cref="WidgetFontDefaults"/> 并持久化。
    /// </summary>
    public partial class FontDefaultsDialog : Window
    {
        /// <summary>字号合法范围。</summary>
        private const double MinFontSize = 4, MaxFontSize = 300;

        public FontDefaultsDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadCurrentValues();   // ItemsSource（ODP 绑定）Loaded 后才激活，构造器赋值会失败
            FontFamilyBox.SelectionChanged += (_, _) => RefreshPreview();
            FontSizeBox.TextChanged += (_, _) => RefreshPreview();
            BoldBox.Click += (_, _) => RefreshPreview();
            ItalicBox.Click += (_, _) => RefreshPreview();
            UnderlineBox.Click += (_, _) => RefreshPreview();
        }

        /// <summary>加载当前配置到 UI（Loaded 后执行，确保字体下拉 ItemsSource 已激活）。</summary>
        private void LoadCurrentValues()
        {
            FontFamilyBox.SelectedValue = WidgetFontDefaults.FontFamily;
            FontSizeBox.Text = WidgetFontDefaults.FontSize.ToString("0.#");
            BoldBox.IsChecked = WidgetFontDefaults.FontWeight == "Bold";
            ItalicBox.IsChecked = WidgetFontDefaults.FontStyle == "Italic";
            UnderlineBox.IsChecked = WidgetFontDefaults.TextDecoration == "Underline";
            RefreshPreview();
        }

        /// <summary>刷新预览（字号非法/越界时回退 18）。</summary>
        private void RefreshPreview()
        {
            try
            {
                var family = FontFamilyBox.SelectedValue as string;
                PreviewText.FontFamily = string.IsNullOrEmpty(family) ? null : new FontFamily(family);
                double size = ParseClampedSize();
                PreviewText.FontSize = size > 0 ? size : 18;
                PreviewText.FontWeight = BoldBox.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
                PreviewText.FontStyle = ItalicBox.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
                PreviewText.TextDecorations = UnderlineBox.IsChecked == true ? TextDecorations.Underline : null;
            }
            catch
            {
                // 字体名非法 → 预览保持默认
            }
        }

        /// <summary>解析字号并钳制到合法范围（非法返回 0）。</summary>
        private double ParseClampedSize()
        {
            if (!double.TryParse(FontSizeBox.Text, out var size)) return 0;
            return Math.Clamp(size, MinFontSize, MaxFontSize);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // SelectedValue 为 null（下拉未激活/未选择）时保持原配置，不覆盖
            var family = FontFamilyBox.SelectedValue as string;
            if (!string.IsNullOrEmpty(family))
                WidgetFontDefaults.FontFamily = family;

            double size = ParseClampedSize();
            if (size > 0)
                WidgetFontDefaults.FontSize = size;

            WidgetFontDefaults.FontWeight = BoldBox.IsChecked == true ? "Bold" : "Normal";
            WidgetFontDefaults.FontStyle = ItalicBox.IsChecked == true ? "Italic" : "Normal";
            WidgetFontDefaults.TextDecoration = UnderlineBox.IsChecked == true ? "Underline" : "None";
            WidgetFontDefaults.Save();
            DialogResult = true;
        }
    }
}
