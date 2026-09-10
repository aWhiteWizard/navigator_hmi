using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    // ═══════════════════════════════════════════════════════════════
    // MQTT 模型（Y-2 ④通信批 2026-09-10 契约先行 + Z 循环多连接管理器 2026-09-11 重构）
    // 与 proto MqttSettings/MqttConnection/MqttConfig/MqttTopic/MqttBinding 逐字段对齐
    // （字段号 = proto 字段号；两端一致性由 MqttContractTests 字段号审计测试锁——
    // V-3c 8b95036「PC fw/proto 漏同步」N+9 教训直接对治，2026-09-10）
    // 设计源: v1.1-design §5.4 P0-1（Config→Topic→Binding 三层，proto 一次设计到位）
    //         + §5.4 追加段（Z 循环：MqttSettings.connections 多连接管理器
    //         Connection→Topic→Binding 三层归属；旧单份字段 3/4/5/6 deprecated 仅迁移读取）
    // ═══════════════════════════════════════════════════════════════

    /// <summary>MQTT 协议版本（与 proto MqttVersion 值一致）。
    /// 命名豁免：V3_1_1 带下划线为协议镜像名（对齐 MQTT_V3_1_1 wire 语义），非 PascalCase 惯例例外。</summary>
    public enum MqttVersion
    {
        /// <summary>MQTT 3.1.1（默认；本地匿名 1883 测试基线）</summary>
        V3_1_1 = 0,
        /// <summary>MQTT 5.0</summary>
        V5_0 = 1,
    }

    /// <summary>主题方向（发布/订阅分离——proto MqttTopicDirection 值一致）。</summary>
    public enum MqttTopicDirection
    {
        /// <summary>发布（HMI → Broker）</summary>
        Publish = 0,
        /// <summary>订阅（Broker → HMI）</summary>
        Subscribe = 1,
    }

    /// <summary>JSON 模板枚举（KV / 带时间戳——proto MqttJsonTemplate 值一致；模板枚举两端同步）。</summary>
    public enum MqttJsonTemplate
    {
        /// <summary>键值对 {"字段名":"值"}</summary>
        Kv = 0,
        /// <summary>键值对 + 时间戳</summary>
        KvWithTimestamp = 1,
    }

    /// <summary>连接配置（复用——新主载体为 MqttConnection.Config=2；旧单份 MqttSettings.Config=3 deprecated 仅迁移读取；
    /// 凭据两级加密——Password 存编译侧加密包，绝不明文进 .navihmi）。</summary>
    [ProtoContract]
    public class MqttConfig : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _broker = "";

        /// <summary>broker 主机/IP（不含协议前缀；本地测试 "127.0.0.1"）</summary>
        [ProtoMember(1)]
        public string Broker
        {
            get => _broker;
            set { if (_broker != value) { _broker = value ?? ""; OnPropertyChanged(); } }
        }

        private int _port = 1883;

        /// <summary>端口（默认 1883）</summary>
        [ProtoMember(2)]
        public int Port
        {
            get => _port;
            set { if (_port != value) { _port = value; OnPropertyChanged(); } }
        }

        private MqttVersion _version = MqttVersion.V3_1_1;

        /// <summary>协议版本（3.1.1 / 5.0）</summary>
        [ProtoMember(3)]
        public MqttVersion Version
        {
            get => _version;
            set { if (_version != value) { _version = value; OnPropertyChanged(); } }
        }

        private string _clientId = "";

        /// <summary>客户端 ID（空 = FW 自动生成）</summary>
        [ProtoMember(4)]
        public string ClientId
        {
            get => _clientId;
            set { if (_clientId != value) { _clientId = value ?? ""; OnPropertyChanged(); } }
        }

        private string _username = "";

        /// <summary>用户名（匿名 = 空）</summary>
        [ProtoMember(5)]
        public string Username
        {
            get => _username;
            set { if (_username != value) { _username = value ?? ""; OnPropertyChanged(); } }
        }

        private string _password = "";

        /// <summary>密码加密包（PC 编译侧再加密；FW 端解密使用；绝不明文进 .navihmi）</summary>
        [ProtoMember(6)]
        public string Password
        {
            get => _password;
            set { if (_password != value) { _password = value ?? ""; OnPropertyChanged(); } }
        }

        private int _keepAliveSec = 60;

        /// <summary>keepAlive 心跳（秒，默认 60；**0 = MQTT 协议禁用心跳——合法值**）。
        /// IsRequired=true 恒写：初始值 60 ≠ CLR 默认 0，若用户显式设 0 而无 IsRequired，
        /// protobuf-net 按「值==CLR 默认」省略 → wire 缺省 → 回读 60 静默失真（4_bugs bool-default-loss 同族 int 延伸）。</summary>
        [ProtoMember(7, IsRequired = true)]
        public int KeepAliveSec
        {
            get => _keepAliveSec;
            set { if (_keepAliveSec != value) { _keepAliveSec = value; OnPropertyChanged(); } }
        }

        private bool _enableTls;

        /// <summary>TLS 开关（本轮恒 false——V1.2 按需补；proto 预留）。</summary>
        [ProtoMember(8)]
        public bool EnableTls
        {
            get => _enableTls;
            set { if (_enableTls != value) { _enableTls = value; OnPropertyChanged(); } }
        }

        private string _statusTag = "";

        /// <summary>连接状态回写变量名（回写 4 态数值 0-3——断开/连接中/已连接/错误；MqttConnectionState 为 proto 专属枚举，
        /// C# 无镜像——FW 侧实现；空 = 不回写）</summary>
        [ProtoMember(9)]
        public string StatusTag
        {
            get => _statusTag;
            set { if (_statusTag != value) { _statusTag = value ?? ""; OnPropertyChanged(); } }
        }
    }

    /// <summary>主题配置（发布/订阅分离维护：publish 禁 +/#、≤512；subscribe 合法 wildcard、同父节点禁重复）。</summary>
    [ProtoContract]
    public class MqttTopic : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _name = "";

        /// <summary>主题配置名（连接内唯一标识，Binding 引用锚；如 "泵站状态"——Z 循环：跨连接允许同名，归属连接收窄）</summary>
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value ?? ""; OnPropertyChanged(); } }
        }

        private MqttTopicDirection _direction = MqttTopicDirection.Publish;

        /// <summary>方向（发布/订阅）</summary>
        [ProtoMember(2)]
        public MqttTopicDirection Direction
        {
            get => _direction;
            set { if (_direction != value) { _direction = value; OnPropertyChanged(); } }
        }

        private string _topic = "";

        /// <summary>topic 路径（发布禁 +/#、≤512；订阅允许通配符 +/# 但同父节点禁重复）</summary>
        [ProtoMember(3)]
        public string Topic
        {
            get => _topic;
            set { if (_topic != value) { _topic = value ?? ""; OnPropertyChanged(); } }
        }

        private int _qos;

        /// <summary>QoS 0/1/2</summary>
        [ProtoMember(4)]
        public int Qos
        {
            get => _qos;
            set { if (_qos != value) { _qos = value; OnPropertyChanged(); } }
        }

        private bool _retain;

        /// <summary>保留消息（发布侧）</summary>
        [ProtoMember(5)]
        public bool Retain
        {
            get => _retain;
            set { if (_retain != value) { _retain = value; OnPropertyChanged(); } }
        }

        private int _publishIntervalMs;

        /// <summary>周期发布间隔（ms；0 = 不周期，仅按需——事件驱动为主）</summary>
        [ProtoMember(6)]
        public int PublishIntervalMs
        {
            get => _publishIntervalMs;
            set { if (_publishIntervalMs != value) { _publishIntervalMs = value; OnPropertyChanged(); } }
        }

        private MqttJsonTemplate _jsonTemplate = MqttJsonTemplate.Kv;

        /// <summary>JSON 模板（KV / 带时间戳）</summary>
        [ProtoMember(7)]
        public MqttJsonTemplate JsonTemplate
        {
            get => _jsonTemplate;
            set { if (_jsonTemplate != value) { _jsonTemplate = value; OnPropertyChanged(); } }
        }

        private string _responseTopic = "";

        /// <summary>响应主题（请求-响应模式用；空 = 无）</summary>
        [ProtoMember(8)]
        public string ResponseTopic
        {
            get => _responseTopic;
            set { if (_responseTopic != value) { _responseTopic = value ?? ""; OnPropertyChanged(); } }
        }

        /// <summary>方向中文显示（UI 用；[ProtoIgnore] 语义——非 ProtoMember 不落盘）。</summary>
        public string DirectionText => Direction == MqttTopicDirection.Publish ? "发布" : "订阅";

        /// <summary>JSON 模板显示名（UI 用；KV / KV+时间戳）。</summary>
        public string JsonTemplateText => JsonTemplate == MqttJsonTemplate.Kv ? "KV" : "KV+时间戳";
    }

    /// <summary>变量↔字段绑定（三层映射最底层：TagName 引用 Tag.Name，FieldName = JSON 字段名）。</summary>
    [ProtoContract]
    public class MqttBinding : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _topicName = "";

        /// <summary>所属 MqttTopic.Name（发布/订阅方向由 topic 决定）</summary>
        [ProtoMember(1)]
        public string TopicName
        {
            get => _topicName;
            set { if (_topicName != value) { _topicName = value ?? ""; OnPropertyChanged(); } }
        }

        private string _tagName = "";

        /// <summary>变量名（Tag.Name；订阅 = 写入目标，发布 = 数据源）</summary>
        [ProtoMember(2)]
        public string TagName
        {
            get => _tagName;
            set { if (_tagName != value) { _tagName = value ?? ""; OnPropertyChanged(); } }
        }

        private string _fieldName = "";

        /// <summary>JSON 字段名</summary>
        [ProtoMember(3)]
        public string FieldName
        {
            get => _fieldName;
            set { if (_fieldName != value) { _fieldName = value ?? ""; OnPropertyChanged(); } }
        }
    }

    /// <summary>单个 broker 连接（Z 循环多连接管理器 MqttSettings.Connections 元素——
    /// 本连接 Config + Topics + Bindings 三层归属，Binding 挂哪棵 Topic 树即属哪个连接）。</summary>
    [ProtoContract]
    public class MqttConnection : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _name = "";

        /// <summary>连接名（工程内唯一；UI 树节点名含 broker IP 摘要）</summary>
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value ?? ""; OnPropertyChanged(); } }
        }

        private MqttConfig _config = new();

        /// <summary>连接参数（复用 MqttConfig——broker/port/凭据 DPAPI/keepAlive/TLS/StatusTag 按连接）</summary>
        [ProtoMember(2)]
        public MqttConfig Config
        {
            get => _config;
            set { if (!ReferenceEquals(_config, value)) { _config = value ?? new MqttConfig(); OnPropertyChanged(); } }
        }

        private List<MqttTopic> _topics = new();

        /// <summary>本连接主题（发布/订阅；同名 Topic 跨连接互不干扰——不同 broker 命名空间）</summary>
        [ProtoMember(3)]
        public List<MqttTopic> Topics
        {
            get => _topics;
            set { if (!ReferenceEquals(_topics, value)) { _topics = value ?? new List<MqttTopic>(); OnPropertyChanged(); } }
        }

        private List<MqttBinding> _bindings = new();

        /// <summary>本连接变量↔字段绑定（tag 不加归属字段，经 Binding→Topic→Connection 路由）</summary>
        [ProtoMember(4)]
        public List<MqttBinding> Bindings
        {
            get => _bindings;
            set { if (!ReferenceEquals(_bindings, value)) { _bindings = value ?? new List<MqttBinding>(); OnPropertyChanged(); } }
        }
    }

    /// <summary>工程级 MQTT 连接管理器根（HMIProject 24；null = 未配置 MQTT）。</summary>
    [ProtoContract]
    public class MqttSettings : INotifyPropertyChanged
    {
        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>触发 PropertyChanged 事件</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool _enableMqtt;

        /// <summary>EnableMqtt 总开关（禁用：PC 灰显、FW 不建连接对象）。</summary>
        [ProtoMember(1)]
        public bool EnableMqtt
        {
            get => _enableMqtt;
            set { if (_enableMqtt != value) { _enableMqtt = value; OnPropertyChanged(); } }
        }

        private int _schemaVersion = 1;

        /// <summary>JSON 模板 schema 版本号（PC/设备共用——联调硬约束，不匹配告警）</summary>
        [ProtoMember(2)]
        public int SchemaVersion
        {
            get => _schemaVersion;
            set { if (_schemaVersion != value) { _schemaVersion = value; OnPropertyChanged(); } }
        }

        private MqttConfig _config = new();

        /// <summary>[deprecated Z 循环] 旧单份连接配置——连接参数真源改 MqttConnection.Config（仅迁移读取）。</summary>
        [ProtoMember(3)]
        public MqttConfig Config
        {
            get => _config;
            set { if (!ReferenceEquals(_config, value)) { _config = value ?? new MqttConfig(); OnPropertyChanged(); } }
        }

        private List<MqttTopic> _topics = new();

        /// <summary>[deprecated Z 循环] 旧单份主题组（仅迁移读取——归其 DeviceName 连接下）。</summary>
        [ProtoMember(4)]
        public List<MqttTopic> Topics
        {
            get => _topics;
            set { if (!ReferenceEquals(_topics, value)) { _topics = value ?? new List<MqttTopic>(); OnPropertyChanged(); } }
        }

        private List<MqttBinding> _bindings = new();

        /// <summary>[deprecated Z 循环] 旧单份绑定表（仅迁移读取）。</summary>
        [ProtoMember(5)]
        public List<MqttBinding> Bindings
        {
            get => _bindings;
            set { if (!ReferenceEquals(_bindings, value)) { _bindings = value ?? new List<MqttBinding>(); OnPropertyChanged(); } }
        }

        private string _deviceName = "";

        /// <summary>[deprecated Z 循环] 旧单份选定 MQTT 设备名（仅迁移读取——Z-4 迁移归 Connections[0]；
        /// 新结构连接参数真源 = MqttConnection.Config，DeviceConfig 不再承担 MQTT 连接参数）。</summary>
        [ProtoMember(6)]
        public string DeviceName
        {
            get => _deviceName;
            set { if (_deviceName != value) { _deviceName = value ?? ""; OnPropertyChanged(); } }
        }

        private List<MqttConnection> _connections = new();

        /// <summary>多连接管理器（Z 循环 2026-09-11：每项 Name + Config + Topics + Bindings——连接归属：Binding 挂哪棵 Topic 树即属该连接）</summary>
        [ProtoMember(7)]
        public List<MqttConnection> Connections
        {
            get => _connections;
            set { if (!ReferenceEquals(_connections, value)) { _connections = value ?? new List<MqttConnection>(); OnPropertyChanged(); } }
        }
    }
}
