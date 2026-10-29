using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AH 修复（2026-10-30 用户实测）：AI 传 `params:{"target_screen":"设备诊断"}` 时，
    /// 云端后端把嵌套对象转成 **JSON 字符串**，而命令层判定 `raw is Dictionary&lt;string,string&gt;`，
    /// 不匹配就静默变空字典 ⇒ 动作永远"缺必填参数"，AI 改 9 步都没用。
    /// 本测试锁死 AIAgent 侧的归一化：JSON 对象串 / Dictionary&lt;string,object&gt; / K=V 串 都要落成
    /// Dictionary&lt;string,string&gt;，并验证 execute 之后事件动作真的带上了参数。
    /// </summary>
    public class AgentDictParamTests
    {
        private sealed class NoopBackend : ILLMBackend
        {
            public string BackendName => "Fake";
            public bool IsAvailable => true;
            public bool IsModelLoaded => true;
            public System.Threading.Tasks.Task<ChatResponse> ChatAsync(
                IReadOnlyList<ChatMessage> messages, string fcSchemaJson, System.Threading.CancellationToken ct = default)
                => System.Threading.Tasks.Task.FromResult(new ChatResponse { Content = "OK" });
            public void Dispose() { }
        }

        private static (AIAgent agent, CommandService svc, HMIProject project) Create()
        {
            var project = new HMIProject { Name = "AI参数字典" };
            project.Screens.Add(new Screen { Name = "欢迎页", Type = ScreenType.Custom, Width = 1024, Height = 600 });
            var svc = new CommandService(project);
            svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "欢迎页", ["widget_type"] = "button" });
            var agent = new AIAgent(svc, new NoopBackend());
            return (agent, svc, project);
        }

        private static object? InvokeNormalize(AIAgent agent, string cmd, Dictionary<string, object?> args)
        {
            var m = typeof(AIAgent).GetMethod("NormalizeDictArgs", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return m.Invoke(agent, new object?[] { cmd, args });
        }

        private static CommandResult InvokeExecute(AIAgent agent, string cmd, Dictionary<string, object?> args)
        {
            var m = typeof(AIAgent).GetMethod("ExecuteCommand", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (CommandResult)m.Invoke(agent, new object?[] { cmd, args })!;
        }

        [Fact]
        public void 归一化_JSON对象字符串_转成字典()
        {
            var (agent, _, _) = Create();
            var args = new Dictionary<string, object?>
            {
                ["screen_name"] = "欢迎页",
                ["widget_name"] = "button_1",
                ["event"] = "onClick",
                ["action"] = "screen_switch",
                ["params"] = "{\"target_screen\":\"欢迎页\"}",   // ← 后端 JsonValueToObject 的产物形态
            };
            var r = (Dictionary<string, object?>)InvokeNormalize(agent, "bind_event", args)!;
            var dict = Assert.IsType<Dictionary<string, string>>(r["params"]);
            Assert.Equal("欢迎页", dict["target_screen"]);
        }

        [Fact]
        public void 归一化_KV串_转成字典()
        {
            var (agent, _, _) = Create();
            var args = new Dictionary<string, object?>
            {
                ["screen_name"] = "欢迎页",
                ["widget_name"] = "button_1",
                ["event"] = "onClick",
                ["action"] = "tag_write",
                ["params"] = "tag_name=温度,value=1",
            };
            var r = (Dictionary<string, object?>)InvokeNormalize(agent, "bind_event", args)!;
            var dict = Assert.IsType<Dictionary<string, string>>(r["params"]);
            Assert.Equal("温度", dict["tag_name"]);
            Assert.Equal("1", dict["value"]);
        }

        [Fact]
        public void 归一化_params_json别名_被采纳()
        {
            var (agent, _, _) = Create();
            var args = new Dictionary<string, object?>
            {
                ["screen_name"] = "欢迎页",
                ["widget_name"] = "button_1",
                ["event"] = "onClick",
                ["action"] = "screen_switch",
                ["params_json"] = "{\"target_screen\":\"欢迎页\"}",
            };
            var r = (Dictionary<string, object?>)InvokeNormalize(agent, "bind_event", args)!;
            var dict = Assert.IsType<Dictionary<string, string>>(r["params"]);
            Assert.Equal("欢迎页", dict["target_screen"]);
        }

        [Fact]
        public void 执行bind_event_动作参数真的落库()
        {
            var (agent, _, project) = Create();
            var r = InvokeExecute(agent, "bind_event", new Dictionary<string, object?>
            {
                ["screen_name"] = "欢迎页",
                ["widget_name"] = "button_1",
                ["event"] = "onClick",
                ["action"] = "screen_switch",
                ["params"] = "{\"target_screen\":\"欢迎页\"}",   // 关键：JSON 字符串形态
            });
            Assert.True(r.Success, r.ErrorMessage);
            var act = project.Screens[0].Widgets[0].Events.Single().Actions.Single();
            Assert.Equal("欢迎页", act.Parameters["target_screen"]);
        }

        [Fact]
        public void 执行add_event_平铺参数兜底落库()
        {
            var (agent, _, project) = Create();
            var r = InvokeExecute(agent, "add_event", new Dictionary<string, object?>
            {
                ["screen_name"] = "欢迎页",
                ["widget_name"] = "button_1",
                ["event_type"] = "onClick",
                ["action_type"] = "screen_switch",
                ["target_screen"] = "欢迎页",   // ← 模型另一种写法：动作参数平铺在顶层
            });
            Assert.True(r.Success, r.ErrorMessage);
            var act = project.Screens[0].Widgets[0].Events.Single().Actions.Single();
            Assert.Equal("欢迎页", act.Parameters["target_screen"]);
        }
    }
}
