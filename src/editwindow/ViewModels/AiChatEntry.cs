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
}
