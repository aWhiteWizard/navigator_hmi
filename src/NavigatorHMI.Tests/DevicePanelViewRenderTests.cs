using NavigatorHMI.CommandLayer;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>L-B2 UI 层验证：DevicePanelView 渲染后 ComboBox 类型下拉有选项（DataContext=真实 VM）。</summary>
    public class DevicePanelViewRenderTests
    {
        [Fact]
        public void DevicePanelView_类型下拉有选项()
        {
            // WPF UI 需 STA 线程（xUnit 默认 MTA）
            RunOnSta(RunTest);
        }

        private static void RunOnSta(System.Action action)
        {
            Exception? threadEx = null;
            var t = new System.Threading.Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { threadEx = ex; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
            if (threadEx != null) throw threadEx;
        }

        private void RunTest()
        {
            var vm = new DevicePanelViewModel(new MockCommandService());
            var view = new DevicePanelView { DataContext = vm };

            // 强制布局渲染（WPF 无窗口也可走 Measure/Arrange 初始化绑定）
            view.Measure(new Size(400, 300));
            view.Arrange(new Rect(0, 0, 400, 300));
            view.UpdateLayout();

            // 查找 ComboBox（x:Name 未设，用逻辑树遍历）
            var combo = FindComboBox(view);
            Assert.NotNull(combo);

            // 检查 ItemsSource 绑定是否生效（Items 需展开；ItemsSource 直接可查）
            var src = combo!.ItemsSource;
            Assert.NotNull(src);
            var srcList = src!.Cast<object>().ToList();
            Assert.True(srcList.Count > 0, $"ItemsSource 应有选项，实际 {srcList.Count}");
            // DisplayMemberPath="Model"——源对象是 DeviceProfile，取 Model 属性验证
            var first = srcList[0];
            var model = first.GetType().GetProperty("Model")?.GetValue(first)?.ToString() ?? "";
            Assert.Equal("NavigatorHMI-7", model);

            vm.Dispose();
        }

        private static System.Windows.Controls.ComboBox? FindComboBox(DependencyObject root)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.ComboBox cb) return cb;
                var found = FindComboBox(child);
                if (found != null) return found;
            }
            return null;
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
