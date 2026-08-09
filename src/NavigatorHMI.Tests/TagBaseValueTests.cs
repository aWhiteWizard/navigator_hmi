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
        public void 空串清空放行()
        {
            var svc = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "T", ["data_type"] = "FLOAT", ["base_value"] = "" });
            Assert.True(r.Success);
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
    }
}