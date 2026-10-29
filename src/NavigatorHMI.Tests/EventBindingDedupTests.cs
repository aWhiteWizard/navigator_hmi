using System.Collections.Generic;
using System.Linq;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.Core.Services;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AH 修复（2026-10-30 用户实测 AI 建工程三个问题的回归防线）：
    /// ① bind_event 每次新建 WidgetEvent ⇒ 7 条重复 onClick ⇒ FW 逐条输出同名 QML 属性 ⇒
    ///    "Property value set multiple times" ⇒ 整个画面加载失败 = 设备纯色屏；
    /// ② update_event 只改首个匹配 ⇒ 7 条里改 1 条 ⇒ 编译仍报错，用户看到"改了不生效"；
    /// ③ 动作缺必填参数（如 screen_switch 的 target_screen）设备端静默无效 ⇒ 编译期现在必须报错。
    /// </summary>
    public class EventBindingDedupTests
    {
        private static (CommandService svc, HMIProject project) Create()
        {
            var project = new HMIProject { Name = "事件去重" };
            project.Screens.Add(new Screen { Name = "测试画面", Type = ScreenType.Custom, Width = 1024, Height = 600 });
            var svc = new CommandService(project);
            var r = svc.Execute("add_widget", new Dictionary<string, object?>
            {
                ["screen_name"] = "测试画面",
                ["widget_type"] = "button",
            });
            Assert.True(r.Success);
            return (svc, project);
        }

        private static Dictionary<string, object?> Bind(string evt, string action, Dictionary<string, string>? p = null)
            => new()
            {
                ["screen_name"] = "测试画面",
                ["widget_name"] = "button_1",
                ["event"] = evt,
                ["action"] = action,
                ["params"] = p ?? new Dictionary<string, string>(),
            };

        private static Dictionary<string, object?> Update(string evt, string action, Dictionary<string, string> p)
            => new()
            {
                ["screen_name"] = "测试画面",
                ["widget_name"] = "button_1",
                ["event_type"] = evt,
                ["action_type"] = action,
                ["params"] = p,
            };

        [Fact]
        public void bind_event_同事件重复调用_合并为一条事件()
        {
            var (svc, project) = Create();
            Assert.True(svc.Execute("bind_event", Bind("onClick", "screen_prev")).Success);
            Assert.True(svc.Execute("bind_event", Bind("onClick", "screen_next")).Success);
            var events = project.Screens[0].Widgets[0].Events;
            // 关键：只有 1 条 onClick（旧行为是 2 条 ⇒ FW 输出 2 个 onHmiClicked ⇒ 白屏）
            Assert.Single(events);
            Assert.Equal(2, events[0].Actions.Count);
        }

        [Fact]
        public void bind_event_同事件三次调用_仍只有一条事件()
        {
            var (svc, project) = Create();
            svc.Execute("bind_event", Bind("onClick", "screen_prev"));
            svc.Execute("bind_event", Bind("onClick", "screen_next"));
            svc.Execute("bind_event", Bind("onClick", "tag_toggle", new() { ["tag_name"] = "温度" }));
            Assert.Single(project.Screens[0].Widgets[0].Events);
        }

        [Fact]
        public void add_event_完全相同动作_返回DUPLICATE且不重复追加()
        {
            var (svc, project) = Create();
            var args = new Dictionary<string, object?>
            {
                ["screen_name"] = "测试画面",
                ["widget_name"] = "button_1",
                ["event_type"] = "onClick",
                ["action_type"] = "tag_toggle",
                ["params"] = new Dictionary<string, string> { ["tag_name"] = "温度" },
            };
            Assert.True(svc.Execute("add_event", args).Success);
            var second = svc.Execute("add_event", args);
            Assert.False(second.Success);
            Assert.Equal("DUPLICATE", second.ErrorCode);
            Assert.Single(project.Screens[0].Widgets[0].Events[0].Actions);
        }

        [Fact]
        public void update_event_一次改完全部重复事件的动作参数()
        {
            var (svc, project) = Create();
            // 手工造 3 条重复 onClick（模拟历史数据）
            for (int i = 0; i < 3; i++)
                project.Screens[0].Widgets[0].Events.Add(new WidgetEvent
                {
                    Type = EventType.onClick,
                    Actions = new() { new EventAction { Type = ActionType.screen_switch } },
                });

            var r = svc.Execute("update_event", Update("onClick", "screen_switch",
                new Dictionary<string, string> { ["target_screen"] = "测试画面" }));
            Assert.True(r.Success, r.ErrorMessage);
            var all = project.Screens[0].Widgets[0].Events.SelectMany(e => e.Actions).ToList();
            Assert.Equal(3, all.Count);
            Assert.All(all, a => Assert.Equal("测试画面", a.Parameters["target_screen"]));
        }

        [Fact]
        public void compile_动作缺必填参数_报错并点名参数()
        {
            var (svc, project) = Create();
            svc.Execute("bind_event", Bind("onClick", "screen_switch"));   // 故意不给 target_screen
            project.ProjectFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ev_dedup_" + System.Guid.NewGuid().ToString("N")[..6] + ".hmiproj");
            var result = ProjectGenerator.Compile(project);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("target_screen") && e.Contains("screen_switch"));
        }

        [Fact]
        public void compile_重复事件类型_给出告警但不阻断()
        {
            var (svc, project) = Create();
            for (int i = 0; i < 3; i++)
                project.Screens[0].Widgets[0].Events.Add(new WidgetEvent
                {
                    Type = EventType.onClick,
                    Actions = new() { new EventAction { Type = ActionType.screen_prev } },
                });
            project.ProjectFilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ev_dup_" + System.Guid.NewGuid().ToString("N")[..6] + ".hmiproj");
            var result = ProjectGenerator.Compile(project);
            Assert.Contains(result.Warnings, w => w.Contains("重复定义") && w.Contains("onClick"));
        }
    }
}
