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

        /// <summary>部署工程容器（POST /api/transfer，body=zip 字节）。失败一次直接报错不重试。</summary>
        public static async Task<DeviceOpResult> DeployAsync(string ip, string zipPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                return DeviceOpResult.Fail("INVALID_PARAM", "IP 与部署包路径必填且文件须存在");
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(EventTimeoutMs) };
                using var content = new ByteArrayContent(await File.ReadAllBytesAsync(zipPath, ct).ConfigureAwait(false));
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                using var resp = await client.PostAsync($"http://{ip}/api/transfer", content, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseResponse(body, resp.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or ArgumentException)
            {
                return DeviceOpResult.Fail("UNREACHABLE", $"无法连接设备 {ip}: {ex.Message}");
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
