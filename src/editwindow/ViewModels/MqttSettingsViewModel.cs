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
    /// Z 循环 MQTT 多连接管理器配置页 ViewModel（2026-09-11 重构，取代 Y-3b 单连接版）。
    /// 数据模型：HMIProject.MqttSettings（EnableMqtt 总开关 + Connections 多连接——每连接 name + Config 内联连接参数
    /// + Topics + Bindings，连接归属：Binding 挂哪棵 Topic 树即属该连接；proto 24 Z-1 契约）。**连接参数真源 = MqttConnection.Config**（Y 循环
    /// DeviceConfig 连接真源废弃——Z-4 通讯页收窄只配 Modbus）。
    /// 页面两态：总览（SelectedConnectionName 空：总开关 + 连接列表/新建）↔ 连接页（参数编辑 + 本连接 Topic/Binding）。
    /// GUI/CLI/AI 同一入口：本页编辑经 CommandService（mqtt_* 命令——Z 循环映射命令带 connection_name）落库（脏标记/撤销一致）；
    /// 外部命令改动 → CommandExecuted 事件同步刷新。
    /// </summary>
    public class MqttSettingsViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        /// <summary>连接列表行包装（显示 Name + broker:port 摘要——Z 循环连接参数内联）。</summary>
        public sealed class ConnectionItem
        {
            public MqttConnection Model { get; }
            public ConnectionItem(MqttConnection model) { Model = model; }
            public string Name => Model.Name;
            public string Summary
            {
                get
                {
                    var cfg = Model.Config;
                    if (cfg == null || string.IsNullOrWhiteSpace(cfg.Broker)) return "未填写 broker";
                    return cfg.Port > 0 ? $"{cfg.Broker}:{cfg.Port}" : cfg.Broker;
                }
            }
        }

        /// <summary>连接列表（总览页；随工程 MqttSettings.Connections 同步）。</summary>
        public ObservableCollection<ConnectionItem> Connections { get; } = new();

        /// <summary>当前选中连接（null/空 = 总览态——显示总开关 + 连接列表）。</summary>
        private string _selectedConnectionName = "";

        /// <summary>连接切换事件（SelectedConnectionName 变化触发——上层（EditWindowViewModel 树高亮刷新 +
        /// code-behind 清 PasswordBox 防跨连接残留——reviewer Z-2 🔴C1/🟡C3）。</summary>
        public event Action? ConnectionSwitched;

        /// <summary>连接参数应用成功事件（上层清 PasswordBox 明文——reviewer Z-2 🔴C1）。</summary>
        public event Action? PasswordApplied;

        public string SelectedConnectionName
        {
            get => _selectedConnectionName;
            set
            {
                if (_selectedConnectionName == (value ?? "")) return;
                _selectedConnectionName = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOverview));
                OnPropertyChanged(nameof(IsConnectionPage));
                OnPropertyChanged(nameof(CurrentConnectionName));   // 🟡C2（reviewer Z-2）：页头标题通知
                LoadConnectionEdits();
                ConnectionSwitched?.Invoke();
            }
        }

        /// <summary>总览态（未选中连接）。</summary>
        public bool IsOverview => string.IsNullOrEmpty(SelectedConnectionName);

        /// <summary>连接页态（已选中某连接）。</summary>
        public bool IsConnectionPage => !IsOverview;

        /// <summary>当前连接（按 SelectedConnectionName 查；不存在 → null——连接被删后回落总览）。</summary>
        public MqttConnection? SelectedConnection
            => Project.MqttSettings?.Connections.FirstOrDefault(c => c.Name == SelectedConnectionName);

        /// <summary>当前连接显示名（连接页标题）。</summary>
        public string CurrentConnectionName => SelectedConnectionName;

        /// <summary>EnableMqtt 总开关（绑定 UI CheckBox；变化经 mqtt_set_enabled 命令落库——命令层统一入口纪律）。</summary>
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

        // ═══ 连接参数编辑缓冲（连接页；不直绑模型——「应用」一次性经 mqtt_update_connection 落库，防直改无脏标记）═══
        private string _connBrokerText = "";
        public string ConnBrokerText { get => _connBrokerText; set { _connBrokerText = value ?? ""; OnPropertyChanged(); } }

        private string _connPortText = "1883";
        public string ConnPortText { get => _connPortText; set { _connPortText = value ?? ""; OnPropertyChanged(); } }

        private int _connVersionIndex;
        public int ConnVersionIndex { get => _connVersionIndex; set { _connVersionIndex = value; OnPropertyChanged(); } }

        private string _connClientIdText = "";
        public string ConnClientIdText { get => _connClientIdText; set { _connClientIdText = value ?? ""; OnPropertyChanged(); } }

        private string _connUsernameText = "";
        public string ConnUsernameText { get => _connUsernameText; set { _connUsernameText = value ?? ""; OnPropertyChanged(); } }

        private string _connKeepAliveText = "60";
        public string ConnKeepAliveText { get => _connKeepAliveText; set { _connKeepAliveText = value ?? ""; OnPropertyChanged(); } }

        private string _connStatusTagText = "";
        public string ConnStatusTagText { get => _connStatusTagText; set { _connStatusTagText = value ?? ""; OnPropertyChanged(); } }

        /// <summary>密码编辑（PasswordBox 明文暂存；「应用」时 DPAPI 加密后提交——Y-3a BuildMqttJson 同链）。</summary>
        private string _passwordPlain = "";
        public string PasswordPlain { get => _passwordPlain; set { _passwordPlain = value ?? ""; OnPropertyChanged(); } }

        /// <summary>密码已存密文（连接页显示「已设置/未设置」）。</summary>
        public bool HasStoredPassword => !string.IsNullOrEmpty(SelectedConnection?.Config?.Password);

        // ═══ Topic 操作（当前连接内）═══
        public ObservableCollection<MqttTopic> Topics { get; } = new();
        private MqttTopic? _selectedTopic;
        public MqttTopic? SelectedTopic { get => _selectedTopic; set { _selectedTopic = value; OnPropertyChanged(); LoadTopicEdits(); } }

        private string _newTopicName = "";
        public string NewTopicName { get => _newTopicName; set { _newTopicName = value; OnPropertyChanged(); } }
        private string _newTopicPath = "";
        public string NewTopicPath { get => _newTopicPath; set { _newTopicPath = value; OnPropertyChanged(); } }
        public int NewTopicDirectionIndex { get; set; }   // 0=发布 1=订阅

        // Topic 编辑缓冲（选中行 → 编辑区）
        private int _topicQosIndex;
        public int TopicQosIndex { get => _topicQosIndex; set { _topicQosIndex = value; OnPropertyChanged(); } }
        private bool _topicRetain;
        public bool TopicRetain { get => _topicRetain; set { _topicRetain = value; OnPropertyChanged(); } }
        private string _topicIntervalText = "0";
        public string TopicIntervalText { get => _topicIntervalText; set { _topicIntervalText = value ?? ""; OnPropertyChanged(); } }
        private int _topicTemplateIndex;
        public int TopicTemplateIndex { get => _topicTemplateIndex; set { _topicTemplateIndex = value; OnPropertyChanged(); } }
        private string _topicResponseText = "";
        public string TopicResponseText { get => _topicResponseText; set { _topicResponseText = value ?? ""; OnPropertyChanged(); } }

        // ═══ Binding 操作（当前连接内）═══
        public ObservableCollection<MqttBinding> Bindings { get; } = new();
        private MqttBinding? _selectedBinding;
        public MqttBinding? SelectedBinding { get => _selectedBinding; set { _selectedBinding = value; OnPropertyChanged(); } }

        /// <summary>Binding 新增行：Topic 下拉选项（当前连接 Topics——归属本连接）。</summary>
        public ObservableCollection<MqttTopic> BindingTopicOptions { get; } = new();

        /// <summary>Binding 新增行：变量下拉选项（工程 Tags）。</summary>
        public ObservableCollection<Tag> BindingTagOptions { get; } = new();

        private int _newBindingTopicIndex = -1;
        public int NewBindingTopicIndex { get => _newBindingTopicIndex; set { _newBindingTopicIndex = value; OnPropertyChanged(); } }
        private int _newBindingTagIndex = -1;
        public int NewBindingTagIndex { get => _newBindingTagIndex; set { _newBindingTagIndex = value; OnPropertyChanged(); } }
        private string _newBindingFieldText = "";
        public string NewBindingFieldText { get => _newBindingFieldText; set { _newBindingFieldText = value ?? ""; OnPropertyChanged(); } }

        // ═══ 命令 ═══
        public ICommand AddConnectionCommand { get; }
        public ICommand DeleteConnectionCommand { get; }
        public ICommand ApplyConnectionCommand { get; }
        public ICommand BackToOverviewCommand { get; }
        public ICommand AddTopicCommand { get; }
        public ICommand UpdateTopicCommand { get; }
        public ICommand DeleteTopicCommand { get; }
        public ICommand AddBindingCommand { get; }
        public ICommand DeleteBindingCommand { get; }

        public MqttSettingsViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;
            AddConnectionCommand = new RelayCommand(AddNewConnection);
            DeleteConnectionCommand = new RelayCommand(DeleteSelectedConnection);
            ApplyConnectionCommand = new RelayCommand(ApplyConnection);
            BackToOverviewCommand = new RelayCommand(() => SelectedConnectionName = "");
            AddTopicCommand = new RelayCommand(AddTopic);
            UpdateTopicCommand = new RelayCommand(UpdateSelectedTopic);
            DeleteTopicCommand = new RelayCommand(DeleteSelectedTopic);
            AddBindingCommand = new RelayCommand(AddBinding);
            DeleteBindingCommand = new RelayCommand(DeleteSelectedBinding);

            CommandService.CommandExecuted += OnCommandExecuted;
            Refresh();
        }

        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // mqtt_* + 变量/列表命令（Binding 下拉的 tag 源；Z 循环连接删除/改名后回落总览）
            if (!result.Success || !(cmdName.StartsWith("mqtt_", StringComparison.Ordinal)
                 || cmdName is "create_tag" or "update_tag" or "delete_tag"))
                return;
            // Y Check 修复（reviewer 🟡1 2026-09-11）：AI 后台线程触发时跨线程改 ObservableCollection 会被吞
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => Refresh()));
                return;
            }
            Refresh();
        }

        /// <summary>从工程模型同步连接列表/集合（CommandExecuted/页面打开/连接切换时调用）。
        /// 连接集合增量对齐（禁 Clear+Add 瞬态——ComboBox/ListBox 选中失配回写同族教训，见 4_bugs wpf-combobox-selectedvalue-clearadd）。</summary>
        public void Refresh()
        {
            var conns = Project.MqttSettings?.Connections ?? new System.Collections.Generic.List<MqttConnection>();
            // 连接列表增量对齐
            for (int i = Connections.Count - 1; i >= 0; i--)
                if (!conns.Any(c => c.Name == Connections[i].Name)) Connections.RemoveAt(i);
            int ci = 0;
            foreach (var c in conns)
            {
                var existing = Connections.FirstOrDefault(x => x.Name == c.Name);
                if (existing == null)
                    Connections.Insert(Math.Min(ci, Connections.Count), new ConnectionItem(c));
                ci++;
            }
            // 选中连接被删 → 回落总览
            if (!IsOverview && SelectedConnection == null)
                SelectedConnectionName = "";

            OnPropertyChanged(nameof(EnableMqtt));
            OnPropertyChanged(nameof(IsOverview));
            OnPropertyChanged(nameof(IsConnectionPage));
            OnPropertyChanged(nameof(HasStoredPassword));
            LoadConnectionEdits();
        }

        /// <summary>选中连接变化/刷新时载入连接参数编辑缓冲 + Topic/Binding 集合。</summary>
        private void LoadConnectionEdits()
        {
            var c = SelectedConnection;
            var cfg = c?.Config;
            ConnBrokerText = cfg?.Broker ?? "";
            ConnPortText = cfg != null && cfg.Port > 0 ? cfg.Port.ToString() : "1883";
            ConnVersionIndex = cfg?.Version == MqttVersion.V5_0 ? 1 : 0;
            ConnClientIdText = cfg?.ClientId ?? "";
            ConnUsernameText = cfg?.Username ?? "";
            ConnKeepAliveText = cfg != null && cfg.KeepAliveSec > 0 ? cfg.KeepAliveSec.ToString() : "60";
            ConnStatusTagText = cfg?.StatusTag ?? "";
            _passwordPlain = "";
            OnPropertyChanged(nameof(PasswordPlain));
            OnPropertyChanged(nameof(HasStoredPassword));
            // 🟡C5（reviewer Z-2）：切连接清选中项——防跨连接同名 Topic 选中态残留误更新
            if (_selectedTopic != null) { _selectedTopic = null; OnPropertyChanged(nameof(SelectedTopic)); }
            if (_selectedBinding != null) { _selectedBinding = null; OnPropertyChanged(nameof(SelectedBinding)); }

            SyncCollection(Topics, c?.Topics ?? Enumerable.Empty<MqttTopic>());
            SyncCollection(Bindings, c?.Bindings ?? Enumerable.Empty<MqttBinding>());
            SyncCollection(BindingTopicOptions, c?.Topics ?? Enumerable.Empty<MqttTopic>());
            SyncCollection(BindingTagOptions, Project.Tags);
            NewBindingTopicIndex = BindingTopicOptions.Count > 0 ? 0 : -1;
            NewBindingTagIndex = BindingTagOptions.Count > 0 ? 0 : -1;
            OnPropertyChanged(nameof(NewBindingTopicIndex));
            OnPropertyChanged(nameof(NewBindingTagIndex));
        }

        /// <summary>Topic 选中行变化 → 载入编辑区缓冲。</summary>
        private void LoadTopicEdits()
        {
            var t = SelectedTopic;
            TopicQosIndex = t?.Qos ?? 0;
            TopicRetain = t?.Retain ?? false;
            TopicIntervalText = t?.PublishIntervalMs.ToString() ?? "0";
            TopicTemplateIndex = t?.JsonTemplate == MqttJsonTemplate.KvWithTimestamp ? 1 : 0;
            TopicResponseText = t?.ResponseTopic ?? "";
        }

        // ═══ 命令实现 ═══

        /// <summary>新建连接（public——连接管理（总览）页「＋ 新建连接」按钮调用；照画面添加模式——
        /// 默认名「连接N」，命令落库成功后 SelectedConnectionName 置新名进入连接页编辑）。</summary>
        public void AddNewConnection()
        {
            var s = Project.MqttSettings ?? new MqttSettings { SchemaVersion = 1 };
            int n = 1;
            var taken = new System.Collections.Generic.HashSet<string>(s.Connections.Select(c => c.Name));
            while (taken.Contains($"连接{n}")) n++;
            var name = $"连接{n}";
            var r = CommandService.Execute("mqtt_add_connection", new Dictionary<string, object?> { ["name"] = name });
            if (!r.Success) { System.Windows.MessageBox.Show(r.ErrorMessage ?? "新建连接失败"); return; }
            SelectedConnectionName = name;
        }

        private void DeleteSelectedConnection()
        {
            if (IsOverview || SelectedConnection == null) return;
            if (System.Windows.MessageBox.Show($"删除 MQTT 连接 \"{SelectedConnectionName}\"？（连带删除其主题与绑定）",
                    "删除连接", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning)
                != System.Windows.MessageBoxResult.OK) return;
            var r = CommandService.Execute("mqtt_delete_connection", new Dictionary<string, object?> { ["name"] = SelectedConnectionName });
            if (!r.Success) { System.Windows.MessageBox.Show(r.ErrorMessage ?? "删除连接失败"); return; }
            SelectedConnectionName = "";   // 回落总览（Refresh 里也会兜底）
        }

        /// <summary>应用连接参数（缓冲字段 + 密码 DPAPI 加密 → mqtt_update_connection 一次性落库）。</summary>
        private void ApplyConnection()
        {
            if (IsOverview || SelectedConnection == null) return;
            var name = SelectedConnectionName;
            var p = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["broker"] = ConnBrokerText.Trim(),
                ["port"] = ConnPortText.Trim(),
                ["version"] = ConnVersionIndex == 1 ? "1" : "0",
                ["client_id"] = ConnClientIdText.Trim(),
                ["username"] = ConnUsernameText.Trim(),
                ["keep_alive_sec"] = ConnKeepAliveText.Trim(),
                ["status_tag"] = ConnStatusTagText.Trim(),
            };
            if (_passwordPlain.Length > 0)
                p["password"] = CredentialStore.Encrypt(_passwordPlain);   // 明文 → DPAPI 密文（绝不明文入库）
            var r = CommandService.Execute("mqtt_update_connection", p);
            if (!r.Success) { System.Windows.MessageBox.Show(r.ErrorMessage ?? "应用连接参数失败"); return; }
            _passwordPlain = "";
            OnPropertyChanged(nameof(PasswordPlain));
            OnPropertyChanged(nameof(HasStoredPassword));
            PasswordApplied?.Invoke();   // 🔴C1（reviewer Z-2）：应用成功清 PasswordBox 明文（防残留入下一连接）
        }

        /// <summary>新增 Topic（当前连接内；校验后经命令层落库——查重/同父收窄本连接）。</summary>
        private void AddTopic()
        {
            if (IsOverview || SelectedConnection == null) return;
            var name = NewTopicName.Trim();
            var path = NewTopicPath.Trim();
            if (name.Length == 0) { System.Windows.MessageBox.Show("主题配置名不能为空"); return; }
            if (path.Length == 0) { System.Windows.MessageBox.Show("Topic 路径不能为空"); return; }
            if (path.Length > 512) { System.Windows.MessageBox.Show("Topic 路径不能超过 512 字符"); return; }
            var dir = NewTopicDirectionIndex == 1 ? MqttTopicDirection.Subscribe : MqttTopicDirection.Publish;
            if (dir == MqttTopicDirection.Publish && (path.Contains('+') || path.Contains('#')))
            { System.Windows.MessageBox.Show("发布 Topic 禁止使用通配符 +/#"); return; }
            if (dir == MqttTopicDirection.Subscribe && !IsValidSubscribeTopic(path))
            { System.Windows.MessageBox.Show("订阅 Topic 通配符非法（+ 占整级、# 仅可结尾）"); return; }
            var r = CommandService.Execute("mqtt_add_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = SelectedConnectionName,
                ["name"] = name,
                ["topic"] = path,
                ["direction"] = dir == MqttTopicDirection.Publish ? "publish" : "subscribe",
            });
            if (!r.Success) { System.Windows.MessageBox.Show(r.ErrorMessage ?? "添加失败"); return; }
            NewTopicName = ""; NewTopicPath = "";
        }

        /// <summary>更新选中 Topic 属性（编辑区缓冲 → mqtt_update_topic）。</summary>
        private void UpdateSelectedTopic()
        {
            if (IsOverview || SelectedTopic == null) return;
            var r = CommandService.Execute("mqtt_update_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = SelectedConnectionName,
                ["name"] = SelectedTopic.Name,
                ["qos"] = TopicQosIndex.ToString(),
                ["retain"] = TopicRetain.ToString().ToLowerInvariant(),
                ["publish_interval_ms"] = TopicIntervalText.Trim(),
                ["json_template"] = TopicTemplateIndex.ToString(),
                ["response_topic"] = TopicResponseText,
            });
            if (!r.Success) System.Windows.MessageBox.Show(r.ErrorMessage ?? "更新失败");
        }

        private void DeleteSelectedTopic()
        {
            if (IsOverview || SelectedTopic == null) return;
            var r = CommandService.Execute("mqtt_delete_topic", new Dictionary<string, object?>
            {
                ["connection_name"] = SelectedConnectionName,
                ["name"] = SelectedTopic.Name,
            });
            if (!r.Success) System.Windows.MessageBox.Show(r.ErrorMessage ?? "删除失败");
        }

        /// <summary>新增 Binding（当前连接内 topic + 工程 tag → mqtt_set_binding）。</summary>
        private void AddBinding()
        {
            if (IsOverview || SelectedConnection == null) return;
            if (NewBindingTopicIndex < 0 || NewBindingTopicIndex >= BindingTopicOptions.Count)
            { System.Windows.MessageBox.Show("请先为连接添加主题（Binding 挂本连接 Topic 树）"); return; }
            if (NewBindingTagIndex < 0 || NewBindingTagIndex >= BindingTagOptions.Count)
            { System.Windows.MessageBox.Show("请选择变量"); return; }
            var field = NewBindingFieldText.Trim();
            if (field.Length == 0) { System.Windows.MessageBox.Show("JSON 字段名不能为空"); return; }
            var r = CommandService.Execute("mqtt_set_binding", new Dictionary<string, object?>
            {
                ["connection_name"] = SelectedConnectionName,
                ["topic_name"] = BindingTopicOptions[NewBindingTopicIndex].Name,
                ["tag_name"] = BindingTagOptions[NewBindingTagIndex].Name,
                ["field_name"] = field,
            });
            if (!r.Success) { System.Windows.MessageBox.Show(r.ErrorMessage ?? "添加绑定失败"); return; }
            NewBindingFieldText = "";
        }

        private void DeleteSelectedBinding()
        {
            if (IsOverview || SelectedBinding == null || SelectedConnection == null) return;
            var r = CommandService.Execute("mqtt_remove_binding", new Dictionary<string, object?>
            {
                ["connection_name"] = SelectedConnectionName,
                ["topic_name"] = SelectedBinding.TopicName,
                ["tag_name"] = SelectedBinding.TagName,
            });
            if (!r.Success) System.Windows.MessageBox.Show(r.ErrorMessage ?? "删除绑定失败");
        }

        private static bool IsValidSubscribeTopic(string topic)
        {
            // 与命令层 MqttTopicValidator 同语义：+ 占整级、# 独占末级
            var levels = topic.Split('/');
            for (int i = 0; i < levels.Length; i++)
            {
                var lv = levels[i];
                if (lv.Contains('#')) return lv == "#" && i == levels.Length - 1;
                if (lv.Contains('+') && lv != "+") return false;
            }
            return true;
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
