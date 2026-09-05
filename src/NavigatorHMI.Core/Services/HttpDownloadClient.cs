using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 设备 HTTP 传输客户端（K 循环 K-4/K-5）——与 FW 接收端（K-8b httpreceiver）端点对应：
    ///   POST /api/transfer（部署容器 zip 上传，body=zip 字节；K-8b 全量接收 64MB 边界内）
    ///   POST /api/vnc {enable} / POST /api/blink {enable}（设备操作，K-9）
    ///   GET /api/version（工程版本，CheckVersion 三分：一致跳过/不同需下载/错误报错）
    /// 失败一次直接报错不重试（设计已定）；超时 30s（大文件按分块放大——K-8b 全量 64MB 边界内单请求）。
    /// </summary>
    public static class HttpDownloadClient
    {
        /// <summary>传输/指令结果。</summary>
        public class DeviceOpResult
        {
            /// <summary>是否成功。</summary>
            public bool Success { get; private set; }

            /// <summary>错误码（TRANSFER_FAILED/TRANSFER_BUSY/UNREACHABLE/HTTP_x）或 OK。</summary>
            public string Code { get; private set; } = "OK";

            /// <summary>错误消息/服务端消息。</summary>
            public string Message { get; private set; } = "";

            public static DeviceOpResult Ok(string message = "")
                => new() { Success = true, Code = "OK", Message = message };

            public static DeviceOpResult Fail(string code, string message)
                => new() { Success = false, Code = code, Message = message };
        }

        private const int EventTimeoutMs = 30000;   // 单请求事件超时（设计 §3.4 定标）
        private const int ProgressPollIntervalMs = 200;   // D-B4：设备端进度轮询间隔（200ms，轻量）

        /// <summary>T-1a（2026-09-05 T 循环）：部署进度信息——Percent=设备端统一进度（PC 与 FW 屏同源同值）；
        /// CurrentFile=当前上传的 zip 条目名（PC 按发送字节窗口估算；mp4 等已编码媒体压缩≈1:1 故准；安装段 null）；传输段 SentBytes/TotalBytes。</summary>
        public sealed class DeployProgressInfo
        {
            public int Percent { get; init; }
            public string Stage { get; init; } = "";
            public string? CurrentFile { get; init; }
            public long SentBytes { get; init; }
            public long TotalBytes { get; init; }
        }

        private const int ChunkSizeBytes = 4 * 1024 * 1024;          // T-1a：分块大小（FW 端不校验块大小，仅累计落盘）
        private const long LegacyWholePostLimit = 64L * 1024 * 1024; // ≤64MB 走整包单 POST（兼容旧 FW——无分块支持）；>64MB 分块（需 FW ≥ T-1a 分块协议）

        /// <summary>
        /// 部署工程容器（旧签名保留——≤64MB 整包单 POST + 轮询设备进度；deploy_firmware 等整包路径用）。
        /// 大包（>64MB）请用 DeployProjectAsync（分块 + 磁盘预检）。
        /// </summary>
        public static async Task<DeviceOpResult> DeployAsync(string ip, string zipPath,
            Func<int, string, bool>? onProgress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                return DeviceOpResult.Fail("INVALID_PARAM", "IP 与部署包路径必填且文件须存在");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Func<DeployProgressInfo, bool>? cb = onProgress == null ? null : p => onProgress(p.Percent, p.Stage);
            return await DeployWholeAsync(ip, zipPath, new FileInfo(zipPath).Length, cb, cts).ConfigureAwait(false);
        }

        /// <summary>
        /// 部署工程容器（T-1a 2026-09-05 新链路）：上传前磁盘预检（GET /api/device/info disk_free → cap=空闲×2/3，
        /// 用户拍板「不大于设备最大存储 2/3」——超限先报错不浪费传输；旧 FW 无 disk 字段时 ≤64MB 整包照走、>64MB 报错提示升级）；
        /// zip ≤64MB 整包单 POST（旧 FW 兼容）；&gt;64MB 分块循环 POST（X-Tf-Id/Index/Total/Size 头，FW 每块 append 落盘
        /// + 块进度 0~70 实时——大包不受内存限制）。进度单一真源 = 设备 /api/progress 轮询（PC 与 FW 屏同值同进度）；
        /// CurrentFile 由 zip 条目按发送字节估算（mp4 等已编码媒体压缩≈1:1 故准）。
        /// </summary>
        public static async Task<DeviceOpResult> DeployProjectAsync(string ip, string zipPath, Func<DeployProgressInfo, bool>? onProgress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                return DeviceOpResult.Fail("INVALID_PARAM", "IP 与部署包路径必填且文件须存在");
            var zipLen = new FileInfo(zipPath).Length;
            if (zipLen <= 0) return DeviceOpResult.Fail("INVALID_PARAM", "部署包为空");

            // 1. 磁盘预检（cap = 设备空闲 ×2/3；disk_free 不可得=旧 FW → ≤64MB 整包兼容放行，>64MB 分块需新 FW）
            long? diskFree = null;
            try
            {
                var info = await GetDeviceInfoRawAsync(ip, ct).ConfigureAwait(false);
                if (info.Ok && info.DiskFree >= 0) diskFree = info.DiskFree;
            }
            catch { /* 预检失败不阻塞——传输层自会报错 */ }
            if (diskFree is > 0)
            {
                var cap = diskFree.Value / 3 * 2;
                if (zipLen > cap)
                    return DeviceOpResult.Fail("TRANSFER_TOO_LARGE",
                        $"部署包超过设备剩余空间 2/3（需 {zipLen / (1024 * 1024)} MB / 上限 {cap / (1024 * 1024)} MB）——请清理设备存储或改用 RTSP 流");
            }
            if (diskFree is null && zipLen > LegacyWholePostLimit)
                return DeviceOpResult.Fail("TRANSFER_TOO_LARGE",
                    "部署包超过 64MB 且设备不支持分块上传（旧固件）——请先升级设备固件再传大包");

            // 2. 传输（>64MB 分块；≤64MB 整包单 POST 兼容）
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return zipLen > LegacyWholePostLimit
                ? await DeployChunkedAsync(ip, zipPath, zipLen, onProgress, cts).ConfigureAwait(false)
                : await DeployWholeAsync(ip, zipPath, zipLen, onProgress, cts).ConfigureAwait(false);
        }

        /// <summary>T-1a：分块部署（>64MB）——按块循环 POST /api/transfer（X-Tf-* 头），FW 每块 append 落盘；
        /// 末块响应=安装结果；进度轮询设备 /api/progress（0~70 接收块级实时 + 70~100 安装）同源。</summary>
        private static async Task<DeviceOpResult> DeployChunkedAsync(string ip, string zipPath, long zipLen,
            Func<DeployProgressInfo, bool>? onProgress, CancellationTokenSource cts)
        {
            // zip 条目清单（名+未压缩长——当前文件估算映射用；mp4 媒体压缩≈1:1 故发送字节窗口映射准）
            var entries = new List<(string Name, long Len)>();
            long totalUncompressed = 0;
            using (var za = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in za.Entries)
                {
                    if (e.Length <= 0) continue;   // 目录/空条目不参与映射
                    entries.Add((e.FullName, e.Length));
                    totalUncompressed += e.Length;
                }
            }
            var transferState = new ChunkSendState(zipPath, zipLen, entries, totalUncompressed);
            var poller = onProgress != null ? PollDeviceProgressAsync(ip, onProgress, transferState, cts.Token) : null;
            try
            {
                var id = Guid.NewGuid().ToString("N");
                var total = (int)((zipLen + ChunkSizeBytes - 1) / ChunkSizeBytes);
                // 审查 🟡 B（2026-09-05 PC 复审）：末块响应 = 完整安装结果（FW 完成安装才写）——末块 POST 不得
                // 被 120s 传输超时误杀（超大工程慢存储安装可能超 120s → 两侧结果分歧）；末块用无超时 client，
                // 非末块保持 120s（块级往返毫秒级）
                using var blockClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
                using var finalClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSizeBytes, useAsync: true);
                var buf = new byte[ChunkSizeBytes];
                for (int idx = 0; idx < total; idx++)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var n = await fs.ReadAsync(buf.AsMemory(0, ChunkSizeBytes), cts.Token).ConfigureAwait(false);
                    if (n <= 0) return DeviceOpResult.Fail("TRANSFER_FAILED", "部署包读取中断");
                    var chunk = n == buf.Length ? buf : buf[..n];
                    using var content = new ByteArrayContent(chunk);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    content.Headers.Add("X-Tf-Id", id);
                    content.Headers.Add("X-Tf-Index", idx.ToString());
                    content.Headers.Add("X-Tf-Total", total.ToString());
                    content.Headers.Add("X-Tf-Size", zipLen.ToString());
                    var client = idx == total - 1 ? finalClient : blockClient;
                    using var resp = await client.PostAsync($"http://{ip}/api/transfer", content, cts.Token).ConfigureAwait(false);
                    var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    transferState.SetSent(transferState.SentBytes + n);
                    if (idx < total - 1)
                    {
                        // 非末块：期望 CHUNK_OK（块已落盘）；FW 拒绝（会话错乱/超 cap）→ 立即失败
                        if (!body.Contains("\"CHUNK_OK\"", StringComparison.Ordinal) || !resp.IsSuccessStatusCode)
                        {
                            var r = ParseResponse(body, resp.StatusCode);
                            return DeviceOpResult.Fail(r.Code, r.Message);
                        }
                        continue;
                    }
                    // 末块：响应 = 安装结果（finishTransfer SUCCESSFUL_REBOOT/错误）；先给 poller 1s 收尾窗口
                    //（它已见设备 100/active=false 自然终止——finally 再 cancel+await 幂等兜底）
                    var result = ParseResponse(body, resp.StatusCode);
                    if (poller != null)
                    {
                        try { await Task.WhenAny(poller, Task.Delay(1000, cts.Token)).ConfigureAwait(false); } catch { }
                    }
                    return result;
                }
                return DeviceOpResult.Fail("TRANSFER_FAILED", "分块循环异常结束");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException or IOException)
            {
                if (ex is OperationCanceledException && cts.IsCancellationRequested) throw;
                return DeviceOpResult.Fail("UNREACHABLE", $"无法连接设备 {ip}: {ex.Message}");
            }
            finally
            {
                // 审查 🟡 A（2026-09-05 PC 复审）：poller 统一收尾——任意提前 return（读中断/非末块失败/循环异常）
                // 都不漏 cancel+await（防孤立 poller 悬挂至 FW 30s TTL）；末块已 await 过 → 幂等
                if (poller != null)
                {
                    cts.Cancel();
                    try { await poller.ConfigureAwait(false); } catch { }
                }
            }
        }

        /// <summary>T-1a：整包部署（≤64MB 旧 FW 兼容）——原逻辑（单 POST + 轮询设备进度）。</summary>
        private static async Task<DeviceOpResult> DeployWholeAsync(string ip, string zipPath, long zipLen,
            Func<DeployProgressInfo, bool>? onProgress, CancellationTokenSource cts)
        {
            var transferState = new ChunkSendState(zipPath, zipLen, new List<(string, long)>(), 0);   // 整包无条目映射（CurrentFile=包名）
            var poller = onProgress != null ? PollDeviceProgressAsync(ip, onProgress, transferState, cts.Token) : null;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
                using var content = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, cts.Token).ConfigureAwait(false));
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                using var resp = await client.PostAsync($"http://{ip}/api/transfer", content, cts.Token).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var result = ParseResponse(body, resp.StatusCode);
                if (poller != null)
                {
                    try { await Task.WhenAny(poller, Task.Delay(1000, cts.Token)).ConfigureAwait(false); } catch { }
                    cts.Cancel();
                    try { await poller.ConfigureAwait(false); } catch { }
                }
                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException)
            {
                if (poller != null) { cts.Cancel(); try { await poller.ConfigureAwait(false); } catch { } }
                return DeviceOpResult.Fail("UNREACHABLE", $"无法连接设备 {ip}: {ex.Message}");
            }
        }

        /// <summary>T-1a：分块发送共享状态——已发字节（发送循环写）+ zip 条目映射（估算当前文件——poller 只读快照）。</summary>
        private sealed class ChunkSendState
        {
            private readonly string _zipName;
            private readonly long _zipLen;
            private readonly List<(string Name, long Len)> _entries;
            private readonly long _totalUncompressed;
            private long _sent;
            public ChunkSendState(string zipPath, long zipLen, List<(string, long)> entries, long totalUncompressed)
            {
                _zipName = Path.GetFileName(zipPath);
                _zipLen = zipLen;
                _entries = entries;
                _totalUncompressed = totalUncompressed;
            }
            public long SentBytes { get => Interlocked.Read(ref _sent); }
            public long TotalBytes => _zipLen;
            public void SetSent(long v) => Interlocked.Exchange(ref _sent, v);
            public string? EstimateCurrentFile()
            {
                if (_entries.Count == 0 || _zipLen <= 0) return _zipName;
                // 按发送字节占比 → 未压缩目标字节（mp4 等媒体压缩≈1:1，占比映射准；整体压缩比差异仅影响文字提示精度）
                var targetU = (double)SentBytes / _zipLen * _totalUncompressed;
                long acc = 0;
                foreach (var (name, len) in _entries)
                {
                    acc += len;
                    if (targetU <= acc) return name;
                }
                return _entries[^1].Name;
            }
        }

        /// <summary>
        /// D-B4/T-1a：轮询设备端进度（GET /api/progress → {progress, stage, active}）——进度单一真源（PC 与 FW 屏同值）。
        /// 终止条件（完备，防无限轮询）：
        ///   ① progress>=100（安装完成）；② 见过正进度后 active=false（传输结束）；③ progress=-1（设备端失败）；
        ///   ④ 回调返回 false（UI 提前终止）；⑤ 轮询总时长上限（60s 硬兜底）。
        /// 陈旧 100 防误判：首次响应即 (100,false) 且从未见过正进度/active → 视为上次部署残留，继续轮询。
        /// 回调载荷含 CurrentFile（state 按发送字节估算 zip 条目——mp4 等媒体压缩≈1:1 故准）+ 发送字节。
        /// </summary>
        private static async Task PollDeviceProgressAsync(string ip, Func<DeployProgressInfo, bool> onProgress, ChunkSendState state, CancellationToken ct)
        {
            using var pollClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
            var lastPct = -1;
            var seenActive = false;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                try
                {
                    var body = await pollClient.GetStringAsync($"http://{ip}/api/progress", ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    var pct = root.TryGetProperty("progress", out var pr) ? pr.GetInt32() : -1;
                    var stage = root.TryGetProperty("stage", out var st) ? st.GetString() ?? "" : "";
                    var active = root.TryGetProperty("active", out var ac) && ac.GetBoolean();
                    if (pct >= 0 && pct != lastPct)
                    {
                        lastPct = pct;
                        var info = new DeployProgressInfo
                        {
                            Percent = pct,
                            Stage = stage,
                            // 审查 🟡（2026-09-05 PC 复审）：安装段（≥70）不显示「上传文件」——CurrentFile 仅传输段有意义
                            //（70~100 由设备解压/写入驱动，PC 发送已完成——避免「正在上传: 末条目」矛盾文案）
                            CurrentFile = pct < 70 ? state.EstimateCurrentFile() : null,
                            SentBytes = state.SentBytes,
                            TotalBytes = state.TotalBytes,
                        };
                        if (!onProgress(info)) return;   // 回调返回 false → 提前终止
                    }
                    if (active) seenActive = true;
                    if (pct >= 100 && seenActive) return;
                    if (pct < 0 && seenActive) return;
                    if (!active && lastPct > 0 && seenActive) return;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    if (lastPct >= 100) return;                 // 完成瞬间端点短暂不可达 → 收尾退出
                }
                try { await Task.Delay(ProgressPollIntervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>设备闪烁指令（POST /api/blink {enable}——K-9 FW 覆盖层闪烁）。</summary>
        public static async Task<DeviceOpResult> BlinkAsync(string ip, bool enable, CancellationToken ct = default)
            => await PostJsonAsync(ip, "/api/blink", $"{{\"enable\":{(enable ? "true" : "false")}}}", ct);

        /// <summary>VNC 运行时启停指令（POST /api/vnc {enable}——K-9 FW 运行时启停 5900）。</summary>
        public static async Task<DeviceOpResult> VncAsync(string ip, bool enable, CancellationToken ct = default)
            => await PostJsonAsync(ip, "/api/vnc", $"{{\"enable\":{(enable ? "true" : "false")}}}", ct);

        /// <summary>工程版本查询（GET /api/version——CheckVersion 三分：一致跳过/不同需下载/错误报错）。</summary>
        public static async Task<(bool Ok, string? Version, string? Error)> GetVersionAsync(string ip, CancellationToken ct = default)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
                var body = await client.GetStringAsync($"http://{ip}/api/version", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var ver = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
                return (true, ver, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or ArgumentException or InvalidOperationException)
            {
                return (false, null, $"版本查询失败: {ex.Message}");
            }
        }

        /// <summary>设备信息查询（GET /api/device/info——型号/尺寸/ID/固件版本；A1 连接测试三分数据源）。
        /// D1：固件版本前置检查用（/api/device/info version = 固件版本，术语口径 FW httreceiver L150）。
        /// 2026-09-04 调试 OTA：FirmwareTs = 设备当前固件 OTA 打包时刻（旧固件无 firmware_ts 字段 → "0"）——
        /// PC 端对调试包（版本恒 v1.1.0）按打包时刻先后判断覆盖。</summary>
        public static async Task<(bool Ok, string? Version, string? FirmwareTs, string? Error)> GetDeviceInfoAsync(string ip, CancellationToken ct = default)
        {
            try
            {
                var raw = await GetDeviceInfoRawAsync(ip, ct).ConfigureAwait(false);
                return (raw.Ok, raw.Version, raw.FirmwareTs, raw.Error);
            }
            catch (Exception ex)
            {
                return (false, null, "0", $"设备信息查询失败: {ex.Message}");
            }
        }

        /// <summary>T-1a：设备信息原始解析（含 disk_free/disk_total——部署磁盘预检 cap 用；旧 FW 无字段 → DiskFree=-1）。</summary>
        private static async Task<(bool Ok, string? Version, string? FirmwareTs, long DiskFree, string? Error)> GetDeviceInfoRawAsync(string ip, CancellationToken ct)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
            var body = await client.GetStringAsync($"http://{ip}/api/device/info", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var ver = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            var ts = root.TryGetProperty("firmware_ts", out var t) ? t.GetString() : null;
            var free = root.TryGetProperty("disk_free", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt64() : -1L;
            return (true, ver, string.IsNullOrEmpty(ts) ? "0" : ts, free, null);
        }

        /// <summary>POST JSON 到设备端点（vnc/blink）。</summary>
        private static async Task<DeviceOpResult> PostJsonAsync(string ip, string endpoint, string json, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ip)) return DeviceOpResult.Fail("INVALID_PARAM", "IP 必填");
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var resp = await client.PostAsync($"http://{ip}{endpoint}", content, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseResponse(body, resp.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException)
            {
                return DeviceOpResult.Fail("UNREACHABLE", $"无法连接设备 {ip}: {ex.Message}");
            }
        }

        /// <summary>解析 FW JSON 响应（code 字段：OK/SUCCESSFUL_REBOOT 成功；其余失败）。</summary>
        private static DeviceOpResult ParseResponse(string body, System.Net.HttpStatusCode status)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                if (status != System.Net.HttpStatusCode.OK)
                    return DeviceOpResult.Fail(string.IsNullOrEmpty(code) || code == "OK" ? $"HTTP_{(int)status}" : code, message);
                if (code == "OK" || code == "SUCCESSFUL_REBOOT" || code == "SUCCESSFUL")
                    return DeviceOpResult.Ok(message);
                return DeviceOpResult.Fail(string.IsNullOrEmpty(code) ? $"HTTP_{(int)status}" : code, message);
            }
            catch (JsonException)
            {
                return DeviceOpResult.Fail($"HTTP_{(int)status}", status == System.Net.HttpStatusCode.OK ? "响应解析失败" : $"HTTP {(int)status}");
            }
        }
    }
}
