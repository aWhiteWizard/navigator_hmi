using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 变量新建/编辑对话框。
    /// 来源：下拉选择「内部变量」（Source 为空）或通讯设备（Modbus 寄存器 / MQTT 主题），选中设备后填写地址。
    /// 采集周期：下拉默认值（100ms~10s）。描述保持自由文本。
    /// 两种模式：null=新建；非 null=编辑（预填当前值）。返回字段副本，由上层走 CommandService 落库。
    /// </summary>
    public partial class TagEditDialog : Window
    {
        private readonly Tag? _existing;
        private readonly HMIProject _project;

        /// <summary>用户确认后输出的新变量（编辑模式下为修改后的副本）。</summary>
        public Tag Result { get; private set; } = new();

        /// <summary>来源下拉的 Tag 值：null（内部变量）| DeviceConfig（设备）| 原始 URI 字符串（编辑反推不匹配时兜底）。</summary>
        private readonly object? _internalSource = null;

        public TagEditDialog(Tag? existing, HMIProject project)
        {
            InitializeComponent();
            _existing = existing;
            _project = project;
            PopulateSourceBox();

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
                var dtItem = DataTypeBox.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i => i.Content?.ToString() == existing.DataType.ToString())
                 ?? DataTypeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Content?.ToString() == nameof(TagDataType.FLOAT));   // 找不到钳制 FLOAT
                DataTypeBox.SelectedItem = dtItem;
                UnitBox.Text = existing.Unit;
                DeadbandBox.Text = existing.Deadband.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DescriptionBox.Text = existing.Description;
                SelectScanInterval(existing.ScanIntervalMs);
                SelectSource(existing.Source);
            }
        }

        // ═══════════════ 来源下拉 ═══════════════

        /// <summary>填充来源下拉：第一项「内部变量」，其后为通讯配置的设备。</summary>
        private void PopulateSourceBox()
        {
            SourceBox.Items.Add(new ComboBoxItem { Content = "内部变量", Tag = _internalSource });
            foreach (var dev in _project.Devices)
            {
                SourceBox.Items.Add(new ComboBoxItem { Content = $"{dev.Protocol}: {dev.Name}", Tag = dev });
            }
        }

        /// <summary>编辑模式：按现有 Source 反推下拉选中项与地址。</summary>
        private void SelectSource(string source)
        {
            source = source ?? "";
            if (source.Length == 0)
            {
                SourceBox.SelectedIndex = 0;   // 内部变量
                return;
            }
            // modbus://{slaveId}/{addr}
            if (source.StartsWith("modbus://", StringComparison.OrdinalIgnoreCase))
            {
                var rest = source["modbus://".Length..];
                var slash = rest.IndexOf('/');
                var slaveIdStr = slash > 0 ? rest[..slash] : rest;
                var addr = slash > 0 ? rest[(slash + 1)..] : "";
                if (int.TryParse(slaveIdStr, out var slaveId))
                {
                    var dev = _project.Devices.FirstOrDefault(d => d.Protocol is ProtocolType.ModbusRTU or ProtocolType.ModbusTCP
                                                                   && GetSlaveId(d) == slaveId);
                    if (dev != null)
                    {
                        SelectSourceItem(dev);
                        AddressBox.Text = addr;
                        return;
                    }
                }
            }
            // mqtt://{topic}
            else if (source.StartsWith("mqtt://", StringComparison.OrdinalIgnoreCase))
            {
                var topic = source["mqtt://".Length..];
                var dev = _project.Devices.FirstOrDefault(d => d.Protocol == ProtocolType.MQTT);
                if (dev != null)
                {
                    SelectSourceItem(dev);
                    AddressBox.Text = topic;
                    return;
                }
            }
            // 反推失败（设备已删/来源格式变化）：动态追加「自定义」兜底项，保留原始来源不丢失
            var fallback = new ComboBoxItem { Content = $"自定义: {source}", Tag = source };
            SourceBox.Items.Add(fallback);
            SourceBox.SelectedItem = fallback;
        }

        private void SelectSourceItem(DeviceConfig dev)
        {
            foreach (ComboBoxItem item in SourceBox.Items)
            {
                if (ReferenceEquals(item.Tag, dev)) { SourceBox.SelectedItem = item; return; }
            }
        }

        /// <summary>来源下拉切换：选中设备显示地址输入，内部变量/自定义隐藏。</summary>
        private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceBox.SelectedItem is not ComboBoxItem item) return;
            bool needAddress = item.Tag is DeviceConfig;
            AddressLabel.Visibility = needAddress ? Visibility.Visible : Visibility.Collapsed;
            AddressBox.Visibility = needAddress ? Visibility.Visible : Visibility.Collapsed;
        }

        // ═══════════════ 采集周期 ═══════════════

        /// <summary>编辑模式：按毫秒值选中下拉项（不在默认列表时动态插入，防回填静默失败）。</summary>
        private void SelectScanInterval(int ms)
        {
            var msStr = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
            foreach (ComboBoxItem item in ScanIntervalBox.Items)
            {
                if (item.Tag?.ToString() == msStr) { ScanIntervalBox.SelectedItem = item; return; }
            }
            var extra = new ComboBoxItem { Content = $"{ms} 毫秒", Tag = msStr };
            ScanIntervalBox.Items.Add(extra);
            ScanIntervalBox.SelectedItem = extra;
        }

        // ═══════════════ 确认 ═══════════════

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (name.Length == 0) { ShowError("变量名不能为空"); return; }
            if (_project.Tags.Any(t => t.Name == name && !ReferenceEquals(t, _existing)))
            { ShowError($"变量 \"{name}\" 已存在"); return; }
            // 数据类型解析（按枚举名匹配下拉项，枚举序与 XAML 项序解耦）
            if (DataTypeBox.SelectedItem is not ComboBoxItem dtItem
                || !Enum.TryParse<TagDataType>(dtItem.Content.ToString(), out var dt))
            { ShowError("请选择数据类型"); return; }

            // 来源：内部变量 → 空；设备 → 组装 URI；自定义 → 原 URI
            string source;
            if (SourceBox.SelectedItem is not ComboBoxItem srcItem)
            { ShowError("请选择来源"); return; }
            if (srcItem.Tag is DeviceConfig dev)
            {
                var addr = AddressBox.Text.Trim();
                if (addr.Length == 0) { ShowError("请填写地址（寄存器地址或 MQTT 主题）"); return; }
                source = dev.Protocol == ProtocolType.MQTT
                    ? $"mqtt://{addr}"
                    : $"modbus://{GetSlaveId(dev)}/{addr}";
            }
            else if (srcItem.Tag is string customUri)
            {
                source = customUri;   // 编辑反推失败兜底：保留原始来源
            }
            else
            {
                source = "";   // 内部变量
            }

            // 采集周期：从下拉项 Tag 读取毫秒数
            if (ScanIntervalBox.SelectedItem is not ComboBoxItem scanItem
                || !int.TryParse(scanItem.Tag?.ToString(), out var scan) || scan <= 0)
            { ShowError("请选择采集周期"); return; }

            // 死区：数字 + 有限（拦截 NaN/Infinity）
            if (!double.TryParse(DeadbandBox.Text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var deadband))
            { ShowError("死区必须是数字"); return; }
            if (!double.IsFinite(deadband))
            { ShowError("死区必须是有限数字"); return; }

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

        /// <summary>从设备 ConnectionInfo JSON 解析从站号（缺省 1）。</summary>
        private static int GetSlaveId(DeviceConfig dev)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(dev.ConnectionInfo) ? "{}" : dev.ConnectionInfo);
                if (doc.RootElement.TryGetProperty("slaveId", out var s) && s.ValueKind == JsonValueKind.Number)
                    return s.GetInt32();
            }
            catch (JsonException) { }
            return 1;
        }
    }
}
