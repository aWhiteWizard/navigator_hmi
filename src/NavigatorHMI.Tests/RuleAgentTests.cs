using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>RuleAgent 规则模板引擎测试：画面/控件/变量/报警模板匹配与命令执行。</summary>
    public class RuleAgentTests
    {
        private static (RuleAgent agent, HMIProject project) Create()
        {
            var project = new HMIProject();
            project.Screens.Add(new Screen { Name = "全局画面", Width = 800, Height = 480, Type = ScreenType.Custom });
            var svc = new CommandService(project);
            return (new RuleAgent(svc), project);
        }

        // ── 画面 ──
        [Fact]
        public void 创建画面()
        {
            var (a, p) = Create();
            var r = a.Process("创建画面 温度监控");
            Assert.Contains("✓", r);
            Assert.Contains(p.Screens, s => s.Name == "温度监控");
        }

        [Fact]
        public void 删除画面()
        {
            var (a, p) = Create();
            p.Screens.Add(new Screen { Name = "备用", Width = 800, Height = 480, Type = ScreenType.Custom });
            var r = a.Process("删除画面 备用");
            Assert.Contains("✓", r);
            Assert.DoesNotContain(p.Screens, s => s.Name == "备用");
        }

        [Fact]
        public void 重命名画面()
        {
            var (a, p) = Create();
            p.Screens.Add(new Screen { Name = "旧名", Width = 800, Height = 480, Type = ScreenType.Custom });
            var r = a.Process("把画面旧名改名为新名");
            Assert.Contains("✓", r);
            Assert.Contains(p.Screens, s => s.Name == "新名");
        }

        [Fact]
        public void 保护画面不可删除()
        {
            var (a, p) = Create();
            p.Screens.Add(new Screen { Name = "模板", Width = 800, Height = 480, Type = ScreenType.Template });
            var r = a.Process("删除画面 模板");
            Assert.Contains("PROTECTED", r);
            Assert.Contains(p.Screens, s => s.Name == "模板");
        }

        // ── 控件 ──
        [Fact]
        public void 放置按钮到全局画面()
        {
            var (a, p) = Create();
            var r = a.Process("放一个按钮");
            Assert.Contains("✓", r);
            Assert.Single(p.Screens[0].Widgets);
            Assert.IsType<ButtonWidget>(p.Screens[0].Widgets[0]);
        }

        [Fact]
        public void 放置IO域到指定画面()
        {
            var (a, p) = Create();
            p.Screens.Add(new Screen { Name = "监控画面", Width = 800, Height = 480, Type = ScreenType.Custom });
            var r = a.Process("在监控画面上放一个IO域");
            Assert.Contains("✓", r);
            Assert.Contains(p.Screens.First(s => s.Name == "监控画面").Widgets, w => w is IOFieldWidget);
        }

        [Fact]
        public void 放置数值显示()
        {
            var (a, p) = Create();
            var r = a.Process("添加一个数值显示");
            Assert.Contains("✓", r);
            Assert.Contains(p.Screens[0].Widgets, w => w is NumericDisplayWidget);
        }

        [Fact]
        public void 放置图片()
        {
            var (a, p) = Create();
            var r = a.Process("放置一个图片");
            Assert.Contains("✓", r);
            Assert.Contains(p.Screens[0].Widgets, w => w is ImageWidget);
        }

        [Fact]
        public void 删除控件()
        {
            var (a, p) = Create();
            a.Process("放一个按钮");
            var name = p.Screens[0].Widgets[0].ObjectName;
            var r = a.Process($"删除控件{name}");
            Assert.Contains("✓", r);
            Assert.Empty(p.Screens[0].Widgets);
        }

        // ── 变量 ──
        [Fact]
        public void 创建变量默认FLOAT()
        {
            var (a, p) = Create();
            var r = a.Process("创建变量 温度");
            Assert.Contains("✓", r);
            var tag = Assert.Single(p.Tags);
            Assert.Equal(TagDataType.FLOAT, tag.DataType);
        }

        [Fact]
        public void 创建变量指定类型()
        {
            var (a, p) = Create();
            var r = a.Process("创建变量 计数 类型 INT32");
            Assert.Contains("✓", r);
            var tag = Assert.Single(p.Tags);
            Assert.Equal(TagDataType.INT32, tag.DataType);
        }

        [Fact]
        public void 删除变量()
        {
            var (a, p) = Create();
            a.Process("创建变量 温度");
            var r = a.Process("删除变量 温度");
            Assert.Contains("✓", r);
            Assert.Empty(p.Tags);
        }

        [Fact]
        public void 绑定控件到变量()
        {
            var (a, p) = Create();
            a.Process("放一个数值显示");
            a.Process("创建变量 温度");
            var name = p.Screens[0].Widgets[0].ObjectName;
            var r = a.Process($"把{name}绑定到变量温度");
            Assert.Contains("✓", r);
            Assert.Equal("温度", ((NumericDisplayWidget)p.Screens[0].Widgets[0]).BoundTag);
        }

        // ── 报警 ──
        [Fact]
        public void 创建报警()
        {
            var (a, p) = Create();
            a.Process("创建变量 温度");
            var r = a.Process("创建报警 温度过高 关联变量 温度 阈值 80");
            Assert.Contains("✓", r);
            Assert.Single(p.Alarms);
            Assert.Equal("温度过高", p.Alarms[0].Name);
        }

        [Fact]
        public void 删除报警()
        {
            var (a, p) = Create();
            a.Process("创建变量 温度");
            a.Process("创建报警 温度过高 关联变量 温度 阈值 80");
            var r = a.Process("删除报警 温度过高");
            Assert.Contains("✓", r);
            Assert.Empty(p.Alarms);
        }

        // ── 未识别 ──
        [Fact]
        public void 未识别输入返回提示()
        {
            var (a, _) = Create();
            var r = a.Process("今天天气怎么样");
            Assert.Contains("未识别", r);
        }
    }
}