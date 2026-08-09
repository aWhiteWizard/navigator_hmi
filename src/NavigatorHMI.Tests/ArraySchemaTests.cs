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

    // ── #3（2026-08-10）：array_layout 数值参数向 AI 开放（W3 起点 / Z2 改阵列根因）──

    [Fact]
    public void CompactSchema_含起点与圆心半径()
    {
        var defs = new List<CommandDefinition> { new ArrayLayoutHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        Assert.Contains("\"start_x\"", json);   // W3：AI 可传矩形起点
        Assert.Contains("\"start_y\"", json);
        Assert.Contains("\"center_x\"", json);  // Z2：AI 可传圆心/半径（改阵列中心/半径）
        Assert.Contains("\"center_y\"", json);
        Assert.Contains("\"radius\"", json);
        Assert.Contains("\"spacing_x\"", json);
        Assert.Contains("\"spacing_y\"", json);   // 🟡：补 spacing_y 断言
        Assert.Contains("\"start_angle\"", json);
        Assert.Contains("\"end_angle\"", json);
    }

    [Fact]
    public void CompactSchema_resizeWidget含width与height()
    {
        // 🟡：ScreenWidgetParams 改动影响 move 与 resize 两命令——resize 开放也要回归
        var defs = new List<CommandDefinition> { new ResizeWidgetHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        Assert.Contains("\"width\"", json);
        Assert.Contains("\"height\"", json);
    }

    [Fact]
    public void CompactSchema_addWidget含x与y()
    {
        var defs = new List<CommandDefinition> { new AddWidgetHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        Assert.Contains("\"x\"", json);   // AI 创建控件可指定位置
        Assert.Contains("\"y\"", json);
    }

    [Fact]
    public void CompactSchema_moveWidget含x与y()
    {
        var defs = new List<CommandDefinition> { new MoveWidgetHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        Assert.Contains("\"x\"", json);   // AI 移动控件可指定坐标
        Assert.Contains("\"y\"", json);
    }
}
