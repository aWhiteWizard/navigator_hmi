using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-3c：deploy 前置自动编译门禁测试（编译失败拒绝传输；成功打包返回容器路径）（2026-08-30）。
    /// 注：DeviceConnectionService 为 static 单例——与 DeviceConnectionTests 同 collection 强制串行防 UseStub 竞争。
    /// </summary>
    [Collection("设备连接")]
    public class DeployGateTests : IDisposable
    {
        private readonly string _dir;
        private readonly HMIProject _project;
        private readonly CommandService _svc;

        public DeployGateTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_deploygate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _project = new HMIProject
            {
                Name = "门禁测试",
                ProjectFilePath = Path.Combine(_dir, "gate-test.hmiproj")
            };
            _project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            _svc = new CommandService(_project);
            DeviceConnectionService.UseStub = true;   // 门禁：连接状态用 stub 建立
            DeviceConnectionService.Disconnect();
            _svc.Execute("connect", new Dictionary<string, object?> { ["ip"] = "192.168.1.146", ["model"] = "NavigatorHMI" });
        }

        public void Dispose()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = false;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void 编译失败_拒绝传输_COMPILE_FAILED()
        {
            _project.Screens[0].Widgets.Add(new ButtonWidget { ObjectName = "b1", BoundTag = "悬空变量" });

            var result = _svc.Execute("deploy_project", new Dictionary<string, object?> { ["device_ip"] = "192.168.1.146" });
            Assert.False(result.Success);
            Assert.Equal("COMPILE_FAILED", result.ErrorCode);
        }

        [Fact]
        public void 编译成功_打包返回容器路径()
        {
            _project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });

            var result = _svc.Execute("deploy_project", new Dictionary<string, object?> { ["device_ip"] = "192.168.1.146" });
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            var package = result.Data!.GetType().GetProperty("package")?.GetValue(result.Data) as string;
            Assert.NotNull(package);
            Assert.True(File.Exists(package), $"部署容器应存在: {package}");
            Assert.EndsWith(".deploy.zip", package);
        }
    }
}
