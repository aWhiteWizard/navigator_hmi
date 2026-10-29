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
    /// AH（2026-10-30 用户需求「Agent 应该有 ToDo」）：Agent 维护**结构化**任务清单，
    /// 模型通过 plan_tasks 工具整份登记/更新，GUI 侧边栏显示 x/y 进度，
    /// 自动续跑以"还有未完成项"为准（不再靠文本关键词猜）。
    /// </summary>
    public class AgentTaskListTests
    {
        private sealed class ScriptedBackend : ILLMBackend
        {
            private readonly Queue<ChatResponse> _script;
            public ScriptedBackend(Queue<ChatResponse> s) { _script = s; }
            public string BackendName => "Scripted";
            public bool IsAvailable => true;
            public bool IsModelLoaded => true;
            public System.Threading.Tasks.Task<ChatResponse> ChatAsync(
                IReadOnlyList<ChatMessage> msgs, string schema, System.Threading.CancellationToken ct = default)
                => System.Threading.Tasks.Task.FromResult(_script.Count > 0 ? _script.Dequeue() : new ChatResponse { Content = "结束" });
            public void Dispose() { }
        }

        private static ChatResponse Tool(string name, Dictionary<string, object?> args) => new()
        {
            ToolCalls = new List<ToolCall> { new(name, args) { Id = "t" } },
        };

        private static (AIAgent agent, CommandService svc, HMIProject project, ScriptedBackend backend) Create(Queue<ChatResponse> script)
        {
            var project = new HMIProject { Name = "ToDo" };
            var svc = new CommandService(project);
            var backend = new ScriptedBackend(script);
            var agent = new AIAgent(svc, backend) { MaxIterations = 12, MaxAutoContinues = 5 };
            return (agent, svc, project, backend);
        }

        [Fact]
        public void plan_tasks_登记清单_进度与未完成判定正确()
        {
            var (agent, _, _, _) = Create(new Queue<ChatResponse>());
            var m = typeof(AIAgent).GetMethod("ExecuteCommand", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var r = (CommandResult)m.Invoke(agent, new object?[]
            {
                AIAgent.PlanTasksTool,
                new Dictionary<string, object?>
                {
                    ["items"] = "[x] 第1步：建变量\n[ ] 第2步：建画面\n[ ] 第3步：放控件",
                },
            })!;

            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(3, agent.Tasks.TotalCount);
            Assert.Equal(1, agent.Tasks.DoneCount);
            Assert.True(agent.Tasks.HasUnfinished);
            Assert.Equal("任务进度 1/3", agent.Tasks.ProgressText);
        }

        [Fact]
        public void plan_tasks_支持JSON数组形态()
        {
            var (agent, _, _, _) = Create(new Queue<ChatResponse>());
            var m = typeof(AIAgent).GetMethod("ExecuteCommand", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var r = (CommandResult)m.Invoke(agent, new object?[]
            {
                AIAgent.PlanTasksTool,
                new Dictionary<string, object?> { ["items"] = "[\"建变量\", {\"title\":\"建画面\",\"done\":true}]" },
            })!;

            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(2, agent.Tasks.TotalCount);
            Assert.True(agent.Tasks.Items[1].Done);
        }

        [Fact]
        public async System.Threading.Tasks.Task 清单未完成_即使文本无关键词也会自动续跑()
        {
            // 关键场景：模型登记了 ToDo，然后只回一句"第1步做完了"（无 `- [ ]`、无"继续"字样）
            // 旧逻辑会直接结束；新逻辑看清单里还有未完成项 ⇒ 自动续跑把剩下步骤做完。
            var script = new Queue<ChatResponse>(new[]
            {
                Tool(AIAgent.PlanTasksTool, new Dictionary<string, object?>
                {
                    ["items"] = "[ ] 第1步：建画面A\n[ ] 第2步：建画面B",
                }),
                Tool("create_screen", new Dictionary<string, object?> { ["name"] = "画面A" }),
                new ChatResponse { Content = "第1步已完成。" },   // ← 纯文本、无关键词
                Tool(AIAgent.PlanTasksTool, new Dictionary<string, object?>
                {
                    ["items"] = "[x] 第1步：建画面A\n[ ] 第2步：建画面B",
                }),
                Tool("create_screen", new Dictionary<string, object?> { ["name"] = "画面B" }),
                Tool(AIAgent.PlanTasksTool, new Dictionary<string, object?>
                {
                    ["items"] = "[x] 第1步：建画面A\n[x] 第2步：建画面B",
                }),
                new ChatResponse { Content = "全部完成。" },
            });
            var (agent, _, project, backend) = Create(script);

            var reply = await agent.ChatAsync("按文档建两个画面");

            Assert.Contains("全部完成", reply);
            Assert.Contains(project.Screens, s => s.Name == "画面A");
            Assert.Contains(project.Screens, s => s.Name == "画面B");
            Assert.Equal(2, agent.Tasks.DoneCount);
            Assert.False(agent.Tasks.HasUnfinished);
        }

        [Fact]
        public void 新会话_清空清单()
        {
            var (agent, _, _, _) = Create(new Queue<ChatResponse>());
            var m = typeof(AIAgent).GetMethod("ExecuteCommand", BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(agent, new object?[]
            {
                AIAgent.PlanTasksTool,
                new Dictionary<string, object?> { ["items"] = "[ ] 步骤1" },
            });
            Assert.Equal(1, agent.Tasks.TotalCount);
            agent.Reset();
            Assert.Equal(0, agent.Tasks.TotalCount);
        }
    }
}
