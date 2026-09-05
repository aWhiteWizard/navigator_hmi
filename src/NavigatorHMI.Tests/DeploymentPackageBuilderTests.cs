using System.IO.Compression;
using System.Text.Json;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-3b：部署包 zip 容器测试（manifest + 资源收集去重 + RTSP 不入包 + 路径穿越白名单）（2026-08-30）。
    /// 后续扩充：M-3 ① 瓦片打包（2026-08-30）、O-A1 任意路径图片收集+引用改写（2026-08-30）、
    /// P-6 Frame 视频收集（media/ 前缀 + 64MB 护栏 + RTSP 不入包，2026-09-04）。
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
        public void 同内容资源_不同路径_各自入包_不按内容去重()
        {
            // N-7（2026-08-30）：去重键由 sha256 改为工程内相对路径——图片路径是语义标识（FW 按 imagePath 加载），
            // 同内容不同路径的图片必须各自入包；原「同内容只打一份」语义取消（对齐瓦片 M-3 ① rel 键先例）
            var content = new byte[] { 0x11, 0x22 };
            WriteImage("a.png", content);
            WriteImage("b.png", content);   // 同内容不同名
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "a.png" });
            _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img2", ImagePath = "b.png" });

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Contains("res/a.png", files.Select(f => f.Path));
            Assert.Contains("res/b.png", files.Select(f => f.Path));
            Assert.Equal(2, files.Count(f => f.Path.StartsWith("res/")));
            Assert.Equal(4, files.Count);   // manifest + app + res×2

            // manifest 两条独立条目，sha256 各自计算（键已改 rel，哈希不再等于字典键）
            var manifestBytes = files.First(f => f.Path == "manifest.json").Bytes;
            var manifest = JsonSerializer.Deserialize<List<DeploymentPackageBuilder.ManifestEntry>>(
                System.Text.Encoding.UTF8.GetString(manifestBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Equal(2, manifest.Count(m => m.Type == "res"));
            Assert.All(manifest.Where(m => m.Type == "res"), m => Assert.Equal(64, m.Sha256.Length));   // SHA256 hex 长度
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
            // tiles/ 子目录 z/x/y.png 结构（Web Mercator）——路径样例 z=10/x=807/y=420、z=11/x=1615/y=840，
            // 非业务常量：仅验证「tiles/ 子目录被收集、target 保留根级前缀」的打包行为（2026-08-30 用户代码评论澄清：
            // 0x10/0x20 是临时 PNG 文件内容字节，路径是测试样例——与真实工程瓦片区域无关，不会丢地图）
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
            Assert.Equal(2, tileEntries.Count);   // 本测试场景固定写入 2 张瓦片 → 断言 2 条条目（评论：数量断言绑定测试场景，非生产逻辑）
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
            // （2026-08-30 用户代码评论澄清：0xAA,0xBB 是临时 PNG 内容字节（两瓦片同内容），路径是测试样例——
            // 与工程区域（成都/北京）无关；真实瓦片区域由工程配置的 bounds 决定，此处不涉及丢地图）
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

        // ── O-A1：任意路径图片收集入包 + .navihmi 引用改写为包内路径（2026-08-30 用户语义）──

        /// <summary>写一个含给定 ImagePath/列表项的合法 .navihmi（NavihmiProject），供改写验证。</summary>
        private void WriteValidNavihmi(string? imagePath = null, string? listItem = null, params string[] extraImages)
        {
            var dto = new NavihmiProject { Name = "部署测试", Version = "1.0" };
            if (imagePath != null)
                dto.Screens.Add(new NavihmiScreen { Name = "画面A", Type = ScreenType.Custom,
                    Widgets = { new NavihmiWidget { ObjectName = "img1", Type = NavihmiWidgetType.Image, ImagePath = imagePath } } });
            foreach (var extra in extraImages)
            {
                if (dto.Screens.Count == 0)
                    dto.Screens.Add(new NavihmiScreen { Name = "画面A", Type = ScreenType.Custom });
                dto.Screens[0].Widgets.Add(new NavihmiWidget { ObjectName = "img_" + Guid.NewGuid().ToString("N").Substring(0, 6), Type = NavihmiWidgetType.Image, ImagePath = extra });
            }
            if (listItem != null)
                dto.Lists.Add(new ListDef { Name = "图片列表", Type = ListType.Image, Items = { listItem } });
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, dto);
            File.WriteAllBytes(_navihmiPath, ms.ToArray());
        }

        private static NavihmiProject ReadNavihmiFromZip(List<(string Path, byte[] Bytes)> files)
        {
            var app = files.First(f => f.Path == "app/app.navihmi").Bytes;
            using var ms = new MemoryStream(app);
            return Serializer.Deserialize<NavihmiProject>(ms);
        }

        [Fact]
        public void 控件目录外绝对路径图片_收集入包并改写navihmi引用()
        {
            // O-A1（用户语义）：控件 ImagePath 为工程目录外绝对路径（用户文件对话框正常场景）→ 收集文件本身入包 res/<basename>，
            // 且 .navihmi 内 ImagePath 改写为包内相对路径（FW 拼 <root>/res/ 加载——设备不关心 PC 路径）
            var outsideDir = Path.Combine(Path.GetTempPath(), "navihmi_outside_dir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            var outsideImg = Path.Combine(outsideDir, "avatar.png");
            File.WriteAllBytes(outsideImg, new byte[] { 0x99 });
            try
            {
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = outsideImg });
                WriteValidNavihmi(imagePath: outsideImg);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Contains("res/avatar.png", files.Select(f => f.Path));   // 目录外文件入包（basename）

                var dto = ReadNavihmiFromZip(files);
                Assert.Equal("avatar.png", dto.Screens[0].Widgets[0].ImagePath);   // 改写为包内相对路径
            }
            finally { try { Directory.Delete(outsideDir, true); } catch { } }
        }

        [Fact]
        public void 图片列表目录外绝对路径项_收集入包并改写navihmi列表()
        {
            var outsideDir = Path.Combine(Path.GetTempPath(), "navihmi_outside_list_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            var outsideImg = Path.Combine(outsideDir, "welcome.PNG");
            File.WriteAllBytes(outsideImg, new byte[] { 0x77 });
            try
            {
                _project.Lists.Add(new ListDef { Name = "图片列表", Type = ListType.Image, Items = { outsideImg } });
                WriteValidNavihmi(listItem: outsideImg);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Contains("res/welcome.PNG", files.Select(f => f.Path));

                var dto = ReadNavihmiFromZip(files);
                Assert.Equal("welcome.PNG", dto.Lists[0].Items[0]);   // 列表项改写为包内相对路径
            }
            finally { try { Directory.Delete(outsideDir, true); } catch { } }
        }

        [Fact]
        public void 目录外同名图片_各自入包唯一化()
        {
            var outsideDir1 = Path.Combine(Path.GetTempPath(), "n1_" + Guid.NewGuid().ToString("N"));
            var outsideDir2 = Path.Combine(Path.GetTempPath(), "n2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir1); Directory.CreateDirectory(outsideDir2);
            var img1 = Path.Combine(outsideDir1, "same.png");
            var img2 = Path.Combine(outsideDir2, "same.png");
            File.WriteAllBytes(img1, new byte[] { 0x11 });
            File.WriteAllBytes(img2, new byte[] { 0x22 });
            try
            {
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = img1 });
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img2", ImagePath = img2 });
                WriteValidNavihmi(imagePath: img1);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                var resNames = files.Select(f => f.Path).Where(p => p.StartsWith("res/")).OrderBy(p => p).ToList();
                Assert.Contains("res/same.png", resNames);
                Assert.Contains("res/same_2.png", resNames);   // 第二个唯一化（加序号）
            }
            finally { try { Directory.Delete(outsideDir1, true); } catch { } try { Directory.Delete(outsideDir2, true); } catch { } }
        }

        [Fact]
        public void 目录内与目录外同名_目录内优先_遍历序无关()
        {
            // 审查 🟡1 回归：目录内根引用 a.png 与目录外 a.png 共存——目录内（规范名）必须优先得 a.png，
            // 目录外唯一化为 a_2.png；**目录外引用在前遍历序也不得错图**（修复前由遍历序决定谁活）
            var outside = Path.Combine(NewTempDir("outside_same"), "a.png");
            File.WriteAllBytes(outside, new byte[] { 0x22 });
            WriteImage("a.png", new byte[] { 0x11 });   // 工程根目录 a.png
            try
            {
                // 目录外引用在前（img1）——考验收集次序
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = outside });
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img2", ImagePath = "a.png" });
                WriteValidNavihmi(imagePath: outside, extraImages: "a.png");

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Contains("res/a.png", files.Select(f => f.Path));      // 目录内规范名
                Assert.Contains("res/a_2.png", files.Select(f => f.Path));    // 目录外唯一化

                var dto = ReadNavihmiFromZip(files);
                // 两个控件各自改写正确（目录外 img1 → a_2.png；目录内 img2 → a.png）
                var paths = dto.Screens[0].Widgets.OrderBy(w => w.ObjectName).Select(w => w.ImagePath).ToList();
                Assert.Equal("a.png", paths.Single(p => p == "a.png"));
                Assert.Equal("a_2.png", paths.Single(p => p == "a_2.png"));
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(outside)!, true); } catch { } }
        }

        [Fact]
        public void 同文件相对与绝对双拼写_两引用都改写包内路径()
        {
            // 审查 🟡2 回归：同一文件被相对拼写（GUI 存相对）与绝对拼写（AI/CLI 传绝对）各引用一次——
            // 两处引用都必须改写为包内路径（资源按包名一份；map 无条件登记，不留盘符路径在 .navihmi）
            WriteImage("welcome.PNG", new byte[] { 0x33 });
            var abs = Path.Combine(_dir, "welcome.PNG");
            try
            {
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = "welcome.PNG" });
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img2", ImagePath = abs });
                WriteValidNavihmi(imagePath: "welcome.PNG", extraImages: abs);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Equal(1, files.Count(f => f.Path == "res/welcome.PNG"));   // 资源按包名一份

                var dto = ReadNavihmiFromZip(files);
                Assert.All(dto.Screens[0].Widgets, w => Assert.Equal("welcome.PNG", w.ImagePath));   // 两引用都改写
            }
            finally { }
        }

        [Fact]
        public void 目录外同文件被多个控件引用_单份入包_引用都改写同名()
        {
            // 复审 🟡：同一目录外文件被两条控件引用（相同绝对串，GUI 对话框选同一图常见）——
            // 只入包一份（不产生 name_2 孤儿条目），两条引用都改写为同一包名
            var outside = Path.Combine(NewTempDir("multi_ref"), "avatar.png");
            File.WriteAllBytes(outside, new byte[] { 0x5A });
            try
            {
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img1", ImagePath = outside });
                _project.Screens[0].Widgets.Add(new ImageWidget { ObjectName = "img2", ImagePath = outside });
                WriteValidNavihmi(imagePath: outside, extraImages: outside);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Equal(1, files.Count(f => f.Path == "res/avatar.png"));   // 单份入包（无 avatar_2 孤儿）

                var dto = ReadNavihmiFromZip(files);
                Assert.All(dto.Screens[0].Widgets, w => Assert.Equal("avatar.png", w.ImagePath));
            }
            finally { try { Directory.Delete(Path.GetDirectoryName(outside)!, true); } catch { } }
        }

        private static string NewTempDir(string tag)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_" + tag + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ── P-6（2026-09-04）：Frame 视频模式本地视频收集（media/ 前缀 + 单文件 64MB 护栏 + RTSP 不入包）──

        /// <summary>写一个含 Frame 视频控件（ShowVideo/VideoSource）的合法 .navihmi，供视频引用改写验证。</summary>
        private void WriteValidNavihmiWithVideo(string videoSource, bool showVideo = true)
        {
            var dto = new NavihmiProject { Name = "部署测试", Version = "1.0" };
            dto.Screens.Add(new NavihmiScreen
            {
                Name = "画面A",
                Type = ScreenType.Custom,
                Widgets = { new NavihmiWidget { ObjectName = "fr1", Type = NavihmiWidgetType.Frame, ShowVideo = showVideo, VideoSource = videoSource } }
            });
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, dto);
            File.WriteAllBytes(_navihmiPath, ms.ToArray());
        }

        [Fact]
        public void Frame视频模式本地视频_入包media前缀并改写navihmi引用()
        {
            WriteImage("demo.mp4", new byte[] { 0x10, 0x20, 0x30 });
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = "demo.mp4" });
            WriteValidNavihmiWithVideo("demo.mp4");

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.Contains("media/demo.mp4", files.Select(f => f.Path));   // media/ 前缀（FW resolveVideoPath 拼 <工程目录>/media/ 加载）

            // manifest 条目 target 保留 media/ 前缀
            var manifestBytes = files.First(f => f.Path == "manifest.json").Bytes;
            var manifest = JsonSerializer.Deserialize<List<DeploymentPackageBuilder.ManifestEntry>>(
                System.Text.Encoding.UTF8.GetString(manifestBytes),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.Contains(manifest, m => m.Target == "media/demo.mp4" && m.Type == "res");

            // .navihmi 内 videoSource 改写为包内 rel（无 media 前缀——FW 端拼装）
            var dto = ReadNavihmiFromZip(files);
            Assert.Equal("demo.mp4", dto.Screens[0].Widgets[0].VideoSource);
        }

        [Fact]
        public void Frame视频RTSP流_不入包不改写_设备端直连()
        {
            const string rtsp = "rtsp://192.168.1.10:554/stream1";
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = rtsp });
            WriteValidNavihmiWithVideo(rtsp);

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.DoesNotContain(files, f => f.Path.StartsWith("media/"));   // RTSP 不入包
            var dto = ReadNavihmiFromZip(files);
            Assert.Equal(rtsp, dto.Screens[0].Widgets[0].VideoSource);        // 原样保留（设备端直连）
        }

        [Fact]
        public void 非视频模式Frame_不收集视频()
        {
            WriteImage("bg.png", new byte[] { 0x55 });
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = false, ImagePath = "bg.png" });
            WriteValidNavihmiWithVideo("", showVideo: false);

            var zipPath = Build();
            var files = ReadZip(zipPath);
            Assert.DoesNotContain(files, f => f.Path.StartsWith("media/"));   // 无视频收集
            Assert.Contains("res/bg.png", files.Select(f => f.Path));         // 普通 Frame 背景图仍按图收集
        }

        [Fact]
        public void 目录外视频文件_入包media_basename唯一化()
        {
            var outsideDir = Path.Combine(Path.GetTempPath(), "navihmi_out_video_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            var vid = Path.Combine(outsideDir, "clip.mp4");
            File.WriteAllBytes(vid, new byte[] { 0x11, 0x22 });
            try
            {
                _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = vid });
                WriteValidNavihmiWithVideo(vid);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.Contains("media/clip.mp4", files.Select(f => f.Path));
                var dto = ReadNavihmiFromZip(files);
                Assert.Equal("clip.mp4", dto.Screens[0].Widgets[0].VideoSource);   // 目录外改写为 basename
            }
            finally { try { Directory.Delete(outsideDir, true); } catch { } }
        }

        [Fact]
        public void 目录外非视频扩展名引用_拦截不入包()
        {
            // 目录外护栏同图片：仅视频扩展名白名单（防打包任意系统文件外传原语）
            var outsideDir = Path.Combine(Path.GetTempPath(), "navihmi_out_exe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            var evil = Path.Combine(outsideDir, "payload.exe");
            File.WriteAllBytes(evil, new byte[] { 0x44 });
            try
            {
                _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = evil });
                WriteValidNavihmiWithVideo(evil);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                Assert.DoesNotContain(files, f => f.Path.StartsWith("media/"));
                var dto = ReadNavihmiFromZip(files);
                Assert.Equal(evil, dto.Screens[0].Widgets[0].VideoSource);   // 未入映射 → 不改写（保持原值）
            }
            finally { try { Directory.Delete(outsideDir, true); } catch { } }
        }

        [Fact]
        public void 中文视频文件名_入包ASCII化video序号_防ffmpeg百分号编码打不开()
        {
            // Q 循环 Check（2026-09-05 黑屏第三层根因）：Qt6.4 ffmpeg 后端 avformat_open_input 拿
            // QUrl.toEncoded(PreferLocalFile) 的百分号编码串（中文→%E5%A4%A7...），ffmpeg file 协议不解码 %XX
            // → 字面 % 串打开失败「Could not open file」。media 包名强制 ASCII（video_N.ext），DTO 引用同步改写
            var outsideDir = Path.Combine(Path.GetTempPath(), "navihmi_中文视频_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            var vid = Path.Combine(outsideDir, "02_大佬演示视频.mp4");
            File.WriteAllBytes(vid, new byte[] { 0x33, 0x44 });
            try
            {
                _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = vid });
                WriteValidNavihmiWithVideo(vid);

                var zipPath = Build();
                var files = ReadZip(zipPath);
                var mediaEntry = Assert.Single(files, f => f.Path.StartsWith("media/"));   // 恰一份 media
                Assert.Matches(@"^media/video_\d+\.mp4$", mediaEntry.Path);                // ASCII video_N 名
                Assert.True(mediaEntry.Path.All(c => c < 128), $"media 包名含非 ASCII: {mediaEntry.Path}");
                var dto = ReadNavihmiFromZip(files);
                Assert.Equal(Path.GetFileName(mediaEntry.Path), dto.Screens[0].Widgets[0].VideoSource);  // DTO 同步改写
                Assert.True(dto.Screens[0].Widgets[0].VideoSource.All(c => c < 128));
            }
            finally { try { Directory.Delete(outsideDir, true); } catch { } }
        }

        [Fact]
        public void 同一中文视频多控件引用_单份入包不重复()
        {
            // 审查 🟡 回归网（2026-09-05）：absPackMap 缓存 ASCII 化后最终名——同文件多引用
            // （GUI 多控件共用同视频常见）不得生成 video_1/video_2 双份（N×体积可撞 64MB 总和拒收）
            var outsideDir = Path.Combine(Path.GetTempPath(), "navihmi_中文多引用_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outsideDir);
            var vid = Path.Combine(outsideDir, "共用_演示视频.mp4");
            File.WriteAllBytes(vid, new byte[] { 0x77, 0x88 });
            try
            {
                _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = vid });
                _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr2", ShowVideo = true, VideoSource = vid });
                // 审查 🟡-2：DTO 序列化两控件（helper 单控件 → Assert.All 失真）——两控件改写一致断言真实
                var dto2 = new NavihmiProject { Name = "多引用", Version = "1.0" };
                dto2.Screens.Add(new NavihmiScreen
                {
                    Name = "画面A",
                    Type = ScreenType.Custom,
                    Widgets =
                    {
                        new NavihmiWidget { ObjectName = "fr1", Type = NavihmiWidgetType.Frame, ShowVideo = true, VideoSource = vid },
                        new NavihmiWidget { ObjectName = "fr2", Type = NavihmiWidgetType.Frame, ShowVideo = true, VideoSource = vid }
                    }
                });
                using (var ms2 = new MemoryStream()) { Serializer.Serialize(ms2, dto2); File.WriteAllBytes(_navihmiPath, ms2.ToArray()); }

                var zipPath = Build();
                var files = ReadZip(zipPath);
                var mediaEntries = files.Where(f => f.Path.StartsWith("media/")).ToList();
                Assert.Single(mediaEntries);   // 两控件同源 → 恰一份
                Assert.True(mediaEntries[0].Path.All(c => c < 128));
                var dto = ReadNavihmiFromZip(files);
                Assert.Equal(2, dto.Screens[0].Widgets.Count(w => w.Type == NavihmiWidgetType.Frame));
                Assert.All(dto.Screens[0].Widgets.Where(w => w.Type == NavihmiWidgetType.Frame),
                    w => Assert.Equal(Path.GetFileName(mediaEntries[0].Path), w.VideoSource));   // 两控件改写一致
            }
            finally { try { Directory.Delete(outsideDir, true); } catch { } }
        }

        [Fact]
        public void 中文改名videoN与真实videoN同名_唯一化不静默错媒体()
        {
            // 审查 🟡-1 回归网（2026-09-05）：目录内中文视频改名 video_1.mp4 先占名 + 工程根级真实
            // video_1.mp4 后处理 → else-if 必须 UniquePack 唯一化（否则同 pack TryAdd 失败后者内容不入包 DTO 错指）
            WriteImage("素材/演示中文.mp4", new byte[] { 0xAA });        // 目录内中文 → ASCII 化 video_1.mp4
            WriteImage("video_1.mp4", new byte[] { 0xBB });             // 根级真实 ASCII video_1.mp4（撞改名名）
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "frA", ShowVideo = true, VideoSource = "素材/演示中文.mp4" });
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "frB", ShowVideo = true, VideoSource = "video_1.mp4" });
            var dv = new NavihmiProject { Name = "撞名", Version = "1.0" };
            dv.Screens.Add(new NavihmiScreen
            {
                Name = "画面A",
                Type = ScreenType.Custom,
                Widgets =
                {
                    new NavihmiWidget { ObjectName = "frA", Type = NavihmiWidgetType.Frame, ShowVideo = true, VideoSource = "素材/演示中文.mp4" },
                    new NavihmiWidget { ObjectName = "frB", Type = NavihmiWidgetType.Frame, ShowVideo = true, VideoSource = "video_1.mp4" }
                }
            });
            using (var ms = new MemoryStream()) { Serializer.Serialize(ms, dv); File.WriteAllBytes(_navihmiPath, ms.ToArray()); }

            var zipPath = Build();
            var files = ReadZip(zipPath);
            var mediaEntries = files.Where(f => f.Path.StartsWith("media/")).ToList();
            Assert.Equal(2, mediaEntries.Count);   // 两源各自入包（不静默丢）
            // 内容区分防错指：video_1.mp4=中文源(0xAA)、video_1_2.mp4=真实 ascii 源(0xBB)
            var byName = mediaEntries.ToDictionary(f => f.Path, f => f.Bytes);
            Assert.Equal(new byte[] { 0xAA }, byName["media/video_1.mp4"]);
            var second = mediaEntries.Single(f => f.Path != "media/video_1.mp4");
            Assert.Matches(@"^media/video_1_\d+\.mp4$", second.Path);
            Assert.Equal(new byte[] { 0xBB }, second.Bytes);
            var dto = ReadNavihmiFromZip(files);
            Assert.Equal("video_1.mp4", dto.Screens[0].Widgets.Single(w => w.ObjectName == "frA").VideoSource);
            Assert.Equal(Path.GetFileName(second.Path), dto.Screens[0].Widgets.Single(w => w.ObjectName == "frB").VideoSource);
        }

        [Fact]
        public void 视频超防呆上限_打包抛异常报错()
        {
            // T-1a（2026-09-05）：单文件护栏放宽为防呆上限 MaxUploadBytes（1GB）——原 64MB 静态上限由部署端动态 cap 取代
            WriteImage("big.mp4", new byte[] { 0x00 });
            using (var fs = new FileStream(Path.Combine(_dir, "big.mp4"), FileMode.Create, FileAccess.Write))
                fs.SetLength(1024L * 1024 * 1024 + 1);   // 稀疏扩展，不实际写 1GB
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = true, VideoSource = "big.mp4" });
            WriteValidNavihmiWithVideo("big.mp4");

            var ex = Assert.Throws<InvalidOperationException>(() => Build());
            Assert.Contains("防呆上限", ex.Message);
        }

        [Fact]
        public void Text列表被Frame引用_本地项入包改写_未引用Text不收集()
        {
            // T-3（2026-09-05 T 循环；S 循环黑屏根因回归——4_bugs video-source-list-media-packaging.md 坑 1）：
            // Text 型列表被 Frame.VideoListRef 引用 → 本地项入包 + DTO 改写（按**引用**收集而非仅 Video 型）；
            // 未引用 Text 列表项不收集（TextList 控件文本语义不受扰）；RTSP 项原样不改写。
            // 审查 🟡（2026-09-05 PC 复审）：中文目录内名 → ASCII 化 video_N（**非恒等改写**——正向改写可观测；
            // 恒等改写（ASCII 原名 pack==原路径）删掉改写循环断言也过、不可观测）
            WriteImage("素材/演示源A.mp4", new byte[] { 0xAA });   // 目录内中文 → ASCII 化 video_N.mp4
            WriteImage("clipB.mp4", new byte[] { 0xBB });
            // 模型（收集侧——videoRefs 从 project.Lists/Screens 收集）
            _project.Lists.Add(new ListDef { Name = "源表A", Type = ListType.Text, Items = { "素材/演示源A.mp4", "rtsp://192.168.1.10/desktop" } });
            _project.Lists.Add(new ListDef { Name = "文本表B", Type = ListType.Text, Items = { "clipB.mp4", "普通文本项" } });
            _project.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "frV", ShowVideo = true, VideoListRef = "源表A" });
            // DTO（改写侧——navihmiPath 反序列化对象）同构覆盖
            var dv = new NavihmiProject { Name = "Text引用", Version = "1.0" };
            dv.Lists.Add(new ListDef { Name = "源表A", Type = ListType.Text, Items = { "素材/演示源A.mp4", "rtsp://192.168.1.10/desktop" } });
            dv.Lists.Add(new ListDef { Name = "文本表B", Type = ListType.Text, Items = { "clipB.mp4", "普通文本项" } });
            dv.Screens.Add(new NavihmiScreen
            {
                Name = "画面A",
                Type = ScreenType.Custom,
                Widgets = { new NavihmiWidget { ObjectName = "frV", Type = NavihmiWidgetType.Frame, ShowVideo = true, VideoListRef = "源表A" } }
            });
            using (var ms = new MemoryStream()) { Serializer.Serialize(ms, dv); File.WriteAllBytes(_navihmiPath, ms.ToArray()); }

            var zipPath = Build();
            var files = ReadZip(zipPath);
            var mediaEntries = files.Where(f => f.Path.StartsWith("media/")).ToList();
            Assert.Single(mediaEntries);   // 仅被引用 Text 列表的源A 入包——clipB（未引用）不入
            Assert.Matches(@"^media/video_\d+\.mp4$", mediaEntries[0].Path);   // 中文目录内源 → ASCII 化（非恒等）
            var dto = ReadNavihmiFromZip(files);
            var lstA = dto.Lists.Single(l => l.Name == "源表A");
            Assert.Equal(Path.GetFileName(mediaEntries[0].Path), lstA.Items[0]);   // 正向改写为 ASCII 包名（可观测）
            Assert.Equal("rtsp://192.168.1.10/desktop", lstA.Items[1]);            // RTSP 原样（不入包不改写）
            var lstB = dto.Lists.Single(l => l.Name == "文本表B");
            Assert.Equal("clipB.mp4", lstB.Items[0]);   // 未引用 Text 未收集未改写（负向——clipB 文件真实存在仍不入包）
            Assert.Equal("普通文本项", lstB.Items[1]);
        }
    }
}
