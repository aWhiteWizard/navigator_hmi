using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// W-B：后台加载异步化测试（2026-09-07）。
    /// ProjectFileService.LoadAsync = Task.Run 反序列化 + 阶段进度回调。
    /// 覆盖：成功加载与同步 Load 等价（内容/清脏）、文件不存在/损坏异常语义一致、进度回调触发。
    /// </summary>
    public class ProjectLoadAsyncTests
    {
        private static HMIProject NewProject()
            => new() { Name = "测试工程" };

        private static string TempFilePath(string name)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_loadasync_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        [Fact]
        public async Task 异步加载成功_内容与同步一致_不置脏()
        {
            var p = NewProject();
            p.Name = "异步加载内容";
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            var path = TempFilePath("ok.hmiproj");
            ProjectFileService.Save(p, path);

            var loaded = await ProjectFileService.LoadAsync(path);

            Assert.Equal("异步加载内容", loaded.Name);
            Assert.Single(loaded.Screens);
            Assert.Single(loaded.Tags);
            Assert.False(loaded.IsDirty, "异步加载完成后不应处于脏状态（与同步 Load 一致清脏）");
            Assert.Equal(path, loaded.ProjectFilePath);
        }

        [Fact]
        public async Task 异步加载文件不存在_抛FileNotFoundException()
        {
            var missing = TempFilePath("missing_" + Guid.NewGuid().ToString("N") + ".hmiproj");
            await Assert.ThrowsAsync<FileNotFoundException>(() => ProjectFileService.LoadAsync(missing));
        }

        [Fact]
        public async Task 异步加载损坏文件_抛InvalidDataException()
        {
            var path = TempFilePath("corrupt.hmiproj");
            File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 });   // 非法 protobuf

            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => ProjectFileService.LoadAsync(path));
            Assert.Contains("工程文件损坏或格式不兼容", ex.Message);
        }

        [Fact]
        public async Task 异步加载_进度回调报告阶段文本()
        {
            var p = NewProject();
            var path = TempFilePath("progress.hmiproj");
            ProjectFileService.Save(p, path);

            var stages = new List<string>();
            var progress = new Progress<string>(s => stages.Add(s));

            var loaded = await ProjectFileService.LoadAsync(path, progress);

            Assert.NotNull(loaded);
            Assert.NotEmpty(stages);
            Assert.Contains(stages, s => s.Contains("读取工程文件"));
            Assert.Contains(stages, s => s.Contains("工程解析完成"));
        }
    }
}
