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

        /// <summary>假设备：按路径返回预置响应（transfer→deployResponse / progress→progressSequence 依次 / 其余 OK）。
        /// T-1a：deviceInfoResponse 非空时 /api/device/info 返回该 JSON（磁盘预检测试用——null 走默认 OK 无 disk_free）。</summary>
        private string StartFakeDevice(string deployResponse, out string ipWithPort, string[]? progressSequence = null, string? deviceInfoResponse = null)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var port = Random.Shared.Next(25000, 50000);
                try
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    listener.Start();
                    var progressIdx = 0;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (listener.IsListening)
                            {
                                var ctx = await listener.GetContextAsync();
                                var path = ctx.Request.Url?.AbsolutePath ?? "";
                                string body;
                                if (path == "/api/transfer") body = deployResponse;
                                else if (path == "/api/progress" && progressSequence != null)
                                {
                                    // D-B4：按序返回设备进度（越界返回最后一个——轮询直到 100 后由 PC 侧终止）
                                    var idx = Math.Min(progressIdx, progressSequence.Length - 1);
                                    body = progressSequence[idx];
                                    progressIdx++;
                                }
                                else if (path == "/api/device/info" && deviceInfoResponse != null) body = deviceInfoResponse;
                                else if (path == "/api/version") body = "{\"version\":\"1.0\"}";
                                else body = "{\"code\":\"OK\"}";
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
        public void deploy_project_视频超防呆上限_编译失败拒绝传输()
        {
            // Q-4/T-1a（2026-09-04 用户裁决 + 2026-09-05 单文件护栏放宽为防呆上限 1GB）：超防呆上限视频在
            // **编译阶段**拦截——deploy_project 前置编译门禁报 COMPILE_FAILED（>64MB 不再报——部署动态 cap 把关）
            var project = new HMIProject { Name = "超限视频", ProjectFilePath = Path.Combine(_dir, "big.hmiproj") };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            using (var fs = new FileStream(Path.Combine(_dir, "big.mp4"), FileMode.Create, FileAccess.Write))
                fs.SetLength(1024L * 1024 * 1024 + 1);   // 稀疏扩展（不实际写 1GB）
            project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = "big.mp4" });

            var svc = new CommandService(project);
            DeviceConnectionService.UseStub = true;
            DeviceConnectionService.Disconnect();
            svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI-7" });

            var result = svc.Execute("deploy_project", new Dictionary<string, object?> { ["device_ip"] = "192.168.1.146" });
            Assert.False(result.Success);
            Assert.Equal("COMPILE_FAILED", result.ErrorCode);
        }

        [Fact]
        public async Task Deploy_进度回调_收到设备进度序列()
        {
            // D-B4：假设备按序返回进度 5→45→100（设备端接收→解压→完成），PC 轮询回调应依次收到
            var zip = await MakeProject();
            var progressSequence = new[]
            {
                "{\"progress\":5,\"stage\":\"接收完成，开始安装\",\"active\":true}",
                "{\"progress\":45,\"stage\":\"解压安装包…\",\"active\":true}",
                "{\"progress\":100,\"stage\":\"安装完成\",\"active\":false}",
            };
            StartFakeDevice("{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"部署成功\",\"stage\":\"Finish\"}", out var ip, progressSequence);

            var received = new List<int>();
            var result = await HttpDownloadClient.DeployAsync(ip, zip, (pct, stage) => { received.Add(pct); return true; });

            Assert.True(result.Success, result.Message);
            Assert.Contains(5, received);
            Assert.Contains(45, received);
            Assert.Contains(100, received);
        }

        [Fact]
        public async Task Deploy_进度回调_返回false提前终止()
        {
            // D-B4：回调返回 false → 轮询提前终止（不阻塞整体部署成功）
            var zip = await MakeProject();
            var progressSequence = new[]
            {
                "{\"progress\":5,\"stage\":\"接收完成\",\"active\":true}",
                "{\"progress\":45,\"stage\":\"解压安装包…\",\"active\":true}",
                "{\"progress\":100,\"stage\":\"安装完成\",\"active\":false}",
            };
            StartFakeDevice("{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"部署成功\"}", out var ip, progressSequence);

            var calls = 0;
            var result = await HttpDownloadClient.DeployAsync(ip, zip, (pct, stage) => { calls++; return calls < 2; });

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, calls);   // 第一次回调（5%）→ 返回 true；第二次（45%）→ 返回 false 终止
        }

        [Fact]
        public async Task Deploy_设备端失败_进度负值_不挂起()
        {
            // D-B4 审查修复：设备端早失败（progress=-1）→ 轮询须终止（曾见 active 后 -1），DeployAsync 限时返回失败
            var zip = await MakeProject();
            var progressSequence = new[]
            {
                "{\"progress\":5,\"stage\":\"接收完成\",\"active\":true}",
                "{\"progress\":-1,\"stage\":\"SHA256 校验失败\",\"active\":false}",
            };
            StartFakeDevice("{\"code\":\"TRANSFER_FAILED\",\"message\":\"SHA256 校验失败\",\"stage\":\"Install\"}", out var ip, progressSequence);

            var received = new List<int>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));   // 防挂起硬兜底
            var result = await HttpDownloadClient.DeployAsync(ip, zip, (pct, stage) => { received.Add(pct); return true; }, cts.Token);

            Assert.False(result.Success);
            Assert.Equal("TRANSFER_FAILED", result.Code);
            Assert.Contains(5, received);   // 曾见正进度，随后 -1 触发终止
        }

        [Fact]
        public async Task Deploy_陈旧100_仍持续轮询到新进度()
        {
            // D-B4 审查修复：上次部署残留 (100,false) 首个响应 → 不得立即当完成（防进度条假满）——
            // 需本会话见过正进度/active 才认完成；后续 5→45→100 应被正常收到
            var zip = await MakeProject();
            var progressSequence = new[]
            {
                "{\"progress\":100,\"stage\":\"安装完成\",\"active\":false}",   // 陈旧残留（首次响应）
                "{\"progress\":5,\"stage\":\"接收完成\",\"active\":true}",
                "{\"progress\":45,\"stage\":\"解压安装包…\",\"active\":true}",
                "{\"progress\":100,\"stage\":\"安装完成\",\"active\":false}",
            };
            StartFakeDevice("{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"部署成功\"}", out var ip, progressSequence);

            var received = new List<int>();
            var result = await HttpDownloadClient.DeployAsync(ip, zip, (pct, stage) => { received.Add(pct); return true; });

            Assert.True(result.Success, result.Message);
            Assert.Contains(5, received);    // 陈旧 100 未阻断后续新进度
            Assert.Contains(45, received);
            Assert.Contains(100, received);
        }

        [Fact]
        public async Task DeployProject_磁盘预检超限_拒绝传输不发transfer()
        {
            // T-1a（2026-09-05）：上传前磁盘预检（cap = 设备空闲 ×2/3）——mock disk_free=1MB → 任何 zip 超 cap
            // → TRANSFER_TOO_LARGE 且**不发 /api/transfer**（预检把关，不浪费传输）
            var zip = await MakeProject();
            var transferHit = 0;
            StartFakeDevice("{\"code\":\"SUCCESSFUL_REBOOT\",\"message\":\"不应到达\"}", out var ip,
                deviceInfoResponse: "{\"version\":\"1.1.0\",\"firmware_ts\":\"0\",\"disk_total\":13421772800,\"disk_free\":1}");
            // mock 内 transfer 命中计数（StartFakeDevice 无钩子——改监听端口响应路径为"不应到达"可间接验证：
            // 若发了 transfer，ParseResponse 见 code 非 OK 失败码 → Success=false 且 Code 非 TRANSFER_TOO_LARGE）
            _ = transferHit;

            var result = await HttpDownloadClient.DeployProjectAsync(ip, zip);

            Assert.False(result.Success);
            Assert.Equal("TRANSFER_TOO_LARGE", result.Code);   // 预检拦截（若误发 transfer 会得 SHOCK 类错误码）
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
