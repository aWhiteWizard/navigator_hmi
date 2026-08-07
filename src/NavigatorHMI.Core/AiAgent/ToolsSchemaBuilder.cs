using System.Collections.Generic;
using System.Text;
using NavigatorHMI.CommandLayer;

namespace NavigatorHMI.AiAgent
{
    /// <summary>
    /// 将 Command Layer 的命令定义转换为 Function Calling 工具 Schema（OpenAI tools JSON 数组）。
    /// 供 LLM 后端注入 system prompt / tools 字段，约束模型输出合法的命令名与参数。
    /// </summary>
    public static class ToolsSchemaBuilder
    {
        /// <summary>生成 OpenAI 风格 tools 数组 JSON 字符串。compact=true 时省略参数 description（schema 缩小，模型调工具准确率更高）。</summary>
        public static string Build(IEnumerable<CommandDefinition> commands, bool compact = false)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var cmd in commands)
            {
                if (!first) sb.Append(',');
                first = false;
                AppendCommand(sb, cmd, compact);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendCommand(StringBuilder sb, CommandDefinition cmd, bool compact)
        {
            sb.Append("{\"type\":\"function\",\"function\":{");
            sb.Append("\"name\":").Append(JsonEscape(cmd.Name)).Append(',');
            sb.Append("\"description\":").Append(JsonEscape(cmd.Description)).Append(',');
            sb.Append("\"parameters\":{\"type\":\"object\",\"properties\":{");
            bool first = true;
            foreach (var kv in cmd.Parameters)
            {
                if (compact && !kv.Value.Required && !kv.Value.KeepInCompact) continue;   // 精简模式：跳过可选参数（KeepInCompact 除外）（在逗号逻辑前，避免残留逗号）
                if (!first) sb.Append(',');
                first = false;
                AppendParameter(sb, kv.Key, kv.Value, compact);
            }
            sb.Append('}');
            // required 列表（必填参数）
            var required = new List<string>();
            foreach (var kv in cmd.Parameters)
                if (kv.Value.Required) required.Add(kv.Key);
            if (required.Count > 0)
            {
                sb.Append(",\"required\":[");
                for (int i = 0; i < required.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonEscape(required[i]));
                }
                sb.Append(']');
            }
            sb.Append("}}}");   // parameters + function + command 三层闭合（缺一即 JSON 非法，tools 静默失效）
        }

        private static void AppendParameter(StringBuilder sb, string key, ParameterDefinition p, bool compact)
        {
            sb.Append(JsonEscape(key)).Append(":{\"type\":").Append(JsonEscape(ToJsonType(p.Type)));
            if (!compact && p.Description.Length > 0)
                sb.Append(",\"description\":").Append(JsonEscape(p.Description));
            if (p.EnumValues is { Length: > 0 })
            {
                sb.Append(",\"enum\":[");
                for (int i = 0; i < p.EnumValues.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonEscape(p.EnumValues[i]));
                }
                sb.Append(']');
            }
            // compact 不输出 default：默认值会诱导模型省略必填参数（如 bind_tag 的 tag_name 默认空串 = 静默解绑）
            if (!compact && p.DefaultValue != null)
                sb.Append(",\"default\":").Append(JsonValue(p.DefaultValue));
            sb.Append('}');
        }

        /// <summary>模型层参数类型 → JSON Schema 类型。</summary>
        private static string ToJsonType(string t) => t switch
        {
            "int" => "integer",
            "double" => "number",
            "bool" => "boolean",
            "dict" => "object",
            _ => "string",   // string / enum
        };

        /// <summary>默认值 JSON 序列化（数字不带引号，字符串带引号）。</summary>
        private static string JsonValue(object v)
        {
            if (v is bool b) return b ? "true" : "false";
            if (v is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (v is int i) return i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return JsonEscape(v.ToString() ?? "");
        }

        private static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
