using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Q-4（2026-09-04 用户裁决「打包应该编译的时候做」）：Frame 视频源 >64MB 编译期校验——
    /// 编译即报错（部署打包侧护栏保留双保险）；网络流不入包不校验；小视频/无视频不误报。
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
                fs.SetLength(64L * 1024 * 1024 + 1);   // 稀疏扩展（不实际写 64MB）
            return v;
        }

        [Fact]
        public void 视频超64MB_编译报错()
        {
            var v = MakeBigVideo(_dir);
            var r = ProjectGenerator.Compile(MakeProject(v));
            Assert.True(r.HasErrors);
            Assert.Contains(r.Errors, e => e.Contains("64MB") && e.Contains("fr1"));
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
    }
}
