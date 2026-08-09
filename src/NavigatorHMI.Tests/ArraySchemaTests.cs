using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using System.Text.Json;

namespace NavigatorHMI.Tests;

/// <summary>任务 6 回归：array_layout 的 cols/rows 必须在 compact schema 中可见（否则 AI 无法传 M×N 参数——9c 规则死规则）。</summary>
public class ArraySchemaTests
{
    [Fact]
    public void CompactSchema_含cols与rows()
    {
        var defs = new List<CommandDefinition> { new ArrayLayoutHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        Assert.Contains("\"cols\"", json);
        Assert.Contains("\"rows\"", json);
        Assert.Contains("\"center_start\"", json);   // 既有 KeepInCompact 参数不受影响
    }

    [Fact]
    public void CompactSchema_不输出default()
    {
        var defs = new List<CommandDefinition> { new ArrayLayoutHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        Assert.DoesNotContain("default", json);   // compact 不输出默认值（防模型省略参数）
    }
}
