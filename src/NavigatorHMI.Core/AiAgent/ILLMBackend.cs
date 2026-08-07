using System;
using System.Collections.Generic;

namespace NavigatorHMI.AiAgent
{
    /// <summary>模型请求的工具调用（OpenAI tool_calls 协议；本地 Qwen `<tool_call>` 解析后也归一到此结构）。</summary>
    public class ToolCall
    {
        public ToolCall() { }
        public ToolCall(string name, Dictionary<string, object?>? arguments = null)
        {
            Name = name;
            if (arguments != null) Arguments = arguments;
        }
        /// <summary>调用 ID（tool 消息回填用）。</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
        /// <summary>命令名（蛇形命名）。</summary>
        public string Name { get; set; } = "";
        /// <summary>命令参数。</summary>
        public Dictionary<string, object?> Arguments { get; set; } = new();
    }

    /// <summary>
    /// 对话消息。system/user 用 Content；assistant 无 tool_calls 时用 Content，有 tool_calls 时用 ToolCalls
    /// （Content 可为空）；tool 消息用 ToolCallId 关联 + Content 放执行结果。云端/本地后端按各自协议渲染。
    /// </summary>
    public class ChatMessage
    {
        public ChatMessage() { }
        public ChatMessage(string role, string content) { Role = role; Content = content; }
        /// <summary>角色：system | user | assistant | tool</summary>
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        /// <summary>assistant 消息的工具调用列表（OpenAI 协议）。</summary>
        public List<ToolCall>? ToolCalls { get; set; }
        /// <summary>tool 消息关联的调用 ID。</summary>
        public string? ToolCallId { get; set; }
    }

    /// <summary>模型响应：Content（无工具调用时的文本）或 ToolCalls（请求的工具调用）。</summary>
    public class ChatResponse
    {
        /// <summary>模型文本（无工具调用时）。</summary>
        public string Content { get; set; } = "";
        /// <summary>模型请求的工具调用列表。</summary>
        public List<ToolCall>? ToolCalls { get; set; }
        /// <summary>是否有工具调用。</summary>
        public bool IsToolCall => ToolCalls is { Count: > 0 };
        /// <summary>解析失败原因（后端尽力而为的提示）。</summary>
        public string? ParseError { get; set; }
    }

    /// <summary>
    /// LLM 推理后端抽象。本地（LLamaSharp chatml `<tool_call>`）与云端（OpenAI 兼容 HTTP tools）
    /// 实现同一接口，输出统一为 ChatResponse（Content / ToolCalls）。AIAgent 负责 agent loop
    /// （执行 tool_calls → 结果回填 → 多轮直到模型返回文本或哨兵）。
    /// </summary>
    public interface ILLMBackend : IDisposable
    {
        /// <summary>后端显示名（如 "DeepSeek API" / "本地 Qwen2.5-7B"）。</summary>
        string BackendName { get; }
        /// <summary>后端是否可用（配置/模型就绪）。</summary>
        bool IsAvailable { get; }
        /// <summary>本地模型是否已加载（云端恒 true）。</summary>
        bool IsModelLoaded { get; }
        /// <summary>
        /// 单次推理。messages 为完整对话历史（AIAgent 维护）；fcSchemaJson 为 OpenAI tools 数组 JSON。
        /// </summary>
        Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, string fcSchemaJson, System.Threading.CancellationToken ct = default);
    }
}
