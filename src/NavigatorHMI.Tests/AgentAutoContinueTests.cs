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
    /// AH 修复（2026-10-30 用户实测「AI 只做 20 个操作就停了，以前能做 98 个」）：
    /// 提示词要求模型"每完成一步重发清单"，模型照做后返回**纯文本** ⇒ Agent 循环遇到纯文本即 return
    /// ⇒ 大任务被切碎、几十步就停。修复后：文本里仍有未勾选项（`- [ ]`）时 Agent **自动续跑**
    /// （插一条"继续执行"提示继续循环），不要求用户手动回「继续」。
    /// </summary>
    public class AgentAutoContinueTests
    {
        /// <summary>按脚本依次返回：工具调用 → 带未完成清单的文本 → 工具调用 → 完成文本。</summary>
        private sealed class ScriptedBackend : ILLMBackend
        {
            private readonly Queue<ChatResponse> _script;
            public int Calls { get; private set; }
            public ScriptedBackend(Queue<ChatResponse> script) { _script = script; }
            public string BackendName => "Scripted";
            public bool IsAvailable => true;
            public bool IsModelLoaded => true;
            public System.Threading.Tasks.Task<ChatResponse> ChatAsync(
                IReadOnlyList<ChatMessage> messages, string fcSchemaJson, System.Threading.CancellationToken ct = default)
            {
                Calls++;
                var r = _script.Count > 0 ? _script.Dequeue() : new ChatResponse { Content = "结束" };
                return System.Threading.Tasks.Task.FromResult(r);
            }
            public void Dispose() { }
        }

        private static ChatResponse Tool(string name, Dictionary<string, object?> args) => new()
        {
            ToolCalls = new List<ToolCall> { new(name, args) { Id = "t" + name } },
        };

        private static ChatResponse Text(string t) => new() { Content = t };

        private static (AIAgent agent, CommandService svc, HMIProject project, ScriptedBackend backend) Create(Queue<ChatResponse> script)
        {
            var project = new HMIProject { Name = "自动续跑" };
            var svc = new CommandService(project);
            var backend = new ScriptedBackend(script);
            var agent = new AIAgent(svc, backend) { MaxIterations = 10, MaxAutoContinues = 5 };
            return (agent, svc, project, backend);
        }

        [Fact]
        public async System.Threading.Tasks.Task 文本含未勾选项_自动续跑直到完成()
        {
            var script = new Queue<ChatResponse>(new[]
            {
                Tool("create_screen", new Dictionary<string, object?> { ["name"] = "画面A" }),
                Text("- [x] 第1步：建画面A\n- [ ] 第2步：建画面B"),   // ← 中途汇报（未完成）
                Tool("create_screen", new Dictionary<string, object?> { ["name"] = "画面B" }),
                Text("- [x] 第1步：建画面A\n- [x] 第2步：建画面B\n全部完成"),
            });
            var (agent, _, project, backend) = Create(script);

            var reply = await agent.ChatAsync("按文档建两个画面");

            Assert.Contains("全部完成", reply);
            // 关键：两个画面都建出来了（旧实现停在第二个文本回复，画面B 不会存在）
            Assert.Contains(project.Screens, s => s.Name == "画面A");
            Assert.Contains(project.Screens, s => s.Name == "画面B");
            Assert.True(backend.Calls >= 4, $"应至少调用 4 次后端，实际 {backend.Calls}");
        }

        [Fact]
        public async System.Threading.Tasks.Task 文本已全部勾选_不再续跑直接结束()
        {
            var script = new Queue<ChatResponse>(new[]
            {
                Tool("create_screen", new Dictionary<string, object?> { ["name"] = "画面A" }),
                Text("- [x] 第1步：建画面A\n任务完成"),   // ← 无未勾选项
            });
            var (agent, _, _, backend) = Create(script);

            var reply = await agent.ChatAsync("建一个画面");

            Assert.Contains("任务完成", reply);
            Assert.Equal(2, backend.Calls);   // 不再多发一轮
        }

        [Fact]
        public void LooksUnfinished_判定规则()
        {
            var m = typeof(AIAgent).GetMethod("LooksUnfinished", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool F(string s) => (bool)m.Invoke(null, new object[] { s })!;
            Assert.True(F("- [ ] 第2步"));
            Assert.True(F("还需要继续执行剩余步骤"));
            Assert.True(F("回复「继续」从第3步接着做"));
            Assert.False(F("- [x] 第1步\n- [x] 第2步\n全部完成"));
            Assert.False(F("你好，有什么可以帮你？"));
            Assert.False(F(""));
        }
    }
}
