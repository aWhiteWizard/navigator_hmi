using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NavigatorHMI.AiAgent
{
    /// <summary>Agent 任务清单里的一项（AH 2026-10-30 用户需求「Agent 应该有 ToDo」）。</summary>
    public class AgentTaskItem
    {
        public AgentTaskItem(string title, bool done = false)
        {
            Title = title ?? "";
            Done = done;
        }

        /// <summary>任务标题（模型给出的步骤描述）。</summary>
        public string Title { get; set; }

        /// <summary>是否已完成。</summary>
        public bool Done { get; set; }
    }

    /// <summary>
    /// Agent 的结构化 ToDo 清单。
    ///
    /// 为什么需要它（而不是让模型在文本里画勾选框）：文本清单只是一段话——GUI 无法渲染进度、
    /// Agent 自己也无法可靠判断"还剩几步"，于是出现「只做 20 个操作就停」这类问题。
    /// 有了结构化清单：① 侧边栏能显示 x/y 进度条；② 自动续跑有准确判据（还有未完成项就继续）；
    /// ③ 用户随时看得见 AI 到底做到哪一步。
    ///
    /// 生命周期：随会话存在（AIAgent.Reset 清空）；模型通过 `plan_tasks` 工具整份覆写。
    /// </summary>
    public class AgentTaskList
    {
        /// <summary>清单项（ObservableCollection 便于 WPF 直接绑定刷新）。</summary>
        public ObservableCollection<AgentTaskItem> Items { get; } = new();

        /// <summary>清单变化通知（GUI 用于刷新进度/滚动）。</summary>
        public event Action? Changed;

        public int TotalCount => Items.Count;
        public int DoneCount => Items.Count(i => i.Done);
        public bool HasUnfinished => Items.Any(i => !i.Done);
        public bool IsEmpty => Items.Count == 0;

        /// <summary>进度文本（如 "任务进度 3/7"）。</summary>
        public string ProgressText => Items.Count == 0 ? "" : $"任务进度 {DoneCount}/{TotalCount}";

        /// <summary>整份覆写（模型每次传完整清单，比增量更不容易出错）。</summary>
        public void Set(IEnumerable<AgentTaskItem> items)
        {
            Items.Clear();
            foreach (var it in items) Items.Add(it);
            Changed?.Invoke();
        }

        /// <summary>标记某一项完成（按标题包含匹配；找不到返回 false）。</summary>
        public bool MarkDone(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var hit = Items.FirstOrDefault(i => i.Title.Contains(title.Trim(), StringComparison.Ordinal));
            if (hit == null) return false;
            if (!hit.Done) { hit.Done = true; Changed?.Invoke(); }
            return true;
        }

        /// <summary>清空（新建会话时调用）。</summary>
        public void Clear()
        {
            if (Items.Count == 0) return;
            Items.Clear();
            Changed?.Invoke();
        }

        /// <summary>供 prompt 注入的紧凑清单（模型下一轮能看到自己做到哪）。</summary>
        public string ToPromptLine()
        {
            if (Items.Count == 0) return "";
            var parts = Items.Select((t, i) => $"{(t.Done ? "[x]" : "[ ]")}{i + 1}.{t.Title}");
            return string.Join(" ", parts);
        }
    }
}
