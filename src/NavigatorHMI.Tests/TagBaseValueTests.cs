using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>create_tag/update_tag 的 base_value 按类型校验（A1 回归：NaN/Infinity 拒绝）。</summary>
    public class TagBaseValueTests
    {
        private static CommandService Create()
        {
            var project = new HMIProject();
            return new CommandService(project);
        }

        [Fact]
        public void FLOAT_NaN拒绝()
        {
            var svc = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T", ["data_type"] = "FLOAT", ["base_value"] = "NaN" });
            Assert.False(r.Success);
            Assert.Contains("base_value", r.ErrorMessage);
        }

        [Fact]
        public void FLOAT_Infinity拒绝()
        {
            var svc = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T", ["data_type"] = "FLOAT", ["base_value"] = "Infinity" });
            Assert.False(r.Success);
            Assert.Contains("base_value", r.ErrorMessage);
        }

        [Fact]
        public void FLOAT_合法通过()
        {
            var svc = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T", ["data_type"] = "FLOAT", ["base_value"] = "25.5" });
            Assert.True(r.Success);
        }

        [Fact]
        public void FLOAT空串_默认0_0()
        {
            var project = new HMIProject();
            var svc = new CommandService(project);
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T", ["data_type"] = "FLOAT", ["base_value"] = "" });
            Assert.True(r.Success);
            Assert.Equal("0.0", project.Tags[0].BaseValue);   // D4：FLOAT 空串 → 默认 0.0（不再清空）
        }

        [Fact]
        public void DATETIME_合法与非法base_value()
        {
            var project = new HMIProject();
            var svc = new CommandService(project);
            var ok = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T1", ["data_type"] = "DATETIME", ["base_value"] = "2026-08-08 12:30:00" });
            Assert.True(ok.Success);
            var bad = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T2", ["data_type"] = "DATETIME", ["base_value"] = "abc" });
            Assert.False(bad.Success);
            Assert.Equal("INVALID_PARAM", bad.ErrorCode);
        }

        [Fact]
        public void DATETIME_未传基准值默认全零()
        {
            var project = new HMIProject();
            var svc = new CommandService(project);
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "TD", ["data_type"] = "DATETIME" });
            Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
            Assert.Equal("0000:00:00 00:00:00", project.Tags[0].BaseValue);   // 任务8：默认全 0 基准值
        }

        [Fact]
        public void DATETIME_全零字面放行()
        {
            var project = new HMIProject();
            var svc = new CommandService(project);
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "TZ", ["data_type"] = "DATETIME", ["base_value"] = "0000:00:00 00:00:00" });
            Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");   // 0000 年 TryParse 失败但全 0 字面放行
            Assert.Equal("0000:00:00 00:00:00", project.Tags[0].BaseValue);
        }

        [Fact]
        public void DATETIME_全零字面含非法字符拒绝()
        {
            var project = new HMIProject();
            var svc = new CommandService(project);
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "TZ2", ["data_type"] = "DATETIME", ["base_value"] = "0000年00月00日" });
            Assert.False(r.Success);   // 白名单外字符（年/月/日）→ 拒绝（0000 无效且非全 0 字面）
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

    // ── D1：DATETIME 基准值不得删空（按格式校验；删空/非法都拒绝）──

    [Fact]
    public void DATETIME_updateTag删空基准值拒绝()
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "TD", ["data_type"] = "DATETIME" });
        var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "TD", ["base_value"] = "" });
        Assert.False(r.Success);
        Assert.Equal("INVALID_PARAM", r.ErrorCode);
        Assert.Contains("不能为空", r.ErrorMessage);
        // 基准值保持原值（未被清空）
        Assert.Equal("0000:00:00 00:00:00", project.Tags[0].BaseValue);
    }

    [Fact]
    public void DATETIME_updateTag非法基准值拒绝()
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "TD2", ["data_type"] = "DATETIME" });
        var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "TD2", ["base_value"] = "abc" });
        Assert.False(r.Success);
        Assert.Equal("INVALID_PARAM", r.ErrorCode);
    }

    // ── D4：数字变量基准值默认 0/0.0 ──

    [Theory]
    [InlineData("FLOAT", "0.0")]
    [InlineData("INT32", "0")]
    [InlineData("INT16", "0")]
    [InlineData("UINT16", "0")]
    public void 数字变量未传基准值默认0(string dataType, string expect)
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "N1", ["data_type"] = dataType });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(expect, project.Tags[0].BaseValue);   // D4：数字变量默认基准值 0/0.0
    }

    [Fact]
    public void 数字变量显式传基准值不被覆盖()
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "N2", ["data_type"] = "FLOAT", ["base_value"] = "25.5" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal("25.5", project.Tags[0].BaseValue);   // 显式传值不被 D4 默认覆盖
    }

    [Fact]
    public void updateTag组合参数校验失败_重命名不落库()
    {
        // 原子性回归：new_name + 非法 base_value → 校验失败时重命名/级联不得生效（防部分落库）
        var project = new HMIProject();
        var svc = new CommandService(project);
        svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "A", ["data_type"] = "FLOAT", ["base_value"] = "1.0" });
        var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "A", ["new_name"] = "B", ["base_value"] = "abc" });
        Assert.False(r.Success);
        Assert.Equal("INVALID_PARAM", r.ErrorCode);
        Assert.True(project.Tags.Any(t => t.Name == "A"), "校验失败后重命名不得生效");
        Assert.False(project.Tags.Any(t => t.Name == "B"));
    }

    [Fact]
    public void updateTag改类型到DATETIME_空基准值自动补全零()
    {
        // D1 边界：STRING 变量（空基准值）改类型 → DATETIME → 自动补全 0 默认（防无基准值 DATETIME 变量）
        var project = new HMIProject();
        var svc = new CommandService(project);
        svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "S", ["data_type"] = "STRING" });
        var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "S", ["data_type"] = "DATETIME" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(TagDataType.DATETIME, project.Tags[0].DataType);
        Assert.Equal("0000:00:00 00:00:00", project.Tags[0].BaseValue);
    }

    // ── #4（2026-08-10）：BOOL 基准值只能 false/true、默认 false ──

    [Fact]
    public void BOOL_createTag默认false()
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "B1", ["data_type"] = "BOOL" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal("false", project.Tags[0].BaseValue);   // #4：BOOL 默认基准值 false
    }

    [Fact]
    public void BOOL_updateTag空串归一false()
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "B2", ["data_type"] = "BOOL", ["base_value"] = "true" });
        var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "B2", ["base_value"] = "" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal("false", project.Tags[0].BaseValue);   // #4：BOOL 空串归一 false（不制造无基准值）
    }

    [Fact]
    public void BOOL_updateTag改类型补false()
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "B3", ["data_type"] = "STRING" });
        var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "B3", ["data_type"] = "BOOL" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(TagDataType.BOOL, project.Tags[0].DataType);
        Assert.Equal("false", project.Tags[0].BaseValue);   // #4：改类型到 BOOL 自动补 false
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("2")]
    public void BOOL_非法基准值拒绝(string bad)
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "B4", ["data_type"] = "BOOL", ["base_value"] = bad });
        Assert.False(r.Success);   // #4：BOOL 只能 false/true（"1"/"0" 不再接受）
        Assert.Equal("INVALID_PARAM", r.ErrorCode);
    }

    [Theory]
    [InlineData("false", "false")]
    [InlineData("true", "true")]
    [InlineData("True", "true")]
    [InlineData("FALSE", "false")]
    [InlineData("TRUE", "true")]
    public void BOOL_合法基准值通过_归一存储(string input, string expect)
    {
        var project = new HMIProject();
        var svc = new CommandService(project);
        var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "B5", ["data_type"] = "BOOL", ["base_value"] = input });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Equal(expect, project.Tags[0].BaseValue);   // #4：BOOL 归一存储（大小写变体 → true/false 小写）
    }
    }
}
