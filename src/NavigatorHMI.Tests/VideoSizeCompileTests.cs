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
        public void 非视频模式Frame_不校验()
        {
            // ShowVideo=false（普通 Frame）即使残留大视频源路径也不校验（无文件不报）
            var r = ProjectGenerator.Compile(MakeProject("C:\\不存在\\huge.mp4", showVideo: false));
            Assert.False(r.HasErrors, string.Join("; ", r.Errors));
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
