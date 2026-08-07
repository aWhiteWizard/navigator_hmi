using System;
using System.Windows;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// AI 助手设置窗口：配置云端 API Key（DPAPI 加密存储）。
    /// 密码框不回填明文（已配置时仅提示）；留空保存 = 清除已存 Key（回退环境变量）。
    /// </summary>
    public partial class AiSettingsWindow : Window
    {
        public AiSettingsWindow()
        {
            InitializeComponent();
            var cfg = AiConfigStore.Load();
            HasKeyHint.Text = string.IsNullOrEmpty(cfg.ApiKey)
                ? "（当前未配置，将使用环境变量）"
                : "（已配置 Key：输入新值可替换；留空保存 = 清除）";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = AiConfigStore.Load();
            cfg.ApiKey = ApiKeyBox.Password;   // 空 = 清除（回退环境变量）
            if (AiConfigStore.Save(cfg))
                DialogResult = true;
            else
                MessageBox.Show("配置保存失败（无权限或磁盘不可写）。\nAPI Key 未保存。", "AI 助手设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
