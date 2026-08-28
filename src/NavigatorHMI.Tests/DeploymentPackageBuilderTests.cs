using System.IO.Compression;
using System.Text.Json;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-3b：部署包 zip 容器测试（manifest + 资源收集去重 + RTSP 不入包 + 路径穿越白名单）（2026-08-30）。
    /// </summary>
    public class DeploymentPackageBuilderTests : IDisposable
    {
        private readonly string _dir;
        private readonly HMIProject _project;
        private readonly string _navihmiPath;

        public DeploymentPackageBuilderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_deploy_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _project = new HMIProject
            {
                Name = "部署测试",
                Version = "1.0",
                ProjectFilePath = Path.Combine(_dir, "deploy-test.hmiproj")
            };
            _project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            _navihmiPath = Path.Combine(_dir, "deploy-test.navihmi");
            File.WriteAllBytes(_navihmiPath, new byte[] { 1, 2, 3, 4 });
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private void WriteImage(string name, byte[] content)
        {
            var full = Path.Combine(_dir, name);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(full, content);
        }

        private string Build()
            => DeploymentPackageBuilder.Build(_project, _navihmiPath, Path.Combine(_dir, "output"));

        private static List<(string Path, byte[] Bytes)> ReadZip(string zipPath)
        {
            var files = new List<(string, byte[])>();
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                using var ms = new MemoryStream();
                using var es = e.Open();
                es.CopyTo(ms);
                files.Add((e.FullName, ms.ToArray()));
            }
            return files;
        }

        [Fact]
        public void 基本打包_含manifest_app主包_图片资源()
        {
            WriteImage("a.png", new byte[] { 0xAA });
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "a.png" });

            var zipPath = Build();
            Assert.True(File.Exists(zipPath));

            var files = ReadZip(zipPath);
            var names = files.Select(f => f.Path).ToHashSet();
            Assert.Contains("manifest.json", names);
            Assert.Contains("app/app.navihmi", names);
            Assert.Contains("res/a.png", names);

            // manifest 内容校验
            var manifestBytes = files.First(f => f.Path == "manifest.json").Bytes;
            var manifest = JsonSerializer.Deserialize<List<DeploymentPackageBuilder.ManifestEntry>>(
                System.Text.Encoding.UTF8.GetString(manifestBytes))!;
            Assert.Equal(2, manifest.Count);
            Assert.Equal("app", manifest[0].Type);
            Assert.Equal("app/app.navihmi", manifest[0].Target);
            Assert.Equal("1.0", manifest[0].Version);
            Assert.Equal("res", manifest[1].Type);
            Assert.Equal("res/a.png", manifest[1].Target);
            Assert.Equal(64, manifest[1].Sha256.Length);   // SHA256 hex 长度
        }

        [Fact]
        public void 同内容资源_哈希去重_只打一份()
        {
            var content = new byte[] { 0x11, 0x22 };
            WriteImage("a.png", content);
            WriteImage("b.png", content);   // 同内容不同名
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "a.png" });
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img2", ImagePath = "b.png" });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Equal(1, files.Count(f => f.Path.StartsWith("res/")));
            Assert.Equal(3, files.Count);   // manifest + app + res×1
        }

        [Fact]
        public void 图片列表项_打包_RTSP不入包()
        {
            WriteImage("list1.png", new byte[] { 0x33 });
            _project.Lists.Add(new ListDef { Name = "图片列表", Type = ListType.Image, Items = { "list1.png", "rtsp://192.168.1.10/stream" } });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Contains("res/list1.png", files.Select(f => f.Path));
            Assert.DoesNotContain(files, f => f.Path.Contains("rtsp") || f.Path.Contains("stream"));
        }

        [Fact]
        public void 路径穿越引用_不入包()
        {
            // 工程目录外放一个文件，控件用 ../ 引用 → 白名单排除
            var outside = Path.Combine(Path.GetTempPath(), "navihmi_outside_" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(outside, new byte[] { 0x44 });
            try
            {
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "../" + Path.GetFileName(outside) });

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.DoesNotContain(files, f => f.Path.Contains(Path.GetFileName(outside)));
                Assert.Equal(2, files.Count);   // manifest + app（无 res）
            }
            finally { File.Delete(outside); }
        }

        [Fact]
        public void 缺失资源文件_静默跳过()
        {
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "不存在.png" });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Equal(2, files.Count);   // manifest + app
        }

        [Fact]
        public void 子目录同名资源_不同内容_不冲突各自打包()
        {
            // 🔴-1 回归：不同子目录同名文件（内容不同）→ target 保留相对路径，zip 条目不冲突
            WriteImage("图标/start.png", new byte[] { 0x11 });
            WriteImage("按钮/start.png", new byte[] { 0x22 });
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "图标/start.png" });
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ImagePath = "按钮/start.png" });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            var resPaths = files.Select(f => f.Path).Where(p => p.StartsWith("res/")).OrderBy(p => p).ToList();
            Assert.Contains("res/图标/start.png", resPaths);
            Assert.Contains("res/按钮/start.png", resPaths);
        }

        [Fact]
        public void FrameWidget图片_打包()
        {
            WriteImage("frame-bg.png", new byte[] { 0x55 });
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ImagePath = "frame-bg.png" });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Contains("res/frame-bg.png", files.Select(f => f.Path));
        }

        [Fact]
        public void 同前缀目录资源_白名单正确排除()
        {
            // 🟡-1 回归：工程目录为 navihmi_deploy_test_xxx 时，navihmi_deploy_test_xxx2 下文件必须被排除
            var siblingDir = _dir + "2";
            Directory.CreateDirectory(siblingDir);
            try
            {
                File.WriteAllBytes(Path.Combine(siblingDir, "sibling.png"), new byte[] { 0x66 });
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "../" + Path.GetFileName(siblingDir) + "/sibling.png" });

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Equal(2, files.Count);   // manifest + app（sibling 被白名单排除）
            }
            finally { Directory.Delete(siblingDir, true); }
        }
    }
}
