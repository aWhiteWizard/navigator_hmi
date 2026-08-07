using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NavigatorHMI.AiAgent
{
    /// <summary>
    /// 云端 LLM 后端：OpenAI 兼容 Chat Completions API（DeepSeek / 通义千问 / 任意兼容服务）。
    /// 原生 tools 字段 + tool_calls 循环。配置：环境变量 NAVIGATOR_HMI_AI_KEY（必填）、
    /// NAVIGATOR_HMI_AI_ENDPOINT（默认 DeepSeek）、NAVIGATOR_HMI_AI_MODEL（默认 deepseek-chat）。
    /// </summary>
    public class CloudLLMBackend : ILLMBackend
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _maxTokens;

        public const string DefaultEndpoint = "https://api.deepseek.com/v1";
        public const string DefaultModel = "deepseek-chat";

        public CloudLLMBackend(string? apiKey = null, string? endpoint = null, string? model = null, int maxTokens = 1024)
        {
            // key 读取顺序：构造参数 → NAVIGATOR_HMI_AI_KEY → DEEPSEEK_API（进程级 → User 级，防进程环境快照过期）
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("NAVIGATOR_HMI_AI_KEY")
                          ?? Environment.GetEnvironmentVariable("DEEPSEEK_API")
                          ?? Environment.GetEnvironmentVariable("DEEPSEEK_API", EnvironmentVariableTarget.User)
                          ?? "";
            _endpoint = (endpoint ?? Environment.GetEnvironmentVariable("NAVIGATOR_HMI_AI_ENDPOINT") ?? DefaultEndpoint).TrimEnd('/');
            _model = model ?? Environment.GetEnvironmentVariable("NAVIGATOR_HMI_AI_MODEL") ?? DefaultModel;
            _maxTokens = maxTokens;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            if (_apiKey.Length > 0)
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public string BackendName => $"云端 {_model}";
        public bool IsAvailable => _apiKey.Length > 0;
        public bool IsModelLoaded => true;

        public async Task<ChatResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, string fcSchemaJson, CancellationToken ct = default)
        {
            if (!IsAvailable)
                throw new InvalidOperationException("未配置 API Key：请设置环境变量 NAVIGATOR_HMI_AI_KEY");

            var body = new JsonObject
            {
                ["model"] = _model,
                ["messages"] = BuildMessages(messages),
                ["max_tokens"] = _maxTokens,
                ["temperature"] = 0.2,
                ["stream"] = false,
            };
            if (!string.IsNullOrWhiteSpace(fcSchemaJson))
            {
                try
                {
                    body["tools"] = JsonNode.Parse(fcSchemaJson);   // ToolsSchemaBuilder 输出即 OpenAI tools 数组
                    body["tool_choice"] = "auto";
                }
                catch (JsonException) { /* tools 解析失败则不带 tools（正常不会发生，schema 由命令层生成） */ }
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var resp = await _http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"API 请求失败 [{resp.StatusCode}]: {Truncate(text, 300)}");

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                throw new InvalidOperationException("API 响应缺少 choices");
            var msg = choices[0].GetProperty("message");

            var response = new ChatResponse();
            if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                response.Content = content.GetString() ?? "";

            if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ToolCall>();
                foreach (var call in calls.EnumerateArray())
                {
                    if (!call.TryGetProperty("function", out var fn) || !fn.TryGetProperty("name", out var name)
                     || name.ValueKind != JsonValueKind.String) continue;
                    var tc = new ToolCall(name.GetString()!);
                    if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        tc.Id = id.GetString()!;
                    if (fn.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String)
                    {
                        try
                        {
                            using var argsDoc = JsonDocument.Parse(args.GetString() ?? "{}");
                            foreach (var prop in argsDoc.RootElement.EnumerateObject())
                                tc.Arguments[prop.Name] = JsonValueToObject(prop.Value);
                        }
                        catch (JsonException) { /* 参数解析失败则空参数（命令层校验兜底） */ }
                    }
                    list.Add(tc);
                }
                response.ToolCalls = list;
            }
            return response;
        }

        /// <summary>ChatMessage 列表 → OpenAI messages 数组（assistant tool_calls / tool tool_call_id 协议）。</summary>
        private static JsonArray BuildMessages(IReadOnlyList<ChatMessage> messages)
        {
            var arr = new JsonArray();
            foreach (var m in messages)
            {
                var node = new JsonObject { ["role"] = m.Role };
                switch (m.Role)
                {
                    case "assistant" when m.ToolCalls is { Count: > 0 }:
                        node["content"] = null;
                        var calls = new JsonArray();
                        foreach (var tc in m.ToolCalls)
                        {
                            calls.Add(new JsonObject
                            {
                                ["id"] = tc.Id,
                                ["type"] = "function",
                                ["function"] = new JsonObject
                                {
                                    ["name"] = tc.Name,
                                    ["arguments"] = JsonSerializer.Serialize(tc.Arguments),
                                },
                            });
                        }
                        node["tool_calls"] = calls;
                        break;
                    case "tool":
                        node["content"] = m.Content;
                        node["tool_call_id"] = m.ToolCallId ?? "";
                        break;
                    default:
                        node["content"] = m.Content;
                        break;
                }
                arr.Add(node);
            }
            return arr;
        }

        private static object? JsonValueToObject(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : (el.TryGetDouble(out var d) ? d : el.GetRawText()),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.GetRawText(),
        };

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

        public void Dispose() => _http.Dispose();
    }
}
