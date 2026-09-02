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
        private readonly string _prevFwRoot;
        private readonly bool _prevStub = DeviceConnectionService.UseStub;

        public DevicePanelFirmwareGateTests()
        {
            _prevFwRoot = FirmwareFolderService.DefaultRoot;
            _fwDir = Path.Combine(Path.GetTempPath(), "navihmi_fwgate_" + Guid.NewGuid().ToString("N")[..6]);
            FirmwareFolderService.Initialize(_fwDir);
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = true;
        }

        public void Dispose()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = _prevStub;
            FirmwareFolderService.Initialize(null);
            try { Directory.Delete(_fwDir, true); } catch { }
        }

        private static string MakeFw(string dir, string version)
        {
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, $"NavigatorHMI_v{version}.fw");
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
            // stub 会话型号 NavigatorHMI-7 → profile.SizeInch "7寸" → firmware/7寸/ 子目录
            var newest = MakeFw(Path.Combine(_fwDir, "7寸"), "1.10.0");
            MakeFw(Path.Combine(_fwDir, "7寸"), "1.9.0");
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

                // 断开 → 固件库清空 + 门禁复位（须在 Dispose 前——Dispose 取消订阅后 IsConnected 不再更新）
                DeviceConnectionService.Disconnect();
                Assert.False(vm.IsConnected);
                Assert.Empty(vm.FirmwareFiles);
                Assert.Null(vm.SelectedFirmware);
                Assert.False(vm.CanUpgrade);
            }
            finally { vm.Dispose(); }
        }

        [Fact]
        public void 手动选包_入列置顶_选中()
        {
            var outside = MakeFw(_fwDir, "9.9.9");   // 目录外/手动（根目录不在 7寸 子目录 → 不自动出现）
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
    }
}
