namespace NavigatorHMI.ViewModels
{
    /// <summary>AI 侧边栏对话条目。Role: user（用户）/ ai（AI 回复）/ status（系统提示，如"思考中"/错误）。</summary>
    public class AiChatEntry
    {
        public AiChatEntry() { }
        public AiChatEntry(string role, string content) { Role = role; Content = content; }
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    /// <summary>
    /// AI 侧边栏 ToDo 清单行（AH 2026-10-30 用户需求「Agent 应该有 ToDo」）。
    /// 由 AgentTaskItem 投影而来，绑定到 ItemsControl 显示 ✔/○ 与进度。
    /// </summary>
    public class AiTaskRow
    {
        public AiTaskRow(int index, string title, bool done)
        {
            Index = index;
            Title = title;
            Done = done;
        }

        /// <summary>序号（1 起）。</summary>
        public int Index { get; }
        public string Title { get; }
        public bool Done { get; }

        /// <summary>勾选标记。</summary>
        public string Mark => Done ? "✔" : "○";
        /// <summary>已完成的项显示为灰色。</summary>
        public string TextColor => Done ? "#8A8A8A" : "#333333";
    }
}
