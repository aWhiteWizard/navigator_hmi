using System;
using System.IO;
using System.Text.Json;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 控件工厂默认字体配置（全局）。所有控件模型的字体字段默认值从此读取——
    /// 修改后新添加的控件统一采用新默认字体。持久化到 %APPDATA%\NavigatorHMI\font_defaults.json。
    /// </summary>
    public static class WidgetFontDefaults
    {
        private static string _fontFamily = "Microsoft YaHei UI";
        /// <summary>默认字体族。</summary>
        public static string FontFamily { get => _fontFamily; set => _fontFamily = value; }

        private static double _fontSize = 14;
        /// <summary>默认字号（像素）。</summary>
        public static double FontSize { get => _fontSize; set => _fontSize = value; }

        private static string _fontWeight = "Normal";
        /// <summary>默认字重：Normal / Bold。</summary>
        public static string FontWeight { get => _fontWeight; set => _fontWeight = value; }

        private static string _fontStyle = "Normal";
        /// <summary>默认字型：Normal / Italic。</summary>
        public static string FontStyle { get => _fontStyle; set => _fontStyle = value; }

        private static string _textDecoration = "None";
        /// <summary>默认下划线：None / Underline。</summary>
        public static string TextDecoration { get => _textDecoration; set => _textDecoration = value; }

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavigatorHMI", "font_defaults.json");

        static WidgetFontDefaults()
        {
            Load();
        }

        /// <summary>从配置文件加载（不存在/损坏时保持默认值，不阻塞）。</summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var data = JsonSerializer.Deserialize<FontDefaultsData>(File.ReadAllText(FilePath));
                if (data == null) return;
                if (!string.IsNullOrWhiteSpace(data.FontFamily)) _fontFamily = data.FontFamily;
                if (data.FontSize > 0) _fontSize = data.FontSize;
                if (!string.IsNullOrWhiteSpace(data.FontWeight)) _fontWeight = data.FontWeight;
                if (!string.IsNullOrWhiteSpace(data.FontStyle)) _fontStyle = data.FontStyle;
                if (!string.IsNullOrWhiteSpace(data.TextDecoration)) _textDecoration = data.TextDecoration;
            }
            catch (Exception ex)
            {
                // 配置文件损坏 → 保持默认值
                System.Diagnostics.Debug.WriteLine($"[WidgetFontDefaults] Load 失败: {ex.Message}");
            }
        }

        /// <summary>保存到配置文件（失败静默）。</summary>
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new FontDefaultsData
                {
                    FontFamily = _fontFamily,
                    FontSize = _fontSize,
                    FontWeight = _fontWeight,
                    FontStyle = _fontStyle,
                    TextDecoration = _textDecoration
                }));
            }
            catch (Exception ex)
            {
                // 持久化失败不阻塞
                System.Diagnostics.Debug.WriteLine($"[WidgetFontDefaults] Save 失败: {ex.Message}");
            }
        }

        private class FontDefaultsData
        {
            public string FontFamily { get; set; } = "";
            public double FontSize { get; set; } = 0;
            public string FontWeight { get; set; } = "";
            public string FontStyle { get; set; } = "";
            public string TextDecoration { get; set; } = "";
        }
    }
}
