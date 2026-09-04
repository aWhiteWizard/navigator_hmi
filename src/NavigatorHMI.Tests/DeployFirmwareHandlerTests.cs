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

        /// <summary>假设备：/api/transfer → deployResponse；/api/device/info → {version: devVersion[, firmware_ts: devTs]}。返回 ip:port。</summary>
        private string StartFakeDevice(string devVersion, string deployResponse, string? devTs = null)
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
                                    // 2026-09-04：firmware_ts 字段（旧设备无 → 缺省 "0"——GetDeviceInfoAsync 兜底）
                                    "/api/device/info" => devTs == null
                                        ? $"{{\"version\":\"{devVersion}\"}}"
                                        : $"{{\"version\":\"{devVersion}\",\"firmware_ts\":\"{devTs}\"}}",
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
        public void 同版本_设备带v前缀_触发跳过()
        {
            // B-4 修复回归：真实 FW /api/device/info version 返回 "vX.Y.Z"（带 v 前缀）——
            // 不归一 v 则设备版本首段 "v1" 解析 0 → 同版本也被判"包新"→ 重复升级（VERSION_SAME 永不触发）
            var fwSame = MakeFw("1.1.1");
            var ip = StartFakeDevice("v1.1.1", "{}");   // 设备 version 带 v（真实 FW 格式）
            var r = Exec(fwSame, ip);
            Assert.Equal("VERSION_SAME", r.ErrorCode);   // 同版本（忽略 v 前缀）→ 跳过

            var fwOldV = MakeFw("1.1.0");
            var ip2 = StartFakeDevice("v1.1.1", "{}");
            var r2 = Exec(fwOldV, ip2);
            Assert.Equal("VERSION_OLDER", r2.ErrorCode);   // 设备 v1.1.1 归一后 1.1.0 旧 → 拒绝
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
            // ≥完整 header 128B（校验收紧后 <128B 先报过短——魔数校验需先过长度关）
            File.WriteAllBytes(bad, Encoding.ASCII.GetBytes("NOTFW").Concat(Enumerable.Repeat((byte)0x20, 128)).ToArray());
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

        // ── 2026-09-04 调试 OTA：调试包（NavigatorHMI_v1.1.0_<inch>_<14位时刻>.fw，版本恒 v1.1.0）──
        // 版本前置检查改按打包时刻先后（用户裁决「按打包时间」）：包 header ts > 设备 firmware_ts → 放行；否则拒。
        // 旧设备无 firmware_ts（响应缺字段 → PC 兜底 "0"）→ 任意非 0 时刻包放行（首次装载 1.1.0 场景——语义 1.1.0 < 1.1.4 也不拦）。

        /// <summary>生成调试命名固件（header version 1.1.0 + Build 时刻 ts；文件名带 14 位时刻尾）。</summary>
        private string MakeDebugFw(string nameTs = "20260904210242")
        {
            var std = MakeFw("1.1.0");
            var dbg = Path.Combine(_dir, $"NavigatorHMI_v1.1.0_7inch_{nameTs}.fw");
            File.Move(std, dbg);
            return dbg;
        }

        [Fact]
        public void 调试包_设备旧固件无ts_放行_语义更旧不拦()
        {
            // 设备 v1.1.4（旧固件无 firmware_ts）装 v1.1.0 调试包——调试分支只看打包时刻：包 ts > 0 → 放行
            var fw = MakeDebugFw();
            var ip = StartFakeDevice("v1.1.4", "{\"code\":\"SUCCESSFUL_REBOOT\"}");
            var r = Exec(fw, ip);
            Assert.True(r.Success, r.ErrorMessage);
        }

        [Fact]
        public void 调试包_设备ts更新_拒绝VERSION_SAME()
        {
            // 设备已装更晚时刻的调试固件（firmware_ts 未来大值）→ 包 ts 更旧 → 拒（时刻倒退防呆）
            var fw = MakeDebugFw();
            var ip = StartFakeDevice("v1.1.0", "{}", devTs: "9999999999");
            var r = Exec(fw, ip);
            Assert.False(r.Success);
            Assert.Equal("VERSION_SAME", r.ErrorCode);
        }

        [Fact]
        public void 调试包_设备ts旧_同版放行覆盖()
        {
            // 设备 firmware_ts=1（早期调试包）→ 新包 ts 更新 → 同版本 v1.1.0 放行覆盖（每次编完都能 OTA 的核心场景）
            var fw = MakeDebugFw();
            var ip = StartFakeDevice("v1.1.0", "{\"code\":\"SUCCESSFUL_REBOOT\"}", devTs: "1");
            var r = Exec(fw, ip);
            Assert.True(r.Success, r.ErrorMessage);
        }

        [Fact]
        public void 非调试命名_同语义版本_仍按VERSION_SAME拒()
        {
            // 回归：标准命名（NavigatorHMI_v1.1.0.fw）不走调试分支——同版仍拒（防呆保留）
            var fw = MakeFw("1.1.0");
            var ip = StartFakeDevice("v1.1.0", "{}");
            var r = Exec(fw, ip);
            Assert.False(r.Success);
            Assert.Equal("VERSION_SAME", r.ErrorCode);
        }

        [Fact]
        public void 新标准命名_同语义版本_仍按VERSION_SAME拒()
        {
            // 🔵-3 回归：NavigatorHMI_7inch_v1.1.0.fw（新标准正式命名——尺寸在版本前）尾段 "v1.1.0" 非 14 位数字 → 非调试 → 同版拒
            var app = Path.Combine(_dir, "app2.bin");
            File.WriteAllBytes(app, new byte[] { 1, 2, 3 });
            var fw = FwPackageBuilder.Build("1.1.0", "7寸", _dir,
                new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app }).Path;
            var ip = StartFakeDevice("v1.1.0", "{}");
            var r = Exec(fw, ip);
            Assert.False(r.Success);
            Assert.Equal("VERSION_SAME", r.ErrorCode);
        }

        [Fact]
        public void 调试包_设备ts非数字_归0放行()
        {
            // 防御：设备 firmware_ts 非数字（异常值）→ long.TryParse 归 0 → 任意非 0 时刻包放行
            var fw = MakeDebugFw();
            var ip = StartFakeDevice("v1.1.0", "{\"code\":\"SUCCESSFUL_REBOOT\"}", devTs: "abc");
            var r = Exec(fw, ip);
            Assert.True(r.Success, r.ErrorMessage);
        }
    }
}
