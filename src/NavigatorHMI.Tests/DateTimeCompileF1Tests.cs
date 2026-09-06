using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// F-1（2026-09-06 用户报告 DateTime 控件时间不变）：编译产物不再下发占位/旧残留 Text（DtText 恒空，
    /// 防 FW dtText 优先压死 1Hz 实时）；Format 透传 FW 按格式实时。运行时实时由 FW HmiDateTime
    /// （未绑定 dtFormat 1Hz / 绑定 boundTag 订阅 DataManager）承担，见 FW HmiDateTime.qml。
    /// </summary>
    public class DateTimeCompileF1Tests
    {
        private static HMIProject EmptyProject()
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_datetime_f1_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return new HMIProject
            {
                Name = "DateTimeF1",
                ProjectFilePath = Path.Combine(dir, "dt.hmiproj")
            };
        }

        private static void CleanOutput(HMIProject p)
        {
            var output = Path.Combine(Path.GetDirectoryName(p.ProjectFilePath)!, "output");
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }

        [Fact]
        public void 编译产物_未绑定DateTime_DtText不下发_Format透传()
        {
            var p = EmptyProject();
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Screens[0].Widgets.Add(new DateTimeWidget
            {
                ObjectName = "dt1",
                Text = "2026-01-01 00:00:00",   // 旧占位残留（D2 后 Text 不参与显示）
                Format = "yyyy/MM/dd HH:mm"
            });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "dt1");
                Assert.Equal(NavihmiWidgetType.DateTime, dto.Type);
                Assert.True(string.IsNullOrEmpty(dto.DtText), "未绑定 DateTime 不应下发占位/旧 Text（防 FW 死时间）");
                Assert.Equal("yyyy/MM/dd HH:mm", dto.DtFormat);
            }
            finally { CleanOutput(p); }
        }

        [Fact]
        public void 编译产物_绑定DateTime_DtText也不下发()
        {
            var p = EmptyProject();
            p.Tags.Add(new Tag { Name = "dtTag", DataType = TagDataType.DATETIME, BaseValue = "2026-09-06 12:00:00" });
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Screens[0].Widgets.Add(new DateTimeWidget { ObjectName = "dt2", BoundTag = "dtTag" });
            try
            {
                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single().Widgets.Single(w => w.ObjectName == "dt2");
                Assert.True(string.IsNullOrEmpty(dto.DtText), "绑定 DateTime 显示走 FW boundTag 订阅 DataManager 实时值，不下发静态文本");
            }
            finally { CleanOutput(p); }
        }
    }
}
