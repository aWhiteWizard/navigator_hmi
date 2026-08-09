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
            ModelPathBox.Text = cfg.LocalModelPath;   // P3-7：模型路径移设置窗
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择本地 GGUF 模型",
                Filter = "GGUF 模型 (*.gguf)|*.gguf|所有文件 (*.*)|*.*",
            };
            if (!string.IsNullOrEmpty(ModelPathBox.Text))
            {
                var dir = System.IO.Path.GetDirectoryName(ModelPathBox.Text);
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            if (dlg.ShowDialog(this) == true)
                ModelPathBox.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = AiConfigStore.Load();
            cfg.ApiKey = ApiKeyBox.Password;   // 空 = 清除（回退环境变量）
            cfg.LocalModelPath = ModelPathBox.Text.Trim();   // P3-7：模型路径一并保存
            if (AiConfigStore.Save(cfg))
                DialogResult = true;
            else
                MessageBox.Show("配置保存失败（无权限或磁盘不可写）。\nAPI Key 未保存。", "AI 助手设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
