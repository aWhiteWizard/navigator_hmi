using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// P 循环 P-3（2026-09-02）：B1 设备端世界地图开关编译行为测试。
    /// 语义（用户定稿）：开关只影响设备端显示（编译产物过滤 WorldMap Screen），组态软件编辑态始终保留；
    /// 默认勾选；开关关时 StartScreen/事件引用世界地图或无自定义画面 → 编译报错。
    /// </summary>
    public class IncludeWorldMapOnDeviceTests
    {
        private static HMIProject ProjectWithWorldMap(bool includeOnDevice = true)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_b1_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "B1 测试",
                ProjectFilePath = Path.Combine(dir, "b1-test.hmiproj"),
                IncludeWorldMapOnDevice = includeOnDevice
            };
            p.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap });
            p.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template });
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            if (includeOnDevice)
            {
                // X-1 校验（2026-09-08）：设备端包含世界地图 ⇒ 须划定作业范围 ≥3 点——测试工程补足（否则 3g 编译校验拦截，
                // P-3 测试 helper 未同步 X-1 语义，2026-09-10 Y 循环修复）
                var wm = new WorldMapConfig();
                for (int i = 0; i < 4; i++)
                    wm.WorkRangePoints.Add(new WorkRangePoint());
                p.WorldMap = wm;
            }
            return p;
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void 默认勾选_编译产物含WorldMap()
        {
            var p = ProjectWithWorldMap(includeOnDevice: true);
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                Assert.Contains(nav.Screens, s => s.Type == ScreenType.WorldMap);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 取消勾选_编译产物过滤WorldMap_保留自定义与全局()
        {
            var p = ProjectWithWorldMap(includeOnDevice: false);
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                Assert.DoesNotContain(nav.Screens, s => s.Type == ScreenType.WorldMap);
                Assert.Contains(nav.Screens, s => s.Type == ScreenType.Custom);
                Assert.Contains(nav.Screens, s => s.Type == ScreenType.Template);
                Assert.False(nav.IncludeWorldMapOnDevice);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 取消勾选_StartScreen指向世界地图_报错()
        {
            var p = ProjectWithWorldMap(includeOnDevice: false);
            p.StartScreen = "世界地图";
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("启动画面") && e.Contains("世界地图"));
                Assert.Null(result.OutputPath);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 取消勾选_画面事件跳转世界地图_报错()
        {
            var p = ProjectWithWorldMap(includeOnDevice: false);
            p.Screens.First(s => s.Type == ScreenType.Custom).Widgets.Add(new ButtonWidget
            {
                ObjectName = "btn1",
                Events = new List<WidgetEvent>
                {
                    new() {
                        Type = EventType.onClick,
                        Actions = new List<EventAction>
                        {
                            new() { Type = ActionType.screen_switch, Parameters = new() { ["target_screen"] = "世界地图" } }
                        }
                    }
                }
            });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("世界地图"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 取消勾选_无自定义画面_报错()
        {
            var p = ProjectWithWorldMap(includeOnDevice: false);
            var custom = p.Screens.First(s => s.Type == ScreenType.Custom);
            p.Screens.Remove(custom);
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("无自定义画面"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 取消勾选_地图级事件跳转世界地图_报错()
        {
            var p = ProjectWithWorldMap(includeOnDevice: false);
            p.WorldMap = new WorldMapConfig
            {
                Events = new List<WidgetEvent>
                {
                    new() {
                        Type = EventType.onClick,
                        Actions = new List<EventAction>
                        {
                            new() { Type = ActionType.screen_switch, Parameters = new() { ["target_screen"] = "世界地图" } }
                        }
                    }
                }
            };
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.True(result.HasErrors);
                Assert.Contains(result.Errors, e => e.Contains("世界地图事件"));
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 取消勾选_源工程WorldMap画面与配置保留_仅编译产物过滤()
        {
            var p = ProjectWithWorldMap(includeOnDevice: false);
            p.WorldMap = new WorldMapConfig();   // 有地图配置（应不下发但源工程保留）
            try
            {
                Assert.Contains(p.Screens, s => s.Type == ScreenType.WorldMap);   // 编译前后源工程不变量
                Assert.NotNull(p.WorldMap);
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                Assert.DoesNotContain(nav.Screens, s => s.Type == ScreenType.WorldMap);
                Assert.Null(nav.WorldMap);   // 地图配置死数据不下发（审查 🟡 已处理）
                // 编译不改源工程
                Assert.Contains(p.Screens, s => s.Type == ScreenType.WorldMap);
                Assert.NotNull(p.WorldMap);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void IncludeWorldMapOnDevice_模型层roundTrip_默认true与false均保留()
        {
            // protobuf-net IsRequired round-trip：true 与 false 都须落盘读回一致（防省略 false 丢值——4_bugs bool-default-loss）
            foreach (var value in new[] { true, false })
            {
                var dir = Path.Combine(Path.GetTempPath(), "navihmi_b1_rt_test");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"b1-rt-{value}.hmiproj");
                try
                {
                    var p = new HMIProject { Name = "RT", ProjectFilePath = path, IncludeWorldMapOnDevice = value };
                    using (var fs = File.Create(path)) Serializer.Serialize(fs, p);
                    using var rfs = File.OpenRead(path);
                    var back = Serializer.Deserialize<HMIProject>(rfs);
                    Assert.Equal(value, back.IncludeWorldMapOnDevice);
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            }
        }

        [Fact]
        public void 勾选_StartScreen世界地图_正常编译()
        {
            var p = ProjectWithWorldMap(includeOnDevice: true);
            p.StartScreen = "世界地图";
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
            }
            finally { CleanOutput(p); }
        }
    }
}
