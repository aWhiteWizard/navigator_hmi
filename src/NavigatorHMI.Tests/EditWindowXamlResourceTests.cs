using NavigatorHMI.Common;
using NavigatorHMI.Views;
using System;
using System.Threading;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AB-6c（2026-09-11）：EditWindow XAML 资源分区（ResourceDictionary 外部化）的运行期验证。
    /// 背景：XAML 资源解析错误**编译期不报**（StaticResource 找不到键只在运行期抛 XamlParseException），
    /// 故必须有「构造窗口」级别的冒烟测试兜住——本测试即该闸门。
    /// 注：EditWindow 构造需要至少一个画面（空工程会在画面查找处抛 Sequence contains no matching element，属既有前提）。
    /// </summary>
    public class EditWindowXamlResourceTests
    {
        private static void RunOnSta(Action action)
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { threadEx = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            // 保留原始堆栈（xUnit 只显示 rethrow 点，内层帧会丢——包成文本便于定位）
            if (threadEx != null)
                throw new InvalidOperationException("STA 线程内失败：" + threadEx, threadEx);
        }

        [Fact]
        public void EditWindow_构造_资源解析无异常()
        {
            RunOnSta(() =>
            {
                var project = new HMIProject { Name = "XAML 资源冒烟" };
                // VM 构造要求：全局画面（Template）+ 世界地图画面（WorldMap）必须存在（EditWindowViewModel:1394/1396）
                project.Screens.Add(new Screen { Name = "全局", Width = 1024, Height = 600, Type = ScreenType.Template });
                project.Screens.Add(new Screen { Name = "世界地图", Width = 1024, Height = 600, Type = ScreenType.WorldMap });
                project.Screens.Add(new Screen { Name = "画面1", Width = 1024, Height = 600, Type = ScreenType.Custom });
                var window = new EditWindow(project);
                // 关键资源可用（外部 ResourceDictionary 已并入 + 跨字典引用可解析）
                Assert.NotNull(window.FindResource("PropLabel"));
                Assert.NotNull(window.FindResource("BoolToVisibilityConverter"));
                Assert.NotNull(window.FindResource("PropPosition"));
                window.Close();
            });
        }
    }
}
