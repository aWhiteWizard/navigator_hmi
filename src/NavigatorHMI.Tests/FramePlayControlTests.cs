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
            project.Tags.Add(new Tag { Name = "计数", DataType = TagDataType.UINT16 });   // Check ③（2026-09-05）
            project.Tags.Add(new Tag { Name = "序号", DataType = TagDataType.INT32 });    // Check ③
            project.Lists.Add(new ListDef { Name = "视频源列表A", Type = ListType.Video });   // Check ②（2026-09-05）
            project.Lists.Add(new ListDef { Name = "文本表1", Type = ListType.Text });
            project.Lists.Add(new ListDef { Name = "图片表1", Type = ListType.Image });

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

        [Fact]
        public void 视频源选择下拉_仅UINT16与INT32_哨兵首项()
        {
            // Check ③（2026-09-05）：去 INT16——UINT16/INT32 可见；INT16/BOOL/FLOAT/STRING 不可选（索引非负语义）
            var vm = MakeVm();
            var names = vm.FrameVideoIndexTagOptions.Select(t => t.Name).ToList();
            Assert.Equal("", names[0]);                       // 哨兵（（无绑定）显示）
            Assert.Contains("计数", names);                   // UINT16
            Assert.Contains("序号", names);                   // INT32
            Assert.DoesNotContain("温度", names);             // INT16 已排除（Check ③）
            Assert.DoesNotContain("开关1", names);            // BOOL
            Assert.DoesNotContain("压力", names);             // FLOAT
            Assert.DoesNotContain("标签", names);             // STRING
        }

        [Fact]
        public void 视频列表下拉_仅Video型_不含Text与Image()
        {
            // S-5 + Check 收紧（2026-09-05）：下拉仅列 Video 型（视频列表页建）——Text 型（文本列表页）与 Image 型排除
            //（Check ② 曾放开 Text——用户实测后收回：文本列表给 TextList 控件，不混入 Frame 视频列表）
            var vm = MakeVm();
            Assert.Equal("", vm.VideoListOptions[0]);         // 哨兵首项
            Assert.Contains("视频源列表A", vm.VideoListOptions);   // Video 型（视频列表页建）
            Assert.DoesNotContain("文本表1", vm.VideoListOptions);   // Text 型排除（Check 收紧）
            Assert.DoesNotContain("图片表1", vm.VideoListOptions); // Image 型排除
        }

        [Fact]
        public void 视频源选择当前绑定INT16_保留占位不静默清()
        {
            // 审查 🟡（2026-09-05 PC 复审）：③ 收紧 INT16 后——遗留工程已绑 INT16 变量 → 下拉保留该项
            // （防 SelectedItem 失配回写清空——wpf-combobox-style 场景 B 防护；与播放控制同款）
            var project = new HMIProject { Name = "视频源选择", ProjectFilePath = @"C:\vidx-test.hmiproj" };
            project.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            var f = new FrameWidget { ObjectName = "视频1", ShowVideo = true, VideoListRef = "L", VideoIndexTag = "温度" };
            project.Screens[0].Widgets.Add(f);
            project.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.INT16 });
            var vm = new PropertyViewModel { Project = project };
            vm.SelectWidgets(new[] { f });
            Assert.Contains("温度", vm.FrameVideoIndexTagOptions.Select(t => t.Name));   // INT16 已绑但被新过滤排除——占位保留
        }
    }
}
