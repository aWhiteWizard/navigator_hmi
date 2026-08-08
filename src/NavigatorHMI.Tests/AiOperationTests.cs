using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>AIAgent 操作清单记录（A13 回归：BLOCKED/CLI 路径各记一条）。</summary>
    public class AiOperationTests
    {
        /// <summary>Fake 后端：首轮返回 create_screen 工具调用，次轮返回完成文本。</summary>
        private class FakeBackend : ILLMBackend
        {
            public string BackendName => "fake";
            public bool IsAvailable => true;
            public bool IsModelLoaded => true;
            public void Dispose() { }
            private int _round;
            public Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessage> history, string toolsJson, CancellationToken ct = default)
            {
                if (_round++ == 0)
                {
                    return Task.FromResult(new ChatResponse
                    {
                        ToolCalls = new List<ToolCall> { new() { Name = "create_screen", Id = "call_1", Arguments = new Dictionary<string, object?> { ["name"] = "温度监控" } } }
                    });
                }
                return Task.FromResult(new ChatResponse { Content = "已创建画面 温度监控。" });
            }
        }

        [Fact]
        public async Task 操作清单记录执行的命令()
        {
            var project = new HMIProject();
            var svc = new CommandService(project);
            var agent = new AIAgent(svc, new FakeBackend());
            await agent.ChatAsync("创建一个画面叫温度监控");
            var op = Assert.Single(agent.LastOperations);
            Assert.Equal("create_screen", op.CommandName);
            Assert.True(op.Success);
            Assert.Contains("温度监控", op.ArgsSummary);
            Assert.Contains(project.Screens, s => s.Name == "温度监控");
        }
    }
}