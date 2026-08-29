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

            // manifest 内容校验（PropertyNameCaseInsensitive：序列化已切 CamelCase——L-A1 联调与 FW 契约对齐）
            var manifestBytes = files.First(f => f.Path == "manifest.json").Bytes;
            var manifest = JsonSerializer.Deserialize<List<DeploymentPackageBuilder.ManifestEntry>>(
                System.Text.Encoding.UTF8.GetString(manifestBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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

        // ── M-3 ①：世界地图瓦片打包（工程目录 tiles/ 子目录 z/x/y.png）──

        [Fact]
        public void 工程目录瓦片_打进包_根级tiles前缀()
        {
            // tiles/ 子目录 z/x/y.png 结构（Web Mercator）
            WriteImage("tiles/10/807/420.png", new byte[] { 0x10 });
            WriteImage("tiles/11/1615/840.png", new byte[] { 0x20 });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            var names = files.Select(f => f.Path).ToHashSet();
            Assert.Contains("tiles/10/807/420.png", names);
            Assert.Contains("tiles/11/1615/840.png", names);

            // manifest 条目（type=res，target 保留根级 tiles/ 前缀——FW httreceiver 落盘到工程目录 tiles/，单文件加载按同目录探测）
            var manifestBytes = files.First(f => f.Path == "manifest.json").Bytes;
            var manifest = JsonSerializer.Deserialize<List<DeploymentPackageBuilder.ManifestEntry>>(
                System.Text.Encoding.UTF8.GetString(manifestBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var tileEntries = manifest.Where(m => m.Target.StartsWith("tiles/")).ToList();
            Assert.Equal(2, tileEntries.Count);
            Assert.All(tileEntries, t => Assert.Equal("res", t.Type));
        }

        [Fact]
        public void 工程目录无瓦片目录_不影响普通打包()
        {
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "a.png" });
            WriteImage("a.png", new byte[] { 0xAA });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.DoesNotContain(files, f => f.Path.StartsWith("tiles/"));
            Assert.Contains("res/a.png", files.Select(f => f.Path));
        }

        [Fact]
        public void 瓦片与图片资源_内容相同_各自入包不冲突()
        {
            // 同一内容文件同时被图片引用 + 位于 tiles/ —— target 不同（res/ vs tiles/），不因哈希去重互相吞
            var content = new byte[] { 0x77 };
            WriteImage("a.png", content);
            WriteImage("tiles/10/807/420.png", content);
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "a.png" });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Contains("res/a.png", files.Select(f => f.Path));
            Assert.Contains("tiles/10/807/420.png", files.Select(f => f.Path));
        }

        [Fact]
        public void 同内容不同路径瓦片_各自入包_不按内容去重()
        {
            // M-3 ① 审查修正：瓦片路径 z/x/y.png 是语义标识（FW 按路径加载）——同内容不同路径
            // （空白/纯色块瓦片常见）必须各自入包；若按内容 sha256 去重会吞掉同内容瓦片 → FW 按路径加载缺失
            var content = new byte[] { 0xAA, 0xBB };   // 两张同内容瓦片
            WriteImage("tiles/10/807/420.png", content);
            WriteImage("tiles/10/808/420.png", content);

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Contains("tiles/10/807/420.png", files.Select(f => f.Path));
            Assert.Contains("tiles/10/808/420.png", files.Select(f => f.Path));

            // manifest 两条独立条目
            var manifestBytes = files.First(f => f.Path == "manifest.json").Bytes;
            var manifest = JsonSerializer.Deserialize<List<DeploymentPackageBuilder.ManifestEntry>>(
                System.Text.Encoding.UTF8.GetString(manifestBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal(2, manifest.Count(m => m.Target.StartsWith("tiles/")));
        }
    }
}
