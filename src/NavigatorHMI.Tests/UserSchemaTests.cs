using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using System.Text.Json;

namespace NavigatorHMI.Tests;

/// <summary>A1 回归：create_user 的 group_name 必须在 compact schema 中可见（否则 AI 创建用户看不到该参数，只能默认访客）。</summary>
public class UserSchemaTests
{
    /// <summary>构造 create_user 的 compact schema 并解析为 JsonElement（结构化断言，避免子串误判）。</summary>
    private static JsonElement CreateUserTool()
    {
        var defs = new List<CommandDefinition> { new CreateUserHandler().Definition };
        var json = ToolsSchemaBuilder.Build(defs, compact: true);
        using var doc = JsonDocument.Parse(json);
        var tool = doc.RootElement.EnumerateArray().First(t => t.GetProperty("function").GetProperty("name").GetString() == "create_user");
        return tool.GetProperty("function").GetProperty("parameters").Clone();
    }

    [Fact]
    public void CompactSchema_含group_name()
    {
        var p = CreateUserTool();
        var props = p.GetProperty("properties");
        Assert.True(props.TryGetProperty("group_name", out var gn), "compact schema 应含 group_name（A1：AI 设组参数可见）");
        Assert.Equal("string", gn.GetProperty("type").GetString());
    }

    [Fact]
    public void CompactSchema_create_user必填参数保留()
    {
        var p = CreateUserTool();
        var props = p.GetProperty("properties");
        Assert.True(props.TryGetProperty("user_name", out _));
        Assert.True(props.TryGetProperty("password", out _));
    }

    [Fact]
    public void CompactSchema_required含必填不含group_name()
    {
        var p = CreateUserTool();
        var required = p.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("user_name", required);
        Assert.Contains("password", required);
        Assert.DoesNotContain("group_name", required);   // group_name 可选（省略走默认访客）
    }

    [Fact]
    public void CompactSchema_group_name不输出default()
    {
        var p = CreateUserTool();
        var gn = p.GetProperty("properties").GetProperty("group_name");
        Assert.False(gn.TryGetProperty("default", out _), "compact 不输出默认值（防模型省略参数）");
    }
}
