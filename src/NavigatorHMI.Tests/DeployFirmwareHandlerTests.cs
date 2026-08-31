using System.Net;
using System.Text;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// D 循环批 2 D1（2026-08-30）：DeployFirmwareHandler 落地测试——
    /// 版本比较 / .fw 定位 / 版本前置检查三分（一致跳过/旧拒绝）/ 魔数非法 / 文件缺失 / 端到端上传。
    /// </summary>
    [Collection("设备连接")]
    public class DeployFirmwareHandlerTests : IDisposable
    {
        private HttpListener? _listener;
        private readonly string _dir;

        public DeployFirmwareHandlerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_fw_handler_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { _listener?.Stop(); } catch { }
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = false;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string MakeFw(string version)
        {
            var app = Path.Combine(_dir, "app.bin");
            File.WriteAllBytes(app, new byte[] { 1, 2, 3 });
            return FwPackageBuilder.Build(version, _dir,
                new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app }).Path;
        }

        /// <summary>假设备：/api/transfer → deployResponse；/api/device/info → {version: devVersion}。返回 ip:port。</summary>
        private string StartFakeDevice(string devVersion, string deployResponse)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var port = Random.Shared.Next(25000, 50000);
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    listener.Start();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (listener.IsListening)
                            {
                                var ctx = await listener.GetContextAsync();
                                var path = ctx.Request.Url?.AbsolutePath ?? "";
                                var body = path switch
                                {
                                    "/api/device/info" => $"{{\"version\":\"{devVersion}\"}}",
                                    "/api/transfer" => deployResponse,
                                    _ => "{\"code\":\"OK\"}",
                                };
                                var buf = Encoding.UTF8.GetBytes(body);
                                ctx.Response.ContentType = "application/json";
                                ctx.Response.ContentLength64 = buf.Length;
                                await ctx.Response.OutputStream.WriteAsync(buf);
                                ctx.Response.Close();
                            }
                        }
                        catch { }
                    });
                    _listener = listener;
                    return $"127.0.0.1:{port}";
                }
                catch (HttpListenerException) { }
            }
            throw new InvalidOperationException("无法启动假设备");
        }

        private CommandResult Exec(string fwPath, string ip)
        {
            // RequiresConnection 门禁：需先建立会话（stub 连接——DeviceHandlers 校验链路复用 K-5 模式）
            var svc = new CommandService(new HMIProject { Name = "固件测试" });
            DeviceConnectionService.UseStub = true;
            DeviceConnectionService.Disconnect();
            svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = ip, ["model"] = "NavigatorHMI-7" });
            return svc.Execute("deploy_firmware", new Dictionary<string, object?>
            {
                ["device_ip"] = ip, ["file_path"] = fwPath,
            });
        }

        [Fact]
        public void 版本比较_语义三段()
        {
            // internal 方法不经测试项目直测——通过命令路径行为验证（旧版本拒绝/新版本放行在下方测试）
            var fwOld = MakeFw("1.0.0");
            var ip = StartFakeDevice("1.1.0", "{}");
            var r1 = Exec(fwOld, ip);
            Assert.Equal("VERSION_OLDER", r1.ErrorCode);   // 旧于设备 → 拒绝

            var fwNew = MakeFw("1.2.0");
            var ip2 = StartFakeDevice("1.1.0", "{\"code\":\"SUCCESSFUL_REBOOT\"}");
            var r2 = Exec(fwNew, ip2);
            Assert.True(r2.Success);                       // 新于设备 → 放行
        }

        [Fact]
        public void 最新固件定位_语义版本排序()
        {
            // 审查 🔴 修复验证：v1.9.0 与 v1.10.0 并存必须选 v1.10.0（字典序会误选 v1.9.0）
            // FindLatestFw 为 internal——经反射调用（单点验证，不引入全局 InternalsVisibleTo）
            MakeFw("1.9.0");
            var fwNew = MakeFw("1.10.0");
            MakeFw("1.2.0");
            var method = typeof(DeployFirmwareHandler).GetMethod("FindLatestFw",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var found = (string?)method.Invoke(null, new object[] { _dir });
            Assert.NotNull(found);
            Assert.Equal(fwNew, found);   // 语义最新 v1.10.0（非 v1.9.0）
        }

        [Fact]
        public void 固件过短_报损坏()
        {
            var shortFw = Path.Combine(_dir, "short.fw");
            File.WriteAllBytes(shortFw, new byte[] { 1, 2, 3 });   // 3B < 20B
            var ip = StartFakeDevice("1.0.0", "{}");
            var result = Exec(shortFw, ip);
            Assert.False(result.Success);
            Assert.Equal("INVALID_PARAM", result.ErrorCode);
            Assert.Contains("过短", result.ErrorMessage);
        }

        [Fact]
        public void 版本一致_返回VERSION_SAME()
        {
            var fw = MakeFw("1.1.0");
            var ip = StartFakeDevice("1.1.0", "{}");
            var result = Exec(fw, ip);
            Assert.False(result.Success);
            Assert.Equal("VERSION_SAME", result.ErrorCode);
            Assert.Contains("无需升级", result.ErrorMessage);
        }

        [Fact]
        public void 固件旧于设备_拒绝()
        {
            var fw = MakeFw("1.0.0");
            var ip = StartFakeDevice("1.1.0", "{}");
            var result = Exec(fw, ip);
            Assert.False(result.Success);
            Assert.Equal("VERSION_OLDER", result.ErrorCode);
            Assert.Contains("旧于设备当前", result.ErrorMessage);
        }

        [Fact]
        public async Task 固件新于设备_端到端上传成功()
        {
            var fw = MakeFw("1.2.0");
            var ip = StartFakeDevice("1.1.0", "{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"OTA 完成\",\"stage\":\"Finish\"}");
            var result = Exec(fw, ip);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(result.Data);   // 成功返回 Data（匿名对象：package/version/message）
        }

        [Fact]
        public void 魔数非法_拒绝()
        {
            var bad = Path.Combine(_dir, "bad.fw");
            File.WriteAllBytes(bad, Encoding.ASCII.GetBytes("NOTFW" + new string(' ', 30)));
            var ip = StartFakeDevice("1.0.0", "{}");
            var result = Exec(bad, ip);
            Assert.False(result.Success);
            Assert.Equal("INVALID_PARAM", result.ErrorCode);
            Assert.Contains("魔数", result.ErrorMessage);
        }

        [Fact]
        public void 固件文件不存在_拒绝()
        {
            var ip = StartFakeDevice("1.0.0", "{}");
            var result = Exec(Path.Combine(_dir, "不存在.fw"), ip);
            Assert.False(result.Success);
            Assert.Equal("FILE_NOT_FOUND", result.ErrorCode);
        }
    }
}
