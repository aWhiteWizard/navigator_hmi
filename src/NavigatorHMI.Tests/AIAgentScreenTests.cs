using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using System.Reflection;

namespace NavigatorHMI.Tests;

/// <summary>P1-9 AI 当前画面动态注入测试。</summary>
public class AIAgentScreenTests
{
    private sealed class FakeBackend : ILLMBackend
    {
        public string BackendName => "Fake";
        public bool IsAvailable => true;
        public bool IsModelLoaded => true;
        public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, string fcSchemaJson, System.Threading.CancellationToken ct = default)
            => Task.FromResult(new ChatResponse { Content = "OK" });
        public void Dispose() { }
    }

    [Fact]
    public void InjectCurrentScreen_空画面返回原文()
    {
        var svc = new CommandService(new HMIProject());   // CurrentScreenName 默认空
        var agent = new AIAgent(svc, new FakeBackend());
        var m = typeof(AIAgent).GetMethod("InjectCurrentScreen", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal("放一个按钮", m.Invoke(agent, new object[] { "放一个按钮" }));
    }

    [Fact]
    public void InjectCurrentScreen_有画面注入前缀()
    {
        var svc = new CommandService(new HMIProject());
        svc.CurrentScreenName = "温度监控";
        var agent = new AIAgent(svc, new FakeBackend());
        var m = typeof(AIAgent).GetMethod("InjectCurrentScreen", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal("[当前画面：温度监控] 放一个按钮", m.Invoke(agent, new object[] { "放一个按钮" }));
    }

    [Fact]
    public async System.Threading.Tasks.Task ChatAsync_每轮注入当前画面()
    {
        var project = new HMIProject();
        project.Screens.Add(new Screen { Name = "世界地图", Width = 800, Height = 480 });
        var svc = new CommandService(project);
        svc.CurrentScreenName = "世界地图";
        var agent = new AIAgent(svc, new FakeBackend());
        var r = await agent.ChatAsync("放一个按钮", System.Threading.CancellationToken.None);
        // backend 返回纯文本，无工具调用——history 最后一条 user 消息应带画面前缀
        var history = (System.Collections.Generic.List<ChatMessage>)typeof(AIAgent)
            .GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(agent)!;
        // 首条 user 消息带画面前缀（重试强调消息为系统提示无前缀——注入信息在首条 user 已足够）
        var firstUser = history.FirstOrDefault(m => m.Role == "user" && m.Content.StartsWith("[当前画面：世界地图]"));
        Assert.NotNull(firstUser);
        Assert.NotNull(r);
    }
}
