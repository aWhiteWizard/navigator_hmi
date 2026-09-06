using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// V-6：增量编译链（P13 骨架）——变化域分类/画面粒度记录/compile 摘要与基线重置。
    /// 防漏登记锁：命令层注册表全部命令必须被 ProjectChangeTracker.DomainOf 显式归类
    /// （新命令漏登记 → 变化摘要缺域 → 本测试红）。
    /// </summary>
    public class ChangeTrackerTests
    {
        private static List<CommandDefinition> AllDefinitions()
            => new CommandService(new HMIProject()).GetAvailableCommands();

        /// <summary>有意不参与变化跟踪的命令（工程操作/查询/设备运行时/设计期模板库）。</summary>
        private static readonly HashSet<string> KnownNoneCommands = new()
        {
            "create_project", "open_project", "save_project", "compile",
            "save_screen_template",   // W-A: 模板库=设计期复用，非画面/编译数据段
            "connect", "disconnect", "scan_devices", "deploy_project",
            "deploy_firmware", "blink_device", "vnc",
            "current_screen", "list_users",
        };

        [Fact]
        public void 全部命令_域映射完备()
        {
            var allNames = AllDefinitions().Select(d => d.Name).ToHashSet();
            var unclassified = allNames
                .Where(n => ProjectChangeTracker.DomainOf(n) == ChangeDomain.None && !KnownNoneCommands.Contains(n))
                .ToList();
            Assert.True(unclassified.Count == 0,
                "命令未登记变化域（DomainOf=None 且不在已知 None 名单，须在 ProjectChangeTracker.DomainOf 归类）: "
                + string.Join(", ", unclassified));
        }

        [Theory]
        [InlineData("create_screen", ChangeDomain.Screen)]
        [InlineData("add_widget", ChangeDomain.Widget)]
        [InlineData("bind_event", ChangeDomain.Event)]
        [InlineData("create_tag", ChangeDomain.Tag)]
        [InlineData("update_alarm", ChangeDomain.Alarm)]
        [InlineData("create_user", ChangeDomain.User)]
        [InlineData("create_group", ChangeDomain.Group)]
        [InlineData("update_list", ChangeDomain.List)]
        [InlineData("configure_device", ChangeDomain.Device)]
        [InlineData("set_default_font", ChangeDomain.Font)]
        [InlineData("update_world_map", ChangeDomain.WorldMap)]
        [InlineData("connect", ChangeDomain.None)]
        [InlineData("compile", ChangeDomain.None)]
        [InlineData("save_project", ChangeDomain.None)]
        [InlineData("list_users", ChangeDomain.None)]
        public void 域分类_正确(string cmd, ChangeDomain expected)
            => Assert.Equal(expected, ProjectChangeTracker.DomainOf(cmd));

        [Fact]
        public void 记录_画面粒度_与域累积()
        {
            var t = new ProjectChangeTracker();
            t.Record("create_screen", new() { ["name"] = "温度页" });
            t.Record("add_widget", new() { ["screen_name"] = "温度页" });
            t.Record("create_tag", new() { ["name"] = "温度" });

            var s = t.Snapshot();
            Assert.True(s.Any);
            Assert.Contains(ChangeDomain.Screen, s.Domains);
            Assert.Contains(ChangeDomain.Widget, s.Domains);
            Assert.Contains(ChangeDomain.Tag, s.Domains);
            Assert.Contains("温度页", s.Screens);   // 画面粒度：add_widget 的画面定位
            Assert.DoesNotContain("温度", s.Screens);   // 变量不是画面
        }

        [Fact]
        public void 非修改命令_不记录()
        {
            var t = new ProjectChangeTracker();
            t.Record("connect", new() { ["ip"] = "192.168.1.146" });
            t.Record("current_screen", new());
            t.Record("compile", new());
            Assert.False(t.Snapshot().Any);
        }

        [Fact]
        public void 重命名画面_摘要列新名()
        {
            var t = new ProjectChangeTracker();
            t.Record("rename_screen", new() { ["name"] = "旧页", ["new_name"] = "新页" });
            Assert.Contains("新页", t.Snapshot().Screens);
        }

        /// <summary>compile 集成测试专用工程：独立临时目录（对齐 CompileValidationTests 惯例——
        /// 避免与 deploy/compile 用例（AiSecurityGate 等）并行写同一 cwd output 目录互扰）。</summary>
        private static HMIProject TempProject()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_v6_tracker_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return new HMIProject { Name = "V6跟踪", ProjectFilePath = Path.Combine(dir, "v6-tracker.hmiproj") };
        }

        [Fact]
        public void 编译成功_输出摘要并重置基线()
        {
            var project = TempProject();
            var svc = new CommandService(project);
            // 修改画面（在全局画面上加控件即可，无需真实工程文件）
            svc.Execute("create_screen", new() { ["name"] = "摘要页" });
            Assert.True(project.ChangeTracker.Snapshot().Any, "create_screen 后应有未编译变化");

            var compileResult = svc.Execute("compile", new());
            Assert.True(compileResult.Success);
            // handler Data 匿名对象：经 JSON 探测 changed_domains 含 画面、changed_screens 含 摘要页
            using var doc = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(compileResult.Data));
            var domains = string.Join(",", doc.RootElement.GetProperty("changed_domains").EnumerateArray().Select(x => x.GetString()));
            Assert.Contains("画面", domains);
            var screens = doc.RootElement.GetProperty("changed_screens").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Contains("摘要页", screens);

            // 编译成功后基线重置（无未编译变化）
            Assert.False(project.ChangeTracker.Snapshot().Any, "compile 成功应重置变化基线");
        }

        [Fact]
        public void 失败命令不记录_编译失败保留基线_成功编译重置()
        {
            var project = TempProject();
            var svc = new CommandService(project);
            // ① 失败命令不触发变化记录（handler 校验层拒绝，非 BUILD_FAILED）
            svc.Execute("create_screen", new() { ["name"] = "页A" });
            var r = svc.Execute("add_widget", new() { ["screen_name"] = "页A", ["widget_type"] = "bogus_type" });
            Assert.False(r.Success);   // 未知控件类型被 handler 拒绝（失败命令不 Record）

            // ② 真实 BUILD_FAILED：BoundTag 悬空 → ProjectGenerator 编译校验失败（CompileValidationTests 同构场景）
            var sw = project.Screens.First(s => s.Name == "页A");
            sw.Widgets.Add(new ButtonWidget { ObjectName = "b1", BoundTag = "不存在的变量" });
            var fail = svc.Execute("compile", new());
            Assert.False(fail.Success);
            Assert.Equal("BUILD_FAILED", fail.ErrorCode);
            // 编译失败不重置基线（create_screen 的画面域变化仍待编译）
            var pending = project.ChangeTracker.Snapshot();
            Assert.True(pending.Any, "编译失败应保留未编译变化");
            Assert.Contains("页A", pending.Screens);

            // ③ 修复后编译成功 → 摘要输出并重置基线
            sw.Widgets.Clear();
            var ok = svc.Execute("compile", new());
            Assert.True(ok.Success);
            Assert.False(project.ChangeTracker.Snapshot().Any, "compile 成功应重置变化基线");
        }
    }
}
