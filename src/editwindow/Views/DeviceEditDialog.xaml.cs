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
                ShowProtocolPanel(ProtocolType.ModbusTCP);   // 默认 ModbusTCP
                NameBox.Focus();
            }
            else
            {
                DialogTitle.Text = $"编辑设备 - {existing.Name}";
                Title = "编辑设备";
                NameBox.Text = existing.Name;
                ProtocolBox.SelectedIndex = (int)existing.Protocol;
                ShowProtocolPanel(existing.Protocol);
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

        private void ShowProtocolPanel(ProtocolType pt)
        {
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
                    if (root.TryGetProperty("baud", out var b) && b.ValueKind == JsonValueKind.Number) RtuBaudBox.Text = b.GetInt32().ToString();
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
                    if (root.TryGetProperty("broker", out var br) && br.ValueKind == JsonValueKind.String) MqttBrokerBox.Text = br.GetString() ?? MqttBrokerBox.Text;
                    if (root.TryGetProperty("clientId", out var c) && c.ValueKind == JsonValueKind.String) MqttClientIdBox.Text = c.GetString() ?? MqttClientIdBox.Text;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                // 旧数据 JSON 损坏/字段类型不符（如数字 port 存成字符串）：保留默认参数，允许用户重新填写
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
                    _ => BuildJson(intKeys: Array.Empty<string>(),
                        ("broker", MqttBrokerBox.Text.Trim()),
                        ("clientId", MqttClientIdBox.Text.Trim())),
                };
            }
            catch (Exception ex) { ShowError(ex.Message); return; }

            // 范围校验：从站号 1-247、端口 1-65535
            if (pt is ProtocolType.ModbusRTU)
            {
                if (int.TryParse(RtuSlaveBox.Text.Trim(), out var s) && (s < 1 || s > 247)) { ShowError("从站号必须在 1-247 之间"); return; }
            }
            else if (pt == ProtocolType.ModbusTCP)
            {
                if (int.TryParse(TcpPortBox.Text.Trim(), out var p) && (p < 1 || p > 65535)) { ShowError("端口必须在 1-65535 之间"); return; }
                if (int.TryParse(TcpSlaveBox.Text.Trim(), out var s) && (s < 1 || s > 247)) { ShowError("从站号必须在 1-247 之间"); return; }
            }

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

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void ShowError(string msg)
        {
            ErrorBox.Text = msg;
            ErrorBox.Visibility = Visibility.Visible;
        }
    }
}
