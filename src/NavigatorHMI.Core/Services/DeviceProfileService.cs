using System.Text.Json;

namespace NavigatorHMI.Common
{
    /// <summary>设备能力（device-profile capability 字段；承接 A1 门禁 + 编译/传输能力）。</summary>
    public class DeviceCapability
    {
        /// <summary>支持固件升级（flash）。</summary>
        public bool Flash { get; set; } = true;

        /// <summary>支持工程部署（deploy）。</summary>
        public bool Deploy { get; set; } = true;

        /// <summary>支持固件下载（firmware）。</summary>
        public bool Firmware { get; set; } = true;

        /// <summary>支持信息查询（info）。</summary>
        public bool Info { get; set; } = true;

        /// <summary>编译版本（新建项目/版本校验用）。</summary>
        public string CompileVersion { get; set; } = "V1";

        /// <summary>传输接口支持（http 等）。</summary>
        public List<string> TransferInterfaces { get; set; } = new() { "http" };

        /// <summary>上传大小上限（MB）。</summary>
        public int UploadSizeLimitMB { get; set; } = 64;
    }

    /// <summary>画布限制（P1 画布适配用；本循环仅建模）。</summary>
    public class DeviceCanvasLimit
    {
        /// <summary>最大控件数（0=不限）。</summary>
        public int MaxWidgets { get; set; }
    }

    /// <summary>
    /// 设备能力描述文件（device-profile.json，K 循环 K-7）——加新型号不改代码，只加描述文件。
    /// schema：model / sizeInch / width / height / interfaces[rs485|eth] / capability / canvasLimit。
    /// </summary>
    public class DeviceProfile
    {
        /// <summary>设备型号（FW /api/device/info 上报值，如 NavigatorHMI-7）。</summary>
        public string Model { get; set; } = "";

        /// <summary>尺寸（7寸/4寸，A1 连接测试校验用）。</summary>
        public string SizeInch { get; set; } = "";

        /// <summary>屏幕宽度（像素）。</summary>
        public int Width { get; set; }

        /// <summary>屏幕高度（像素）。</summary>
        public int Height { get; set; }

        /// <summary>设备接口（rs485/eth）。</summary>
        public List<string> Interfaces { get; set; } = new();

        /// <summary>能力（门禁/传输）。</summary>
        public DeviceCapability Capability { get; set; } = new();

        /// <summary>画布限制。</summary>
        public DeviceCanvasLimit CanvasLimit { get; set; } = new();
    }

    /// <summary>
    /// 设备能力描述服务（K-7）——加载 device-profiles/*.json（内置默认 + 外部文件覆盖，同名外部优先）+ 目录变更热重载（FileSystemWatcher）。
    /// CLI connect / GUI 设备面板 / 新建项目对话框共用。
    /// </summary>
    public static class DeviceProfileService
    {
        private static readonly object _lock = new();
        private static List<DeviceProfile> _profiles = new();
        private static FileSystemWatcher? _watcher;

        static DeviceProfileService()
        {
            lock (_lock) { ReloadLocked(); }   // 静态构造即加载内置默认（未 Initialize 也可查询）
        }

        /// <summary>描述文件目录（Initialize 设置；null=未初始化，仅内置默认）。</summary>
        public static string? Directory { get; private set; }

        /// <summary>配置变更事件（热重载通知，新建项目对话框/设备面板刷新）。</summary>
        public static event Action? ProfilesChanged;

        /// <summary>当前全部 profile（内置默认 + 外部文件，外部同名覆盖内置）。</summary>
        public static IReadOnlyList<DeviceProfile> Profiles
        {
            get { lock (_lock) { return _profiles; } }
        }

        /// <summary>按型号查询（忽略大小写）；未知型号返回 null。</summary>
        public static DeviceProfile? GetByModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            return Profiles.FirstOrDefault(p => string.Equals(p.Model, model, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>型号是否已配置描述文件（未知型号 → 保守降级）。</summary>
        public static bool IsKnown(string model) => GetByModel(model) != null;

        /// <summary>初始化：加载内置默认 + 外部目录文件（存在时），并挂目录热重载。可重复调用（重载）。目录不存在时创建并写入内置默认样例（保证「加新型号只加文件」有模板）。</summary>
        public static void Initialize(string? directory = null)
        {
            lock (_lock)
            {
                Directory = directory;
                _watcher?.Dispose();
                _watcher = null;
                EnsureSampleProfilesLocked();
                ReloadLocked();

                if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory))
                {
                    try
                    {
                        _watcher = new FileSystemWatcher(directory, "*.json")
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                            EnableRaisingEvents = true
                        };
                        FileSystemEventHandler onChanged = (s, e) => Reload();
                        RenamedEventHandler onRenamed = (s, e) => Reload();
                        _watcher.Changed += onChanged;
                        _watcher.Created += onChanged;
                        _watcher.Deleted += onChanged;
                        _watcher.Renamed += onRenamed;
                    }
                    catch (Exception ex)
                    {
                        // 目录无权限等 → 降级为仅内置默认（不崩对话框构造）
                        System.Diagnostics.Trace.WriteLine($"[DeviceProfileService] 热重载监视启动失败 {directory}: {ex.Message}");
                        _watcher = null;
                    }
                }
            }
            NotifyProfilesChanged();
        }

        /// <summary>重新加载（热重载/手动刷新）。</summary>
        public static void Reload()
        {
            lock (_lock) { ReloadLocked(); }
            NotifyProfilesChanged();
        }

        /// <summary>逐个隔离订阅者异常（FileSystemWatcher 后台线程触发——线程池未处理异常默认终止进程，比对 SessionChanged 先例）。</summary>
        private static void NotifyProfilesChanged()
        {
            var handlers = ProfilesChanged?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (var d in handlers)
            {
                try { ((Action)d)(); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DeviceProfileService] ProfilesChanged 订阅者异常: {ex}"); }
            }
        }

        /// <summary>目录不存在时创建并写入内置默认样例 profile（7寸/4寸），保证外部描述文件机制有模板可抄。</summary>
        private static void EnsureSampleProfilesLocked()
        {
            if (string.IsNullOrEmpty(Directory)) return;
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    foreach (var def in DefaultProfiles())
                    {
                        var file = Path.Combine(Directory, def.Model + ".json");
                        if (!File.Exists(file))
                            File.WriteAllText(file, JsonSerializer.Serialize(def, SampleJsonOptions));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DeviceProfileService] 样例 profile 写入失败: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions SampleJsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

        private static void ReloadLocked()
        {
            var merged = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);

            // 内置默认（7寸/4寸——加新型号只需加外部文件）
            foreach (var def in DefaultProfiles())
                merged[def.Model] = def;

            // 外部文件覆盖（同名外部优先）
            if (!string.IsNullOrEmpty(Directory) && System.IO.Directory.Exists(Directory))
            {
                foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<DeviceProfile>(File.ReadAllText(file), SampleJsonOptions);
                        if (p != null && !string.IsNullOrWhiteSpace(p.Model))
                        {
                            // 语义级 null 兜底（外部 JSON "capability":null 等——坏 JSON 有 catch，语义 null 须兜底防消费端 NRE）
                            p.Capability ??= new DeviceCapability();
                            p.CanvasLimit ??= new DeviceCanvasLimit();
                            merged[p.Model] = p;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"[DeviceProfileService] 描述文件解析失败 {file}: {ex.Message}");
                    }
                }
            }

            _profiles = merged.Values.ToList();
        }

        private static List<DeviceProfile> DefaultProfiles()
            => new()
            {
                new DeviceProfile
                {
                    Model = "NavigatorHMI-7",
                    SizeInch = "7寸",
                    Width = 1024,
                    Height = 600,
                    Interfaces = new List<string> { "eth" },
                    Capability = new DeviceCapability()
                },
                new DeviceProfile
                {
                    Model = "NavigatorHMI-4",
                    SizeInch = "4寸",
                    Width = 720,
                    Height = 720,
                    Interfaces = new List<string> { "rs485", "eth" },
                    Capability = new DeviceCapability()
                }
            };
    }
}
