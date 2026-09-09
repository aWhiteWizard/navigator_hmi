using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// Y-3b MQTT 三层映射配置页 ViewModel（2026-09-10 ④通信批；Y Check 裁决 2026-09-11 改造）。
    /// 数据模型：HMIProject.MqttSettings（EnableMqtt + DeviceName 选定设备 + Topics + Bindings + SchemaVersion）——
    /// proto 24 契约（Y-2 已建）；**连接参数真源 = DeviceConfig MQTT 设备 connection_info JSON**（Y-3a 裁决：
    /// 设备在通讯配置页创建/编辑；本页「MQTT 设备」下拉只做引用选定（MqttSettings.DeviceName 落盘），不重复存连接）。
    /// GUI/CLI/AI 同一入口：本页编辑经 CommandService（mqtt_* 命令）落库（脏标记/撤销一致）；
    /// 外部命令改动 → CommandExecuted 事件同步刷新。
    /// </summary>
    public class MqttSettingsViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        /// <summary>MQTT 设备下拉条目（通讯页 Protocol==MQTT 设备；显示「名 (broker IP)」——Y Check 裁决）。
        /// Name=设备名；Display 含 broker 地址便于区分多 MQTT 通信。</summary>
        public sealed class MqttDeviceOption
        {
            public string Name { get; }
            public string BrokerIp { get; }
            public MqttDeviceOption(string name, string brokerIp) { Name = name; BrokerIp = brokerIp; }
            public string Display => BrokerIp.Length > 0 ? $"{Name} ({BrokerIp})" : Name;
        }

        /// <summary>MQTT 设备选项集合（每次刷新同步——通讯页建/改/删设备后更新；index0 恒为「未选定」空占位，其后为 MQTT 设备）。</summary>
        public ObservableCollection<MqttDeviceOption> MqttDevices { get; } = new();

        /// <summary>Y Check 修复（reviewer 🔴1 复审 2026-09-11）：Refresh 同步期间抑制 SelectedValue TwoWay 回写——
        /// SyncDeviceOptions 移除当前选中项（改名级联/删设备）瞬间 ComboBox SelectedValue 失配会异步回写清空 DeviceName；
        /// suppress 窗口覆盖同步块 + Dispatcher 延迟一拍（防逃出同步窗口的异步回写）。</summary>
        private bool _suppressDeviceSelection;

        /// <summary>当前选定的 MQTT 设备名（MqttSettings.DeviceName；空 = 未选定）。</summary>
        public string SelectedMqttDeviceName
        {
            get => Project.MqttSettings?.DeviceName ?? "";
            set
            {
                if (_suppressDeviceSelection) return;   // Refresh 同步/异步窗口内不回写（reviewer 🔴1）
                var cur = Project.MqttSettings?.DeviceName ?? "";
                if (cur == (value ?? "")) return;
                var r = CommandService.Execute("mqtt_set_device", new Dictionary<string, object?> { ["device_name"] = value ?? "" });
                if (!r.Success) System.Windows.MessageBox.Show(r.ErrorMessage ?? "选定设备失败");
                OnPropertyChanged();
                OnPropertyChanged(nameof(MqttDevice));
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }

        /// <summary>当前生效的 MQTT 设备（按 MqttSettings.DeviceName 查；未选定/不存在 → null——提示去通讯页选）。</summary>
        public DeviceConfig? MqttDevice
        {
            get
            {
                var dn = Project.MqttSettings?.DeviceName;
                if (!string.IsNullOrEmpty(dn))
                    return Project.Devices.FirstOrDefault(d => d.Name == dn && d.Protocol == ProtocolType.MQTT);
                return null;
            }
        }

        /// <summary>Topics 显示集合（随工程 MqttSettings.Topics 同步）。</summary>
        public ObservableCollection<MqttTopic> Topics { get; } = new();

        /// <summary>Bindings 显示集合。</summary>
        public ObservableCollection<MqttBinding> Bindings { get; } = new();

        private MqttTopic? _selectedTopic;
        public MqttTopic? SelectedTopic
        {
            get => _selectedTopic;
            set { _selectedTopic = value; OnPropertyChanged(); }
        }

        private MqttBinding? _selectedBinding;
        public MqttBinding? SelectedBinding
        {
            get => _selectedBinding;
            set { _selectedBinding = value; OnPropertyChanged(); }
        }

        private string _newTopicName = "";
        public string NewTopicName { get => _newTopicName; set { _newTopicName = value; OnPropertyChanged(); } }

        private string _newTopicPath = "";
        public string NewTopicPath { get => _newTopicPath; set { _newTopicPath = value; OnPropertyChanged(); } }

        private MqttTopicDirection _newTopicDirection = MqttTopicDirection.Publish;
        public MqttTopicDirection NewTopicDirection { get => _newTopicDirection; set { _newTopicDirection = value; OnPropertyChanged(); } }

        /// <summary>新增行方向下拉索引（0=发布 1=订阅——XAML ComboBox 序）。</summary>
        public int NewTopicDirectionIndex
        {
            get => NewTopicDirection == MqttTopicDirection.Publish ? 0 : 1;
            set { NewTopicDirection = value == 1 ? MqttTopicDirection.Subscribe : MqttTopicDirection.Publish; OnPropertyChanged(); }
        }

        /// <summary>EnableMqtt 总开关（绑定 UI CheckBox；变化经 mqtt_set_enabled 命令落库——命令层统一入口纪律，脏标记/CLI 对等）。</summary>
        public bool EnableMqtt
        {
            get => Project.MqttSettings?.EnableMqtt ?? false;
            set
            {
                if ((Project.MqttSettings?.EnableMqtt ?? false) == value) return;
                var r = CommandService.Execute("mqtt_set_enabled", new Dictionary<string, object?> { ["enabled"] = value });
                if (!r.Success) System.Windows.MessageBox.Show(r.ErrorMessage ?? "设置失败");
                OnPropertyChanged();
            }
        }

        /// <summary>请求跳转通讯配置页（Y Check 2026-09-11：MQTT 设备创建/编辑收敛通讯配置——本页无设备时提示去建）。</summary>
        public event Action? OpenCommunicationRequested;

        public ICommand NewTopicCommand { get; }
        public ICommand DeleteTopicCommand { get; }
        public ICommand GoToCommunicationCommand { get; }

        public MqttSettingsViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;
            NewTopicCommand = new RelayCommand(AddTopic);
            DeleteTopicCommand = new RelayCommand(DeleteSelectedTopic);
            GoToCommunicationCommand = new RelayCommand(() => OpenCommunicationRequested?.Invoke());

            CommandService.CommandExecuted += OnCommandExecuted;
            Refresh();
        }

        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // reviewer 🟡3：MQTT 命令 + 设备配置命令（连接真源 = DeviceConfig——通讯页建/改/删 MQTT 设备后本页需刷新）
            if (!result.Success || !(cmdName.StartsWith("mqtt_", StringComparison.Ordinal)
                 || cmdName is "configure_device" or "update_device" or "delete_device"))
                return;
            // Y Check 修复（reviewer 🟡1 2026-09-11）：AI 后台线程触发时跨线程改 ObservableCollection 会被吞
            // （对照 VariableManagerViewModel 同款封送——CommandExecuted 同步逐订阅者调用，本 handler 可能在后台线程）
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => Refresh()));
                return;
            }
            Refresh();
        }

        /// <summary>从工程模型同步设备选项 + Topics/Bindings 集合（CommandExecuted/页面打开时调用）。
        /// Y Check 修复（reviewer 🔴1 2026-09-11）：设备选项用增量对齐（禁 Clear+Add 瞬态）+ suppress 窗口——
        /// Clear/移除当前选中项会让绑定 ComboBox SelectedValue 失配 → TwoWay 回写清空 DeviceName（选中即丢）；
        /// suppress 置位覆盖同步变更 + Dispatcher 延迟一拍清除（防异步回写逃窗）。</summary>
        public void Refresh()
        {
            _suppressDeviceSelection = true;
            try
            {
                // 设备选项增量对齐：占位首项（Name=""）恒在 index0；其后按 Name 同步（新增 append、删除 remove、broker 变化原位更新）
                SyncDeviceOptions();
                SyncCollection(Topics, Project.MqttSettings?.Topics ?? Enumerable.Empty<MqttTopic>());
                SyncCollection(Bindings, Project.MqttSettings?.Bindings ?? Enumerable.Empty<MqttBinding>());
                OnPropertyChanged(nameof(SelectedMqttDeviceName));
                OnPropertyChanged(nameof(MqttDevice));
                OnPropertyChanged(nameof(ConnectionSummary));
                OnPropertyChanged(nameof(HasMqttDevice));
                OnPropertyChanged(nameof(MqttSettingsReady));
                OnPropertyChanged(nameof(EnableMqtt));   // reviewer 🟡3：外部 mqtt_set_enabled 后 CheckBox 同步
            }
            finally
            {
                _suppressDeviceSelection = false;
                // 延迟一拍再清：绑定失配回写可能经 Dispatcher 异步逃出同步窗口（KB wpf-combobox-style §补充1）
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    new Action(() => _suppressDeviceSelection = false));
            }
        }

        /// <summary>设备选项增量对齐（reviewer 🔴1：禁 Clear+Add——保持占位首项与已选设备项实例稳定，TwoWay SelectedValue 不失配）。
        /// 结构：index0 = 空「未选定」占位（Name=""）；其后按工程 MQTT 设备序。增量规则：仅 remove 不存在的、update broker 变化的、append 新增的。</summary>
        private void SyncDeviceOptions()
        {
            // 0) 确保占位首项（index0，Name=""）——首次刷新集合为空也先插占位，防首台真实设备落 index0（幽灵/清空语义丢失）
            if (MqttDevices.Count == 0 || MqttDevices[0].Name.Length > 0)
                MqttDevices.Insert(0, new MqttDeviceOption("", ""));
            var desired = new List<MqttDeviceOption> { new("", "") };
            desired.AddRange(Project.Devices.Where(d => d.Protocol == ProtocolType.MQTT)
                .Select(d => new MqttDeviceOption(d.Name, BrokerIpOf(d.ConnectionInfo))));
            // 1) 移除多余（index0 占位恒保留——从 index1 起删不存在的）
            for (int i = MqttDevices.Count - 1; i >= 1; i--)
                if (!desired.Any(x => x.Name == MqttDevices[i].Name))
                    MqttDevices.RemoveAt(i);
            // 2) 更新/追加（index0 后按 desired 序对齐；Name 相同 → 复用实例原位刷 Display——防 SelectedItem 引用失效）
            int anchor = 1;
            foreach (var d in desired.Skip(1))
            {
                var existing = MqttDevices.Skip(1).FirstOrDefault(x => x.Name == d.Name);
                if (existing != null)
                {
                    if (existing.BrokerIp != d.BrokerIp)
                        MqttDevices[MqttDevices.IndexOf(existing)] = d;   // broker 变化原位替换（保持位置）
                }
                else
                {
                    MqttDevices.Insert(Math.Min(anchor, MqttDevices.Count), d);   // 追加到设备区（index0 占位后）
                }
                anchor++;
            }
        }

        private static string BrokerIpOf(string connectionInfo)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(connectionInfo) ? "{}" : connectionInfo);
                if (doc.RootElement.TryGetProperty("broker", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.String)
                    return b.GetString() ?? "";
            }
            catch (System.Text.Json.JsonException) { /* 回落空 */ }
            return "";
        }

        public bool HasMqttDevice => MqttDevice != null;
        public bool MqttSettingsReady => Project.MqttSettings != null;

        /// <summary>连接摘要（未选定/无设备时提示去通讯配置新建/选择）。</summary>
        public string ConnectionSummary
        {
            get
            {
                if (MqttDevice == null) return "未选定 MQTT 设备——请到「通讯配置」创建 MQTT 设备后在此下拉选择";
                var summary = MqttDevice.ConnectionInfo;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(
                        string.IsNullOrWhiteSpace(summary) ? "{}" : summary);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("broker", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = $"Broker: {b.GetString()}";
                        if (root.TryGetProperty("port", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number)
                            s += $":{p.GetInt32()}";
                        s += $"  (设备: {MqttDevice.Name})";
                        return s;
                    }
                }
                catch (System.Text.Json.JsonException) { /* 回落原始串 */ }
                return $"设备: {MqttDevice.Name}";
            }
        }

        /// <summary>新增 Topic（校验方向/topic 路径合法后经命令层落库）。</summary>
        private void AddTopic()
        {
            var name = NewTopicName.Trim();
            var path = NewTopicPath.Trim();
            if (name.Length == 0) { System.Windows.MessageBox.Show("主题配置名不能为空"); return; }
            if (path.Length == 0) { System.Windows.MessageBox.Show("Topic 路径不能为空"); return; }
            // 主题校验（§5.4 动态 topic 规则——发布禁 +/#、≤512；订阅允许通配符但同父禁重复；同名配置查重）
            if (path.Length > 512) { System.Windows.MessageBox.Show("Topic 路径不能超过 512 字符"); return; }
            if (NewTopicDirection == MqttTopicDirection.Publish && (path.Contains('+') || path.Contains('#')))
            { System.Windows.MessageBox.Show("发布 Topic 禁止使用通配符 +/#"); return; }
            if (NewTopicDirection == MqttTopicDirection.Subscribe && !IsValidSubscribeTopic(path))
            { System.Windows.MessageBox.Show("订阅 Topic 通配符非法（+ 占整级、# 仅可结尾"); return; }
            var existing = Project.MqttSettings?.Topics.Any(t => t.Name == name) ?? false;
            if (existing) { System.Windows.MessageBox.Show($"主题配置 \"{name}\" 已存在"); return; }
            var r = CommandService.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["topic"] = path,
                ["direction"] = NewTopicDirection == MqttTopicDirection.Publish ? "publish" : "subscribe",
            });
            if (!r.Success) { System.Windows.MessageBox.Show(r.ErrorMessage ?? "添加失败"); return; }
            NewTopicName = ""; NewTopicPath = "";
        }

        private static bool IsValidSubscribeTopic(string topic)
        {
            // 与命令层 MqttTopicValidator 同语义（reviewer 🟡1）：+ 占整级、# 独占末级
            var levels = topic.Split('/');
            for (int i = 0; i < levels.Length; i++)
            {
                var lv = levels[i];
                if (lv.Contains('#'))
                    return lv == "#" && i == levels.Length - 1;
                if (lv.Contains('+') && lv != "+")
                    return false;
            }
            return true;
        }

        private void DeleteSelectedTopic()
        {
            if (SelectedTopic == null) return;
            var r = CommandService.Execute("mqtt_delete_topic", new Dictionary<string, object?>
            {
                ["name"] = SelectedTopic.Name,
            });
            if (!r.Success) System.Windows.MessageBox.Show(r.ErrorMessage ?? "删除失败");
        }

        private static void SyncCollection<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> source)
        {
            var src = source.ToList();
            for (int i = target.Count - 1; i >= 0; i--)
                if (!src.Contains(target[i])) target.RemoveAt(i);
            int idx = 0;
            foreach (var item in src)
            {
                if (idx < target.Count && !ReferenceEquals(target[idx], item))
                    target[idx] = item;
                else if (idx >= target.Count)
                    target.Add(item);
                idx++;
            }
            while (target.Count > src.Count) target.RemoveAt(target.Count - 1);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
