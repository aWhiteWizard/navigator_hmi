using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// P 循环 P-5（2026-09-02）：历史记录控件 HistoryView PC 侧测试。
    /// 覆盖：命令层 add_widget history_view、DTO 字段映射（73/74 + Q-6 77 Titles）、模型 round-trip（ProtoInclude 119）、
    /// WindowWidget DisplayMode→DTO 54 值级映射。
    /// </summary>
    public class HistoryViewWidgetTests
    {
        private static HMIProject EmptyProject()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_history_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "历史测试",
                ProjectFilePath = Path.Combine(dir, "history-test.hmiproj")
            };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            return p;
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void add_widget_history_view_放置成功_ObjectName自动()
        {
            var p = EmptyProject();
            var svc = new CommandService(p);
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "画面A", ["widget_type"] = "history_view" });
            Assert.True(r.Success);
            Assert.Single(p.Screens[0].Widgets, w => w is HistoryViewWidget);
            Assert.StartsWith("history_view_", p.Screens[0].Widgets[0].ObjectName);
        }

        [Fact]
        public void add_widget_未知类型historyview拼写_拒绝()
        {
            var p = EmptyProject();
            var svc = new CommandService(p);
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "画面A", ["widget_type"] = "historyview" });
            Assert.False(r.Success);
        }

        [Fact]
        public void 编译产物_HistoryViewDTO字段映射()
        {
            var p = EmptyProject();
            p.Screens[0].Widgets.Add(new HistoryViewWidget
            {
                ObjectName = "hv1",
                Tags = new List<string> { "温度", "压力" },
                DbPath = "my_history.db"
            });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "hv1");
                Assert.Equal(NavihmiWidgetType.HistoryView, dto.Type);
                Assert.Equal(new[] { "温度", "压力" }, dto.HistoryTags);
                Assert.Equal("my_history.db", dto.HistoryDbPath);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译产物_Titles列显示名平行映射到DTO77()
        {
            // Q-6（2026-09-04 用户 Check：HistoryView 变量表格 tag+title）——Titles[i] 平行 Tags[i] → DTO HistoryTagTitles
            var p = EmptyProject();
            p.Screens[0].Widgets.Add(new HistoryViewWidget
            {
                ObjectName = "hv1",
                Tags = new List<string> { "温度", "压力" },
                Titles = new List<string> { "1#炉温", "2#炉压" }
            });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "hv1");
                Assert.Equal(new[] { "1#炉温", "2#炉压" }, dto.HistoryTagTitles);
                Assert.Equal(new[] { "温度", "压力" }, dto.HistoryTags);   // 平行不串
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译产物_无Titles_默认空列表兼容老工程()
        {
            // 老工程/未配 title → HistoryTagTitles 空（FW 回退显示变量名——5-3 老工程兼容保持）
            var p = EmptyProject();
            p.Screens[0].Widgets.Add(new HistoryViewWidget { ObjectName = "hv1", Tags = new List<string> { "温度" } });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "hv1");
                Assert.Empty(dto.HistoryTagTitles);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译产物_WindowDisplayMode映射到DTO54()
        {
            var p = EmptyProject();
            p.Screens[0].Widgets.Add(new WindowWidget { ObjectName = "win1", Type = WindowType.AlarmView, DisplayMode = AlarmDisplayMode.History });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "win1");
                Assert.Equal(NavihmiWidgetType.Window, dto.Type);
                Assert.Equal((int)AlarmDisplayMode.History, dto.DisplayMode);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 模型层roundTrip_HistoryViewProtoInclude119字段保留()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_history_rt_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "history-rt.hmiproj");
            try
            {
                var p = new HMIProject { Name = "RT", ProjectFilePath = path };
                p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
                p.Screens[0].Widgets.Add(new HistoryViewWidget
                {
                    ObjectName = "hv1",
                    Tags = new List<string> { "温度", "压力", "流量" },
                    Titles = new List<string> { "1#炉温", "", "3#流量" },   // 中间空 title（=显示变量名）也要 round-trip 保留
                    DbPath = "custom.db"
                });
                using (var fs = File.Create(path)) Serializer.Serialize(fs, p);
                using var rfs = File.OpenRead(path);
                var back = Serializer.Deserialize<HMIProject>(rfs);
                var hv = Assert.IsType<HistoryViewWidget>(back.Screens[0].Widgets.Single(w => w.ObjectName == "hv1"));
                Assert.Equal(new[] { "温度", "压力", "流量" }, hv.Tags);
                Assert.Equal(new[] { "1#炉温", "", "3#流量" }, hv.Titles);   // Q-6 审查 🟡-2：Titles(ProtoMember 3) round-trip
                Assert.Equal("custom.db", hv.DbPath);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
