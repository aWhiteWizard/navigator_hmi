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
            DataTypeBox.SelectedIndex = 4;   // 🟡：控件就绪后赋值（默认 FLOAT；XAML 移除 SelectedIndex 防 SelectionChanged 构造时序陷阱——wpf-xaml-initialize-trap 教训）

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
                BaseValueBox.Text = existing.BaseValue;
                if (existing.DataType == TagDataType.GPS)   // V-5a：GPS 基准值回填两框（解析 BaseValue 拆经度/纬度）
                {
                    if (GeoPoint.TryParse(existing.BaseValue, out var gpsPt) && gpsPt != null)
                    {
                        // X-1b：编辑框预填 DMS（W-2a 拍板"显示/输入/修改全 DMS，小数只是输入方式"；提交 BuildGpsBaseValue 解析已支持小数/DMS）
                        LngBaseBox.Text = GeoPoint.FormatDms(gpsPt.Longitude, true);
                        LatBaseBox.Text = GeoPoint.FormatDms(gpsPt.Latitude, false);
                        GpsDmsHint.Visibility = Visibility.Collapsed;   // 回填触发 TextChanged → 提示抑制（仅输入状态显示，续23 增补 2）
                    }
                }
                if (existing.DataType == TagDataType.BOOL)   // #4：BOOL 编辑回填下拉；存量 "1"/"True"/"TRUE" 兼容（大小写不敏感——命令层新写入已归一，旧工程可能存变体）
                    BoolBaseBox.SelectedIndex = existing.BaseValue is not null
                        && (string.Equals(existing.BaseValue, "true", StringComparison.OrdinalIgnoreCase) || existing.BaseValue == "1") ? 1 : 0;
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

        /// <summary>#4：BOOL 类型基准值用下拉（false/true——只能填这两个），其余类型用文本框。
        /// V-5a：GPS 类型用两框（经度/纬度，续23 方案 B）。</summary>
        private void DataTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataTypeBox.SelectedItem is not ComboBoxItem bi) return;
            bool isBool = bi.Content?.ToString() == nameof(TagDataType.BOOL);
            bool isGps = bi.Content?.ToString() == nameof(TagDataType.GPS);
            BaseValueBox.Visibility = (isBool || isGps) ? Visibility.Collapsed : Visibility.Visible;
            BoolBaseBox.Visibility = isBool ? Visibility.Visible : Visibility.Collapsed;
            LngBaseBox.Visibility = isGps ? Visibility.Visible : Visibility.Collapsed;
            LatBaseBox.Visibility = isGps ? Visibility.Visible : Visibility.Collapsed;
            if (!isGps) GpsDmsHint.Visibility = Visibility.Collapsed;
        }

        /// <summary>V-5a：GPS 两框输入 → DMS 实时换算提示（TextChanged 显示；LostFocus 隐藏，续23 增补 2）。</summary>
        private void GpsBaseBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (GpsDmsHint == null) return;
            var lngStr = LngBaseBox.Text.Trim();
            var latStr = LatBaseBox.Text.Trim();
            var parts = new List<string>();
            if (lngStr.Length > 0)
                parts.Add(double.TryParse(lngStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lng)
                    ? $"经度→{GeoPoint.FormatDms(lng, true)}" : "经度格式：104.06（负=西经）");
            if (latStr.Length > 0)
                parts.Add(double.TryParse(latStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var lat)
                    ? $"纬度→{GeoPoint.FormatDms(lat, false)}" : "纬度格式：30.67（负=南纬）");
            GpsDmsHint.Text = string.Join("　", parts);
            GpsDmsHint.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GpsBaseBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (GpsDmsHint != null) GpsDmsHint.Visibility = Visibility.Collapsed;
        }

        /// <summary>V-5a：GPS 基准值从两框组装 "(E..., N...)"；非法返回 null 并置 err 提示。</summary>
        private string? BuildGpsBaseValue(out string? err)
        {
            err = null;
            var lngStr = LngBaseBox.Text.Trim();
            var latStr = LatBaseBox.Text.Trim();
            if (lngStr.Length == 0 && latStr.Length == 0) return new GeoPoint(0, 0).ToBaseValue();   // 全空 = 默认 (E0°0'0", N0°0'0")
            if (lngStr.Length == 0 || latStr.Length == 0) { err = "经度和纬度都要填写（或都留空用默认 0）"; return null; }
            // X-1b：提交解析改 TryParseCoord（小数/DMS 双解析 + 范围校验；编辑框现预填 DMS）
            if (!GeoPoint.TryParseCoord(lngStr, true, out var lng)) { err = $"经度格式：104.06（东经为正，负号=西经；范围 ±180）或 DMS 如 E104°3'29.88\""; return null; }
            if (!GeoPoint.TryParseCoord(latStr, false, out var lat)) { err = $"纬度格式：30.67（北纬为正，负号=南纬；范围 ±90）或 DMS 如 N30°40'20.12\""; return null; }
            return new GeoPoint(lng, lat).ToBaseValue();
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

            // 基准值：BOOL 用下拉（false/true——只能填这两个）；数字类型要求可解析为数字；DATETIME 用日期时间格式（任务8：全 0 字面放行）
            // V-5a：GPS 基准值从两框组装（非法 → ShowError 拦截）
            string baseValue;
            if (dt == TagDataType.GPS)
            {
                var gpsBase = BuildGpsBaseValue(out var gpsErr);
                if (gpsBase == null) { ShowError(gpsErr ?? "经纬度格式错误"); return; }
                baseValue = gpsBase;
            }
            else
            {
                baseValue = dt == TagDataType.BOOL
                    ? (BoolBaseBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "false"
                    : BaseValueBox.Text.Trim();
            }
            if (dt == TagDataType.DATETIME && baseValue.Length == 0) baseValue = "0000:00:00 00:00:00";   // 任务8：DATETIME 默认全 0 基准值
            if (baseValue.Length == 0 && dt == TagDataType.FLOAT) baseValue = "0.0";   // D4：数字变量默认基准值 0/0.0
            if (baseValue.Length == 0 && dt is TagDataType.INT16 or TagDataType.UINT16 or TagDataType.INT32) baseValue = "0";
            if (baseValue.Length > 0 && dt != TagDataType.STRING
             && dt != TagDataType.DATETIME
             && dt != TagDataType.BOOL   // #4：BOOL 走下拉恒合法（false/true）
             && dt != TagDataType.GPS    // 世界地图批 2：GPS 基准值用 GeoPoint 校验（DMS/小数度），与命令层 BaseValueValidator 对齐
             && !double.TryParse(baseValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            { ShowError("数字类型变量的基准值必须是数字（如 25.5 / 1）"); return; }
            if (baseValue.Length > 0 && dt == TagDataType.GPS
             && !NavigatorHMI.Common.GeoPoint.TryParse(baseValue, out _))
            { ShowError("GPS 变量的基准值必须是经纬度（如 (E104°3'30\", N30°40'20\") 或 104.0583, 30.6722）"); return; }
            if (baseValue.Length > 0 && dt == TagDataType.DATETIME
             && !DateTime.TryParse(baseValue, out _)
             && !IsZeroDateText(baseValue))
            { ShowError("DATETIME 变量的基准值必须是有效日期时间（如 2026-08-08 12:30:00）"); return; }

            Result = new Tag
            {
                Name = name,
                DataType = dt,
                Source = source,
                Unit = UnitBox.Text.Trim(),
                ScanIntervalMs = scan,
                Deadband = deadband,
                Description = DescriptionBox.Text.Trim(),
                BaseValue = baseValue,
            };
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        /// <summary>任务8：全 0 日期字面（仅 0/数字/冒号/空格/横线/斜杠，无任何非 0 数字）——0000 年 TryParse 失败，需字面放行（与命令层 IsZeroDateLiteral 同规则）。</summary>
        private static bool IsZeroDateText(string s)
        {
            if (!s.All(c => char.IsDigit(c) || c is ':' or ' ' or '-' or '/')) return false;   // 字符白名单：数字/冒号/空格/横线/斜杠
            var digits = s.Where(char.IsDigit).ToList();
            if (digits.Count == 0) return false;
            return digits.All(d => d == '0');
        }

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
