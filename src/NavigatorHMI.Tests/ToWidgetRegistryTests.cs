using System.Reflection;
using NavigatorHMI.Common;
using ProtoBuf;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// P 循环 P-2a：ToWidget 注册表化登记完整性测试（2026-09-02）。
    /// 断言：① 全部 Widget 子类（ProtoInclude 注册的控件类型）均已注册 ToWidget 映射——
    /// 新增控件类型未注册即测试失败（防新增控件静默漏映射）；② 注册类型与 Widget 继承树无漂移；
    /// ③ 逐类型反序列化产物 DTO Type 判别与代表性子类字段正确（补"填错 Type/字段"功能盲区）。
    /// </summary>
    public class ToWidgetRegistryTests
    {
        /// <summary>反射收集 Widget 全部直接子类（ProtoInclude 注册的控件类型）。</summary>
        private static IEnumerable<Type> WidgetSubclasses()
        {
            // 从基类 ProtoInclude 特性收集（与 protobuf 反序列化注册表同源，防漏）
            var includes = typeof(Widget)
                .GetCustomAttributes(typeof(ProtoIncludeAttribute), false)
                .Cast<ProtoIncludeAttribute>()
                .Select(a => a.KnownType);
            return includes;
        }

        [Fact]
        public void 全部控件子类已注册ToWidget映射()
        {
            var declared = WidgetSubclasses().ToList();
            // 下界护栏：反射机制必须正常工作（空集合会让下方 Assert.Empty 假通过——防 vacuous）
            Assert.NotEmpty(declared);
            Assert.Contains(typeof(ButtonWidget), declared);
            Assert.Equal(18, declared.Count);   // 定值锁：当前控件类型总数（新增控件时同步更新，防反射静默失效）

            var registered = ProjectGenerator.RegisteredWidgetTypes.ToHashSet();
            var missing = declared.Where(t => !registered.Contains(t)).ToList();
            Assert.Empty(missing);   // 空 = 全部注册；新增控件未注册 → 失败并列出
        }

        [Fact]
        public void 注册表无多余类型_与ProtoInclude一致()
        {
            var declared = WidgetSubclasses().ToHashSet();
            var extra = ProjectGenerator.RegisteredWidgetTypes.Where(t => !declared.Contains(t)).ToList();
            Assert.Empty(extra);   // 注册表类型应全部来自 Widget 继承树（防僵尸注册）
        }

        /// <summary>编译产物按 ObjectName 断言各控件 DTO Type 判别（期望类型表：控件类型 → NavihmiWidgetType）。</summary>
        public static IEnumerable<object[]> WidgetTypeExpectations()
        {
            yield return new object[] { new ButtonWidget(), NavihmiWidgetType.Button };
            yield return new object[] { new TextWidget(), NavihmiWidgetType.Text };
            yield return new object[] { new LabelWidget(), NavihmiWidgetType.Label };
            yield return new object[] { new RectangleWidget(), NavihmiWidgetType.Rectangle };
            yield return new object[] { new ImageWidget(), NavihmiWidgetType.Image };
            yield return new object[] { new NumericDisplayWidget(), NavihmiWidgetType.NumericDisplay };
            yield return new object[] { new SwitchWidget(), NavihmiWidgetType.Switch };
            yield return new object[] { new LineWidget(), NavihmiWidgetType.Line };
            yield return new object[] { new CircleWidget(), NavihmiWidgetType.Circle };
            yield return new object[] { new EllipseWidget(), NavihmiWidgetType.Ellipse };
            yield return new object[] { new IOFieldWidget(), NavihmiWidgetType.IOField };
            yield return new object[] { new CheckBoxWidget(), NavihmiWidgetType.CheckBox };
            yield return new object[] { new TextListWidget(), NavihmiWidgetType.TextList };
            yield return new object[] { new FrameWidget(), NavihmiWidgetType.Frame };
            yield return new object[] { new ProgressBarWidget(), NavihmiWidgetType.ProgressBar };
            yield return new object[] { new DateTimeWidget(), NavihmiWidgetType.DateTime };
            yield return new object[] { new WindowWidget { Type = WindowType.UserView }, NavihmiWidgetType.Window };
            yield return new object[] { new PolygonWidget(), NavihmiWidgetType.Polygon };
        }

        [Theory]
        [MemberData(nameof(WidgetTypeExpectations))]
        public void 逐类型编译_产物DTO类型判别正确(Widget widget, NavihmiWidgetType expected)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_towidget_registry_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try
            {
                var p = new HMIProject
                {
                    Name = "注册表逐类型抽查",
                    ProjectFilePath = Path.Combine(dir, "registry-type-test.hmiproj")
                };
                p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
                var objName = "w_" + expected;
                widget.ObjectName = objName;
                p.Screens[0].Widgets.Add(widget);

                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                Assert.NotNull(result.OutputPath);

                // 反序列化编译产物，按 ObjectName 断言 Type 判别正确（防注册表填错 Type）
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single(s => s.Type == ScreenType.Custom).Widgets.Single(w => w.ObjectName == objName);
                Assert.Equal(expected, dto.Type);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Window控件子类字段映射_抽查()
        {
            // WindowWidget 字段最密集（含 Title 槽位复用边框色），抽查防注册表漏填
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_towidget_registry_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try
            {
                var p = new HMIProject
                {
                    Name = "注册表 Window 抽查",
                    ProjectFilePath = Path.Combine(dir, "registry-window-test.hmiproj")
                };
                p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
                var ww = new WindowWidget
                {
                    ObjectName = "win1", Type = WindowType.AlarmView, Title = "报警",
                    BorderColor = "#112233", ShowTitleBar = true, ShowHistory = true, SelectedTag = "t1"
                };
                p.Screens[0].Widgets.Add(ww);

                var result = ProjectGenerator.Compile(p);
                Assert.False(result.HasErrors, string.Join("; ", result.Errors));
                using var fs = File.OpenRead(result.OutputPath);
                var nav = Serializer.Deserialize<NavihmiProject>(fs);
                var dto = nav.Screens.Single(s => s.Type == ScreenType.Custom).Widgets.Single(w => w.ObjectName == "win1");
                Assert.Equal(NavihmiWidgetType.Window, dto.Type);
                Assert.Equal((int)WindowType.AlarmView, dto.WindowType);
                Assert.Equal("报警", dto.WinTitle);
                Assert.Equal("#112233", dto.Title);   // Title 槽位复用承载边框色
                Assert.True(dto.ShowTitleBar);
                Assert.True(dto.ShowHistory);
                Assert.Equal("t1", dto.SelectedTag);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
