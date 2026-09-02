using System;
using System.IO;
using System.Linq;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.Core.Services;
using NavigatorHMI.ViewModels;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// O 轮批 C C-4/C-6 设备升级页 VM 测试（审查 🟡3 补）——固件库门禁 + 断开复位 + 手动选包：
    /// DisconnectCommand CanExecute 跟随 IsConnected；CanUpgrade=IsConnected&amp;&amp;SelectedFirmware!=null；
    /// 连接成功刷新固件库（按会话尺寸）+ 自动选最新；断开清固件库复位灰显；SetManualFirmware 置顶。
    /// 注：DeviceConnectionService static 单例——与 DeviceConnectionTests 同 collection 强制串行。
    /// </summary>
    [Collection("设备连接")]
    public class DevicePanelFirmwareGateTests : IDisposable
    {
        private readonly string _fwDir;
        private readonly string _storePath;
        private readonly string _prevFwRoot;
        private readonly string _prevStorePath;
        private readonly bool _prevStub = DeviceConnectionService.UseStub;

        public DevicePanelFirmwareGateTests()
        {
            _prevFwRoot = FirmwareFolderService.DefaultRoot;
            _prevStorePath = DevicePanelViewModel.FirmwareDirStorePath;
            _fwDir = Path.Combine(Path.GetTempPath(), "navihmi_fwgate_" + Guid.NewGuid().ToString("N")[..6]);
            _storePath = Path.Combine(Path.GetTempPath(), "navihmi_fwdir_" + Guid.NewGuid().ToString("N")[..6] + ".txt");
            FirmwareFolderService.Initialize(_fwDir);
            DevicePanelViewModel.FirmwareDirStorePath = _storePath;   // 持久化隔离到临时文件（防 %APPDATA% 真实配置干扰）
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = true;
        }

        public void Dispose()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = _prevStub;
            FirmwareFolderService.Initialize(null);
            DevicePanelViewModel.FirmwareDirStorePath = _prevStorePath;
            try { Directory.Delete(_fwDir, true); } catch { }
            try { File.Delete(_storePath); } catch { }
        }

        /// <summary>平铺建固件（2026-09 标准：文件名带尺寸段 NavigatorHMI_&lt;inch&gt;_v&lt;ver&gt;.fw；sizeInch=null → 旧命名）。</summary>
        private static string MakeFw(string dir, string version, string? sizeInch = null)
        {
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrEmpty(sizeInch)
                ? $"NavigatorHMI_v{version}.fw"
                : $"NavigatorHMI_{NavigatorHMI.Core.Services.FirmwareFolderService.SizeToInchTag(sizeInch)}_v{version}.fw";
            var p = Path.Combine(dir, name);
            File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
            return p;
        }

        /// <summary>mock：固件相关命令返回成功（其余失败）；无真实 HTTP。</summary>
        private class FirmwareOkCommandService : ICommandService
        {
            public event System.Action<string, System.Collections.Generic.Dictionary<string, object?>, CommandResult>? CommandExecuted { add { } remove { } }
            public string? CurrentScreenName { get; set; } = "";
            public CommandResult Execute(string commandName, System.Collections.Generic.Dictionary<string, object?> parameters)
                => commandName is "deploy_firmware" or "disconnect"
                    ? CommandResult.Ok(new { })
                    : CommandResult.Fail("MOCK", "mock");
            public System.Collections.Generic.List<CommandDefinition> GetAvailableCommands() => new();
            public System.Collections.Generic.List<string> GetScreenNames() => new();
            public System.Collections.Generic.List<string> GetGroupNames() => new();
            public System.Collections.Generic.List<(string Name, string Type)> GetCurrentScreenWidgets() => new();
        }

        [Fact]
        public void 未连接_升级门禁灰显_断开按钮灰显()
        {
            var vm = new DevicePanelViewModel(new FirmwareOkCommandService());
            try
            {
                Assert.False(vm.CanUpgrade, "未连接时不可升级");
                Assert.False(vm.UpgradeFirmwareCommand.CanExecute(null));
                Assert.False(vm.DisconnectCommand.CanExecute(null), "未连接时断开按钮应灰显");
            }
            finally { vm.Dispose(); }
        }

        [Fact]
        public void 连接成功_按会话尺寸刷新固件库_自动选最新_断开复位()
        {
            // stub 会话型号 NavigatorHMI-7 → profile.SizeInch "7寸" → 平铺根下 7inch 固件（2026-09 标准命名）
            var newest = MakeFw(_fwDir, "1.10.0", "7寸");
            MakeFw(_fwDir, "1.9.0", "7寸");
            var r = DeviceConnectionService.TestConnection("192.168.1.146", "NavigatorHMI-7");
            Assert.True(r.Success);

            var vm = new DevicePanelViewModel(new FirmwareOkCommandService());
            try
            {
                Assert.True(vm.IsConnected);
                // 连接成功 → 固件库已按会话尺寸刷新（VM 构造即 OnSessionChanged → 无会话；需手动刷新模拟连接后行为——
                // 实际 GUI 连接路径 ExecuteConnectAsync 内 await RefreshFirmwareForSessionAsync，此处经公开命令触发）
                vm.RefreshFirmwareCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.True(vm.FirmwareFiles.Count >= 2, $"固件库应有 2 个 .fw，实际 {vm.FirmwareFiles.Count}");
                Assert.NotNull(vm.SelectedFirmware);
                Assert.Equal(newest, vm.SelectedFirmware!.Path);   // 语义最新 v1.10.0
                Assert.True(vm.CanUpgrade, "已连接 + 已选固件 → 可升级");
                Assert.True(vm.UpgradeFirmwareCommand.CanExecute(null));
                Assert.True(vm.DisconnectCommand.CanExecute(null), "已连接时断开按钮可点");

                // 断开 → 门禁复位（CanUpgrade 灰显）——固件库**保留**（Check 修复 2026-09：本地目录浏览与连接无关，
                // 断开不清列表；未连接不可升级由 CanUpgrade=IsConnected 保证）
                DeviceConnectionService.Disconnect();
                Assert.False(vm.IsConnected);
                Assert.False(vm.CanUpgrade, "断开后不可升级");
                Assert.False(vm.UpgradeFirmwareCommand.CanExecute(null));
            }
            finally { vm.Dispose(); }
        }

        [Fact]
        public void 手动选包_入列置顶_选中()
        {
            var outside = MakeFw(_fwDir, "9.9.9");   // 手动/目录外（旧命名无尺寸段——不自动出现在 7inch 列表，手动置顶）
            var r = DeviceConnectionService.TestConnection("192.168.1.146", "NavigatorHMI-7");
            Assert.True(r.Success);

            var vm = new DevicePanelViewModel(new FirmwareOkCommandService());
            try
            {
                vm.SetManualFirmware(outside);
                Assert.NotNull(vm.SelectedFirmware);
                Assert.Equal(outside, vm.SelectedFirmware!.Path);
                Assert.Contains(vm.FirmwareFiles, f => f.Path == outside);
                Assert.True(vm.CanUpgrade);
            }
            finally { vm.Dispose(); }
        }

        [Fact]
        public void 编辑固件库目录_切换根并持久化()
        {
            // Check 修复（2026-09）：固件库目录可编辑——setter 更新 FirmwareFolderService 根 + 持久化到隔离 store
            var newRoot = Path.Combine(Path.GetTempPath(), "navihmi_fwroot_" + Guid.NewGuid().ToString("N")[..6]);
            MakeFw(newRoot, "2.0.0", "4寸");   // 新根平铺 4inch 固件（4寸 命名——切根后 4寸 尺寸可扫到此固件）

            var vm = new DevicePanelViewModel(new FirmwareOkCommandService());
            try
            {
                vm.FirmwareFolderText = newRoot;
                Assert.Equal(newRoot, vm.FirmwareFolderText);
                Assert.Equal(newRoot, FirmwareFolderService.DefaultRoot);   // 服务根已切换
                // 持久化已写（setter 内 PersistFirmwareFolder）
                Assert.True(File.Exists(_storePath));
                Assert.Equal(newRoot, File.ReadAllText(_storePath).Trim());

                // 新 VM 构造读持久化 → 恢复上次目录（跨会话记住）
                var vm2 = new DevicePanelViewModel(new FirmwareOkCommandService());
                try
                {
                    Assert.Equal(newRoot, vm2.FirmwareFolderText);
                    Assert.Equal(newRoot, FirmwareFolderService.DefaultRoot);
                }
                finally { vm2.Dispose(); }
            }
            finally { vm.Dispose(); }

            // 空值 → 还原默认（运行目录 firmware/，非 newRoot）+ store 清空 + 文本框回填默认根显示（🟡1/🟡4 真实验证）
            var vm3 = new DevicePanelViewModel(new FirmwareOkCommandService());
            try
            {
                var defaultRoot = FirmwareFolderService.DefaultRoot;   // 当前（测试注入 _fwDir）
                vm3.FirmwareFolderText = "";
                // setter 空值分支：Initialize(null) → 默认根（运行目录 firmware/，≠ _fwDir ≠ newRoot）
                Assert.NotEqual(newRoot, FirmwareFolderService.DefaultRoot);
                Assert.NotEqual(defaultRoot, FirmwareFolderService.DefaultRoot);   // 已脱离测试注入根（还原默认）
                Assert.False(FirmwareFolderService.DefaultRoot.StartsWith(_fwDir, StringComparison.OrdinalIgnoreCase));
                // 文本框回填实际默认根（不空白——🟡4）
                Assert.Equal(FirmwareFolderService.DefaultRoot, vm3.FirmwareFolderText);
                // store 清空（持久化空 → 下次启动回默认）
                Assert.True(File.Exists(_storePath));
                Assert.Equal("", File.ReadAllText(_storePath).Trim());

                // 新 VM 构造（store 空）→ 不 Initialize（保持当前默认根）
                var vm4 = new DevicePanelViewModel(new FirmwareOkCommandService());
                try
                {
                    Assert.Equal(FirmwareFolderService.DefaultRoot, vm4.FirmwareFolderText);
                }
                finally { vm4.Dispose(); }
            }
            finally { vm3.Dispose(); }
            try { Directory.Delete(newRoot, true); } catch { }
        }
    }
}
