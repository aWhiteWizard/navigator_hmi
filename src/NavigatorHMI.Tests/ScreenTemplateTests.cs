using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// W-A（P12 画面模板）：save_screen_template/apply_screen_template——存模板深拷贝快照、
    /// 应用模板控件整体替换、无共享引用可独立编辑、受保护画面拒绝。
    /// </summary>
    public class ScreenTemplateTests
    {
        private static HMIProject ProjectWith(params string[] screenNames)
        {
            var p = new HMIProject { Name = "模板测试" };
            foreach (var n in screenNames)
                p.Screens.Add(new Screen { Name = n, Type = ScreenType.Custom });
            return p;
        }

        private static ButtonWidget Btn(string name, double x, double y, string text)
            => new() { ObjectName = name, X = x, Y = y, Text = text };

        [Fact]
        public void save_应用后_布局一致()
        {
            var p = ProjectWith("源页", "目标页");
            p.Screens[0].Widgets.Add(Btn("btn_1", 10, 20, "启动"));
            p.Screens[0].Widgets.Add(new NumericDisplayWidget { ObjectName = "num_2", X = 100, Y = 50 });
            var svc = new CommandService(p);

            var save = svc.Execute("save_screen_template", new Dictionary<string, object?>
                { ["screen_name"] = "源页", ["template_name"] = "控制面板" });
            Assert.True(save.Success);
            Assert.Single(p.Templates);
            Assert.Equal(2, p.Templates[0].Widgets.Count);

            var apply = svc.Execute("apply_screen_template", new Dictionary<string, object?>
                { ["template_name"] = "控制面板", ["screen_name"] = "目标页" });
            Assert.True(apply.Success);

            var target = p.Screens[1];
            Assert.Equal(2, target.Widgets.Count);
            Assert.Equal("btn_1", target.Widgets[0].ObjectName);
            Assert.Equal("启动", ((ButtonWidget)target.Widgets[0]).Text);
            Assert.Equal(10, ((ButtonWidget)target.Widgets[0]).X);
            Assert.Equal(2, target.Widgets.Count(w => w.ObjectName is "btn_1" or "num_2"));
        }

        [Fact]
        public void 模板_深拷贝无共享引用_应用后可独立编辑()
        {
            var p = ProjectWith("源页", "目标页");
            var srcBtn = Btn("btn_1", 10, 20, "启动");
            p.Screens[0].Widgets.Add(srcBtn);
            var svc = new CommandService(p);
            svc.Execute("save_screen_template", new Dictionary<string, object?>
                { ["screen_name"] = "源页", ["template_name"] = "T1" });

            // 改源画面控件 → 模板不受影响（快照独立）
            srcBtn.Text = "改后";
            Assert.Equal("启动", p.Templates[0].Widgets.OfType<ButtonWidget>().Single().Text);

            // 应用 → 改应用结果控件 → 模板/源画面不受影响（独立对象图）
            svc.Execute("apply_screen_template", new Dictionary<string, object?>
                { ["template_name"] = "T1", ["screen_name"] = "目标页" });
            var applied = (ButtonWidget)p.Screens[1].Widgets[0];
            applied.Text = "应用后改";
            Assert.Equal("启动", p.Templates[0].Widgets.OfType<ButtonWidget>().Single().Text);
            Assert.Equal("改后", ((ButtonWidget)p.Screens[0].Widgets[0]).Text);
        }

        [Fact]
        public void 重名保存_覆盖更新不重复()
        {
            var p = ProjectWith("页A");
            p.Screens[0].Widgets.Add(Btn("b1", 0, 0, "v1"));
            var svc = new CommandService(p);
            svc.Execute("save_screen_template", new Dictionary<string, object?>
                { ["screen_name"] = "页A", ["template_name"] = "T1" });
            p.ClearDirty();
            p.Screens[0].Widgets.Clear();
            p.Screens[0].Widgets.Add(Btn("b2", 5, 5, "v2"));
            var r2 = svc.Execute("save_screen_template", new Dictionary<string, object?>
                { ["screen_name"] = "页A", ["template_name"] = "T1" });

            Assert.True(r2.Success);
            Assert.Single(p.Templates);
            Assert.Equal("b2", p.Templates[0].Widgets[0].ObjectName);
            var data = r2.Data.ToString();
            Assert.Contains("updated", data);
            // 🟡2：覆盖更新改嵌套集合（模板内部 Widgets）——根集合 Templates 事件不触发，须显式置脏
            Assert.True(p.IsDirty, "覆盖更新模板应置脏工程（嵌套集合修改须显式 MarkDirty）");
        }

        [Fact]
        public void 受保护画面_不可存模板与不可应用()
        {
            var p = ProjectWith("自定义页");
            p.Screens.Add(new Screen { Name = "全局", Type = ScreenType.Template });
            p.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap });
            var svc = new CommandService(p);

            // Template 画面不可作模板源
            var save = svc.Execute("save_screen_template", new Dictionary<string, object?>
                { ["screen_name"] = "全局", ["template_name"] = "T1" });
            Assert.False(save.Success);
            Assert.Equal("PROTECTED", save.ErrorCode);

            // 世界地图不可作应用目标
            var apply = svc.Execute("apply_screen_template", new Dictionary<string, object?>
                { ["template_name"] = "T1", ["screen_name"] = "世界地图" });
            Assert.False(apply.Success);
        }

        [Fact]
        public void 模板不存在_应用报错()
        {
            var p = ProjectWith("页A");
            var svc = new CommandService(p);
            var r = svc.Execute("apply_screen_template", new Dictionary<string, object?>
                { ["template_name"] = "不存在", ["screen_name"] = "页A" });
            Assert.False(r.Success);
            Assert.Equal("NOT_FOUND", r.ErrorCode);
        }

        [Fact]
        public void 保存加载_模板随工程持久化()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_template_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "t.hmiproj");
            var p = ProjectWith("页A");
            p.ProjectFilePath = path;
            p.Screens[0].Widgets.Add(Btn("b1", 1, 2, "x"));
            var svc = new CommandService(p);
            svc.Execute("save_screen_template", new Dictionary<string, object?>
                { ["screen_name"] = "页A", ["template_name"] = "T1" });
            ProjectManager.Save(p, path);

            var loaded = ProjectFileService.Load(path);
            Assert.Single(loaded.Templates);
            Assert.Equal("T1", loaded.Templates[0].Name);
            Assert.Single(loaded.Templates[0].Widgets);
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
        }
    }
}
