using System.ComponentModel;
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public DevicePanelViewModel(ICommandService commandService)
        {
            _commandService = commandService;
            ConnectCommand = new AsyncRelayCommand(ExecuteConnectAsync, () => CanConnect);
            BlinkCommand = new AsyncRelayCommand(ExecuteBlinkAsync, () => IsConnected);
            VncCommand = new AsyncRelayCommand(ExecuteVncAsync, () => IsConnected);
            DeployCommand = new AsyncRelayCommand(ExecuteDeployAsync, () => CanOperate);
            DeviceProfileService.ProfilesChanged += OnProfilesChanged;
            DeviceConnectionService.SessionChanged += OnSessionChanged;
            RefreshProfiles();
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
        public bool VncOn { get => _vncOn; private set { if (_vncOn != value) { _vncOn = value; OnPropertyChanged(); } } }

        private string _progressText = "";
        public string ProgressText { get => _progressText; private set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } } }

        // ── 门禁（连接 + 能力两层）──
        private bool _canOperate;
        public bool CanOperate { get => _canOperate; private set { if (_canOperate != value) { _canOperate = value; OnPropertyChanged(); } } }

        public bool CanConnect => !string.IsNullOrWhiteSpace(Ip) && SelectedProfile != null;

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

        /// <summary>连接测试（三分：已连接/无法连接/型号·尺寸不匹配——固件版本不匹配 K-3 版本比对后补）。</summary>
        private async Task ExecuteConnectAsync()
        {
            if (!CanConnect) return;
            IsConnected = false;
            StatusText = "测试中…";
            ProgressText = "";
            var profile = SelectedProfile!;
            var result = await Task.Run(() =>
                DeviceConnectionService.TestConnection(Ip.Trim(), profile.Model, profile.SizeInch));
            if (result.Success)
            {
                var s = result.Session!;
                StatusText = $"已连接 {s.Ip} · {s.Model} · 固件 {s.FirmwareVersion ?? "?"}";
                IsConnected = true;
            }
            else
            {
                StatusText = result.Message;
            }
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

        /// <summary>VNC 运行时启停（走命令层；勾选即发——点击时序 OnToggle 先于 Command，此处 VncOn 已是用户意图）。</summary>
        private async Task ExecuteVncAsync()
        {
            if (DeviceConnectionService.Session == null) return;
            var target = VncOn;   // 点击时绑定值已翻转 = 用户意图（勿取反——取反即方向颠倒）
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
