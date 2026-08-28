using System.Net;
using System.Text;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-2：设备连接服务（ConnectionSession/DeviceConnectionService）与命令层门禁测试（2026-08-30）。
    /// stub 模式模拟成功连接（K-8b FW 端点前 CLI/测试用）。
    /// 注：DeviceConnectionService 为 static 单例（UseStub/Session 跨测试共享）——与 DeployGateTests 同 collection 强制串行防竞争。
    /// </summary>
    [Collection("设备连接")]
    public class DeviceConnectionTests : IDisposable
    {
        private readonly bool _prevStub = DeviceConnectionService.UseStub;

        public DeviceConnectionTests()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = true;
        }

        public void Dispose()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = _prevStub;
        }

        private static CommandService NewService()
            => new(new HMIProject { Name = "测试工程" });

        [Fact]
        public void connect_stub成功_建立会话_IsConnected联动()
        {
            var svc = NewService();
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI" });
            Assert.True(result.Success);
            Assert.True(svc.IsConnected);
            Assert.NotNull(DeviceConnectionService.Session);
            Assert.Equal("192.168.1.146", DeviceConnectionService.Session!.Ip);
        }

        [Fact]
        public void connect_stub带尺寸_会话记录尺寸与固件版本()
        {
            var svc = NewService();
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI", ["size_inch"] = "7寸" });
            Assert.True(result.Success);
            Assert.Equal("7寸", DeviceConnectionService.Session!.SizeInch);
            Assert.NotNull(DeviceConnectionService.Session!.FirmwareVersion);
        }

        [Fact]
        public void disconnect_幂等_会话清空()
        {
            var svc = NewService();
            svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI" });
            Assert.NotNull(DeviceConnectionService.Session);

            var r1 = svc.Execute("disconnect", new Dictionary<string, object?>());
            Assert.True(r1.Success);
            Assert.Null(DeviceConnectionService.Session);
            Assert.False(svc.IsConnected);

            var r2 = svc.Execute("disconnect", new Dictionary<string, object?>());   // 幂等
            Assert.True(r2.Success);
        }

        [Fact]
        public void 未连接执行需连接命令_门禁返回CONNECTION_REQUIRED()
        {
            var svc = NewService();
            var result = svc.Execute("deploy_project", new Dictionary<string, object?> { ["device_ip"] = "192.168.1.146" });
            Assert.False(result.Success);
            Assert.Equal("CONNECTION_REQUIRED", result.ErrorCode);
        }

        [Fact]
        public void 连接后执行需连接命令_通过门禁()
        {
            var svc = NewService();
            svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI" });
            var result = svc.Execute("deploy_project", new Dictionary<string, object?> { ["device_ip"] = "192.168.1.146" });
            // 通过门禁进入 handler（handler 仍为骨架 stub——K-3 转真实），非 CONNECTION_REQUIRED 即门禁通过
            Assert.NotEqual("CONNECTION_REQUIRED", result.ErrorCode);
        }

        [Fact]
        public void 非stub模式连不通_返回CONNECTION_FAILED_无会话()
        {
            DeviceConnectionService.UseStub = false;
            var svc = NewService();
            // 127.0.0.1 无监听端口 → 立即 refused（确定性，无网络依赖）
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "127.0.0.1", ["model"] = "NavigatorHMI" });
            Assert.False(result.Success);
            Assert.Equal("CONNECTION_FAILED", result.ErrorCode);
            Assert.Null(DeviceConnectionService.Session);
        }

        [Fact]
        public void 已连接后重连失败_自动断开旧会话()
        {
            // 设计 P0 ①：连接测试失败自动断开——旧会话不得残留（状态栏误判「已连接旧设备」）
            var svc = NewService();
            svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI" });
            Assert.NotNull(DeviceConnectionService.Session);

            DeviceConnectionService.UseStub = false;
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "127.0.0.1", ["model"] = "NavigatorHMI" });
            Assert.False(result.Success);
            Assert.Null(DeviceConnectionService.Session);   // 失败自动断开旧会话
            Assert.False(svc.IsConnected);
        }

        // ── 本地假设备（HttpListener 127.0.0.1 随机端口）——真实路径四分支确定性覆盖 ──
        private static (HttpListener listener, string ip, int port) StartFakeDevice(string body)
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
                            var ctx = await listener.GetContextAsync();
                            var buf = Encoding.UTF8.GetBytes(body);
                            ctx.Response.ContentType = "application/json";
                            ctx.Response.ContentLength64 = buf.Length;
                            await ctx.Response.OutputStream.WriteAsync(buf);
                            ctx.Response.Close();
                        }
                        finally { listener.Stop(); }
                    });
                    return (listener, "127.0.0.1", port);
                }
                catch (HttpListenerException) { /* 端口占用，重试 */ }
            }
            throw new InvalidOperationException("无法启动假设备监听端口");
        }

        [Fact]
        public void 本地假设备_型号尺寸匹配_连接成功()
        {
            DeviceConnectionService.UseStub = false;
            var (_, ip, port) = StartFakeDevice("{\"model\":\"NavigatorHMI-7\",\"sizeInch\":\"7寸\",\"version\":\"1.0.0\"}");
            var svc = NewService();
            // ip 带端口（127.0.0.1:<port>）——http://{ip}/api/device/info 天然支持 host:port；型号须在 device-profile 已知
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = $"{ip}:{port}", ["model"] = "NavigatorHMI-7", ["size_inch"] = "7寸" });
            Assert.True(result.Success);
            Assert.Equal("NavigatorHMI-7", DeviceConnectionService.Session!.Model);
            Assert.Equal("7寸", DeviceConnectionService.Session!.SizeInch);
            Assert.Equal("1.0.0", DeviceConnectionService.Session!.FirmwareVersion);
        }

        [Fact]
        public void 本地假设备_型号不匹配_DEVICE_MISMATCH_无会话()
        {
            DeviceConnectionService.UseStub = false;
            var (_, ip, port) = StartFakeDevice("{\"model\":\"OtherModel\",\"sizeInch\":\"7寸\"}");
            var svc = NewService();
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = $"{ip}:{port}", ["model"] = "NavigatorHMI-7" });
            Assert.False(result.Success);
            Assert.Equal("DEVICE_MISMATCH", result.ErrorCode);
            Assert.Null(DeviceConnectionService.Session);
        }

        [Fact]
        public void 本地假设备_尺寸不匹配_DEVICE_MISMATCH()
        {
            DeviceConnectionService.UseStub = false;
            var (_, ip, port) = StartFakeDevice("{\"model\":\"NavigatorHMI-7\",\"sizeInch\":\"4寸\"}");
            var svc = NewService();
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = $"{ip}:{port}", ["model"] = "NavigatorHMI-7", ["size_inch"] = "7寸" });
            Assert.False(result.Success);
            Assert.Equal("DEVICE_MISMATCH", result.ErrorCode);
        }

        [Fact]
        public void 本地假设备_未知型号_DEVICE_MISMATCH_保守降级()
        {
            // K-7：期望型号未配置描述文件 → 未知型号保守降级（仅连接测试，不允许下载）
            DeviceConnectionService.UseStub = false;
            var (_, ip, port) = StartFakeDevice("{\"model\":\"NavigatorHMI-7\",\"sizeInch\":\"7寸\"}");
            var svc = NewService();
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = $"{ip}:{port}", ["model"] = "UnknownModel" });
            Assert.False(result.Success);
            Assert.Equal("DEVICE_MISMATCH", result.ErrorCode);
            Assert.Contains("未知型号", result.ErrorMessage);
            Assert.Null(DeviceConnectionService.Session);
        }

        [Fact]
        public void 本地假设备_畸形JSON_CONNECTION_FAILED()
        {
            DeviceConnectionService.UseStub = false;
            var (_, ip, port) = StartFakeDevice("这不是JSON");
            var svc = NewService();
            var result = svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = $"{ip}:{port}", ["model"] = "NavigatorHMI-7" });
            Assert.False(result.Success);
            Assert.Equal("CONNECTION_FAILED", result.ErrorCode);
            Assert.Null(DeviceConnectionService.Session);
        }

        [Fact]
        public void SessionChanged事件_连接与断开均触发()
        {
            int count = 0;
            void OnChanged(ConnectionSession? s) => count++;
            DeviceConnectionService.SessionChanged += OnChanged;
            try
            {
                var svc = NewService();
                svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI" });
                svc.Execute("disconnect", new Dictionary<string, object?>());
                Assert.Equal(2, count);
            }
            finally
            {
                DeviceConnectionService.SessionChanged -= OnChanged;
            }
        }
    }
}
