using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环 K-6：AI 安全闸测试（黑名单默认 deploy/可配置 + connect 确认闸）（2026-08-30）。
    /// 注：connect 授权用例触碰 DeviceConnectionService（static 单例）——与 DeviceConnectionTests 等同 collection 串行防竞争。
    /// </summary>
    [Collection("设备连接")]
    public class AiSecurityGateTests
    {
        /// <summary>Fake 后端：按配置返回指定工具调用，次轮返回完成文本。</summary>
        private sealed class FakeBackend(string toolName, Dictionary<string, object?> args) : ILLMBackend
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
                        ToolCalls = new List<ToolCall>
                        {
                            new() { Name = toolName, Id = "call_1", Arguments = args }
                        }
                    });
                }
                return Task.FromResult(new ChatResponse { Content = "完成。" });
            }
        }

        private static Dictionary<string, object?> ConnectArgs()
            => new() { ["ip"] = "192.168.1.146" };

        private static Dictionary<string, object?> DeployArgs()
            => new() { ["device_ip"] = "192.168.1.146" };

        private static CommandService NewService()
            => new(new HMIProject { Name = "安全闸测试" });

        [Fact]
        public async Task 默认黑名单_含高风险下载_AI执行被BLOCKED()
        {
            var saved = AIAgent.Blacklist.ToList();
            try
            {
                var svc = NewService();
                var agent = new AIAgent(svc, new FakeBackend("deploy_project", DeployArgs()));
                await agent.ChatAsync("帮我把工程下载到设备");

                Assert.Contains("deploy_project", AIAgent.Blacklist);
                Assert.Contains("deploy_firmware", AIAgent.Blacklist);
                var op = Assert.Single(agent.LastOperations);
                Assert.Equal("deploy_project", op.CommandName);
                Assert.False(op.Success);   // BLOCKED 记为失败操作
            }
            finally { AIAgent.SetBlacklist(saved); }
        }

        [Fact]
        public async Task 黑名单可配置_移除后AI可执行()
        {
            var saved = AIAgent.Blacklist.ToList();
            try
            {
                AIAgent.SetBlacklist(new List<string>());   // 清空黑名单
                // deploy 需已连接（RequiresConnection 门禁）——stub 建立连接
                DeviceConnectionService.UseStub = true;
                DeviceConnectionService.Disconnect();
                try
                {
                    var svc = NewService();
                    svc.Execute("connect", ConnectArgs());
                    var agent = new AIAgent(svc, new FakeBackend("deploy_project", DeployArgs()));
                    await agent.ChatAsync("确认下载工程");   // 移除黑名单后 deploy 仍需会话内确认（ConfirmCommands 纵深防御）

                    var op = Assert.Single(agent.LastOperations);
                    Assert.Equal("deploy_project", op.CommandName);
                    Assert.True(op.Success);   // 移除黑名单 + 已连接 + 已确认 → 执行成功
                }
                finally
                {
                    DeviceConnectionService.Disconnect();
                    DeviceConnectionService.UseStub = false;
                }
            }
            finally { AIAgent.SetBlacklist(saved); }
        }

        [Fact]
        public async Task 黑名单命令_不暴露给模型_toolsJson不含()
        {
            var saved = AIAgent.Blacklist.ToList();
            try
            {
                var svc = NewService();
                var agent = new AIAgent(svc, new FakeBackend("deploy_project", DeployArgs()));
                // 反射读 _toolsJson：黑名单命令不在工具 schema（模型不可见）
                var field = typeof(AIAgent).GetField("_toolsJson", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var toolsJson = field!.GetValue(agent) as string ?? "";
                Assert.DoesNotContain("deploy_project", toolsJson);
                Assert.DoesNotContain("deploy_firmware", toolsJson);
                Assert.Contains("create_screen", toolsJson);   // 非黑名单命令仍暴露
            }
            finally { AIAgent.SetBlacklist(saved); }
        }

        [Fact]
        public async Task connect需确认_未授权返回CONFIRM_REQUIRED()
        {
            var saved = AIAgent.Blacklist.ToList();
            try
            {
                var svc = NewService();
                var agent = new AIAgent(svc, new FakeBackend("connect", ConnectArgs()));
                await agent.ChatAsync("连接设备 192.168.1.146");

                var op = Assert.Single(agent.LastOperations);
                Assert.Equal("connect", op.CommandName);
                Assert.False(op.Success);   // 未授权 → CONFIRM_REQUIRED
            }
            finally { AIAgent.SetBlacklist(saved); }
        }

        [Fact]
        public async Task connect明确授权后_可执行()
        {
            var saved = AIAgent.Blacklist.ToList();
            try
            {
                var svc = NewService();
                DeviceConnectionService.UseStub = true;
                DeviceConnectionService.Disconnect();
                try
                {
                    var agent = new AIAgent(svc, new FakeBackend("connect", ConnectArgs()));
                    await agent.ChatAsync("确认 connect 设备");   // 含「确认」+「connect」

                    var op = Assert.Single(agent.LastOperations);
                    Assert.Equal("connect", op.CommandName);
                    Assert.True(op.Success);   // 授权后执行（stub 连接成功）
                    Assert.NotNull(DeviceConnectionService.Session);
                }
                finally
                {
                    DeviceConnectionService.Disconnect();
                    DeviceConnectionService.UseStub = false;
                }
            }
            finally { AIAgent.SetBlacklist(saved); }
        }
    }
}
