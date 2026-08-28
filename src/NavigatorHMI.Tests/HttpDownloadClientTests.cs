using System.Net;
using System.Text;
using System.Text.Json;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-5：HttpDownloadClient 传输客户端 + blink/vnc 命令 + deploy 真实传输测试（2026-08-30）。
    /// HttpListener 假设备模拟 FW 端点（/api/transfer、/api/vnc、/api/blink、/api/version）。
    /// </summary>
    [Collection("设备连接")]
    public class HttpDownloadClientTests : IDisposable
    {
        private HttpListener? _listener;
        private readonly string _dir;

        public HttpDownloadClientTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_http_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { _listener?.Stop(); } catch { }
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = false;
            try { Directory.Delete(_dir, true); } catch { }
        }

        /// <summary>假设备：按路径返回预置响应（transfer→deployResponse / 其余 OK）。</summary>
        private string StartFakeDevice(string deployResponse, out string ipWithPort)
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
                                    "/api/transfer" => deployResponse,
                                    "/api/version" => "{\"version\":\"1.0\"}",
                                    _ => "{\"code\":\"OK\"}",
                                };
                                var buf = Encoding.UTF8.GetBytes(body);
                                ctx.Response.ContentType = "application/json";
                                ctx.Response.ContentLength64 = buf.Length;
                                await ctx.Response.OutputStream.WriteAsync(buf);
                                ctx.Response.Close();
                            }
                        }
                        catch { /* listener 停止时结束 */ }
                    });
                    _listener = listener;
                    ipWithPort = $"127.0.0.1:{port}";
                    return listener.Prefixes.First();
                }
                catch (HttpListenerException) { }
            }
            throw new InvalidOperationException("无法启动假设备");
        }

        private async Task<string> MakeProject()
        {
            var project = new HMIProject { Name = "传输测试", ProjectFilePath = Path.Combine(_dir, "t.hmiproj") };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var compile = ProjectGenerator.Compile(project);
            Assert.False(compile.HasErrors);
            return DeploymentPackageBuilder.Build(project, compile.OutputPath!, Path.Combine(_dir, "output"));
        }

        [Fact]
        public async Task Deploy_假设备_成功返回SUCCESSFUL_REBOOT()
        {
            var zip = await MakeProject();
            StartFakeDevice("{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"部署成功，工程重载中\",\"stage\":\"Finish\"}", out var ip);
            var result = await HttpDownloadClient.DeployAsync(ip, zip);
            Assert.True(result.Success, result.Message);
            Assert.Equal("OK", result.Code);
        }

        [Fact]
        public async Task Deploy_假设备返回失败_错误码透传()
        {
            var zip = await MakeProject();
            StartFakeDevice("{\"code\":\"TRANSFER_FAILED\",\"message\":\"SHA256 校验失败\",\"stage\":\"Install\"}", out var ip);
            var result = await HttpDownloadClient.DeployAsync(ip, zip);
            Assert.False(result.Success);
            Assert.Equal("TRANSFER_FAILED", result.Code);
        }

        [Fact]
        public async Task Deploy_设备不可达_UNREACHABLE()
        {
            var zip = await MakeProject();
            var result = await HttpDownloadClient.DeployAsync("127.0.0.1:1", zip);   // 无监听 → refused
            Assert.False(result.Success);
            Assert.Equal("UNREACHABLE", result.Code);
        }

        [Fact]
        public async Task Blink与Vnc指令_假设备_OK()
        {
            StartFakeDevice("{\"code\":\"OK\"}", out var ip);
            var blink = await HttpDownloadClient.BlinkAsync(ip, true);
            Assert.True(blink.Success);
            var vnc = await HttpDownloadClient.VncAsync(ip, false);
            Assert.True(vnc.Success);
        }

        [Fact]
        public async Task GetVersion_返回工程版本()
        {
            StartFakeDevice("{}", out var ip);
            var (ok, version, err) = await HttpDownloadClient.GetVersionAsync(ip);
            Assert.True(ok, err);
            Assert.Equal("1.0", version);
        }

        [Fact]
        public async Task deploy_project命令_端到端_假设备接收()
        {
            var project = new HMIProject { Name = "门禁传输", ProjectFilePath = Path.Combine(_dir, "d.hmiproj") };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            var svc = new CommandService(project);
            DeviceConnectionService.UseStub = true;
            DeviceConnectionService.Disconnect();
            svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI-7" });

            StartFakeDevice("{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"部署成功\"}", out var ip);
            var result = svc.Execute("deploy_project", new Dictionary<string, object?> { ["device_ip"] = ip });
            Assert.True(result.Success, result.ErrorMessage);
        }

        [Fact]
        public void blink_device与vnc命令_参数校验()
        {
            var svc = new CommandService(new HMIProject { Name = "校验" });
            var bad1 = svc.Execute("blink_device", new Dictionary<string, object?> { ["ip"] = "1.2.3.4", ["enable"] = "maybe" });
            Assert.False(bad1.Success);
            Assert.Equal("INVALID_PARAM", bad1.ErrorCode);
            var bad2 = svc.Execute("vnc", new Dictionary<string, object?> { ["ip"] = "1.2.3.4" });
            Assert.False(bad2.Success);
            Assert.Equal("INVALID_PARAM", bad2.ErrorCode);
        }
    }
}
