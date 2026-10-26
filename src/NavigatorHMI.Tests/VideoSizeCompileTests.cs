using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Q-4（2026-09-04 用户裁决「打包应该编译的时候做」）+ T-1a（2026-09-05 单文件护栏放宽为防呆上限 MaxUploadBytes 1GB）：
    /// Frame 视频源超防呆上限编译期报错（部署打包侧护栏保留双保险；>64MB 不再报——由部署端动态 cap 把关）；
    /// 网络流不入包不校验；小视频/无视频不误报。
    /// </summary>
    public class VideoSizeCompileTests : IDisposable
    {
        private readonly string _dir;
        public VideoSizeCompileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_vidsize_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(_dir);
        }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private HMIProject MakeProject(string videoPath, bool showVideo = true)
        {
            var p = new HMIProject { Name = "视频校验", ProjectFilePath = Path.Combine(_dir, "v.hmiproj") };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "fr1", ShowVideo = showVideo, VideoSource = videoPath });
            return p;
        }

        private static string MakeBigVideo(string dir)
        {
            var v = Path.Combine(dir, "big.mp4");
            using (var fs = new FileStream(v, FileMode.Create, FileAccess.Write))
                fs.SetLength(1024L * 1024 * 1024 + 1);   // 稀疏扩展（不实际写 1GB——超防呆上限）
            return v;
        }

        [Fact]
        public void 视频超防呆上限_编译报错()
        {
            var v = MakeBigVideo(_dir);
            var r = ProjectGenerator.Compile(MakeProject(v));
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("防呆上限") && e.Contains("fr1"));
        }

        [Fact]
        public void 小视频_编译通过()
        {
            var v = Path.Combine(_dir, "small.mp4");
            File.WriteAllBytes(v, new byte[] { 1, 2, 3 });
            var r = ProjectGenerator.Compile(MakeProject(v));
            Assert.False(r.HasErrors, string.Join("; ", r.Errors));
        }

        [Fact]
        public void RTSP源_不校验大小()
        {
            // 网络流不入包——即使字面上超限也不参与本地文件校验（无文件可查）
            var r = ProjectGenerator.Compile(MakeProject("rtsp://192.168.1.10:554/stream1"));
            Assert.False(r.HasErrors, string.Join("; ", r.Errors));
        }

        [Fact]
        public void 图片模式残留Frame_编译失败并逐控件列名()
        {
            // AD（2026-10-24 用户裁决）：Frame 恒为视频控件——残留图片配置（图片列表/背景图）⇒ 编译失败并列出控件名。
            // 用户要求"不做兼容，对应控件必须删除"，故校验必须给出画面+控件名+命中字段，便于批量清理。
            var p = new HMIProject { Name = "图片残留", ProjectFilePath = Path.Combine(_dir, "img.hmiproj") };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            // 合规 Frame 需引用**真实存在的**视频列表（否则会被"列表不存在"校验命中，与本用例判据混淆）
            p.Lists.Add(new ListDef { Name = "视频源列表1", Type = ListType.Video, Items = { "video_1.mp4" } });
            p.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "框架1", ImagePath = "bg.png", ListRef = "图片列表1" });
            p.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "视频2", VideoListRef = "视频源列表1" });   // 合规 Frame 不误报
            var r = ProjectGenerator.Compile(p);
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("框架1") && e.Contains("图片列表") && e.Contains("bg.png"));
            Assert.DoesNotContain(r.Errors, e => e.Contains("视频2"));
        }

        [Fact]
        public void 未配视频列表的Frame_编译通过()
        {
            // AD（用户裁决 ①）：未配视频列表的 Frame 允许编译——设备端显示「视频源不可用（未选择视频列表）」
            var p = new HMIProject { Name = "空源", ProjectFilePath = Path.Combine(_dir, "empty.hmiproj") };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Screens[0].Widgets.Add(new FrameWidget { ObjectName = "视频1" });
            var r = ProjectGenerator.Compile(p);
            Assert.False(r.HasErrors, string.Join("; ", r.Errors));
        }

        [Fact]
        public void 编译产物_Frame恒下发ShowVideo真()
        {
            // AD：ShowVideo 恒下发 true——即便模型值被人为置 false（旧工程残留），下发也必须为 true
            var p = MakeProject("rtsp://192.168.1.10:554/stream1", showVideo: false);
            var result = ProjectGenerator.Compile(p);
            Assert.False(result.HasErrors, string.Join("; ", result.Errors));
            using var fs = File.OpenRead(result.OutputPath);
            var nav = Serializer.Deserialize<NavihmiProject>(fs);
            Assert.True(nav.Screens.Single().Widgets.Single(w => w.ObjectName == "fr1").ShowVideo);
        }

        [Fact]
        public void 落大视频的普通Frame也校验()
        {
            // AD：ShowVideo=false 不再豁免大文件校验（Frame 恒视频，源即视频源）
            var big = MakeBigVideo(_dir);
            var r = ProjectGenerator.Compile(MakeProject(big, showVideo: false));
            Assert.True(r.HasErrors, "Frame 恒为视频控件，大文件应照常报错");
            // 复审 🟡8：必须断言是"超防呆上限"那条，否则将来无关规则命中会让本用例假阳性通过
            Assert.Contains(r.Errors, e => e.Contains("防呆上限") && e.Contains("fr1"));
        }

        [Fact]
        public void 编译产物_FramePlayTag映射到DTO78()
        {
            // R-4（2026-09-05 用户 Check）：播放控制布尔变量 → DTO 78（proto play_tag）——三方契约审计
            var p = MakeProject("rtsp://192.168.1.10:554/stream1");
            ((FrameWidget)p.Screens[0].Widgets[0]).PlayTag = "视频播放开关";
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "fr1");
                Assert.Equal("视频播放开关", dto.PlayTag);
            }
            finally
            {
                var outDir = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            }
        }
    }
}
