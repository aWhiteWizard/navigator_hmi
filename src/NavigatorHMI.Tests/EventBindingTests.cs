using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>C8 事件命令层测试：add_event 累积 / remove_event 部分与整体 / update_event 参数替换。</summary>
    public class EventBindingTests
    {
        private static (CommandService svc, HMIProject project) Create()
        {
            var project = new HMIProject();
            project.Screens.Add(new Screen { Name = "测试画面", Width = 800, Height = 480, Type = ScreenType.Custom });
            var svc = new CommandService(project);
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "测试画面", ["widget_type"] = "button" });
            Assert.True(r.Success);
            return (svc, project);
        }

        private static Dictionary<string, object?> P(string screen, string widget, string evt, string action, Dictionary<string, string>? p = null)
            => new()
            {
                ["screen_name"] = screen,
                ["widget_name"] = widget,
                ["event_type"] = evt,
                ["action_type"] = action,
                ["params"] = p ?? new Dictionary<string, string>(),
            };

        [Fact]
        public void add_event_同事件累积动作()
        {
            var (svc, p) = Create();
            var r1 = svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "测试画面" }));
            Assert.True(r1.Success);
            var r2 = svc.Execute("add_event", P("测试画面", "button_1", "onClick", "tag_write", new() { ["tag_name"] = "温度", ["value"] = "1" }));
            Assert.True(r2.Success);
            var we = p.Screens[0].Widgets[0].Events.Single();
            Assert.Equal(EventType.onClick, we.Type);
            Assert.Equal(2, we.Actions.Count);
            Assert.Equal(ActionType.screen_switch, we.Actions[0].Type);
            Assert.Equal(ActionType.tag_write, we.Actions[1].Type);
            Assert.Equal("测试画面", we.Actions[0].Parameters["target_screen"]);
        }

        [Fact]
        public void add_event_不同事件各自独立()
        {
            var (svc, p) = Create();
            svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "测试画面" }));
            svc.Execute("add_event", P("测试画面", "button_1", "onPress", "screen_switch", new() { ["target_screen"] = "测试画面" }));
            Assert.Equal(2, p.Screens[0].Widgets[0].Events.Count);
        }

        [Fact]
        public void remove_event_指定动作_删空则事件也删()
        {
            var (svc, p) = Create();
            svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "测试画面" }));
            svc.Execute("add_event", P("测试画面", "button_1", "onClick", "tag_write", new() { ["tag_name"] = "x" }));
            // 删 1 留 1
            var r = svc.Execute("remove_event", P("测试画面", "button_1", "onClick", "screen_switch"));
            Assert.True(r.Success);
            var we = p.Screens[0].Widgets[0].Events.Single();
            Assert.Single(we.Actions);
            Assert.Equal(ActionType.tag_write, we.Actions[0].Type);
            // 删最后一个 → 事件也删
            svc.Execute("remove_event", P("测试画面", "button_1", "onClick", "tag_write"));
            Assert.Empty(p.Screens[0].Widgets[0].Events);
        }

        [Fact]
        public void remove_event_整个事件()
        {
            var (svc, p) = Create();
            svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch"));
            svc.Execute("add_event", P("测试画面", "button_1", "onPress", "tag_write"));
            var r = svc.Execute("remove_event", new Dictionary<string, object?> { ["screen_name"] = "测试画面", ["widget_name"] = "button_1", ["event_type"] = "onClick" });
            Assert.True(r.Success);
            var we = p.Screens[0].Widgets[0].Events.Single();
            Assert.Equal(EventType.onPress, we.Type);
        }

        [Fact]
        public void update_event_整组替换参数()
        {
            var (svc, p) = Create();
            svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "测试画面" }));
            var r = svc.Execute("update_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "测试画面", ["extra"] = "1" }));
            Assert.True(r.Success);
            var action = p.Screens[0].Widgets[0].Events.Single().Actions.Single();
            Assert.Equal("测试画面", action.Parameters["target_screen"]);
            Assert.Equal(2, action.Parameters.Count);   // 整组替换：旧参数清空
        }

        [Fact]
        public void 新枚举_事件与动作可解析()
        {
            Assert.True(Enum.TryParse<EventType>("onInput", out _));
            Assert.True(Enum.TryParse<EventType>("onOn", out _));
            Assert.True(Enum.TryParse<EventType>("onOff", out _));
            Assert.True(Enum.TryParse<ActionType>("screen_prev", out _));
            Assert.True(Enum.TryParse<ActionType>("tag_toggle", out _));
            Assert.True(Enum.TryParse<ActionType>("acknowledge_alarm", out _));
        }

        // ── C12-15：screen_switch 目标画面容错匹配（别名 → 正式名落库；失败回执列候选）──

        [Fact]
        public void screenSwitch_括号后缀别名匹配_落库正式名()
        {
            var (svc, p) = Create();
            p.Screens.Add(new Screen { Name = "世界地图(主)", Width = 800, Height = 480 });
            var r = svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "世界地图" }));
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("世界地图(主)", p.Screens[0].Widgets[0].Events.Single().Actions.Single().Parameters["target_screen"]);
        }

        [Fact]
        public void screenSwitch_目标画面不存在_回执列候选()
        {
            var (svc, p) = Create();
            p.Screens.Add(new Screen { Name = "泵站1", Width = 800, Height = 480 });
            p.Screens.Add(new Screen { Name = "总览", Width = 800, Height = 480 });
            var r = svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "不存在画面" }));
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Contains("候选", r.ErrorMessage);
            Assert.Contains("泵站1", r.ErrorMessage);
            Assert.Contains("总览", r.ErrorMessage);
        }

        [Fact]
        public void updateEvent_screenSwitch_容错匹配也修正()
        {
            var (svc, p) = Create();
            p.Screens.Add(new Screen { Name = "设备详情", Width = 800, Height = 480 });
            svc.Execute("add_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = "设备详情" }));
            var r = svc.Execute("update_event", P("测试画面", "button_1", "onClick", "screen_switch", new() { ["target_screen"] = " 设备详情 " }));
            Assert.True(r.Success, r.ErrorMessage);   // 去空白规范化命中
            Assert.Equal("设备详情", p.Screens[0].Widgets[0].Events.Single().Actions.Single().Parameters["target_screen"]);
        }
    }
}
