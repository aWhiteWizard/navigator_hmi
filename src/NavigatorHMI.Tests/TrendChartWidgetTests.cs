using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// P 循环 P-4（2026-09-02）：趋势图控件 TrendChart PC 侧测试。
    /// 覆盖：契约映射（ToWidget→DTO 字段 65-72）、命令层 add-widget trend_chart、编译校验（变量存在/类型/XY 需 B）。
    /// </summary>
    public class TrendChartWidgetTests
    {
        private static HMIProject ProjectWithTags(params Tag[] tags)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_trend_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "趋势测试",
                ProjectFilePath = Path.Combine(dir, "trend-test.hmiproj")
            };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            foreach (var t in tags) p.Tags.Add(t);
            return p;
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void add_widget_trend_chart_放置成功_ObjectName自动()
        {
            var p = ProjectWithTags();
            var svc = new CommandService(p);
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "画面A", ["widget_type"] = "trend_chart" });
            Assert.True(r.Success);
            var w = p.Screens[0].Widgets.Single(x => x is TrendChartWidget);
            Assert.StartsWith("trend_chart_", w.ObjectName);
        }

        [Fact]
        public void add_widget_未知类型trendchart拼写_拒绝()
        {
            var p = ProjectWithTags();
            var svc = new CommandService(p);
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "画面A", ["widget_type"] = "trendchart" });
            Assert.False(r.Success);
        }

        [Fact]
        public void 编译产物_TrendChartDTO字段映射正确()
        {
            var p = ProjectWithTags(new Tag { Name = "t1", DataType = TagDataType.INT32 });
            var tc = new TrendChartWidget
            {
                ObjectName = "tc1", TrendMode = TrendMode.XY,
                TrendTagA = "t1", TrendTagB = "t1",
                SampleIntervalMs = 2000, TimeWindowSeconds = 120, LineColor = "#FF0000", LineWidth = 2.5, RefreshRateMs = 800
            };
            p.Screens[0].Widgets.Add(tc);
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "tc1");
                Assert.Equal(NavihmiWidgetType.TrendChart, dto.Type);
                Assert.Equal((int)TrendMode.XY, dto.TrendMode);
                Assert.Equal("t1", dto.TrendTagA);
                Assert.Equal("t1", dto.TrendTagB);
                Assert.Equal(2000, dto.SampleIntervalMs);
                Assert.Equal(120, dto.TimeWindowSeconds);
                Assert.Equal("#FF0000", dto.LineColor);
                Assert.Equal(2.5, dto.LineWidth);
                Assert.Equal(800, dto.RefreshRateMs);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_时间数据模式无变量A_不报错()
        {
            // 时间-数据模式变量 A 可空（控件未绑定时显示占位/无数据——不阻断编译，与其它控件 BoundTag 空一致）
            var p = ProjectWithTags();
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_变量A不存在_报错()
        {
            var p = ProjectWithTags();
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendTagA = "不存在变量" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("变量 A") && e.Contains("不存在变量"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_变量A非数值类型_报错()
        {
            var p = ProjectWithTags(new Tag { Name = "s1", DataType = TagDataType.STRING });
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendTagA = "s1" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("非数值"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_XY模式缺变量B_报错()
        {
            var p = ProjectWithTags(new Tag { Name = "t1", DataType = TagDataType.INT32 });
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendMode = TrendMode.XY, TrendTagA = "t1" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("变量A-B 模式需配置变量 B"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_XY模式双数值变量_正常()
        {
            var p = ProjectWithTags(new Tag { Name = "t1", DataType = TagDataType.INT32 }, new Tag { Name = "t2", DataType = TagDataType.FLOAT });
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendMode = TrendMode.XY, TrendTagA = "t1", TrendTagB = "t2" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 模型层roundTrip_ProtoInclude118子类8字段保留()
        {
            // ProtoInclude(118) 子类经 HMIProject 序列化/反序列化路径（编译路径不经模型序列化——补契约 round-trip）
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_trend_rt_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "trend-rt.hmiproj");
            try
            {
                var p = new HMIProject { Name = "RT", ProjectFilePath = path };
                p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
                p.Screens[0].Widgets.Add(new TrendChartWidget
                {
                    ObjectName = "tc1", TrendMode = TrendMode.XY, TrendTagA = "tA", TrendTagB = "tB",
                    SampleIntervalMs = 2222, TimeWindowSeconds = 333, LineColor = "#00FF00", LineWidth = 3.5, RefreshRateMs = 999
                });
                using (var fs = File.Create(path)) Serializer.Serialize(fs, p);
                using var rfs = File.OpenRead(path);
                var back = Serializer.Deserialize<HMIProject>(rfs);
                var tc = Assert.IsType<TrendChartWidget>(back.Screens[0].Widgets.Single(w => w.ObjectName == "tc1"));
                Assert.Equal(TrendMode.XY, tc.TrendMode);
                Assert.Equal("tA", tc.TrendTagA);
                Assert.Equal("tB", tc.TrendTagB);
                Assert.Equal(2222, tc.SampleIntervalMs);
                Assert.Equal(333, tc.TimeWindowSeconds);
                Assert.Equal("#00FF00", tc.LineColor);
                Assert.Equal(3.5, tc.LineWidth);
                Assert.Equal(999, tc.RefreshRateMs);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void 编译_变量B不存在_报错()
        {
            var p = ProjectWithTags(new Tag { Name = "t1", DataType = TagDataType.INT32 });
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendMode = TrendMode.XY, TrendTagA = "t1", TrendTagB = "不存在B" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("变量 B") && e.Contains("不存在B"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_变量B非数值类型_报错()
        {
            var p = ProjectWithTags(new Tag { Name = "t1", DataType = TagDataType.INT32 }, new Tag { Name = "s1", DataType = TagDataType.STRING });
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendMode = TrendMode.XY, TrendTagA = "t1", TrendTagB = "s1" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("变量 B") && e.Contains("非数值"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译_BOOL变量_数值兼容通过()
        {
            var p = ProjectWithTags(new Tag { Name = "b1", DataType = TagDataType.BOOL });
            p.Screens[0].Widgets.Add(new TrendChartWidget { ObjectName = "tc1", TrendTagA = "b1" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
            }
            finally { CleanOutput(p); }
        }
    }
}
