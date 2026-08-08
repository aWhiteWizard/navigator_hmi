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
    }
}