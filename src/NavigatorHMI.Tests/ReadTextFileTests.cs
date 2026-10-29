using System;
using System.Collections.Generic;
using System.IO;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AH（2026-10-28 用户需求）：AI 要能按需求文档建工程 ⇒ 新增 read_text_file 命令（AI 与 CLI 共用）。
    /// 覆盖：正常读取 / 分页 / 扩展名白名单 / 大小上限 / 不存在 / 中文与 BOM。
    /// </summary>
    public class ReadTextFileTests : IDisposable
    {
        private readonly string _dir;

        public ReadTextFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_readfile_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private string Write(string name, string content, bool bom = false)
        {
            var p = Path.Combine(_dir, name);
            File.WriteAllText(p, content, new System.Text.UTF8Encoding(bom));
            return p;
        }

        private static CommandResult Run(string path, int startLine = 1, int maxLines = 0)
        {
            var h = new ReadTextFileHandler();
            return h.Execute(new HMIProject(), new Dictionary<string, object?>
            {
                ["path"] = path,
                ["start_line"] = startLine,
                ["max_lines"] = maxLines,
            });
        }

        private static string Field(CommandResult r, string name)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var v)
                ? (v.ValueKind == System.Text.Json.JsonValueKind.String ? (v.GetString() ?? "") : v.ToString())
                : "";
        }

        [Fact]
        public void 读取Markdown_返回正文与行数()
        {
            var p = Write("需求文件.md", "# 需求\n画面1\n画面2\n");
            var r = Run(p);
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Contains("画面1", Field(r, "content"));
            Assert.Equal("4", Field(r, "total_lines"));
        }

        [Fact]
        public void 分页读取_只返回指定区间()
        {
            var p = Write("long.txt", "L1\nL2\nL3\nL4\nL5\n");
            var r = Run(p, startLine: 2, maxLines: 2);
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("L2\nL3", Field(r, "content").TrimEnd('\n'));
            Assert.Equal("2", Field(r, "returned_lines"));
        }

        [Fact]
        public void 非文本扩展名_拒绝()
        {
            var p = Path.Combine(_dir, "app.navihmi");
            File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
            var r = Run(p);
            Assert.False(r.Success);
            Assert.Equal("UNSUPPORTED_TYPE", r.ErrorCode);
        }

        [Fact]
        public void 超大小上限_提示分段读()
        {
            // 造一个确定超过上限的文件（字符数 > 上限字节数，ASCII 1 char = 1 byte）
            var big = new string('x', ReadTextFileHandler.MaxBytes + 1024);
            var p = Write("big.txt", big);
            var r = Run(p);
            Assert.False(r.Success);
            Assert.Equal("TOO_LARGE", r.ErrorCode);
        }

        [Fact]
        public void 文件不存在_报NOT_FOUND()
        {
            var r = Run(Path.Combine(_dir, "nope.md"));
            Assert.False(r.Success);
            Assert.Equal("NOT_FOUND", r.ErrorCode);
        }

        [Fact]
        public void 带BOM的中文文件_不乱码()
        {
            var p = Write("bom.md", "需求：温度监控画面\n", bom: true);
            var r = Run(p);
            Assert.True(r.Success, r.ErrorMessage);
            var content = Field(r, "content");
            Assert.Contains("温度监控画面", content);
            // 诊断信息：把前 8 个字符的码点带进断言消息，便于定位 BOM 残留位置
            var head = string.Join(",", content.Take(8).Select(c => ((int)c).ToString()));
            Assert.True(!content.Contains('\uFEFF'), "内容仍含 U+FEFF，前 8 个码点=" + head);
        }

        [Fact]
        public void 起始行越界_报OUT_OF_RANGE()
        {
            var p = Write("short.txt", "only one line\n");
            var r = Run(p, startLine: 99);
            Assert.False(r.Success);
            Assert.Equal("OUT_OF_RANGE", r.ErrorCode);
        }

        [Fact]
        public void 命令已注册进命令层()
        {
            var svc = new CommandService(new HMIProject());
            Assert.Contains(svc.GetAvailableCommands(), c => c.Name == "read_text_file");
        }
    }
}
