using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NavigatorHMI.Common;
using NavigatorHMI.AiAgent;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// AI 助手设置窗口：配置云端 API Key（DPAPI 加密存储）+ 命令黑名单（K-6 安全闸，GUI 可配增删）。
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
            foreach (var name in cfg.Blacklist)
                BlacklistBox.Items.Add(name);
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

        private void AddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var name = BlacklistAddBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (BlacklistBox.Items.Cast<string>().Any(i => string.Equals(i, name, StringComparison.OrdinalIgnoreCase)))
            {
                BlacklistAddBox.Clear();
                return;   // 大小写不敏感去重（命令名全小写，防 "DEPLOY_PROJECT" 重复且永不命中）
            }
            BlacklistBox.Items.Add(name);
            BlacklistAddBox.Clear();
        }

        private void RemoveBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var sel = BlacklistBox.SelectedItem as string;
            if (sel != null) BlacklistBox.Items.Remove(sel);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var cfg = AiConfigStore.Load();
            cfg.ApiKey = ApiKeyBox.Password;   // 空 = 清除（回退环境变量）
            cfg.LocalModelPath = ModelPathBox.Text.Trim();   // P3-7：模型路径一并保存
            cfg.Blacklist = BlacklistBox.Items.Cast<string>().ToList();   // K-6：黑名单保存 + 注入 AIAgent
            if (AiConfigStore.Save(cfg))
            {
                AIAgent.SetBlacklist(cfg.Blacklist);
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("配置保存失败（无权限或磁盘不可写）。\nAPI Key 未保存。", "AI 助手设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
