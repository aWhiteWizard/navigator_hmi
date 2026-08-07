using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace NavigatorHMI.AiAgent
{
    /// <summary>
    /// 本地 LLM 后端：LLamaSharp 加载 GGUF 模型（Qwen2.5-7B Q4_K_M），CPU 推理。
    /// 使用 Qwen 原生 Function Calling：手工构造 chatml 文本（system 注入 tools + 消息历史），
    /// StatelessExecutor 纯文本推理（不走 LLamaSharp template，完全控制 prompt 格式）。
    /// 消息历史由 AIAgent 维护，每次 ChatAsync 完整重放。
    /// </summary>
    public class LocalLLMBackend : ILLMBackend
    {
        private readonly string _modelPath;
        private readonly int _contextSize;
        private readonly int _maxTokens;
        private LLamaWeights? _model;
        private LLamaContext? _context;
        private StatelessExecutor? _executor;
        private readonly object _loadLock = new();

        public LocalLLMBackend(string modelPath, int contextSize = 8192, int maxTokens = 512)
        {
            _modelPath = modelPath;
            _contextSize = contextSize;
            _maxTokens = maxTokens;
        }

        public string BackendName => "本地 Qwen2.5-7B";
        public bool IsAvailable => File.Exists(_modelPath);
        public bool IsModelLoaded => _model != null;
        /// <summary>最近一次加载失败原因（供 UI 提示）。</summary>
        public string LoadError { get; private set; } = "";

        /// <summary>
        /// 默认模型路径查找顺序：① 环境变量 NAVIGATOR_HMI_MODEL → ② exe 目录 models/ → ③ 逐级向上找 models/
        /// （仓库根放一份即可，GUI/CLI 共用，避免复制多份 4.4GB）。
        /// </summary>
        public static string DefaultModelPath
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("NAVIGATOR_HMI_MODEL");
                if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
                var local = Path.Combine(AppContext.BaseDirectory, "models", ModelFileName);
                if (File.Exists(local)) return local;
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    var candidate = Path.Combine(dir.FullName, "models", ModelFileName);
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
                return local;
            }
        }

        /// <summary>模型文件名（Qwen2.5-7B-Instruct Q4_K_M，约 4.4GB）。</summary>
        public const string ModelFileName = "qwen2.5-7b-instruct-q4_k_m.gguf";

        /// <summary>加载模型（线程安全；幂等）。失败返回 false 并记录 LoadError。</summary>
        public bool Load()
        {
            lock (_loadLock)
            {
                if (_model != null) return true;
                try
                {
                    var parameters = new ModelParams(_modelPath)
                    {
                        ContextSize = (uint)_contextSize,
                        GpuLayerCount = 0,   // 本机无 NVIDIA GPU，纯 CPU 推理
                    };
                    _model = LLamaWeights.LoadFromFile(parameters);
                    _context = _model.CreateContext(parameters);
                    _executor = new StatelessExecutor(_model, parameters);
                    return true;
                }
                catch (Exception ex)
                {
                    // 清理部分创建的资源（LoadFromFile 成功但 CreateContext/Executor 失败时防 4.4GB 模型泄漏 + 下次 Load 假成功）
                    _executor = null;
                    _context?.Dispose();
                    _context = null;
                    _model?.Dispose();
                    _model = null;
                    LoadError = ex.Message;
                    return false;
                }
            }
        }

        public async Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, string fcSchemaJson, CancellationToken ct = default)
        {
            if (_executor == null && !Load())
                throw new InvalidOperationException("模型加载失败: " + LoadError);
            if (messages == null || messages.Count == 0) return new ChatResponse();

            var prompt = BuildChatmlPrompt(messages, fcSchemaJson);
            var inference = new InferenceParams
            {
                MaxTokens = _maxTokens,
                AntiPrompts = new List<string> { "<|im_end|>", "<|endoftext|>", "</tool_call>" },   // 注：多 <tool_call> 时首块后截断（Qwen 一次一个调用，影响小）
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.2f },   // FC 场景低温度：确定性优先
                OverflowStrategy = ContextOverflowStrategy.TruncateAndReprefill,   // 长 tools schema 溢出时自动截断重填，不抛异常
            };

            var sb = new StringBuilder();
            await foreach (var token in _executor.InferAsync(prompt, inference, ct))
                sb.Append(token);
            return ParseResponse(sb.ToString());
        }

        /// <summary>解析模型输出：提取 &lt;tool_call&gt; 块 → ToolCalls；否则为普通文本。</summary>
        private static ChatResponse ParseResponse(string text)
        {
            var resp = new ChatResponse();
            var calls = new List<ToolCall>();
            int idx = 0;
            while (true)
            {
                var start = text.IndexOf("<tool_call>", idx, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                var contentStart = start + "<tool_call>".Length;
                var end = text.IndexOf("</tool_call>", contentStart, StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;
                TryParseToolCall(text[contentStart..end].Trim(), calls);
                idx = end + "</tool_call>".Length;
            }
            if (calls.Count > 0) { resp.ToolCalls = calls; return resp; }
            resp.Content = text.Trim();
            return resp;
        }

        private static void TryParseToolCall(string json, List<ToolCall> calls)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return;
                if (!root.TryGetProperty("name", out var name) || name.ValueKind != System.Text.Json.JsonValueKind.String) return;
                var tc = new ToolCall(name.GetString()!);
                if (root.TryGetProperty("arguments", out var args) && args.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var p in args.EnumerateObject())
                        tc.Arguments[p.Name] = JsonValueToObject(p.Value);
                calls.Add(tc);
            }
            catch (System.Text.Json.JsonException) { /* 单块解析失败忽略，其他块继续 */ }
        }

        private static object? JsonValueToObject(System.Text.Json.JsonElement el) => el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => el.GetString(),
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : (el.TryGetDouble(out var d) ? d : el.GetRawText()),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => el.GetRawText(),
        };

        /// <summary>
        /// 构造 Qwen2.5 chatml 文本：system（规则 + tools 注入）→ 消息历史 → assistant 前缀。
        /// tools 由后端注入 system（Qwen 原生 FC 通道），AIAgent 的 system 消息只含规则文本。
        /// </summary>
        private static string BuildChatmlPrompt(IReadOnlyList<ChatMessage> messages, string toolsJson)
        {
            var sb = new StringBuilder();
            // system：取 AIAgent 提供的规则文本 + 追加 tools（Qwen 原生 tools 段）
            string systemRule = "";
            foreach (var m in messages)
                if (m.Role == "system") { systemRule = m.Content; break; }
            sb.Append("<|im_start|>system\n");
            if (systemRule.Length > 0) sb.Append(systemRule).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(toolsJson))
                sb.Append("可用工具（tools，调用时输出 <tool_call> 包裹的 JSON）：\n").Append(toolsJson).Append('\n');
            sb.Append("<|im_end|>\n");

            // 历史消息（跳过 system，避免 tools 重复）
            foreach (var m in messages)
            {
                switch (m.Role)
                {
                    case "system": break;
                    case "user":
                        sb.Append("<|im_start|>user\n").Append(m.Content).Append("\n<|im_end|>\n");
                        break;
                    case "assistant":
                        if (m.ToolCalls is { Count: > 0 })
                        {
                            // Qwen 原生 FC：assistant 输出 <tool_call> 包裹的调用 JSON
                            sb.Append("<|im_start|>assistant\n");
                            foreach (var tc in m.ToolCalls)
                            {
                                sb.Append("<tool_call>\n");
                                sb.Append(System.Text.Json.JsonSerializer.Serialize(new { name = tc.Name, arguments = tc.Arguments }));
                                sb.Append("\n</tool_call>\n");
                            }
                            sb.Append("<|im_end|>\n");
                        }
                        else if (!string.IsNullOrEmpty(m.Content))
                        {
                            sb.Append("<|im_start|>assistant\n").Append(m.Content).Append("\n<|im_end|>\n");
                        }
                        break;
                    case "tool":
                        sb.Append("<|im_start|>tool\n<tool_response>\n").Append(m.Content).Append("\n</tool_response>\n<|im_end|>\n");
                        break;
                }
            }
            // assistant 前缀：让模型接着输出
            sb.Append("<|im_start|>assistant\n");
            return sb.ToString();
        }

        public void Dispose()
        {
            _executor = null;
            _context?.Dispose();
            _model?.Dispose();
            _context = null;
            _model = null;
        }
    }
}
