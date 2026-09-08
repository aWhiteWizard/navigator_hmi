using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// X 循环 X-1（2026-09-08 用户规格——GPS CoordinatePicker 前置）：
    /// 编译校验——① GPS 变量 ⇔ 工程含世界地图画面 + 设备端包含 + 划定作业范围（≥3 点）；
    /// ② 设备端含世界地图 ⇒ 划定作业范围（作业范围生成地图底图）。
    /// </summary>
    public class GpsCompileValidationTests
    {
        private static HMIProject ProjectWith(string screenName = "画面A", ScreenType type = ScreenType.Custom)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_gpscompile_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "GPS 编译测试",
                ProjectFilePath = Path.Combine(dir, "gps-compile-test.hmiproj")
            };
            p.Screens.Add(new Screen { Name = screenName, Type = type });
            return p;
        }

        private static HMIProject WorldMapProject(int rangePoints)
        {
            // 世界地图工程：地图画面 + WorldMap 配置 + 指定范围点数（设备端包含默认勾选）
            var p = ProjectWith("世界地图", ScreenType.WorldMap);
            var wm = new WorldMapConfig();
            for (int i = 0; i < rangePoints; i++)
                wm.WorkRangePoints.Add(new WorkRangePoint());
            p.WorldMap = wm;
            return p;
        }

        [Fact]
        public void GPS变量_无世界地图_编译报错()
        {
            var p = ProjectWith();   // 仅自定义画面——无地图
            p.Tags.Add(new Tag { Name = "位置", DataType = TagDataType.GPS });

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("GPS 变量") && e.Contains("位置") && e.Contains("世界地图"));
        }

        [Fact]
        public void 有世界地图画面_未划作业范围_编译报错()
        {
            var p = WorldMapProject(0);   // 地图画面但无范围点

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("需先划定作业范围") && e.Contains("至少 3 个"));
        }

        [Fact]
        public void 世界地图_划作业范围不足3点_编译报错()
        {
            var p = WorldMapProject(2);   // 范围点 2 < 3

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("需先划定作业范围"));
        }

        [Fact]
        public void GPS变量_有地图有作业范围_编译通过()
        {
            var p = WorldMapProject(3);   // 地图 + 3 范围点（底图就绪）
            p.Tags.Add(new Tag { Name = "位置", DataType = TagDataType.GPS });

            var result = ProjectGenerator.Compile(p);
            Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        }

        [Fact]
        public void 无GPS无地图_普通工程_编译通过()
        {
            var p = ProjectWith();   // 自定义画面 + 无 GPS——不受 X-1 影响
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });

            var result = ProjectGenerator.Compile(p);
            Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        }

        [Fact]
        public void 设备端不含世界地图_取消勾选_含GPS报错()
        {
            // 地图画面存在但设备端不含（IncludeWorldMapOnDevice=false）→ GPS 变量仍报错（无地图底图可用）
            var p = WorldMapProject(3);
            p.IncludeWorldMapOnDevice = false;
            p.Tags.Add(new Tag { Name = "位置", DataType = TagDataType.GPS });

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("GPS 变量") && e.Contains("世界地图"));
        }
    }
}
