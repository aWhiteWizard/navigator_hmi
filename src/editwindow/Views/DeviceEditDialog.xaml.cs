using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 设备通信配置新建/编辑对话框。
    /// 协议联动：ModbusRTU（串口/波特率/从站号）、ModbusTCP（IP/端口/从站号）、MQTT（Broker/ClientId）。
    /// 确定时生成 ConnectionInfo JSON；编辑模式解析 JSON 回填。不直接改模型对象，返回字段副本由上层走 CommandService。
    /// </summary>
    public partial class DeviceEditDialog : Window
    {
        private readonly DeviceConfig? _existing;
        private readonly HMIProject _project;

        /// <summary>编辑模式原存储密码（dpapi: 密文）——用户未改动密码框（占位符）时原样保留；改动则用明文加密覆盖。</summary>
        private string _mqttStoredPassword = "";

        /// <summary>密码框是否被用户改动（reviewer 🟡：占位符 "******" 判等会误伤真实密码恰为 6 星号——
        /// 改 PasswordChanged 置位标志，保存时按此判断用明文加密覆盖或保留原密文）。</summary>
        private bool _mqttPasswordTouched;

        /// <summary>用户确认后输出的配置（编辑模式下为字段副本，非原对象）。</summary>
        public DeviceConfig Result { get; private set; } = new();

        public DeviceEditDialog(DeviceConfig? existing, HMIProject project)
        {
            InitializeComponent();
            _existing = existing;
            _project = project;

            if (existing == null)
            {
                DialogTitle.Text = "新建设备";
                Title = "新建设备";
                // 默认 ModbusTCP（InitializeComponent 已完成，SelectionChanged 正常触发并显示 TCP 参数面板）
                ProtocolBox.SelectedIndex = 1;
                RtuBaudBox.SelectedIndex = 0;   // 默认波特率 9600（无 SelectionChanged 处理器，安全）
                NameBox.Focus();
            }
            else
            {
                DialogTitle.Text = $"编辑设备 - {existing.Name}";
                Title = "编辑设备";
                NameBox.Text = existing.Name;
                // 契约：ProtocolType 枚举声明序 = XAML 下拉项序（ModbusRTU=0/ModbusTCP=1/MQTT=2），加成员时须同步
                ProtocolBox.SelectedIndex = (int)existing.Protocol;   // 触发 SelectionChanged → ShowProtocolPanel
                PrefillConnectionInfo(existing);
            }
        }

        /// <summary>协议切换：显示对应参数面板。</summary>
        private void ProtocolBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProtocolBox.SelectedItem is not ComboBoxItem item) return;
            if (!Enum.TryParse<ProtocolType>(item.Content.ToString(), out var pt)) return;
            ShowProtocolPanel(pt);
        }

        /// <summary>密码框改动标记（reviewer 🟡：占位符判等会误伤真实 6 星号密码——PasswordChanged 置位）。</summary>
        private void MqttPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
            => _mqttPasswordTouched = true;

        private void ShowProtocolPanel(ProtocolType pt)
        {
            // 防御：InitializeComponent 期间事件可能在控件创建前触发（XAML SelectedIndex 陷阱），null 直接返回
            if (RtuPanel == null || TcpPanel == null || MqttPanel == null) return;
            RtuPanel.Visibility = pt == ProtocolType.ModbusRTU ? Visibility.Visible : Visibility.Collapsed;
            TcpPanel.Visibility = pt == ProtocolType.ModbusTCP ? Visibility.Visible : Visibility.Collapsed;
            MqttPanel.Visibility = pt == ProtocolType.MQTT ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>编辑模式：解析 ConnectionInfo JSON 回填参数框（ValueKind 预检，缺字段用默认值兜底）。</summary>
        private void PrefillConnectionInfo(DeviceConfig existing)
        {
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(existing.ConnectionInfo) ? "{}" : existing.ConnectionInfo);
                var root = doc.RootElement;
                if (existing.Protocol == ProtocolType.ModbusRTU)
                {
                    if (root.TryGetProperty("port", out var p) && p.ValueKind == JsonValueKind.String) RtuPortBox.Text = p.GetString() ?? RtuPortBox.Text;
                    if (root.TryGetProperty("baud", out var b) && b.ValueKind == JsonValueKind.Number)
                    {
                        var baud = b.GetInt32().ToString();
                        // 非编辑 ComboBox 仅能选中列表内选项；存储值不在标准列表（如 4800）时动态插入，
                        // 避免回填静默失败导致保存时被默认值悄悄覆盖（wpf-combobox-style §3 同类陷阱）
                        if (!RtuBaudBox.Items.Cast<System.Windows.Controls.ComboBoxItem>().Any(i => i.Content?.ToString() == baud))
                            RtuBaudBox.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = baud });
                        RtuBaudBox.SelectedValue = baud;
                    }
                    if (root.TryGetProperty("slaveId", out var s) && s.ValueKind == JsonValueKind.Number) RtuSlaveBox.Text = s.GetInt32().ToString();
                }
                else if (existing.Protocol == ProtocolType.ModbusTCP)
                {
                    if (root.TryGetProperty("ip", out var ip) && ip.ValueKind == JsonValueKind.String) TcpIpBox.Text = ip.GetString() ?? TcpIpBox.Text;
                    if (root.TryGetProperty("port", out var pt) && pt.ValueKind == JsonValueKind.Number) TcpPortBox.Text = pt.GetInt32().ToString();
                    if (root.TryGetProperty("slaveId", out var s) && s.ValueKind == JsonValueKind.Number) TcpSlaveBox.Text = s.GetInt32().ToString();
                }
                else if (existing.Protocol == ProtocolType.MQTT)
                {
                    // Y-3a：MQTT 全字段回填（broker/port/version/clientId/username/password/keepAlive/enableTls/statusTag）
                    if (root.TryGetProperty("broker", out var br) && br.ValueKind == JsonValueKind.String)
                    {
                        var broker = br.GetString() ?? "";
                        // Y-3a reviewer 🟡2（2026-09-10）：旧数据 broker 含 mqtt:// 前缀（Y-3a 前允许）——
                        // 回填时剥一次前缀迁移（剥后校验放行，保存不再被拒；仅剥已知前缀避免误伤）
                        foreach (var prefix in new[] { "mqtt://", "tcp://" })
                            if (broker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                broker = broker[prefix.Length..];
                                break;
                            }
                        MqttBrokerBox.Text = broker;
                    }
                    if (root.TryGetProperty("port", out var mp) && mp.ValueKind == JsonValueKind.Number) MqttPortBox.Text = mp.GetInt32().ToString();
                    if (root.TryGetProperty("version", out var mv) && mv.ValueKind == JsonValueKind.Number)
                    {
                        var ver = mv.GetInt32().ToString();
                        foreach (System.Windows.Controls.ComboBoxItem item in MqttVersionBox.Items)
                            if (item.Tag?.ToString() == ver) { MqttVersionBox.SelectedItem = item; break; }
                    }
                    if (root.TryGetProperty("clientId", out var c) && c.ValueKind == JsonValueKind.String) MqttClientIdBox.Text = c.GetString() ?? MqttClientIdBox.Text;
                    if (root.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String) MqttUsernameBox.Text = u.GetString() ?? "";
                    if (root.TryGetProperty("password", out var pw) && pw.ValueKind == JsonValueKind.String)
                    {
                        // 密码框不回显密文：已加密显示占位（编辑不动则保留原密文），改动后明文覆盖
                        var stored = pw.GetString() ?? "";
                        _mqttStoredPassword = stored;
                        _mqttPasswordTouched = false;   // 回填不视为用户改动（先于 PasswordBox.Password 赋值——事件会置 true，这里先复位）
                        MqttPasswordBox.Password = CredentialStore.IsEncrypted(stored) ? "******" : stored;
                        _mqttPasswordTouched = false;   // 赋值触发 PasswordChanged → 复位（回填不算改动）
                    }
                    if (root.TryGetProperty("keepAlive", out var ka) && ka.ValueKind == JsonValueKind.Number) MqttKeepAliveBox.Text = ka.GetInt32().ToString();
                    if (root.TryGetProperty("enableTls", out var t) && t.ValueKind == JsonValueKind.True) MqttTlsBox.IsChecked = true;
                    if (root.TryGetProperty("statusTag", out var st) && st.ValueKind == JsonValueKind.String) MqttStatusTagBox.Text = st.GetString() ?? "";
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // 旧数据 JSON 损坏/字段类型不符（如数字 port 存成字符串）：保留默认参数，允许用户重新填写
                System.Diagnostics.Trace.WriteLine($"[DeviceEditDialog] PrefillConnectionInfo 解析失败: {ex.Message}");
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var name = NameBox.Text.Trim();
            if (name.Length == 0) { ShowError("设备名不能为空"); return; }
            if (_project.Devices.Any(d => d.Name == name && !ReferenceEquals(d, _existing)))
            { ShowError($"设备 \"{name}\" 已存在"); return; }

            if (ProtocolBox.SelectedItem is not ComboBoxItem item
                || !Enum.TryParse<ProtocolType>(item.Content.ToString(), out var pt))
            { ShowError("请选择协议"); return; }

            // 数值字段校验（先于 BuildJson，错误消息统一；intKeys 内的键在此已确保可解析）
            if (pt == ProtocolType.ModbusRTU)
            {
                if (!int.TryParse(RtuSlaveBox.Text.Trim(), out var s) || s < 1 || s > 247)
                { ShowError("从站号必须是 1-247 的整数"); return; }
            }
            else if (pt == ProtocolType.ModbusTCP)
            {
                if (!int.TryParse(TcpPortBox.Text.Trim(), out var p) || p < 1 || p > 65535)
                { ShowError("端口必须是 1-65535 的整数"); return; }
                if (!int.TryParse(TcpSlaveBox.Text.Trim(), out var s) || s < 1 || s > 247)
                { ShowError("从站号必须是 1-247 的整数"); return; }
            }
            else if (pt == ProtocolType.MQTT)
            {
                // Y-3a：MQTT 全字段编辑时校验（与命令层 DeviceConnectionInfoValidator 同规则，先于入库双保险）
                var broker = MqttBrokerBox.Text.Trim();
                if (broker.Length == 0) { ShowError("Broker 不能为空（主机/IP，不含协议前缀）"); return; }
                if (broker.StartsWith("mqtt://", StringComparison.OrdinalIgnoreCase)
                 || broker.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                { ShowError("Broker 不应带协议前缀（如 mqtt://），请只填主机/IP"); return; }
                if (broker.Contains('/')) { ShowError("Broker 不应含路径（协议前缀会带 / 路径，请去除）"); return; }   // Y-3a reviewer 🟡：对话框与命令层同规则（防对话框过、命令层拒后编辑丢失）
                if (!int.TryParse(MqttPortBox.Text.Trim(), out var mp) || mp < 1 || mp > 65535)
                { ShowError("端口必须是 1-65535 的整数"); return; }
                var cid = MqttClientIdBox.Text.Trim();
                if (cid.Length > 64 || cid.Contains(' ')) { ShowError("ClientId 必须 ≤64 字符且不含空格"); return; }
                if (!int.TryParse(MqttKeepAliveBox.Text.Trim(), out var ka) || ka < 0)
                { ShowError("keepAlive 必须 ≥0（0 = 禁用心跳）"); return; }
            }

            string json;
            try
            {
                // 数值键按协议区分：RTU 的 port 是串口路径（字符串），TCP 的 port 是整数
                json = pt switch
                {
                    ProtocolType.ModbusRTU => BuildJson(intKeys: new[] { "baud", "slaveId" },
                        ("port", RtuPortBox.Text.Trim()),
                        ("baud", RtuBaudBox.Text),
                        ("slaveId", RtuSlaveBox.Text.Trim())),
                    ProtocolType.ModbusTCP => BuildJson(intKeys: new[] { "port", "slaveId" },
                        ("ip", TcpIpBox.Text.Trim()),
                        ("port", TcpPortBox.Text.Trim()),
                        ("slaveId", TcpSlaveBox.Text.Trim())),
                    ProtocolType.MQTT => BuildMqttJson(),
                };
            }
            catch (Exception ex) { ShowError(ex.Message); return; }

            Result = new DeviceConfig { Name = name, Protocol = pt, ConnectionInfo = json };
            DialogResult = true;
        }

        /// <summary>按键值对生成紧凑 JSON；intKeys 中的键按整数序列化（其余按字符串）。</summary>
        private static string BuildJson(string[] intKeys, params (string Key, string Value)[] fields)
        {
            var sb = new System.Text.StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            {
                var (key, value) = fields[i];
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(key).Append("\":");
                if (intKeys.Contains(key))
                {
                    if (!int.TryParse(value, out var n))
                        throw new InvalidOperationException($"{key} 必须是整数");
                    sb.Append(n);
                }
                else
                {
                    // 直接序列化（自带引号与转义，禁止 Trim('\"') 拼装——空串/含引号值会损坏）
                    sb.Append(JsonSerializer.Serialize(value));
                }
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Y-3a：MQTT 全字段 JSON 拼装（broker/port/version/clientId/username/password/keepAlive/enableTls/statusTag）。
        /// 密码：用户改动（非占位符）→ 明文 DPAPI 加密；未改动 → 保留原密文（_mqttStoredPassword）；新建设备空密码不落字段。
        /// 缺省字段省略（FW 兜底默认），仅显式不同才写入。</summary>
        private string BuildMqttJson()
        {
            var parts = new System.Collections.Generic.List<string>();
            parts.Add($"\"broker\":{JsonSerializer.Serialize(MqttBrokerBox.Text.Trim())}");
            if (int.TryParse(MqttPortBox.Text.Trim(), out var port) && port != 1883)
                parts.Add($"\"port\":{port}");
            if (MqttVersionBox.SelectedItem is System.Windows.Controls.ComboBoxItem vi && vi.Tag?.ToString() == "1")
                parts.Add($"\"version\":1");
            var cid = MqttClientIdBox.Text.Trim();
            if (cid.Length > 0 && cid != "hmi-01") parts.Add($"\"clientId\":{JsonSerializer.Serialize(cid)}");
            var user = MqttUsernameBox.Text.Trim();
            if (user.Length > 0) parts.Add($"\"username\":{JsonSerializer.Serialize(user)}");
            // 密码：改动（PasswordChanged 置位）→ 明文 DPAPI 加密；未动（保留占位符）→ 保留原密文；空 → 不落
            var typed = MqttPasswordBox.Password;
            if (_mqttPasswordTouched)
            {
                if (typed.Length > 0)
                    parts.Add($"\"password\":{JsonSerializer.Serialize(CredentialStore.Encrypt(typed))}");
                // 改动后清空 = 移除密码（不落字段）
            }
            else if (CredentialStore.IsEncrypted(_mqttStoredPassword))
            {
                parts.Add($"\"password\":{JsonSerializer.Serialize(_mqttStoredPassword)}");
            }
            if (int.TryParse(MqttKeepAliveBox.Text.Trim(), out var ka) && ka != 60)
                parts.Add($"\"keepAlive\":{ka}");
            if (MqttTlsBox.IsChecked == true) parts.Add($"\"enableTls\":true");
            var statusTag = MqttStatusTagBox.Text.Trim();
            if (statusTag.Length > 0) parts.Add($"\"statusTag\":{JsonSerializer.Serialize(statusTag)}");
            return "{" + string.Join(",", parts) + "}";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ShowError(string msg)
        {
            ErrorBox.Text = msg;
            ErrorBox.Visibility = Visibility.Visible;
        }
    }
}
