using System.Text.Json;

namespace NavigatorHMI.Common
{
    /// <summary>设备测试连接结果状态（连接层三分；传输层四类细枚举由 K-3 deploy 前三校验拆分）。</summary>
    public enum DeviceTestStatus
    {
        /// <summary>连接成功（会话已建立）</summary>
        Connected,
        /// <summary>连接失败（网络不通/超时/服务未响应）</summary>
        CannotConnect,
        /// <summary>设备类型/尺寸不匹配（型号或尺寸与期望不符）</summary>
        DeviceMismatch
    }

    /// <summary>设备测试连接结果（含成功时的会话对象与回显信息）。</summary>
    public class DeviceTestResult
    {
        private DeviceTestResult(bool success, DeviceTestStatus status, string message, ConnectionSession? session)
        {
            Success = success;
            Status = status;
            Message = message;
            Session = session;
        }

        /// <summary>是否连接成功。</summary>
        public bool Success { get; }

        /// <summary>结果状态（三分）。</summary>
        public DeviceTestStatus Status { get; }

        /// <summary>结果文案（GUI/CLI 展示）。</summary>
        public string Message { get; }

        /// <summary>成功时的连接会话（失败为 null）。</summary>
        public ConnectionSession? Session { get; }

        /// <summary>连接成功。</summary>
        public static DeviceTestResult Ok(ConnectionSession session)
            => new(true, DeviceTestStatus.Connected, "已连接", session);

        /// <summary>连接失败（网络/服务不可达）。</summary>
        public static DeviceTestResult Fail(DeviceTestStatus status, string message)
            => new(false, status, message, null);
    }

    /// <summary>
    /// 设备连接服务单点（K 循环 K-2）——CLI connect / GUI 设备管理面板 / 主窗口状态栏三方对等。
    /// 真实实现：HTTP GET /api/device/info（FW K-8b 端点）→ 校验型号/尺寸 → 建立 <see cref="ConnectionSession"/>。
    /// <see cref="UseStub"/>：K-8b 端点就绪前，测试/无设备环境 CLI 自测用（返回模拟成功）。
    /// </summary>
    public static class DeviceConnectionService
    {
        private static readonly object _lock = new();

        /// <summary>当前连接会话（null = 未连接）。读写经内部锁（K-4 后台扫描/下载场景防竞态）。</summary>
        public static ConnectionSession? Session
        {
            get { lock (_lock) { return _session; } }
        }
        private static ConnectionSession? _session;

        /// <summary>会话变化事件（建立/断开）。触发时逐个隔离订阅者异常（比对 CommandService.SafeInvokeCommandExecuted 先例）。</summary>
        public static event Action<ConnectionSession?>? SessionChanged;

        /// <summary>stub 开关（K-8b 端点就绪前测试/无设备环境用；生产默认 false）。</summary>
        public static bool UseStub { get; set; } = false;

        private const int ConnectTimeoutMs = 2000;

        /// <summary>测试连接并（成功时）建立会话。失败路径统一断开旧会话（设计 P0 ①：连接测试失败自动断开）。
        /// 校验（K-7 profile 驱动）：期望型号已配置描述文件 → 设备上报型号匹配 → 尺寸匹配。</summary>
        public static DeviceTestResult TestConnection(string ip, string model, string? sizeInch = null)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(model))
            {
                SetSession(null);   // 参数非法 → 失败自动断开
                return DeviceTestResult.Fail(DeviceTestStatus.CannotConnect, "IP 与设备型号必填");
            }

            if (UseStub)
            {
                var stub = new ConnectionSession(ip.Trim(), model.Trim(), sizeInch ?? "7寸", "1.0.0", DateTime.UtcNow);
                SetSession(stub);
                return DeviceTestResult.Ok(stub);
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs) };
                var body = client.GetStringAsync($"http://{ip.Trim()}/api/device/info").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var devModel = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
                var devSize = root.TryGetProperty("sizeInch", out var s) ? SizeValue(s) : null;
                var fwVer = root.TryGetProperty("version", out var v) ? v.GetString() : null;

                // K-7 联动：期望型号必须已配置描述文件（未知型号 → 保守降级：拒绝建立会话，下载/部署被 CONNECTION_REQUIRED 门禁灰显）
                var profile = DeviceProfileService.GetByModel(model);
                if (profile == null)
                {
                    SetSession(null);
                    return DeviceTestResult.Fail(DeviceTestStatus.DeviceMismatch,
                        $"未知型号 \"{model}\" 未配置描述文件（device-profiles），连接已拒绝（无法校验能力）");
                }
                if (!devModel.Equals(profile.Model, StringComparison.OrdinalIgnoreCase))
                {
                    SetSession(null);
                    return DeviceTestResult.Fail(DeviceTestStatus.DeviceMismatch,
                        $"型号不匹配：期望 {profile.Model}，设备上报 {devModel}");
                }
                var expectSize = sizeInch ?? profile.SizeInch;
                if (!string.IsNullOrEmpty(expectSize) && !string.IsNullOrEmpty(devSize)
                    && !NormalizeSize(devSize).Equals(NormalizeSize(expectSize), StringComparison.OrdinalIgnoreCase))
                {
                    SetSession(null);
                    return DeviceTestResult.Fail(DeviceTestStatus.DeviceMismatch,
                        $"尺寸不匹配：期望 {expectSize}，设备上报 {devSize}");
                }

                var session = new ConnectionSession(ip.Trim(), devModel, devSize, fwVer, DateTime.UtcNow);
                SetSession(session);
                return DeviceTestResult.Ok(session);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException or System.Text.Json.JsonException)
            {
                SetSession(null);   // 网络失败/畸形 → 失败自动断开
                return DeviceTestResult.Fail(DeviceTestStatus.CannotConnect, $"无法连接 {ip}: {ex.Message}");
            }
        }

        /// <summary>sizeInch 字段取值（字符串直取；数字转字符串——兼容 FW 契约定标前两种线类型）。</summary>
        private static string? SizeValue(JsonElement el)
            => el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.ToString(),
                _ => null
            };

        /// <summary>尺寸比较归一（"7寸" ↔ "7" 等价——FW 契约定标前兼容两种报文形态）。</summary>
        private static string NormalizeSize(string size)
        {
            var s = size.Trim();
            return s.EndsWith("寸", StringComparison.Ordinal) ? s[..^1] : s;
        }

        /// <summary>断开连接（幂等）：会话置 null + 广播事件。连接测试失败自动断开同入口。</summary>
        public static void Disconnect()
        {
            lock (_lock)
            {
                if (_session == null) return;
                SetSessionLocked(null);
            }
        }

        private static void SetSession(ConnectionSession? session)
        {
            lock (_lock)
            {
                SetSessionLocked(session);
            }
        }

        private static void SetSessionLocked(ConnectionSession? session)
        {
            _session = session;
            var handlers = SessionChanged?.GetInvocationList() ?? Array.Empty<Delegate>();
            foreach (var d in handlers)
            {
                try { ((Action<ConnectionSession?>)d)(session); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[DeviceConnectionService] SessionChanged 订阅者异常: {ex}"); }
            }
        }
    }
}
