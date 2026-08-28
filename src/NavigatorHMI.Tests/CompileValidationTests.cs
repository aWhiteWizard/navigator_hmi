using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-3a：编译子系统校验补强（BoundTag 悬空/ListRef 存在性/画面名重复/StartScreen 存在性）
    /// + 输出原子化测试（2026-08-30）。
    /// </summary>
    public class CompileValidationTests
    {
        private static HMIProject ProjectWith(string screenName = "画面A")
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_compile_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var p = new HMIProject
            {
                Name = "编译测试",
                ProjectFilePath = Path.Combine(dir, "compile-test.hmiproj")
            };
            p.Screens.Add(new Screen { Name = screenName, Type = ScreenType.Custom });
            return p;
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void BoundTag悬空_报错不出文件()
        {
            var p = ProjectWith();
            p.Screens[0].Widgets.Add(new ButtonWidget { ObjectName = "b1", BoundTag = "不存在的变量" });

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("绑定的变量") && e.Contains("不存在的变量"));
            Assert.Null(result.OutputPath);
        }

        [Fact]
        public void ListRef不存在_报错()
        {
            var p = ProjectWith();
            p.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ListRef = "不存在的列表" });

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("引用的列表") && e.Contains("不存在的列表"));
        }

        [Fact]
        public void 画面名重复_报错()
        {
            var p = ProjectWith("重名");
            p.Screens.Add(new Screen { Name = "重名", Type = ScreenType.Custom });

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("重名") && e.Contains("重复定义"));
        }

        [Fact]
        public void StartScreen不存在_报错()
        {
            var p = ProjectWith();
            p.StartScreen = "不存在的画面";

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("启动画面") && e.Contains("不存在"));
        }

        [Fact]
        public void 正常工程编译成功_原子输出无临时残留()
        {
            var p = ProjectWith();
            CleanOutput(p);
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });

            var result = ProjectGenerator.Compile(p);
            Assert.False(result.HasErrors);
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath));
            Assert.False(File.Exists(result.OutputPath + ".tmp"), "原子输出不应残留 .tmp");
        }

        [Fact]
        public void 编译失败_不覆盖旧产物()
        {
            var p = ProjectWith();
            CleanOutput(p);
            var result1 = ProjectGenerator.Compile(p);   // 先成功一次（有旧产物）
            Assert.False(result1.HasErrors);
            var oldBytes = File.ReadAllBytes(result1.OutputPath!);

            // 引入 BoundTag 悬空 → 编译失败 → 旧产物应保持
            p.Screens[0].Widgets.Add(new ButtonWidget { ObjectName = "b2", BoundTag = "悬空变量" });
            var result2 = ProjectGenerator.Compile(p);
            Assert.True(result2.HasErrors);
            Assert.Equal(oldBytes, File.ReadAllBytes(result1.OutputPath!));
        }

        [Fact]
        public void 合法绑定_列表与变量存在_编译通过()
        {
            var p = ProjectWith();
            CleanOutput(p);
            p.Tags.Add(new Tag { Name = "图片索引", DataType = TagDataType.INT16 });
            p.Lists.Add(new ListDef { Name = "图片列表", Type = ListType.Image });
            p.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", BoundTag = "图片索引", ListRef = "图片列表" });
            p.StartScreen = "画面A";

            var result = ProjectGenerator.Compile(p);
            Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        }

        [Fact]
        public void WorldMap点BoundTag悬空_报错()
        {
            var p = ProjectWith();
            p.WorldMap = new WorldMapConfig();
            p.WorldMap.WorkPoints.Add(new MapWorkPoint { BoundTag = "悬空点变量" });

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("作业点") && e.Contains("悬空点变量"));
        }

        [Fact]
        public void ListRef类型不匹配_报错()
        {
            var p = ProjectWith();
            p.Lists.Add(new ListDef { Name = "文本列表", Type = ListType.Text });
            p.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ListRef = "文本列表" });   // 图片控件绑文本列表

            var result = ProjectGenerator.Compile(p);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("应为图片列表"));
        }

        [Fact]
        public void ListRef类型匹配_文本列表绑文本控件_通过()
        {
            var p = ProjectWith();
            CleanOutput(p);
            p.Lists.Add(new ListDef { Name = "文本列表", Type = ListType.Text });
            p.Screens[0].Widgets.Add(new TextListWidget { ObjectName = "tl1", ListRef = "文本列表" });

            var result = ProjectGenerator.Compile(p);
            Assert.False(result.HasErrors, string.Join("; ", result.Errors));
        }
    }
}
