using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>
    /// read_text_file：读取本地**文本**文件内容（AH 2026-10-28，用户需求：AI 要能按需求文档建工程）。
    ///
    /// 背景：AI Agent 的工具清单 = 命令层全量命令（减黑名单）。此前只有 open_project 这类"工程文件"入口，
    /// 用户直接把 `xxx_需求文件.md` 路径丢给 AI 时，AI 拿它当 .navihmi 解析 ⇒ "工程文件损坏或格式不兼容"，
    /// 需求文档根本进不了上下文。本命令让 AI（和 CLI）都能**只读**取文本。
    ///
    /// 安全边界（用户 2026-10-28 确认「够用」）：
    ///   · 扩展名白名单：.md/.markdown/.txt/.json/.csv/.yml/.yaml/.xml/.log/.ini/.conf
    ///   · 单文件 ≤ 200 KB（防一次把整本手册塞进上下文烧 token）
    ///   · 只读；不跟随目录；不执行任何内容
    ///   · 分页：start_line/max_lines，便于大文件分段读
    /// </summary>
    public class ReadTextFileHandler : ICommandHandler
    {
        /// <summary>允许读取的扩展名（小写含点）。二进制/可执行一律拒绝。</summary>
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".md", ".markdown", ".txt", ".json", ".csv", ".yml", ".yaml", ".xml", ".log", ".ini", ".conf",
        };

        /// <summary>单文件大小上限（字节）。超出时提示用 start_line/max_lines 分段读。</summary>
        public const int MaxBytes = 200 * 1024;

        public CommandDefinition Definition => new()
        {
            Name = "read_text_file",
            Description = "读取本地文本文件内容（Markdown/文本/JSON/CSV 等需求文档；只读）",
            Parameters = new()
            {
                ["path"] = new() { Type = "string", Required = true, KeepInCompact = true, Description = "文件绝对路径（如 D:\\docs\\需求.md）" },
                ["start_line"] = new() { Type = "int", DefaultValue = 1, KeepInCompact = true, Description = "起始行（1 起，默认 1）" },
                ["max_lines"] = new() { Type = "int", DefaultValue = 0, KeepInCompact = true, Description = "最多返回行数（0=到文件末尾）" },
            },
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            var path = p.GetValueOrDefault("path")?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(path)) return ValidationResult.Fail("缺少必填参数: path");
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var rawPath = (p.GetValueOrDefault("path")?.ToString() ?? "").Trim().Trim('"');
            if (rawPath.Length == 0) return CommandResult.Fail("INVALID_PARAM", "缺少文件路径");

            string full;
            try { full = Path.GetFullPath(rawPath); }
            catch (Exception ex) { return CommandResult.Fail("INVALID_PARAM", $"路径非法: {ex.Message}"); }

            if (!File.Exists(full))
                return CommandResult.Fail("NOT_FOUND", $"文件不存在: {full}");

            var ext = Path.GetExtension(full);
            if (!AllowedExtensions.Contains(ext))
                return CommandResult.Fail("UNSUPPORTED_TYPE",
                    $"不支持的文件类型 \"{ext}\"——只允许读取文本文件（{string.Join("/", AllowedExtensions.OrderBy(x => x))}）");

            var info = new FileInfo(full);
            if (info.Length > MaxBytes)
                return CommandResult.Fail("TOO_LARGE",
                    $"文件 {info.Length / 1024} KB 超过上限 {MaxBytes / 1024} KB——请用 start_line/max_lines 分段读取");

            var startLine = Math.Max(1, ToInt(p.GetValueOrDefault("start_line"), 1));
            var maxLines = Math.Max(0, ToInt(p.GetValueOrDefault("max_lines"), 0));

            string text;
            try { text = File.ReadAllText(full, DetectEncoding(full)); }
            catch (Exception ex) { return CommandResult.Fail("READ_FAILED", $"读取失败: {ex.Message}"); }
            // 显式编码路径不保证剥 BOM（不同 .NET 版本/编码组合行为不一致）⇒ 这里兜一刀，
            // 否则正文首行会带 U+FEFF，AI 看到的第一个字符就是乱码
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);

            var all = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var total = all.Length;
            if (startLine > total)
                return CommandResult.Fail("OUT_OF_RANGE", $"起始行 {startLine} 超出文件总行数 {total}");

            var slice = all.Skip(startLine - 1);
            if (maxLines > 0) slice = slice.Take(maxLines);
            var body = string.Join("\n", slice);
            var returned = slice.Count();

            return CommandResult.Ok(new
            {
                path = full,
                bytes = info.Length,
                total_lines = total,
                start_line = startLine,
                returned_lines = returned,
                encoding = "utf-8(含 BOM 自动剥离)/gb18030(兜底)",
                // content 放最后：AI 侧读 JSON 时正文才是一等公民
                content = body,
            });
        }

        private static int ToInt(object? v, int fallback)
        {
            if (v == null) return fallback;
            if (v is int i) return i;
            return int.TryParse(v.ToString(), out var n) ? n : fallback;
        }

        /// <summary>编码探测：有 UTF-8 BOM 用 UTF-8（ReadAllText 会自动剥离 BOM）；否则先按 UTF-8 严格解码，失败回落 GB18030（老 .md/.txt 常见）。</summary>
        private static Encoding DetectEncoding(string path)
        {
            var head = new byte[3];
            using (var fs = File.OpenRead(path))
            {
                var n = fs.Read(head, 0, 3);
                if (n >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                    return new UTF8Encoding(false);
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
                return new UTF8Encoding(false);
            }
            catch
            {
                try { return Encoding.GetEncoding("GB18030"); }
                catch { return Encoding.UTF8; }
            }
        }
    }
}
