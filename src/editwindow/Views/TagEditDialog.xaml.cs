using System;
using System.Linq;
using System.Windows;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 变量新建/编辑对话框。
    /// 两种模式：null=新建；非 null=编辑（预填当前值，重命名时做唯一性校验排除自身）。
    /// </summary>
    public partial class TagEditDialog : Window
    {
        private readonly Tag? _existing;

        /// <summary>用户确认后输出的新变量（编辑模式下为修改后的副本/原对象）。</summary>
        public Tag Result { get; private set; } = new();

        public TagEditDialog(Tag? existing, HMIProject project)
        {
            InitializeComponent();
            _existing = existing;
            _project = project;

            if (existing == null)
            {
                DialogTitle.Text = "新建变量";
                Title = "新建变量";
                NameBox.Focus();
            }
            else
            {
                DialogTitle.Text = $"编辑变量 - {existing.Name}";
                Title = "编辑变量";
                NameBox.Text = existing.Name;
                DataTypeBox.SelectedIndex = (int)existing.DataType;
                SourceBox.Text = existing.Source;
                UnitBox.Text = existing.Unit;
                ScanIntervalBox.Text = existing.ScanIntervalMs.ToString();
                DeadbandBox.Text = existing.Deadband.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DescriptionBox.Text = existing.Description;
            }
        }

        private readonly HMIProject _project;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            var source = SourceBox.Text.Trim();

            // 名称必填
            if (name.Length == 0) { ShowError("变量名不能为空"); return; }
            // 名称唯一性（编辑模式排除自身）
            if (_project.Tags.Any(t => t.Name == name && !ReferenceEquals(t, _existing)))
            { ShowError($"变量 \"{name}\" 已存在"); return; }
            // 来源必填
            if (source.Length == 0) { ShowError("来源不能为空（modbus:// 或 mqtt://）"); return; }
            // 采集周期：正整数
            if (!int.TryParse(ScanIntervalBox.Text.Trim(), out var scan) || scan <= 0)
            { ShowError("采集周期必须是正整数（毫秒）"); return; }
            // 死区：数字 + 有限（拦截 NaN/Infinity）
            if (!double.TryParse(DeadbandBox.Text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var deadband))
            { ShowError("死区必须是数字"); return; }
            if (!double.IsFinite(deadband))
            { ShowError("死区必须是有限数字"); return; }
            // 数据类型解析（SelectedIndex 依赖契约：TagDataType 枚举声明序 = XAML 下拉项序，加成员时须同步）
            if (DataTypeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem dtItem
                || !Enum.TryParse<TagDataType>(dtItem.Content.ToString(), out var dt))
            { ShowError("请选择数据类型"); return; }

            Result = new Tag
            {
                Name = name,
                DataType = dt,
                Source = source,
                Unit = UnitBox.Text.Trim(),
                ScanIntervalMs = scan,
                Deadband = deadband,
                Description = DescriptionBox.Text.Trim(),
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
