using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Helpers.Creators;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// S-1/S-2（2026-09-05 用户 Check 拍板）：Frame 播放控制下拉仅布尔变量可选；
    /// Frame 新建默认 ObjectName「视频N」（S-2，前缀兼容老「框架N」防重名）。
    /// </summary>
    public class FramePlayControlTests
    {
        private static PropertyViewModel MakeVm(bool selectFrame = true)
        {
            var project = new HMIProject { Name = "播放控制", ProjectFilePath = @"C:\play-test.hmiproj" };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var f = new FrameWidget { ObjectName = "视频1", ShowVideo = true, VideoSource = "rtsp://x/1" };
            project.Screens[0].Widgets.Add(f);
            project.Tags.Add(new Tag { Name = "开关1", DataType = TagDataType.BOOL });
            project.Tags.Add(new Tag { Name = "开关2", DataType = TagDataType.BOOL });
            project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.INT16 });
            project.Tags.Add(new Tag { Name = "压力", DataType = TagDataType.FLOAT });
            project.Tags.Add(new Tag { Name = "标签", DataType = TagDataType.STRING });

            var vm = new PropertyViewModel { Project = project };
            if (selectFrame) vm.SelectWidgets(new[] { f });
            return vm;
        }

        [Fact]
        public void 播放控制下拉_仅布尔变量与哨兵()
        {
            // S-1：Frame 播放控制选项 = （无绑定）哨兵 + 工程 BOOL 变量——INT16/FLOAT/STRING 不得出现
            var vm = MakeVm();
            var names = vm.FramePlayTagOptions.Select(t => t.Name).ToList();
            Assert.Equal("", names[0]);                       // 哨兵（（无绑定）显示）
            Assert.Contains("开关1", names);
            Assert.Contains("开关2", names);
            Assert.DoesNotContain("温度", names);             // INT16 不可选
            Assert.DoesNotContain("压力", names);             // FLOAT 不可选
            Assert.DoesNotContain("标签", names);             // STRING 不可选
        }

        [Fact]
        public void 播放控制当前绑定非布尔_保留占位不静默清()
        {
            // 历史/外部工程绑了非布尔 → 下拉保留该项（防失配回写清空——wpf-combobox-style 场景 B 防护同款）
            var project = new HMIProject { Name = "播放控制", ProjectFilePath = @"C:\play-test2.hmiproj" };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var f = new FrameWidget { ObjectName = "视频1", ShowVideo = true, VideoSource = "rtsp://x/1", PlayTag = "温度" };
            project.Screens[0].Widgets.Add(f);
            project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.INT16 });
            var vm = new PropertyViewModel { Project = project };
            vm.SelectWidgets(new[] { f });
            Assert.Contains("温度", vm.FramePlayTagOptions.Select(t => t.Name));   // 保留当前值项（非布尔但已绑）
        }

        [Fact]
        public void Frame新建默认ObjectName_视频N递增()
        {
            // S-2：新拖 Frame 默认名「视频N」；老「框架N」存在时序号不撞
            var screen = new Screen { Name = "画面A", Type = ScreenType.Custom };
            screen.Widgets.Add(new FrameWidget { ObjectName = "框架1" });   // 老工程残留（不迁移）
            screen.Widgets.Add(new FrameWidget { ObjectName = "视频3" });

            var creator = new FrameWidgetCreator();
            var w = creator.Create(new System.Windows.Point(10, 10), screen);
            Assert.Equal("视频4", w.ObjectName);   // 扫描两前缀取最大 3 → +1
        }
    }
}
