using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NavigatorHMI.Common;
using Serilog;

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
            DecimalsBox.TextChanged += (_, __) => UpdateDecimalsHint();   // AH：小数位提示实时刷新
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
            // AH（2026-10-30）：换算字段装载
            ScaleBox.Text = existing.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
            OffsetBox.Text = existing.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DecimalsBox.Text = existing.Decimals.ToString();
            UpdateDecimalsHint();
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
            // Z 循环（2026-09-11）：MQTT 设备协议 deprecated（移出通讯页——连接在 MQTT 根配置）——
            // 变量来源不再列 MQTT 设备（MQTT 映射由 Binding 承载，Tag.source 的 mqtt:// 语义废弃）
            foreach (var dev in _project.Devices.Where(d => d.Protocol != ProtocolType.MQTT))
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
                    // 2026-09-16 复审补：同从站的设备可能有多个（如"ModbusTCP设备"与"ModbusRTU设备"都是 slaveId=100），
                    // 此前"取第一个"会**静默改写归属**（RTU 变量被改成 TCP 设备 ⇒ 设备端换传输，三层防线都看不见）。
                    // 现在：① 优先按变量原归属（DeviceName）定位；② 唯一候选才自动选；③ 多候选不猜——明示让用户重选。
                    var candidates = _project.Devices
                        .Where(d => d.Protocol is ProtocolType.ModbusRTU or ProtocolType.ModbusTCP && GetSlaveId(d) == slaveId)
                        .ToList();
                    var byName = !string.IsNullOrEmpty(_existing?.DeviceName)
                        ? candidates.FirstOrDefault(d => d.Name == _existing!.DeviceName)
                        : null;
                    if (byName != null)
                    {
                        SelectSourceItem(byName);
                        AddressBox.Text = addr;
                        return;
                    }
                    if (candidates.Count == 1)
                    {
                        SelectSourceItem(candidates[0]);
                        AddressBox.Text = addr;
                        return;
                    }
                    if (candidates.Count > 1)
                    {
                        ShowError($"来源 \"{source}\" 的从站 {slaveId} 对应多个设备（{string.Join(" / ", candidates.Select(d => d.Name))}）——" +
                                  $"请在「来源」里重新选择，避免改错传输方式（RTU/TCP）");
                    }
                }
            }
            // Z 循环：mqtt:// 来源反推分支删除（MQTT 设备 deprecated——旧 mqtt:// 源 tag 打开走下方「自定义」fallback 保留原值）
            // 反推失败（设备已删/来源格式变化）：动态追加「自定义」兜底项，保留原始来源不丢失
            var fallback = new ComboBoxItem { Content = $"自定义: {source}", Tag = source };
            SourceBox.Items.Add(fallback);
            SourceBox.SelectedItem = fallback;
            // 2026-09-16：不可采集的来源（如 4x:0001 / 手写乱码）打开即提示——这是"值恒 0 且无日志"的典型来源
            if (!ModbusTagSource.IsMqttSource(source) && !ModbusTagSource.IsModbusSource(source))
                ShowError($"当前来源 \"{source}\" 设备端无法识别（不会采集，值恒 0）——" +
                          $"请把「来源」改为对应设备并填寄存器地址（如 1 或 4x:0002）后再保存");
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

            // AH（2026-10-31 用户裁决）：**小数位只对 FLOAT 可填**——整型变量换算后设备端取整（21.1 → 21），
            // 填小数位没意义 ⇒ 非 FLOAT 时禁用输入框并给灰字说明（值保留，改回 FLOAT 还能用）。
            bool isFloat = bi.Content?.ToString() == nameof(TagDataType.FLOAT);
            DecimalsBox.IsEnabled = isFloat;
            ScaleBox_UpdateHint(isFloat);
        }

        /// <summary>小数位可用性提示（AH 2026-10-31）：非 FLOAT 时说明"整型换算后取整"。</summary>
        private void ScaleBox_UpdateHint(bool isFloat)
        {
            if (DecimalsHint == null) return;
            if (!isFloat)
            {
                DecimalsHint.Text = "整型变量不支持小数位（换算结果在设备端取整）——需要小数请选 FLOAT";
                DecimalsHint.Foreground = System.Windows.Media.Brushes.Gray;
                return;
            }
            DecimalsHint.Foreground = System.Windows.Media.Brushes.DarkSlateGray;
            UpdateDecimalsHint();
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
            AddressPanel.Visibility = needAddress ? Visibility.Visible : Visibility.Collapsed;
            UpdateAddressHint();
        }

        /// <summary>
        /// 2026-09-16：地址框实时换算提示——把用户的写法（裸数字 / 4x 记法 / 40002 式 / 完整 modbus:// 串）
        /// 立刻显示为"将落库的规范来源"，非法写法当场变红说明原因。
        /// 背景：此前地址是自由文本，用户写 4x:0001 会被拼成 modbus://&lt;从站&gt;/4x:0001 落库，
        /// 设备端（只认 modbus://从站/PDU）直接跳过该变量 ⇒ 值恒 0 且无日志。
        /// </summary>
        private void AddressBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddressHint();

        private void UpdateAddressHint()
        {
            if (AddressHint == null) return;
            if (SourceBox.SelectedItem is not ComboBoxItem item || item.Tag is not DeviceConfig dev)
            {
                AddressHint.Visibility = Visibility.Collapsed;
                return;
            }
            AddressHint.Visibility = Visibility.Visible;
            var text = AddressBox.Text.Trim();
            if (text.Length == 0)
            {
                AddressHint.Foreground = System.Windows.Media.Brushes.DarkSlateGray;
                AddressHint.Text = "可填：裸数字（= PDU 地址，如 1）· 4x 记法（如 4x:0002）· PLC 式（如 40002）";
                return;
            }
            var slave = dev.TryGetSlaveId();
            if (slave == 0)
            {
                AddressHint.Foreground = System.Windows.Media.Brushes.Firebrick;
                AddressHint.Text = $"设备 \"{dev.Name}\" 的连接参数缺少合法 slaveId（1~247）——请先在通讯页修正设备";
                return;
            }
            if (!ModbusTagSource.TryParseRegisterInput(text, out var inSlave, out var reg, out var err))
            {
                AddressHint.Foreground = System.Windows.Media.Brushes.Firebrick;
                AddressHint.Text = "✗ " + err;
                return;
            }
            if (inSlave > 0 && inSlave != slave)
            {
                AddressHint.Foreground = System.Windows.Media.Brushes.Firebrick;
                AddressHint.Text = $"✗ 来源里的从站 {inSlave} 与设备从站 {slave} 不一致——将按设备从站保存为 {ModbusTagSource.Build(slave, reg)}";
                return;
            }
            AddressHint.Foreground = System.Windows.Media.Brushes.DarkGreen;
            AddressHint.Text = $"→ 保存为 {ModbusTagSource.Build(slave, reg)}（保持寄存器 4x 编号 = PDU+1）";
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
            string deviceName = "";   // AB-4（2026-09-11）：变量所属设备（Tag.DeviceName / proto 9）——
                                      // 设备端按它定位连接参数与传输协议（Modbus RTU / TCP 分流 + 一协议多实例）
            if (SourceBox.SelectedItem is not ComboBoxItem srcItem)
            { ShowError("请选择来源"); return; }
            if (srcItem.Tag is DeviceConfig dev)
            {
                var addr = AddressBox.Text.Trim();
                if (addr.Length == 0) { ShowError("请填写地址（寄存器地址）"); return; }
                // 2026-09-16：走单一来源归一化（裸数字/4x 记法/40002 式/完整串 → modbus://<从站>/<PDU>），
                // 非法写法当场拦下（此前会拼成 modbus://<从站>/4x:0001 落库，设备端静默跳过 ⇒ 值恒 0）
                var devSlave = dev.TryGetSlaveId();
                if (devSlave == 0)
                { ShowError($"设备 \"{dev.Name}\" 的连接参数缺少合法 slaveId（1~247）——请先在通讯页修正设备"); return; }
                if (!ModbusTagSource.TryNormalize(addr, devSlave, out var normSource, out var addrErr))
                { ShowError(addrErr); return; }
                source = normSource;
                deviceName = dev.Name;
            }
            else if (srcItem.Tag is string customUri)
            {
                source = customUri;   // 编辑反推失败兜底：保留原始来源
                deviceName = _existing?.DeviceName ?? "";   // 同时保留原有设备归属（勿因反推失败丢关联）
                // 2026-09-16：来源是设备端认不出的写法（如 4x:0001）时明确告警——否则会静默不采集
                if (!ModbusTagSource.IsMqttSource(source) && !ModbusTagSource.IsModbusSource(source))
                    ShowError($"注意：来源 \"{source}\" 设备端无法识别（不会采集，值恒 0）——" +
                              $"请在上方选设备并填寄存器地址（如 1 或 4x:0002）");
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

            // AH（2026-10-30）：换算字段解析与校验
            var scaleText = ScaleBox.Text.Trim();
            if (scaleText.Length == 0) scaleText = "1";
            if (!double.TryParse(scaleText, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var scale) || scale <= 0)
            { ShowError("换算系数必须是大于 0 的数字（默认 1 = 不换算）"); return; }

            var offsetText = OffsetBox.Text.Trim();
            if (offsetText.Length == 0) offsetText = "0";
            if (!double.TryParse(offsetText, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var offset))
            { ShowError("偏移必须是数字（默认 0）"); return; }

            var decText = DecimalsBox.Text.Trim();
            if (decText.Length == 0) decText = "0";
            if (!int.TryParse(decText, out var decimals) || decimals < 0 || decimals > 6)
            { ShowError("小数位必须是 0~6 的整数（0 = 不限制）"); return; }

            // 小数位优先：用户只填了「小数位 1」而没动系数（仍是默认 1）时，自动推出系数 0.1
            var effectiveScale = (decimals > 0 && Math.Abs(scale - 1.0) < 1e-12 && Math.Abs(offset) < 1e-12)
                ? Math.Pow(10, -decimals)
                : scale;

            Result = new Tag
            {
                Name = name,
                DataType = dt,
                Source = source,
                DeviceName = deviceName,
                Unit = UnitBox.Text.Trim(),
                ScanIntervalMs = scan,
                Deadband = deadband,
                Description = DescriptionBox.Text.Trim(),
                BaseValue = baseValue,
                // AH（2026-10-30）：换算（小数位 >0 且未显式改系数 ⇒ 系数 = 10^-小数位）
                Scale = effectiveScale,
                Offset = offset,
                Decimals = decimals,
            };
            DialogResult = true;
        }

        /// <summary>小数位提示：填 N 时反馈「将自动换算系数 = 10^-N」（AH 2026-10-30）。</summary>
        private void UpdateDecimalsHint()
        {
            var text = DecimalsBox.Text.Trim();
            if (int.TryParse(text, out var n) && n >= 1 && n <= 6)
            {
                var derivedScale = Math.Pow(10, -n);
                DecimalsHint.Text = "→ 自动换算系数 "
                    + derivedScale.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "（若系数已手动改过，以系数为准）";
            }
            else
            {
                DecimalsHint.Text = "";
            }
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
            // 2026-09-16：改用模型层单一口径（缺 slaveId 返回 0 由调用方处理，不再静默回退 1）
            var sid = dev.TryGetSlaveId();
            return sid > 0 ? sid : 1;
        }
    }
}
