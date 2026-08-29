using NavigatorHMI.CommandLayer;
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
