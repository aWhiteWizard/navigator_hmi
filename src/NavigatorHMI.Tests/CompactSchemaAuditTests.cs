using NavigatorHMI.AiAgent;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using System.Text.Json;

namespace NavigatorHMI.Tests;

/// <summary>🟡6 全命令 compact 审计回归：功能关键的可选参数必须在 AI compact schema 中可见（否则 AI 无法完成修改类指令）。</summary>
public class CompactSchemaAuditTests
{
    /// <summary>构造单个命令的 compact schema 参数对象（结构化断言，避免子串误判）。</summary>
    private static JsonElement BuildParams(CommandDefinition def)
    {
        var json = ToolsSchemaBuilder.Build(new[] { def }, compact: true);
        using var doc = JsonDocument.Parse(json);
        var tool = doc.RootElement.EnumerateArray().First();
        return tool.GetProperty("function").GetProperty("parameters").Clone();
    }

    private static bool HasProp(JsonElement p, string name)
        => p.GetProperty("properties").TryGetProperty(name, out _);

    // ── update_* 家族（审计根因：重命名/核心修改字段全缺 KeepInCompact）──

    [Fact]
    public void UpdateAlarm_修改字段可见()
    {
        var p = BuildParams(new UpdateAlarmHandler().Definition);
        foreach (var k in new[] { "new_name", "tag_name", "type", "threshold", "deadband", "delay_ms", "severity", "message", "trigger_mode", "category", "priority", "ack_required", "ack_group" })
            Assert.True(HasProp(p, k), $"update_alarm 应含 {k}");
        Assert.False(HasProp(p, "color_override"), "color_override 颜色类刻意隐藏");
    }

    [Fact]
    public void UpdateTag_修改字段可见()
    {
        var p = BuildParams(new UpdateTagHandler().Definition);
        foreach (var k in new[] { "new_name", "data_type", "source", "unit", "scan_interval", "deadband", "base_value" })
            Assert.True(HasProp(p, k), $"update_tag 应含 {k}");
        Assert.False(HasProp(p, "description"), "description 自由文本刻意隐藏");
    }

    [Fact]
    public void UpdateUser_修改字段可见()
    {
        var p = BuildParams(new UpdateUserHandler().Definition);
        Assert.True(HasProp(p, "new_user_name"));
        Assert.True(HasProp(p, "new_password"));
        Assert.True(HasProp(p, "new_group_name"));
    }

    [Fact]
    public void UpdateDevice_修改字段可见()
    {
        var p = BuildParams(new UpdateDeviceHandler().Definition);
        Assert.True(HasProp(p, "new_name"));
        Assert.True(HasProp(p, "protocol"));
        Assert.True(HasProp(p, "connection_info"));
    }

    [Fact]
    public void UpdateList_修改字段可见()
    {
        var p = BuildParams(new UpdateListHandler().Definition);
        Assert.True(HasProp(p, "new_name"));
        Assert.True(HasProp(p, "items"));
    }

    [Fact]
    public void UpdateGroup_修改字段可见()
    {
        var p = BuildParams(new UpdateGroupHandler().Definition);
        Assert.True(HasProp(p, "new_group_name"));
        Assert.True(HasProp(p, "permissions"));
    }

    // ── set_default_font（compact 下曾零参数，命令完全不可用）──

    [Fact]
    public void SetDefaultFont_参数可见()
    {
        var p = BuildParams(new SetDefaultFontHandler().Definition);
        foreach (var k in new[] { "font_family", "font_size", "font_weight", "font_style", "text_decoration" })
            Assert.True(HasProp(p, k), $"set_default_font 应含 {k}");
    }

    // ── 事件动作参数 ──

    [Theory]
    [InlineData(typeof(AddEventHandler))]
    [InlineData(typeof(BindEventHandler))]
    public void 事件命令_params可见(Type handlerType)
    {
        var def = ((ICommandHandler)Activator.CreateInstance(handlerType)!).Definition;
        var p = BuildParams(def);
        Assert.True(HasProp(p, "params"), $"{def.Name} 应含 params（动作核心参数）");
    }

    // ── create_* 常见指令字段 ──

    [Fact]
    public void CreateAlarm_等级消息等可见()
    {
        var p = BuildParams(new CreateAlarmHandler().Definition);
        foreach (var k in new[] { "severity", "message", "deadband", "delay_ms", "trigger_mode", "ack_required" })
            Assert.True(HasProp(p, k), $"create_alarm 应含 {k}");
        Assert.False(HasProp(p, "color_override"), "color_override 刻意隐藏");
    }

    [Fact]
    public void CreateTag_来源单位等可见()
    {
        var p = BuildParams(new CreateTagHandler().Definition);
        foreach (var k in new[] { "source", "unit", "scan_interval", "deadband", "base_value" })
            Assert.True(HasProp(p, k), $"create_tag 应含 {k}");
        Assert.False(HasProp(p, "description"), "description 刻意隐藏");
    }

    [Fact]
    public void CreateGroup_权限可见()
    {
        var p = BuildParams(new CreateGroupHandler().Definition);
        Assert.True(HasProp(p, "permissions"), "create_group 应含 permissions（否则默认全禁权限）");
    }

    [Fact]
    public void CreateScreen_尺寸与类型可见()
    {
        var p = BuildParams(new CreateScreenHandler().Definition);
        Assert.True(HasProp(p, "width"));
        Assert.True(HasProp(p, "height"));
        Assert.True(HasProp(p, "type"));
    }

    [Fact]
    public void CreateProject_尺寸可见()
    {
        var p = BuildParams(new CreateProjectHandler().Definition);
        Assert.True(HasProp(p, "width"));
        Assert.True(HasProp(p, "height"));
    }

    [Fact]
    public void PasteWidget_位置可见()
    {
        var p = BuildParams(new PasteWidgetHandler().Definition);
        Assert.True(HasProp(p, "x"));
        Assert.True(HasProp(p, "y"));
    }

    // ── 必填参数不进 required 列表（可选语义保持）──

    [Fact]
    public void 补齐参数保持可选不进required()
    {
        var p = BuildParams(new UpdateAlarmHandler().Definition);
        var required = p.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Equal(new[] { "name" }, required);
        Assert.DoesNotContain("threshold", required);
    }

    // ── C12-14：作业点/范围点命令的经纬度与绑定变量在 compact schema 可见（原被省略 → AI 无法指定）──

    [Theory]
    [InlineData(typeof(AddWorkPointHandler))]
    [InlineData(typeof(AddWorkRangePointHandler))]
    public void 作业点命令_lngLat_boundTag可见且不进required(Type handlerType)
    {
        var def = ((ICommandHandler)Activator.CreateInstance(handlerType)!).Definition;
        var p = BuildParams(def);
        Assert.True(HasProp(p, "lng_lat"), $"{def.Name} 应含 lng_lat（AI 指定固定经纬度）");
        Assert.True(HasProp(p, "bound_tag"), $"{def.Name} 应含 bound_tag（AI 指定绑定 GPS 变量）");
        var required = p.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.DoesNotContain("lng_lat", required);
        Assert.DoesNotContain("bound_tag", required);
    }
}
