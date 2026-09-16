using NavigatorHMI.Common;
using NavigatorHMI.Views;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
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

        /// <summary>
        /// AB-6c 补强（2026-09-16 复审 🟡）：**模板实例化**闸门。
        /// 为什么需要：字典外置后，模板内部对「共享字典里别的键」的 StaticResource 引用是**延迟解析**的
        /// （`FindResource` 只查键、不实例化模板），只有真正 `LoadContent()` 才会解析 ⇒
        /// 现有只断言 3 个窗口级键的闸门抓不到这类断裂。本测试把 4 个字典里的每个**顶层** DataTemplate
        /// 都实例化一次，把「拆分导致的资源引用断裂」挡在提交前。
        /// ⚠️ 覆盖边界（2026-09-16 复审 ⚪ 记录）：字典内还有 18 个**嵌套（无 x:Key）** DataTemplate
        /// 不被本闸门解析（其内部约 33 处跨字典引用未覆盖）⇒ 本闸门名为「顶层模板」，
        /// 需要更全覆盖时应改为递归遍历（当前以"高风险顶层块全查"为准）。
        /// ⚠️ 另：本闸门只做**解析期**校验，不触发任何事件 ⇒ 转发链（字典 → 窗口）由人工冒烟（B5 六区域）覆盖。
        /// </summary>
        [Fact]
        public void EditWindow_资源字典_全部顶层模板可实例化()
        {
            RunOnSta(() =>
            {
                var project = new HMIProject { Name = "XAML 模板实例化冒烟" };
                project.Screens.Add(new Screen { Name = "全局", Width = 1024, Height = 600, Type = ScreenType.Template });
                project.Screens.Add(new Screen { Name = "世界地图", Width = 1024, Height = 600, Type = ScreenType.WorldMap });
                project.Screens.Add(new Screen { Name = "画面1", Width = 1024, Height = 600, Type = ScreenType.Custom });
                var window = new EditWindow(project);
                try
                {
                    // 有意脆性门：4 个字典缺一即失败（AB-6c 的交付物就是这 4 个）
                    Assert.Equal(4, window.Resources.MergedDictionaries.Count);

                    var failed = new List<string>();
                    int templateCount = 0;
                    foreach (ResourceDictionary dict in window.Resources.MergedDictionaries)
                    {
                        int perDict = 0;
                        foreach (object key in dict.Keys)
                        {
                            if (dict[key] is not DataTemplate template) continue;
                            templateCount++;
                            perDict++;
                            try
                            {
                                var content = template.LoadContent();
                                Assert.NotNull(content);
                            }
                            catch (Exception ex)
                            {
                                failed.Add($"{key}：{ex.GetType().Name} {ex.Message}");
                            }
                        }
                        Assert.True(perDict > 0, "字典 " + dict.Source + " 里没有可实例化的 DataTemplate（拆分是否漏搬？）");
                    }

                    // 基线：40 个资源条目 = 6 基础件（ODP/画刷×2/转换器×3）+ 6 Style + 1 ContextMenu + 27 DataTemplate
                    Assert.True(templateCount >= 27, "顶层模板数异常偏少（实际 " + templateCount + "，基线 27）");
                    Assert.True(failed.Count == 0, "以下模板实例化失败（资源引用断裂）：\n" + string.Join("\n", failed));
                }
                finally
                {
                    window.Close();
                }
            });
        }
    }
}
