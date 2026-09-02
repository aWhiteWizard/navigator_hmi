using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.Core.Services;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 设备管理面板 VM（O 轮批 C C-1 四子页重构，N+31 v2）——设备连接/远程控制/设备升级/工程下载四子页共享：
    /// 连接测试三分 / 闪烁 / VNC 开关 / 固件升级（固件文件夹方案）/ 下载工程 / 门禁灰显。
    /// 服务单点：DeviceConnectionService（会话）+ HttpDownloadClient（传输/指令）+ DeviceProfileService（型号）+ FirmwareFolderService（固件库）。
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
            DisconnectCommand = new AsyncRelayCommand(ExecuteDisconnectAsync, () => IsConnected);
            BlinkCommand = new AsyncRelayCommand(ExecuteBlinkAsync, () => IsConnected);
            VncCommand = new AsyncRelayCommand(ExecuteVncAsync, () => IsConnected);
            DeployCommand = new AsyncRelayCommand(ExecuteDeployAsync, () => CanOperate);
            ScanCommand = new AsyncRelayCommand(ExecuteScanAsync, () => !string.IsNullOrWhiteSpace(SelectedNic));
            OpenViewerCommand = new AsyncRelayCommand(ExecuteOpenViewerAsync, () => VncOn);
            // O-C C-4 设备升级页：固件库刷新（**本地目录枚举，无需连接**——Check 修复：编辑固件库目录后未连接也可扫）
            RefreshFirmwareCommand = new AsyncRelayCommand(ExecuteRefreshFirmwareAsync);
            UpgradeFirmwareCommand = new AsyncRelayCommand(ExecuteUpgradeFirmwareAsync, () => CanUpgrade);
            // Check 修复（2026-09）：固件库目录持久化——**仅当用户显式保存过目录（store 非空）才恢复并覆盖根**；
            // 无持久化时不动全局根（保持默认运行目录 firmware/ 或测试注入的隔离根——🟡3 防 VM 构造覆盖测试全局根）
            var persistedDir = LoadPersistedFirmwareFolder();
            if (!string.IsNullOrEmpty(persistedDir))
            {
                _firmwareFolderText = persistedDir;
                FirmwareFolderService.Initialize(persistedDir);
            }
            else
            {
                _firmwareFolderText = FirmwareFolderService.DefaultRoot;
            }
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
        public bool IsConnected { get => _isConnected; private set { if (_isConnected != value) { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeviceVersionText)); RefreshGate(); } } }

        private bool _blinkOn;
        public bool BlinkOn { get => _blinkOn; private set { if (_blinkOn != value) { _blinkOn = value; OnPropertyChanged(); } } }

        private bool _vncOn;
        public bool VncOn
        {
            get => _vncOn;
            private set { if (_vncOn != value) { _vncOn = value; OnPropertyChanged(); OnPropertyChanged(nameof(VncButtonText)); OpenViewerCommand.NotifyCanExecuteChanged(); } }
        }

        /// <summary>VNC 应用按钮文案（用户定：VNC 单独应用按钮——未开显示「VNC 启用」，已开显示「VNC 停止」）。</summary>
        public string VncButtonText => VncOn ? "VNC 停止" : "VNC 启用";

        /// <summary>设备当前固件版本（O-C C-4 升级页显示；会话建立后回显 /api/device/info version）。</summary>
        public string DeviceVersionText
        {
            get
            {
                var s = DeviceConnectionService.Session;
                return s != null ? $"{s.FirmwareVersion ?? "未知"}" : "未连接";
            }
        }

        // ── O-C C-4 固件文件夹（升级页）──
        // Check 修复（2026-09 用户反馈）：固件库目录**可编辑**——原只读只能刷新；编辑目录 → 切换固件库根
        // （FirmwareFolderService.Initialize）+ 持久化 %APPDATA%\NavigatorHMI\firmware-dir.txt（下次启动记住）。
        private string _firmwareFolderText;
        /// <summary>固件库根目录（可编辑：输入新目录切换固件库位置；空/默认 = 运行目录 firmware/）。</summary>
        public string FirmwareFolderText
        {
            get => _firmwareFolderText;
            set
            {
                var v = (value ?? "").Trim();
                if (_firmwareFolderText == v) return;
                // 空 → 还原默认并回填显示（🟡4：文本框不空白——显示实际生效的默认根，避免「空白但实际有值」困惑）
                if (string.IsNullOrEmpty(v))
                {
                    FirmwareFolderService.Initialize(null);   // 先还原默认根（再读——顺序：还原后才取得到默认值）
                    _firmwareFolderText = FirmwareFolderService.DefaultRoot;
                    PersistFirmwareFolder("");
                    OnPropertyChanged();
                    _ = RefreshFirmwareForSessionAsync();
                    return;
                }
                _firmwareFolderText = v;
                // 输入目录（不存在也不拒绝——刷新时提示，允许先输入后建目录）
                FirmwareFolderService.Initialize(v);
                PersistFirmwareFolder(v);
                OnPropertyChanged();
                _ = RefreshFirmwareForSessionAsync();   // 换目录立即重刷固件列表
            }
        }

        /// <summary>固件库根目录持久化文件（%APPDATA%\NavigatorHMI\firmware-dir.txt——用户级，跨会话记住；
        /// internal 可注入：测试/便携版可改路径）。</summary>
        internal static string FirmwareDirStorePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavigatorHMI", "firmware-dir.txt");

        private static void PersistFirmwareFolder(string dir)
        {
            try
            {
                var appData = Path.GetDirectoryName(FirmwareDirStorePath)!;
                Directory.CreateDirectory(appData);
                File.WriteAllText(FirmwareDirStorePath, dir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DevicePanelVM] 固件库目录持久化失败: {ex.Message}");
            }
        }

        internal static string? LoadPersistedFirmwareFolder()
        {
            try
            {
                if (!File.Exists(FirmwareDirStorePath)) return null;
                var v = File.ReadAllText(FirmwareDirStorePath).Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>固件条目（路径 + 显示名）。</summary>
        public class FirmwareEntry
        {
            public string Path { get; set; } = "";
            public string DisplayName => System.IO.Path.GetFileName(Path);
        }

        private ObservableCollection<FirmwareEntry> _firmwareFiles = new();
        /// <summary>当前尺寸固件库全部 .fw（语义版本降序）。</summary>
        public ObservableCollection<FirmwareEntry> FirmwareFiles
        {
            get => _firmwareFiles;
            private set { if (!ReferenceEquals(_firmwareFiles, value)) { _firmwareFiles = value; OnPropertyChanged(); } }
        }

        private FirmwareEntry? _selectedFirmware;
        /// <summary>选中的固件（默认自动选语义最新；下拉可手动改选历史版本）。</summary>
        public FirmwareEntry? SelectedFirmware
        {
            get => _selectedFirmware;
            set
            {
                if (!ReferenceEquals(_selectedFirmware, value))
                {
                    _selectedFirmware = value;
                    OnPropertyChanged();
                    UpgradeFirmwareCommand.NotifyCanExecuteChanged();
                }
            }
        }

        private bool _firmwareProgressVisible;
        /// <summary>固件升级进度条可见（传输中显示）。</summary>
        public bool FirmwareProgressVisible { get => _firmwareProgressVisible; private set { if (_firmwareProgressVisible != value) { _firmwareProgressVisible = value; OnPropertyChanged(); } } }

        private double _firmwareProgressValue;
        /// <summary>固件升级进度（0~100）。</summary>
        public double FirmwareProgressValue { get => _firmwareProgressValue; private set { if (Math.Abs(_firmwareProgressValue - value) > 0.01) { _firmwareProgressValue = value; OnPropertyChanged(); } } }

        private string _firmwareProgressText = "";
        /// <summary>固件升级进度文本（阶段描述）。</summary>
        public string FirmwareProgressText { get => _firmwareProgressText; private set { if (_firmwareProgressText != value) { _firmwareProgressText = value; OnPropertyChanged(); } } }

        private string _upgradeLog = "";
        /// <summary>升级记录（版本前置结果/传输状态逐行回显）。</summary>
        public string UpgradeLog { get => _upgradeLog; private set { if (_upgradeLog != value) { _upgradeLog = value; OnPropertyChanged(); } } }

        private void AppendUpgradeLog(string line)
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            UpgradeLog = string.IsNullOrWhiteSpace(UpgradeLog) ? $"[{stamp}] {line}" : $"{UpgradeLog}\n[{stamp}] {line}";
        }

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

        private double _progressValue;
        /// <summary>D-B4：下载进度（0~100，设备端真实进度轮询）。</summary>
        public double ProgressValue { get => _progressValue; private set { if (Math.Abs(_progressValue - value) > 0.01) { _progressValue = value; OnPropertyChanged(); } } }

        private bool _progressVisible;
        /// <summary>D-B4：进度条可见（传输中显示，完成/失败隐藏）。</summary>
        public bool ProgressVisible { get => _progressVisible; private set { if (_progressVisible != value) { _progressVisible = value; OnPropertyChanged(); } } }

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

        /// <summary>升级可用：已连接 + 已选固件（O-C C-4 门禁——连接成功前灰显，连接后随固件选择变化）。</summary>
        public bool CanUpgrade => IsConnected && SelectedFirmware != null;

        private void RefreshGate()
        {
            CanOperate = IsConnected && SelectedProfile?.Capability.Deploy == true;
            DeployCommand.NotifyCanExecuteChanged();
            UpgradeFirmwareCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            RefreshFirmwareCommand.NotifyCanExecuteChanged();   // 审查 🟡：门禁命令统一显式通知（防启用态陈旧）
        }

        // ── 命令 ──
        public IAsyncRelayCommand ConnectCommand { get; }
        /// <summary>断开设备连接（O-C C-2 设备连接页；门禁：已连接）。</summary>
        public IAsyncRelayCommand DisconnectCommand { get; }
        public IAsyncRelayCommand BlinkCommand { get; }
        public IAsyncRelayCommand VncCommand { get; }
        public IAsyncRelayCommand DeployCommand { get; }
        public IAsyncRelayCommand ScanCommand { get; }
        /// <summary>打开 VNC 查看器（M-2：仅 VNC 已启用时可执行——CanExecute 绑 VncOn）。</summary>
        public IAsyncRelayCommand OpenViewerCommand { get; }
        /// <summary>刷新固件库（O-C C-4：本地目录枚举——无需连接；尺寸取会话/连接页选中 profile）。</summary>
        public IAsyncRelayCommand RefreshFirmwareCommand { get; }
        /// <summary>升级到所选固件（O-C C-4：deploy_firmware——版本前置三分 + 上传进度；门禁：已连接 + 已选固件）。</summary>
        public IAsyncRelayCommand UpgradeFirmwareCommand { get; }

        /// <summary>断开设备连接（幂等；走命令层 disconnect——DeviceConnectionService 会话清除，状态经 SessionChanged 复位）。</summary>
        private async Task ExecuteDisconnectAsync()
        {
            if (!IsConnected) return;
            EmitOutput("[断开] 正在断开设备连接…");
            var result = await Task.Run(() => _commandService.Execute("disconnect", new Dictionary<string, object?>()));
            if (result.Success)
                EmitOutput("[断开] ✓ 已断开");
            else
                EmitOutput($"[断开] ✗ [{result.ErrorCode}] {result.ErrorMessage}");
        }

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
                // O-C C-4：连接成功后按会话型号尺寸刷新固件库（升级页自动匹配该尺寸最新 .fw）
                await RefreshFirmwareForSessionAsync();
            }
            else
            {
                StatusText = result.Message;
                EmitOutput($"[连接测试] ✗ {result.Message}");
            }
        }

        /// <summary>刷新固件库按钮（O-C C-4：本地目录枚举——无需连接；按尺寸扫固件库并自动选最新）。</summary>
        private async Task ExecuteRefreshFirmwareAsync()
        {
            await RefreshFirmwareForSessionAsync();
        }

        /// <summary>手动指定固件（「浏览…」选择任意位置 .fw——覆盖固件文件夹自动匹配，命令层 --file 语义）。
        /// 选中文件加入列表顶部并选中（若已在列表内则直接选中）。</summary>
        public void SetManualFirmware(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var existing = FirmwareFiles.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                SelectedFirmware = existing;
                return;
            }
            var entry = new FirmwareEntry { Path = path };
            FirmwareFiles.Insert(0, entry);   // 手动项置顶（用户显式选择优先）
            SelectedFirmware = entry;
            AppendUpgradeLog($"手动选择固件: {path}");
        }

        /// <summary>按型号尺寸刷新固件库：扫 固件库根 下该尺寸固件（文件名 NavigatorHMI_&lt;尺寸&gt;inch_vX.Y.Z.fw）→ 语义最新自动选中。
        /// Check 修复（2026-09）：**不依赖连接**（本地目录枚举）——尺寸取会话型号 profile，未连接时取连接页选中 profile
        /// （默认 7 寸——构造 SelectedProfile 预置），无 profile → 清列表提示。编辑固件库目录后即可立即扫描反馈。</summary>
        private async Task RefreshFirmwareForSessionAsync()
        {
            // 尺寸：会话型号 → 连接页选中 profile（未连接也可用）→ null
            string? size = null;
            var session = DeviceConnectionService.Session;
            if (session != null)
                size = DeviceProfileService.GetByModel(session.Model)?.SizeInch ?? session.SizeInch;
            size ??= SelectedProfile?.SizeInch;

            string[] list;
            if (string.IsNullOrEmpty(size))
            {
                list = Array.Empty<string>();
            }
            else
            {
                try
                {
                    // 🟡2 健壮性：枚举包 try/catch——目录无权限/被删（竞态）时降级空列表 + 日志，不抛进 setter/Click
                    list = FirmwareFolderService.ListForSize(size).ToArray();
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
                {
                    list = Array.Empty<string>();
                    System.Diagnostics.Trace.WriteLine($"[DevicePanelVM] 固件库枚举失败（{size}）: {ex.Message}");
                }
            }
            await Task.CompletedTask;   // 同步枚举；保持 async 签名（后续可扩展目录监视）
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            void Apply()
            {
                FirmwareFiles = new ObservableCollection<FirmwareEntry>(
                    list.Select(p => new FirmwareEntry { Path = p }));
                SelectedFirmware = FirmwareFiles.FirstOrDefault();
                if (string.IsNullOrEmpty(size))
                {
                    AppendUpgradeLog("固件库扫描：请先在「设备连接」页选择设备型号，或连接设备后按会话尺寸扫描");
                }
                else
                {
                    // 2026-09 标准修订：平铺命名 NavigatorHMI_<尺寸>inch_v<版本>.fw（无子目录）——提示放根目录 + 命名
                    var inch = FirmwareFolderService.SizeToInchTag(size);
                    AppendUpgradeLog(list.Length == 0
                        ? $"固件库 {FirmwareFolderService.DefaultRoot} 无 {inch} 固件——请放入 NavigatorHMI_{inch}_vX.Y.Z.fw，或点「浏览…」手动选择"
                        : $"固件库 {FirmwareFolderService.DefaultRoot}：发现 {list.Length} 个 {inch} 固件（自动选最新 {SelectedFirmware?.DisplayName}）");
                }
                OnPropertyChanged(nameof(FirmwareFolderText));
            }
            if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(new Action(Apply));
            else Apply();
        }

        /// <summary>升级到所选固件（O-C C-4：命令层 deploy_firmware——版本前置三分 VERSION_SAME/OLDER + 上传进度 + 结果日志；
        /// 设备端校验通过自动重启。进度回调后台线程 → Dispatcher 封送）。</summary>
        private async Task ExecuteUpgradeFirmwareAsync()
        {
            var session = DeviceConnectionService.Session;
            var fw = SelectedFirmware;
            if (session == null || fw == null) return;
            if (!File.Exists(fw.Path))
            {
                AppendUpgradeLog($"固件文件不存在: {fw.Path}");
                return;
            }
            AppendUpgradeLog($"开始升级: {fw.DisplayName} → {session.Ip}（版本前置检查中…）");
            FirmwareProgressVisible = true;
            FirmwareProgressValue = 0;
            FirmwareProgressText = "版本检查 + 上传中…";
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            Func<int, string, bool> progress = (pct, stage) =>
            {
                Action upd = () =>
                {
                    FirmwareProgressValue = pct;
                    FirmwareProgressText = $"上传中… {pct}%（{stage}）";
                };
                if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(upd);
                else upd();
                return true;
            };
            var result = await Task.Run(() => _commandService.Execute("deploy_firmware",
                new Dictionary<string, object?>
                {
                    ["device_ip"] = session.Ip,
                    ["file_path"] = fw.Path,
                    ["progress"] = progress,
                }));
            FirmwareProgressVisible = false;
            if (result.Success)
            {
                FirmwareProgressValue = 100;
                AppendUpgradeLog($"✓ 升级指令完成——设备校验安装后将自动重启（版本前置已通过）");
                EmitOutput($"[设备升级] ✓ {result.ErrorMessage ?? result.ErrorCode}");
            }
            else
            {
                FirmwareProgressText = "";
                // VERSION_SAME/VERSION_OLDER 等前置拒绝为预期分支——日志明确原因（不弹窗打断）
                AppendUpgradeLog(result.ErrorCode is "VERSION_SAME" or "VERSION_OLDER"
                    ? $"[{result.ErrorCode}] {result.ErrorMessage}"
                    : $"✗ [{result.ErrorCode}] {result.ErrorMessage}");
                EmitOutput($"[设备升级] ✗ [{result.ErrorCode}] {result.ErrorMessage}");
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

        /// <summary>从命令层 scan_devices 结果提取设备列表（Data 为匿名对象 {nic,devices:[...],count,message}——devices 是 List&lt;ScannedDevice&gt;）。</summary>
        private static List<ScannedDevice> ExtractDevices(object? data)
        {
            var result = new List<ScannedDevice>();
            if (data == null) return result;
            try
            {
                // 形态 1：匿名对象（CommandService 不序列化 Data——反射读 devices 属性，元素即 ScannedDevice）
                var devicesProp = data.GetType().GetProperty("devices");
                if (devicesProp != null)
                {
                    var raw = devicesProp.GetValue(data) as System.Collections.IEnumerable;
                    if (raw != null)
                    {
                        foreach (var item in raw)
                        {
                            if (item is ScannedDevice sd)   // 元素已是 ScannedDevice——直接取（避免反射大小写坑）
                            {
                                if (!string.IsNullOrWhiteSpace(sd.Ip)) result.Add(sd);
                            }
                            else if (item != null)
                            {
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
            try
            {
                // 大小写不敏感（匿名对象属性可能 PascalCase——ScannedDevice.Ip 等）
                var prop = obj.GetType().GetProperty(name, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                return prop?.GetValue(obj)?.ToString() ?? "";
            }
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

        /// <summary>
        /// 打开 VNC 查看器（M-2）：启动 VncViewer.exe 连接当前会话设备 {ip}:{port}。
        /// 候选路径：应用同目录 VncViewer.exe 优先，开发机 Release 输出路径兜底（外部程序不随包分发）。
        /// 端口（2026-08-30 用户代码评论）：不再写死 5900——从设备能力文件读（DeviceCapability.VncPort，
        /// device-profile 可配，默认 5900 与 FW fw-config.json vnc.port 一致）。
        /// 场景限定（审查 B2）：设备改端口需两端同步（PC 能力文件 vncPort + FW fw-config.json vnc.port；
        /// env 覆盖时含 NAVIHMI_VNC_PORT）；单端修改会导致查看器连不上。
        /// </summary>
        private async Task ExecuteOpenViewerAsync()
        {
            var session = DeviceConnectionService.Session;
            if (session == null) return;
            if (!VncOn)
            {
                EmitOutput("[VNC 查看器] 请先启用 VNC 再打开查看器");
                return;
            }
            var exe = ResolveVncViewerPath();
            if (exe == null)
            {
                EmitOutput("[VNC 查看器] 未找到 VncViewer.exe（同目录/开发机 Release 输出路径均无）——请将 VncViewer.exe 放到组态软件同目录");
                return;
            }
            // 端口以会话设备型号为准（审查建议 3：防连接后切换下拉选型导致端口错用；默认一致无实际影响，防御性）
            var port = DeviceProfileService.Profiles
                .FirstOrDefault(p => p.Model.Equals(session.Model, StringComparison.OrdinalIgnoreCase))
                ?.Capability.VncPort ?? DeviceCapability.DefaultVncPort;
            var args = $"{session.Ip}:{port}";
            EmitOutput($"[VNC 查看器] 启动 {exe} {args}");
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true
                });
                ProgressText = p != null ? "VNC 查看器已启动" : "VNC 查看器启动失败";
            }
            catch (Exception ex)
            {
                EmitOutput($"[VNC 查看器] 启动失败: {ex.Message}");
                ProgressText = $"VNC 查看器启动失败: {ex.Message}";
            }
            await Task.CompletedTask;
        }

        /// <summary>探测 VncViewer.exe 路径（应用同目录优先，开发机 Release 输出路径兜底）。</summary>
        private static string? ResolveVncViewerPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "VncViewer.exe"),
                // 开发机已知 Release 输出路径（外部程序不随包分发，兜底便于本地调试）
                @"D:\workspace\code\VncViewer\bin\Release\net9.0-windows\VncViewer.exe"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        /// <summary>下载工程（deploy 前置自动编译门禁 + CheckVersion 版本比对 + HTTP 传输——命令层统一链路）。
        /// D-B4：进度条显示设备端真实进度（轮询 /api/progress；回调在后台线程，Dispatcher 封送 UI 更新）。</summary>
        private async Task ExecuteDeployAsync()
        {
            if (!CanOperate || DeviceConnectionService.Session == null) return;
            ProgressText = "编译+打包+传输中…（deploy 前置编译门禁）";
            ProgressVisible = true;
            ProgressValue = 0;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            // 进度回调：0~100 + 阶段描述（后台线程调用——封送 UI 线程更新）
            Func<int, string, bool> progress = (pct, stage) =>
            {
                if (dispatcher != null && !dispatcher.CheckAccess())
                    dispatcher.BeginInvoke(new Action(() => { ProgressValue = pct; ProgressText = $"下载中… {pct}%（{stage}）"; }));
                else
                {
                    ProgressValue = pct;
                    ProgressText = $"下载中… {pct}%（{stage}）";
                }
                return true;
            };
            var result = await Task.Run(() => _commandService.Execute("deploy_project",
                new Dictionary<string, object?>
                {
                    ["device_ip"] = DeviceConnectionService.Session.Ip,
                    ["progress"] = progress,
                }));
            ProgressVisible = false;
            ProgressValue = 100;
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
            OnPropertyChanged(nameof(DeviceVersionText));
            if (session == null)
            {
                StatusText = "未连接";
                BlinkOn = false;   // 断开后状态复位（防重连显示旧状态）
                VncOn = false;
                FirmwareProgressText = "";
                FirmwareProgressVisible = false;
                // Check 修复（2026-09）：断开**不清固件库**——固件库是本地目录浏览（可编辑目录/未连接扫描），
                // 与连接无关；升级按钮门禁（CanUpgrade=IsConnected）已保证未连接不可点升级，列表保留供选择浏览。
                // 按连接页选中 profile 重扫（无 profile 则保留当前列表——本地浏览语义）
                if (SelectedProfile != null)
                    _ = RefreshFirmwareForSessionAsync();
            }
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            BlinkCommand.NotifyCanExecuteChanged();
            VncCommand.NotifyCanExecuteChanged();
            RefreshFirmwareCommand.NotifyCanExecuteChanged();   // 审查 🟡：连接/断开时刷新固件库按钮启用态同步
            RefreshGate();
        }
    }
}
