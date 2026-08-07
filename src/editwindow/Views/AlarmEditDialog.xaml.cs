using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 报警规则新建/编辑对话框。
    /// 关联变量从工程变量下拉选择；编辑模式回填现有字段。不直接改模型对象，返回字段副本由上层走 CommandService。
    /// </summary>
    public partial class AlarmEditDialog : Window
    {
        private readonly AlarmRule? _existing;
        private readonly HMIProject _project;

        /// <summary>用户确认后输出的报警（编辑模式下为字段副本，非原对象）。</summary>
        public AlarmRule Result { get; private set; } = new();

        public AlarmEditDialog(AlarmRule? existing, HMIProject project)
        {
            InitializeComponent();
            _existing = existing;
            _project = project;

            PopulateTagBox();
            // 契约：枚举声明序 = XAML 下拉项序（High=0/Low=1/RateChange=2/Deviation=3；Emergency=0/Important=1/Warning=2/Info=3）
            if (existing == null)
            {
                DialogTitle.Text = "新建报警";
                Title = "新建报警";
                TypeBox.SelectedIndex = 0;       // 默认高报
                SeverityBox.SelectedIndex = 2;   // 默认警告
                NameBox.Focus();
            }
            else
            {
                DialogTitle.Text = $"编辑报警 - {existing.Name}";
                Title = "编辑报警";
                NameBox.Text = existing.Name;
                SelectTag(existing.TagName);
                TypeBox.SelectedIndex = (int)existing.Type;
                ThresholdBox.Text = existing.Threshold.ToString(CultureInfo.InvariantCulture);
                DeadbandBox.Text = existing.Deadband.ToString(CultureInfo.InvariantCulture);
                DelayBox.Text = existing.DelayMs.ToString();
                SeverityBox.SelectedIndex = (int)existing.Level;
                MessageInputBox.Text = existing.Message;
            }
        }

        /// <summary>填充变量下拉（工程全部变量名；无变量时显示提示项）。</summary>
        private void PopulateTagBox()
        {
            foreach (var tag in _project.Tags)
                TagBox.Items.Add(new ComboBoxItem { Content = tag.Name });
            if (TagBox.Items.Count == 0)
                TagBox.Items.Add(new ComboBoxItem { Content = "（无变量，请先创建变量）", IsEnabled = false });
        }

        /// <summary>编辑模式选中关联变量；变量不存在（异常数据）时动态补一项防回填静默失败。</summary>
        private void SelectTag(string tagName)
        {
            foreach (ComboBoxItem item in TagBox.Items)
            {
                if (item.Content?.ToString() == tagName) { TagBox.SelectedItem = item; return; }
            }
            if (tagName.Length > 0)
            {
                var fallback = new ComboBoxItem { Content = $"（缺失变量: {tagName}）", IsEnabled = false };
                TagBox.Items.Add(fallback);
                TagBox.SelectedItem = fallback;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (name.Length == 0) { ShowError("报警名称不能为空"); return; }
            if (_project.Alarms.Any(a => a.Name == name && !ReferenceEquals(a, _existing)))
            { ShowError($"报警 \"{name}\" 已存在"); return; }

            if (TagBox.SelectedItem is not ComboBoxItem tagItem || tagItem.Content?.ToString() is not string tagName
             || !_project.Tags.Any(t => t.Name == tagName))
            { ShowError("请选择有效的关联变量"); return; }

            if (TypeBox.SelectedItem is not ComboBoxItem typeItem
                || !Enum.TryParse<AlarmType>(typeItem.Content.ToString(), out var type))
            { ShowError("请选择报警类型"); return; }

            if (!double.TryParse(ThresholdBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
             || !double.IsFinite(threshold))
            { ShowError("阈值必须是有限数字"); return; }

            if (!double.TryParse(DeadbandBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var deadband)
             || !double.IsFinite(deadband))
            { ShowError("回差必须是有限数字"); return; }

            if (!int.TryParse(DelayBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay)
             || delay < 0)
            { ShowError("延迟必须是 ≥0 的整数（毫秒）"); return; }

            if (SeverityBox.SelectedItem is not ComboBoxItem sevItem
                || !Enum.TryParse<Severity>(sevItem.Content.ToString(), out var severity))
            { ShowError("请选择严重等级"); return; }

            Result = new AlarmRule
            {
                Name = name, TagName = tagName, Type = type,
                Threshold = threshold, Deadband = deadband, DelayMs = delay,
                Level = severity, Message = MessageInputBox.Text.Trim(),
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ShowError(string msg)
        {
            ErrorBox.Text = msg;
            ErrorBox.Visibility = Visibility.Visible;
        }
    }
}
