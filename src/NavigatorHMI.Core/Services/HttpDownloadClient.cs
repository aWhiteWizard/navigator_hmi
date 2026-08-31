using System.Net.Http;
using System.Text.Json;

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

        /// <summary>
        /// 部署工程容器（POST /api/transfer，body=zip 字节）。失败一次直接报错不重试。
        /// D-B4：onProgress 非空时轮询 GET /api/progress（设备端接收/解压/落盘真实进度）——
        /// PC 进度条显示设备实际处理进度（真同步，用户 2026-08-30 定：非上传进度）。
        /// 回调语义：0~100 进度 + 阶段描述；返回 true 继续轮询，false 提前终止（UI 取消等）。
        /// </summary>
        public static async Task<DeviceOpResult> DeployAsync(string ip, string zipPath,
            Func<int, string, bool>? onProgress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                return DeviceOpResult.Fail("INVALID_PARAM", "IP 与部署包路径必填且文件须存在");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var poller = onProgress != null ? PollDeviceProgressAsync(ip, onProgress, cts.Token) : null;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
                using var content = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, ct).ConfigureAwait(false));
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                // 先 POST（权威结果）——失败时 poller 由 cts 取消，不泄漏
                using var resp = await client.PostAsync($"http://{ip}/api/transfer", content, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var result = ParseResponse(body, resp.StatusCode);
                // FW transfer 响应在接收+安装全部完成后才写（finishTransfer）——POST 返回时设备端已完成；
                // poller 已在 POST 期间并行看到进度（含 100）。给 poller 短暂收尾时间（等它自然终止：
                // 设备端已完成 → 下一轮 GET 即 progress>=100 终止；最多 1s 兜底防卡）。
                if (poller != null)
                {
                    try { await Task.WhenAny(poller, Task.Delay(1000, ct)).ConfigureAwait(false); } catch { }
                    cts.Cancel();
                    try { await poller.ConfigureAwait(false); } catch { }
                }
                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException)
            {
                // POST 失败（设备不可达等）→ 取消 poller 防泄漏
                if (poller != null)
                {
                    cts.Cancel();
                    try { await poller.ConfigureAwait(false); } catch { }
                }
                return DeviceOpResult.Fail("UNREACHABLE", $"无法连接设备 {ip}: {ex.Message}");
            }
        }

        /// <summary>
        /// D-B4：轮询设备端下载/安装进度（GET /api/progress → {progress, stage, active}）——进度展示用。
        /// 终止条件（完备，防无限轮询）：
        ///   ① progress>=100（安装完成）；
        ///   ② 见过正进度后 active=false（传输结束）；
        ///   ③ progress=-1（设备端失败）；
        ///   ④ 回调返回 false（UI 提前终止）；
        ///   ⑤ 轮询总时长上限（60s 硬兜底——大工程解压/落盘秒级，60s 足够；防设备卡死挂起）。
        /// 陈旧 100 防误判：首次响应即 (100,false) 且从未见过正进度/active → 视为上次部署残留，继续轮询。
        /// </summary>
        private static async Task PollDeviceProgressAsync(string ip, Func<int, string, bool> onProgress, CancellationToken ct)
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
                        if (!onProgress(pct, stage)) return;   // 回调返回 false → 提前终止
                    }
                    if (active) seenActive = true;
                    // 完成判定（全部以 seenActive 为门槛——陈旧 100 残留（首次响应即 100/非 active）不得误判完成）：
                    // ① progress>=100 且本会话见过传输中（active）→ 安装完成；
                    // ② 曾传输中后 progress=-1 → 设备端失败；
                    // ③ 曾见正进度且 active=false → 传输结束（progress 停留中间值但传输已收尾）。
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
        /// D1：固件版本前置检查用（/api/device/info version = 固件版本，术语口径 FW httreceiver L150）。</summary>
        public static async Task<(bool Ok, string? Version, string? Error)> GetDeviceInfoAsync(string ip, CancellationToken ct = default)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
                var body = await client.GetStringAsync($"http://{ip}/api/device/info", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var ver = root.TryGetProperty("version", out var v) ? v.GetString() : null;
                return (true, ver, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or ArgumentException or InvalidOperationException)
            {
                return (false, null, $"设备信息查询失败: {ex.Message}");
            }
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
