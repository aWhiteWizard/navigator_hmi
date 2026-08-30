using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace NavigatorHMI.Common
{
    /// <summary>扫描发现的设备（设计文档 property-device §3.2：IP/型号/ID/尺寸）。</summary>
    public class ScannedDevice
    {
        public string Ip { get; set; } = "";
        public string Model { get; set; } = "";
        public string Id { get; set; } = "";
        public string SizeInch { get; set; } = "";
        public string Version { get; set; } = "";
    }

    /// <summary>
    /// 局域网设备扫描（2026-08-30 L 循环实现，设计文档方案 B：HTTP 网段扫描）。
    /// 按网卡获取 /24 子网 → 并发遍历 IP → GET /api/device/info（短超时）→ 收集响应设备。
    /// L 循环补修：改用真 async（WaitAsync + await）——原 Task.Run + SemaphoreSlim.Wait(cts.Token) +
    /// sync-over-async 在 254 IP 全量扫描时大量 task 卡住（实测仅完成 63/254）。
    /// </summary>
    public static class DeviceScanner
    {
        private const int ProbeTimeoutMs = 500;      // 单 IP 探测超时（设计：2s 总超时内并发）
        private const int ConcurrentLimit = 20;      // 并发上限（设计：并发 10~20）

        /// <summary>扫描指定网卡所在 /24 子网，返回发现的 HMI 设备。</summary>
        public static List<ScannedDevice> Scan(string nicName)
        {
            var ips = GetSubnetIps(nicName);
            if (ips.Count == 0) return new List<ScannedDevice>();

            using var sem = new SemaphoreSlim(ConcurrentLimit);
            var tasks = ips.Select(ip => ProbeAsync(ip, sem)).ToArray();
            try { Task.WaitAll(tasks); } catch { /* 个别任务异常不影响整体 */ }

            // 收集结果（ProbeAsync 写共享列表，线程安全 lock）
            lock (ResultLock)
            {
                var sorted = _results.OrderBy(d => d.Ip, StringComparer.Ordinal).ToList();
                _results.Clear();   // 静态共享列表复用前清空（Scan 可多次调用）
                return sorted;
            }
        }

        private static readonly object ResultLock = new();
        private static readonly List<ScannedDevice> _results = new();

        /// <summary>异步探测单 IP（并发限流；结果写共享列表）。</summary>
        private static async Task ProbeAsync(string ip, SemaphoreSlim sem)
        {
            await sem.WaitAsync();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs) };
                var body = await client.GetStringAsync($"http://{ip}/api/device/info").ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(model)) return;   // 非 HMI 设备（无型号）跳过
                var dev = new ScannedDevice
                {
                    Ip = ip,
                    Model = model,
                    Id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "",
                    SizeInch = root.TryGetProperty("sizeInch", out var s) ? s.GetString() ?? "" : "",
                    Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : ""
                };
                lock (ResultLock) _results.Add(dev);
            }
            catch { /* 超时/异常跳过 */ }
            finally { sem.Release(); }
        }

        /// <summary>
        /// 网卡 → 全部 /24 子网 IP（网卡每个 IPv4 地址前三段 + 1..254，跨网段去重）。
        /// 2026-08-30 用户代码评论修复：原实现仅取第一个 /24 子网（`return result` 提前返回）——
        /// 网卡配置多网段跃点 IP（如 192.168.1.x / 192.168.10.x / 192.168.20.x）时只扫到第一段，
        /// 其余网段设备扫不到；改为遍历网卡全部 IPv4 地址的子网。
        /// 审查 B2：扫描范围 = 网卡 IPv4 地址数 × 254（同网段多地址经 Distinct 去重）；
        /// 并发上限 ConcurrentLimit=20 不变，最坏耗时约 (254×N/20)×500ms（N=2 约 12.7s，Task.Run 内不冻 UI）。
        /// </summary>
        private static List<string> GetSubnetIps(string nicName)
        {
            var result = new List<string>();
            var prefixes = new HashSet<string>(StringComparer.Ordinal);   // 网段前缀去重（同 /24 多地址只扫一次）
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!nic.Name.Equals(nicName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;   // 仅 IPv4
                        var ip = addr.Address.ToString();
                        var prefix = ip.Substring(0, ip.LastIndexOf('.') + 1);   // 如 "192.168.1."
                        if (!prefixes.Add(prefix)) continue;   // 同网段已收集，跳过
                        for (int i = 1; i <= 254; i++)
                            result.Add(prefix + i);
                    }
                    break;   // 只扫目标网卡（匹配到即结束；多 IPv4 地址全收集后 break）
                }
            }
            catch { }
            return result;
        }
    }
}
