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
    /// Y-3b MQTT 三层映射配置页 ViewModel（2026-09-10 ④通信批）。
    /// 数据模型：HMIProject.MqttSettings（EnableMqtt 总开关 + Topics + Bindings + SchemaVersion）——
    /// proto 24 契约（Y-2 已建）；连接参数真源 = DeviceConfig MQTT 设备 connection_info JSON（Y-3a 裁决，
    /// Config 层复用 DeviceEditDialog 编辑，本 VM 不重复存连接）。
    /// GUI/CLI/AI 同一入口：本页编辑经 CommandService（mqtt_* 命令）落库（脏标记/撤销一致）；
    /// 外部命令改动 → CommandExecuted 事件同步刷新。
    /// </summary>
    public class MqttSettingsViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        /// <summary>MQTT 设备连接（DeviceConfig.Protocol==MQTT 首项；null=未配置——Config 层提示先建设备）。</summary>
        public DeviceConfig? MqttDevice => Project.Devices.FirstOrDefault(d => d.Protocol == ProtocolType.MQTT);

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

        /// <summary>请求打开连接配置（DeviceEditDialog 编辑 MQTT 设备；无设备 → 新建）。</summary>
        public event Action<DeviceConfig?>? ConnectionEditRequested;

        public ICommand NewTopicCommand { get; }
        public ICommand DeleteTopicCommand { get; }
        public ICommand EditConnectionCommand { get; }

        public MqttSettingsViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;
            NewTopicCommand = new RelayCommand(AddTopic);
            DeleteTopicCommand = new RelayCommand(DeleteSelectedTopic);
            EditConnectionCommand = new RelayCommand(() => ConnectionEditRequested?.Invoke(MqttDevice));

            CommandService.CommandExecuted += OnCommandExecuted;
            Refresh();
        }

        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // reviewer 🟡3：MQTT 命令 + 设备配置命令（连接真源 = DeviceConfig——通讯页建/改/删 MQTT 设备后本页需刷新）
            if (result.Success && (cmdName.StartsWith("mqtt_", StringComparison.Ordinal)
                 || cmdName is "configure_device" or "update_device" or "delete_device"))
                Refresh();
        }

        /// <summary>从工程模型同步 Topics/Bindings 集合（CommandExecuted/页面打开时调用）。</summary>
        public void Refresh()
        {
            SyncCollection(Topics, Project.MqttSettings?.Topics ?? Enumerable.Empty<MqttTopic>());
            SyncCollection(Bindings, Project.MqttSettings?.Bindings ?? Enumerable.Empty<MqttBinding>());
            OnPropertyChanged(nameof(MqttDevice));
            OnPropertyChanged(nameof(HasMqttDevice));
            OnPropertyChanged(nameof(ConnectionSummary));
            OnPropertyChanged(nameof(MqttSettingsReady));
            OnPropertyChanged(nameof(EnableMqtt));   // reviewer 🟡3：外部 mqtt_set_enabled 后 CheckBox 同步
        }

        public bool HasMqttDevice => MqttDevice != null;
        public bool MqttSettingsReady => Project.MqttSettings != null;

        /// <summary>连接摘要（无设备时提示先建）。</summary>
        public string ConnectionSummary
        {
            get
            {
                if (MqttDevice == null) return "未配置 MQTT 设备——请先「配置连接」新建（broker 主机/IP）";
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
