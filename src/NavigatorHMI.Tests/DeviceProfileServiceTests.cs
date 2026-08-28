using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-7：device-profile 能力文件测试（内置默认/外部文件覆盖/未知型号/能力查询）（2026-08-30）。
    /// 注：DeviceProfileService 为 static 单例（Directory/Profiles 全局共享）——与 DeviceConnectionTests/DeployGateTests
    /// 同 collection 强制串行防跨类竞争。
    /// </summary>
    [Collection("设备连接")]
    public class DeviceProfileServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _prevDir;

        public DeviceProfileServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_profile_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _prevDir = DeviceProfileService.Directory;
            DeviceProfileService.Initialize(_dir);
        }

        public void Dispose()
        {
            DeviceProfileService.Initialize(_prevDir);
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void 内置默认_含7寸4寸_无Test_HMI()
        {
            var models = DeviceProfileService.Profiles.Select(p => p.Model).ToList();
            Assert.Contains("NavigatorHMI-7", models);
            Assert.Contains("NavigatorHMI-4", models);
            Assert.DoesNotContain(models, m => m.Contains("Test"));
            Assert.Equal("7寸", DeviceProfileService.GetByModel("NavigatorHMI-7")!.SizeInch);
            Assert.Equal(1024, DeviceProfileService.GetByModel("NavigatorHMI-7")!.Width);
            Assert.Equal(600, DeviceProfileService.GetByModel("NavigatorHMI-7")!.Height);
        }

        [Fact]
        public void 未知型号_IsKnown为false_查询为null()
        {
            Assert.False(DeviceProfileService.IsKnown("NoSuchModel"));
            Assert.Null(DeviceProfileService.GetByModel("NoSuchModel"));
        }

        [Fact]
        public void 外部文件_新增型号_Reload后可用()
        {
            File.WriteAllText(Path.Combine(_dir, "custom.json"),
                "{\"model\":\"NavigatorHMI-10\",\"sizeInch\":\"10寸\",\"width\":1280,\"height\":800,\"interfaces\":[\"eth\"],\"capability\":{\"compileVersion\":\"V1\"}}");
            DeviceProfileService.Reload();

            Assert.True(DeviceProfileService.IsKnown("NavigatorHMI-10"));
            Assert.Equal("10寸", DeviceProfileService.GetByModel("NavigatorHMI-10")!.SizeInch);
        }

        [Fact]
        public void 外部文件_同名覆盖内置()
        {
            File.WriteAllText(Path.Combine(_dir, "override.json"),
                "{\"model\":\"NavigatorHMI-7\",\"sizeInch\":\"7寸\",\"width\":800,\"height\":480,\"interfaces\":[\"eth\"],\"capability\":{}}");
            DeviceProfileService.Reload();

            // 外部同名覆盖内置（宽高被外部值替换）
            Assert.Equal(800, DeviceProfileService.GetByModel("NavigatorHMI-7")!.Width);
        }

        [Fact]
        public void 外部文件_损坏JSON_跳过不崩溃()
        {
            File.WriteAllText(Path.Combine(_dir, "bad.json"), "这不是JSON{{{");
            DeviceProfileService.Reload();   // 不应抛异常

            Assert.Contains("NavigatorHMI-7", DeviceProfileService.Profiles.Select(p => p.Model));
        }

        [Fact]
        public void 能力字段_编译版本与上传上限()
        {
            var cap = DeviceProfileService.GetByModel("NavigatorHMI-7")!.Capability;
            Assert.Equal("V1", cap.CompileVersion);
            Assert.True(cap.Deploy);
            Assert.Equal(64, cap.UploadSizeLimitMB);
        }

        [Fact]
        public async Task 文件变更_热重载即时生效()
        {
            // FileSystemWatcher 异步触发 → 轮询等待（验收「profile 文件变更热更新即时生效」）
            File.WriteAllText(Path.Combine(_dir, "hot.json"),
                "{\"model\":\"NavigatorHMI-12\",\"sizeInch\":\"12寸\",\"width\":1280,\"height\":800,\"interfaces\":[\"eth\"],\"capability\":{}}");
            for (int i = 0; i < 40; i++)
            {
                if (DeviceProfileService.IsKnown("NavigatorHMI-12")) return;   // 通过
                await Task.Delay(50);
            }
            Assert.Fail("热重载未在 2s 内生效");
        }

        [Fact]
        public void 语义null_能力与画布限制_兜底不崩溃()
        {
            // 🔴-3 回归：外部 JSON "capability":null / "canvasLimit":null 不崩消费端
            File.WriteAllText(Path.Combine(_dir, "null-cap.json"),
                "{\"model\":\"NavigatorHMI-NULL\",\"sizeInch\":\"7寸\",\"capability\":null,\"canvasLimit\":null}");
            DeviceProfileService.Reload();

            var p = DeviceProfileService.GetByModel("NavigatorHMI-NULL");
            Assert.NotNull(p);
            Assert.NotNull(p!.Capability);   // 兜底为默认能力
            Assert.NotNull(p.CanvasLimit);
            Assert.Equal("V1", p.Capability.CompileVersion);
        }
    }
}
