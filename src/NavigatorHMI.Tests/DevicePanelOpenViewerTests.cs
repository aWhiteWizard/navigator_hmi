using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// M 循环 M-2：设备面板「打开 VNC 查看器」按钮门禁测试（2026-08-30）。
    /// OpenViewerCommand.CanExecute 跟随 VncOn——VNC 未启用/断开时灰显，启用后可点。
    /// 注：DeviceConnectionService 为 static 单例（UseStub/Session 跨测试共享）——与 DeviceConnectionTests 同 collection 强制串行防竞争。
    /// </summary>
    [Collection("设备连接")]
    public class DevicePanelOpenViewerTests : IDisposable
    {
        private readonly bool _prevStub = DeviceConnectionService.UseStub;

        public DevicePanelOpenViewerTests()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = true;
        }

        public void Dispose()
        {
            DeviceConnectionService.Disconnect();
            DeviceConnectionService.UseStub = _prevStub;
        }

        /// <summary>mock 命令服务：vnc 命令返回成功（其余失败）。</summary>
        private class VncOkCommandService : ICommandService
        {
            public event System.Action<string, System.Collections.Generic.Dictionary<string, object?>, CommandResult>? CommandExecuted { add { } remove { } }
            public string? CurrentScreenName { get; set; } = "";
            public CommandResult Execute(string commandName, System.Collections.Generic.Dictionary<string, object?> parameters)
                => commandName == "vnc" ? CommandResult.Ok(new { }) : CommandResult.Fail("MOCK", "mock");
            public System.Collections.Generic.List<CommandDefinition> GetAvailableCommands() => new();
            public System.Collections.Generic.List<string> GetScreenNames() => new();
            public System.Collections.Generic.List<string> GetGroupNames() => new();
            public System.Collections.Generic.List<(string Name, string Type)> GetCurrentScreenWidgets() => new();
        }

        [Fact]
        public void VNC未启用_打开查看器按钮灰显()
        {
            var vm = new DevicePanelViewModel(new VncOkCommandService());
            try
            {
                Assert.False(vm.OpenViewerCommand.CanExecute(null), "未启用 VNC 时打开查看器应不可点");
            }
            finally
            {
                vm.Dispose();
            }
        }

        [Fact]
        public void VNC启用后_打开查看器按钮可点_断开后复位灰显()
        {
            // stub 建立会话（VncOn=false 初始）——IP/型号为**模拟会话参数**（2026-08-30 用户代码评论：
            // DeviceConnectionService.UseStub=true 模拟连接成功，不真实联网探测；测试只验证 OpenViewerCommand
            // 门禁（CanExecute 跟随 VncOn），不依赖真实设备 IP——若改真实 IP 需设备在线，测试将不稳定）
            var result = DeviceConnectionService.TestConnection("192.168.1.146", "NavigatorHMI-7");
            Assert.True(result.Success);

            var vm = new DevicePanelViewModel(new VncOkCommandService());
            try
            {
                // 点 VNC 启停按钮（取反语义：false→true 发出 enable=on，mock 成功 → VncOn=true）
                vm.VncCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.True(vm.VncOn, "VNC 启用指令成功后 VncOn 应为 true");
                Assert.True(vm.OpenViewerCommand.CanExecute(null), "VNC 启用后打开查看器应可点");

                // 断开 → VncOn 复位 false → 按钮灰显
                DeviceConnectionService.Disconnect();
                Assert.False(vm.VncOn, "断开后 VncOn 应复位 false");
                Assert.False(vm.OpenViewerCommand.CanExecute(null), "断开后打开查看器应复位灰显");
            }
            finally
            {
                vm.Dispose();
            }
        }
    }
}
