using System.Windows;
using System.Windows.Controls;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 帮助对话框：快捷键 / 操作指南 / CLI 命令 三个分页。
    /// 快捷键数据代码填充（DataGrid 绑定），CLI 命令文本与 CLI 控制台 help 一致。
    /// </summary>
    public partial class HelpDialog : Window
    {
        public HelpDialog()
        {
            InitializeComponent();
            LoadShortcuts();
            LoadCliHelp();
        }

        /// <summary>填充快捷键表。</summary>
        private void LoadShortcuts()
        {
            var items = new[]
            {
                new ShortcutInfo("Ctrl+A", "全选当前画面控件"),
                new ShortcutInfo("Ctrl+C", "复制选中控件"),
                new ShortcutInfo("Ctrl+X", "剪切选中控件（可粘贴还原）"),
                new ShortcutInfo("Ctrl+V", "粘贴控件"),
                new ShortcutInfo("Delete", "删除选中控件"),
                new ShortcutInfo("← → ↑ ↓", "微移选中控件 1px（选中状态保持）"),
                new ShortcutInfo("Shift+方向键", "微移 10px"),
                new ShortcutInfo("Ctrl+Z", "撤销"),
                new ShortcutInfo("Ctrl+Y / Ctrl+Shift+Z", "重做"),
                new ShortcutInfo("Ctrl+S", "保存工程"),
                new ShortcutInfo("ESC", "退出添加控件模式 / 取消两点式绘制"),
                new ShortcutInfo("F1", "打开本帮助"),
            };
            foreach (var s in items)
                ShortcutGrid.Items.Add(s);
        }

        /// <summary>填充 CLI 命令文本（与 CLI 控制台 help 共用单一来源）。</summary>
        private void LoadCliHelp()
        {
            CliHelpTextBlock.Text = NavigatorHMI.Common.CliHelpContent.Text;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>快捷键表行数据。</summary>
        private sealed class ShortcutInfo
        {
            public string Shortcut { get; }
            public string Desc { get; }
            public ShortcutInfo(string shortcut, string desc) { Shortcut = shortcut; Desc = desc; }
        }
    }
}
