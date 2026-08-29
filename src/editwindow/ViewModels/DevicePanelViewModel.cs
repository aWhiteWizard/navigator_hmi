using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 设备管理面板 VM（K 循环 K-4）——连接测试三分 / 闪烁 / VNC 开关 / 下载工程 / 门禁灰显。
    /// 服务单点：DeviceConnectionService（会话）+ HttpDownloadClient（传输/指令）+ DeviceProfileService（型号）。
    /// </summary>
    public class DevicePanelViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ICommandService _commandService;

        /// <summary>输出窗口日志请求（L-B4/L-B1：连接测试/扫描结果写入主窗口「输出」窗口——EditWindow 订阅后 AppendOutput）。</summary>
        public event Action<string>? OutputRequested;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void EmitOutput(string text) => OutputRequested?.Invoke(text);

        public DevicePanelViewModel(ICommandService commandService)
        {
            _commandService = commandService;
            ConnectCommand = new AsyncRelayCommand(ExecuteConnectAsync, () => CanConnect);
            BlinkCommand = new AsyncRelayCommand(ExecuteBlinkAsync, () => IsConnected);
            VncCommand = new AsyncRelayCommand(ExecuteVncAsync, () => IsConnected);
            DeployCommand = new AsyncRelayCommand(ExecuteDeployAsync, () => CanOperate);
            ScanCommand = new AsyncRelayCommand(ExecuteScanAsync, () => !string.IsNullOrWhiteSpace(SelectedNic));
            // L-B2 修复（2026-08-30）：确保服务初始化——Initialize 此前仅在新建项目对话框构造时调用，
            // 直接开设备面板（未先开新建项目）时 DeviceProfileService.Profiles 为空 → 类型下拉空白；
            // Initialize 幂等（lock + 重载），重复调用安全
            DeviceProfileService.Initialize(Path.Combine(AppContext.BaseDirectory, "device-profiles"));
            // L-B2 诊断（2026-08-30 用户反馈类型仍空）：Trace 构造后 Profiles 数量，便于定位（服务层测试已证 Initialize 后非空）
            System.Diagnostics.Trace.WriteLine($"[DevicePanelVM] Initialize 后 Profiles={DeviceProfileService.Profiles.Count}, dir={Path.Combine(AppContext.BaseDirectory, "device-profiles")}");
            DeviceProfileService.ProfilesChanged += OnProfilesChanged;
            DeviceConnectionService.SessionChanged += OnSessionChanged;
            RefreshProfiles();
            RefreshNicList();
            OnSessionChanged(DeviceConnectionService.Session);
        }

        public void Dispose()
        {
            DeviceProfileService.ProfilesChanged -= OnProfilesChanged;
            DeviceConnectionService.SessionChanged -= OnSessionChanged;
        }

        // ── 输入 ──
        private string _ip = "";
        public string Ip { get => _ip; set { if (_ip != value) { _ip = value; OnPropertyChanged(); ConnectCommand.NotifyCanExecuteChanged(); } } }

        /// <summary>设备型号（device-profile 驱动）。</summary>
        public List<DeviceProfile> Profiles { get; private set; } = new();

        private DeviceProfile? _selectedProfile;
        public DeviceProfile? SelectedProfile
        {
            get => _selectedProfile;
            set { if (!ReferenceEquals(_selectedProfile, value)) { _selectedProfile = value; OnPropertyChanged(); ConnectCommand.NotifyCanExecuteChanged(); } }
        }

        // ── 状态 ──
        private string _statusText = "未连接";
        public string StatusText { get => _statusText; private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } } }

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; private set { if (_isConnected != value) { _isConnected = value; OnPropertyChanged(); RefreshGate(); } } }

        private bool _blinkOn;
        public bool BlinkOn { get => _blinkOn; private set { if (_blinkOn != value) { _blinkOn = value; OnPropertyChanged(); } } }

        private bool _vncOn;
        public bool VncOn
        {
            get => _vncOn;
            private set { if (_vncOn != value) { _vncOn = value; OnPropertyChanged(); OnPropertyChanged(nameof(VncButtonText)); } }
        }

        /// <summary>VNC 应用按钮文案（用户定：VNC 单独应用按钮——未开显示「VNC 启用」，已开显示「VNC 停止」）。</summary>
        public string VncButtonText => VncOn ? "VNC 停止" : "VNC 启用";

        /// <summary>网卡列表（搜索设备用；用户定：搜索设备需先选网卡）。</summary>
        public List<string> NicList { get; private set; } = new();

        private string _selectedNic = "";
        /// <summary>选中的网卡（扫描设备用）。</summary>
        public string SelectedNic
        {
            get => _selectedNic;
            set { if (_selectedNic != value) { _selectedNic = value; OnPropertyChanged(); ScanCommand.NotifyCanExecuteChanged(); } }
        }

        private string _progressText = "";
        public string ProgressText { get => _progressText; private set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } } }

        /// <summary>发现的设备列表（搜索设备结果；设计文档 property-device §3.2：IP/型号/ID，点击行自动填入）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<ScannedDevice> FoundDevices { get; } = new();

        private ScannedDevice? _selectedDevice;
        /// <summary>选中的发现设备（点击表格行 → 自动填入 IP+型号并连接）。</summary>
        public ScannedDevice? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (!ReferenceEquals(_selectedDevice, value))
                {
                    _selectedDevice = value;
                    OnPropertyChanged();
                    if (value != null) ApplySelectedDevice(value);
                }
            }
        }

        /// <summary>点击发现设备行：自动填入 IP + 型号（设计文档：点击列表行 → 自动填入型号、ID、IP）。</summary>
        private void ApplySelectedDevice(ScannedDevice dev)
        {
            Ip = dev.Ip;
            var match = Profiles.FirstOrDefault(p => p.Model.Equals(dev.Model, StringComparison.OrdinalIgnoreCase));
            if (match != null) SelectedProfile = match;
            EmitOutput($"[搜索设备] 已选择 {dev.Ip}（{dev.Model}，ID={dev.Id}）——可点「连接测试」建立会话");
        }

        // ── 门禁（连接 + 能力两层）──
        private bool _canOperate;
        public bool CanOperate { get => _canOperate; private set { if (_canOperate != value) { _canOperate = value; OnPropertyChanged(); } } }

        public bool CanConnect => !string.IsNullOrWhiteSpace(Ip) && SelectedProfile != null;

        /// <summary>搜索可用：已选网卡。</summary>
        public bool CanScan => !string.IsNullOrWhiteSpace(SelectedNic);

        private void RefreshGate()
        {
            CanOperate = IsConnected && SelectedProfile?.Capability.Deploy == true;
            DeployCommand.NotifyCanExecuteChanged();
        }

        // ── 命令 ──
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand BlinkCommand { get; }
        public IAsyncRelayCommand VncCommand { get; }
        public IAsyncRelayCommand DeployCommand { get; }
        public IAsyncRelayCommand ScanCommand { get; }

        /// <summary>连接测试（三分：已连接/无法连接/型号·尺寸不匹配——固件版本不匹配 K-3 版本比对后补）。</summary>
        private async Task ExecuteConnectAsync()
        {
            if (!CanConnect) return;
            IsConnected = false;
            StatusText = "测试中…";
            ProgressText = "";
            EmitOutput($"[连接测试] 测试 {Ip.Trim()} ({SelectedProfile?.Model})…");
            var profile = SelectedProfile!;
            DeviceTestResult result;
            try
            {
                result = await Task.Run(() =>
                    DeviceConnectionService.TestConnection(Ip.Trim(), profile.Model, profile.SizeInch));
            }
            catch (Exception ex)
            {
                // L-B3 修复（2026-08-30）：Task.Run 内异常静默吞掉致连接无反馈——捕获并显示
                StatusText = $"连接测试异常: {ex.Message}";
                EmitOutput($"[连接测试] ✗ 异常: {ex.Message}");
                return;
            }
            if (result.Success)
            {
                var s = result.Session!;
                StatusText = $"已连接 {s.Ip} · {s.Model} · 固件 {s.FirmwareVersion ?? "?"}";
                EmitOutput($"[连接测试] ✓ 已连接 {s.Ip} · {s.Model} · 固件 {s.FirmwareVersion ?? "?"}");
                IsConnected = true;
            }
            else
            {
                StatusText = result.Message;
                EmitOutput($"[连接测试] ✗ {result.Message}");
            }
        }

        /// <summary>搜索设备（用户 2026-08-30 定：选网卡后可用；走命令层 scan_devices——HTTP 网段扫描，结果填设备列表表格 + 日志进输出窗口）。</summary>
        private async Task ExecuteScanAsync()
        {
            if (!CanScan) return;
            StatusText = "搜索中…";
            EmitOutput($"[搜索设备] 网卡 {SelectedNic} 扫描中…（/24 子网 HTTP 探测）");
            var result = await Task.Run(() => _commandService.Execute("scan_devices",
                new Dictionary<string, object?> { ["nic"] = SelectedNic }));
            FoundDevices.Clear();
            SelectedDevice = null;
            if (result.Success)
            {
                // 命令层返回 { nic, devices:[{ip,model,id,sizeInch,version}], count, message }
                var devices = ExtractDevices(result.Data);
                foreach (var d in devices) FoundDevices.Add(d);
                StatusText = $"搜索完成，发现 {devices.Count} 台设备";
                EmitOutput($"[搜索设备] ✓ 发现 {devices.Count} 台设备（点击表格行自动填入并连接）");
            }
            else
            {
                StatusText = $"搜索失败: {result.ErrorMessage}";
                EmitOutput($"[搜索设备] ✗ [{result.ErrorCode}] {result.ErrorMessage}");
            }
        }

        /// <summary>从命令层 scan_devices 结果提取设备列表（Data 为匿名对象 {nic,devices:[...],count,message} 或 JSON 字符串）。</summary>
        private static List<ScannedDevice> ExtractDevices(object? data)
        {
            var result = new List<ScannedDevice>();
            if (data == null) return result;
            try
            {
                // 形态 1：匿名对象（CommandService 不序列化 Data——反射读 devices 属性）
                var devicesProp = data.GetType().GetProperty("devices");
                if (devicesProp != null)
                {
                    var raw = devicesProp.GetValue(data) as System.Collections.IEnumerable;
                    if (raw != null)
                    {
                        foreach (var item in raw)
                        {
                            if (item == null) continue;
                            var dev = new ScannedDevice
                            {
                                Ip = GetProp(item, "ip"),
                                Model = GetProp(item, "model"),
                                Id = GetProp(item, "id"),
                                SizeInch = GetProp(item, "sizeInch"),
                                Version = GetProp(item, "version")
                            };
                            if (!string.IsNullOrWhiteSpace(dev.Ip)) result.Add(dev);
                        }
                        return result;
                    }
                }
                // 形态 2：JSON 字符串
                var json = data.ToString() ?? "";
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("devices", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                        var dev = new ScannedDevice
                        {
                            Ip = item.TryGetProperty("ip", out var ip) ? ip.GetString() ?? "" : "",
                            Model = item.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "",
                            Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            SizeInch = item.TryGetProperty("sizeInch", out var s) ? s.GetString() ?? "" : "",
                            Version = item.TryGetProperty("version", out var v) ? v.GetString() ?? "" : ""
                        };
                        if (!string.IsNullOrWhiteSpace(dev.Ip)) result.Add(dev);
                    }
                }
            }
            catch { /* 解析失败返回空 */ }
            return result;
        }

        private static string GetProp(object obj, string name)
        {
            try { return obj.GetType().GetProperty(name)?.GetValue(obj)?.ToString() ?? ""; }
            catch { return ""; }
        }

        /// <summary>设备闪烁（on/off 状态机；走命令层——K-5 CLI 对等单一入口）。</summary>
        private async Task ExecuteBlinkAsync()
        {
            if (DeviceConnectionService.Session == null) return;
            var target = !BlinkOn;
            ProgressText = target ? "闪烁指令发送中…" : "停止闪烁指令发送中…";
            var result = await Task.Run(() => _commandService.Execute("blink_device",
                new Dictionary<string, object?> { ["ip"] = DeviceConnectionService.Session.Ip, ["enable"] = target ? "on" : "off" }));
            BlinkOn = result.Success && target;
            ProgressText = result.Success ? "" : $"闪烁指令失败: {result.ErrorMessage}";
        }

        /// <summary>VNC 运行时启停（走命令层；应用按钮点击切换——2026-08-30 用户定 VNC 单独应用按钮，取反 VncOn 为用户意图）。</summary>
        private async Task ExecuteVncAsync()
        {
            if (DeviceConnectionService.Session == null) return;
            var target = !VncOn;   // 应用按钮：点击切换（原 CheckBox 语义是勾选即发值已翻转——按钮语义改为取反）
            ProgressText = target ? "VNC 启用指令发送中…" : "VNC 停用指令发送中…";
            var result = await Task.Run(() => _commandService.Execute("vnc",
                new Dictionary<string, object?> { ["ip"] = DeviceConnectionService.Session.Ip, ["enable"] = target ? "on" : "off" }));
            VncOn = result.Success && target;
            ProgressText = result.Success ? "" : $"VNC 指令失败: {result.ErrorMessage}";
        }

        /// <summary>下载工程（deploy 前置自动编译门禁 + CheckVersion 版本比对 + HTTP 传输——命令层统一链路）。</summary>
        private async Task ExecuteDeployAsync()
        {
            if (!CanOperate || DeviceConnectionService.Session == null) return;
            ProgressText = "编译+打包+传输中…（deploy 前置编译门禁）";
            var result = await Task.Run(() => _commandService.Execute("deploy_project",
                new Dictionary<string, object?> { ["device_ip"] = DeviceConnectionService.Session.Ip }));
            ProgressText = result.Success ? "部署完成" : $"部署失败: {result.ErrorMessage}";
        }

        private void OnProfilesChanged()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(new Action(RefreshProfiles));
            else
                RefreshProfiles();
        }

        private void RefreshProfiles()
        {
            Profiles = DeviceProfileService.Profiles.ToList();
            if (SelectedProfile == null || !Profiles.Any(p => p.Model == SelectedProfile.Model))
                SelectedProfile = Profiles.FirstOrDefault();
            OnPropertyChanged(nameof(Profiles));
            OnPropertyChanged(nameof(SelectedProfile));
            ConnectCommand.NotifyCanExecuteChanged();
            RefreshGate();   // profile 热更新致 Capability.Deploy 变化 → 下载门禁联动
        }

        /// <summary>枚举本机网卡（搜索设备用；用户 2026-08-30 定：搜索设备需先选网卡）。</summary>
        private void RefreshNicList()
        {
            var nics = new List<string>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        nics.Add(nic.Name);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DevicePanelVM] 网卡枚举失败: {ex.Message}");
            }
            NicList = nics;
            if (SelectedNic == "" || !nics.Contains(SelectedNic))
                SelectedNic = nics.FirstOrDefault() ?? "";
            OnPropertyChanged(nameof(NicList));
            OnPropertyChanged(nameof(SelectedNic));
        }

        /// <summary>会话变化（可能后台线程——封送后更新门禁与状态栏）。</summary>
        private void OnSessionChanged(ConnectionSession? session)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(new Action(() => OnSessionChangedUi(session)));
            else
                OnSessionChangedUi(session);
        }

        private void OnSessionChangedUi(ConnectionSession? session)
        {
            IsConnected = session != null;
            if (session == null)
            {
                StatusText = "未连接";
                BlinkOn = false;   // 断开后状态复位（防重连显示旧状态）
                VncOn = false;
            }
            ConnectCommand.NotifyCanExecuteChanged();
            BlinkCommand.NotifyCanExecuteChanged();
            VncCommand.NotifyCanExecuteChanged();
            RefreshGate();
        }
    }
}
