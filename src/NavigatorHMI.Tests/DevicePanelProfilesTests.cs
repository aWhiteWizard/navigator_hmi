using System.Linq;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>L-B2 根因验证：DevicePanelViewModel 构造后类型下拉数据源（Profiles/SelectedProfile）必须非空——直接开设备面板（未先开新建项目）场景。</summary>
    public class DevicePanelProfilesTests
    {
        [Fact]
        public void DevicePanel构造_Profiles与SelectedProfile非空()
        {
            var vm = new DevicePanelViewModel(new MockCommandService());

            Assert.NotEmpty(vm.Profiles);
            Assert.Contains(vm.Profiles, p => p.Model == "NavigatorHMI-7");
            Assert.NotNull(vm.SelectedProfile);

            vm.Dispose();
        }

        // ── 2026-08-30 用户代码评论 13：VNC 端口配置化（DeviceCapability.VncPort，device-profile 可配）──

        [Fact]
        public void DeviceProfile默认_VncPort为5900()
        {
            // 未配置（内置默认）→ VncPort = 默认 5900（与 FW fw-config.json vnc.port 一致）
            Assert.Equal(DeviceCapability.DefaultVncPort, DeviceProfileService.Profiles
                .First(p => p.Model == "NavigatorHMI-7").Capability.VncPort);
        }

        [Fact]
        public void DeviceProfileJson_vncPort字段_反序列化生效()
        {
            // device-profile JSON 显式配 vncPort → 反序列化到 Capability.VncPort（System.Text.Json 大小写不敏感）
            var json = """
                {
                  "model": "TestVncPort-7",
                  "sizeInch": "7寸",
                  "capability": { "vncPort": 5901 }
                }
                """;
            var profile = System.Text.Json.JsonSerializer.Deserialize<DeviceProfile>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(profile);
            Assert.Equal(5901, profile!.Capability.VncPort);
        }

        private class MockCommandService : ICommandService
        {
            public event System.Action<string, System.Collections.Generic.Dictionary<string, object?>, CommandResult>? CommandExecuted { add { } remove { } }
            public string? CurrentScreenName { get; set; } = "";
            public CommandResult Execute(string commandName, System.Collections.Generic.Dictionary<string, object?> parameters)
                => CommandResult.Fail("MOCK", "mock");
            public System.Collections.Generic.List<CommandDefinition> GetAvailableCommands() => new();
            public System.Collections.Generic.List<string> GetScreenNames() => new();
            public System.Collections.Generic.List<string> GetGroupNames() => new();
            public System.Collections.Generic.List<(string Name, string Type)> GetCurrentScreenWidgets() => new();
        }
    }
}
