using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>CLI/GUI 共用参数净化 CliParamSanitizer 单测（三级分类：路径/标识符/自由文本）。</summary>
    public class CliParamSanitizerTests
    {
        // ── 路径类 ──

        [Theory]
        [InlineData("..\\secret.hmiproj")]
        [InlineData("D:\\..\\x.hmiproj")]
        [InlineData("a/../../b")]
        public void 路径类_含双点拒绝(string value)
        {
            Assert.NotNull(CliParamSanitizer.Validate("path", value));
            Assert.NotNull(CliParamSanitizer.Validate("file", value));
            Assert.NotNull(CliParamSanitizer.Validate("output", value));
        }

        [Theory]
        [InlineData("sub\\screen1.hmiproj")]
        [InlineData("./data/x.hmiproj")]
        public void 路径类_相对路径放行(string value)
        {
            Assert.Null(CliParamSanitizer.Validate("path", value));
        }

        [Fact]
        public void 路径类_绝对路径拒绝()
        {
            Assert.NotNull(CliParamSanitizer.Validate("path", "D:\\data\\x.hmiproj"));   // path 非豁免
            Assert.NotNull(CliParamSanitizer.Validate("source", "D:\\data\\x.png"));    // source 非豁免
        }

        [Fact]
        public void file_绝对路径豁免_但双点仍拦()
        {
            // file 为固件/工程包文件任意目录场景（D 循环 OTA 用户裁决：输入固件路径方案）——绝对路径放行，'..' 仍拦
            Assert.Null(CliParamSanitizer.Validate("file", "D:\\workspace\\fw\\NavigatorHMI_v1.1.1.fw"));
            Assert.Null(CliParamSanitizer.Validate("file", "/mnt/fw/NavigatorHMI_v1.1.1.fw"));
            Assert.NotNull(CliParamSanitizer.Validate("file", "D:\\..\\secret.fw"));
            Assert.NotNull(CliParamSanitizer.Validate("file", "../secret.fw"));
        }

        [Fact]
        public void project_绝对路径豁免_但双点仍拦()
        {
            Assert.Null(CliParamSanitizer.Validate("project", "D:\\任意目录\\x.hmiproj"));
            Assert.NotNull(CliParamSanitizer.Validate("project", "D:\\..\\x.hmiproj"));
        }

        [Fact]
        public void connection_绝对路径豁免_但双点仍拦()
        {
            Assert.Null(CliParamSanitizer.Validate("connection", "{\"port\":\"/dev/ttyUSB0\"}"));
            Assert.NotNull(CliParamSanitizer.Validate("connection", "{\"path\":\"..\"}"));
        }

        [Fact]
        public void source_绝对路径拒绝_相对放行_双点仍拦()
        {
            // source 为路径类但非豁免 key（P14 补充边界）
            Assert.NotNull(CliParamSanitizer.Validate("source", "D:\\data\\x.png"));
            Assert.NotNull(CliParamSanitizer.Validate("source", "..\\x.png"));
            Assert.Null(CliParamSanitizer.Validate("source", "img\\x.png"));
        }

        [Fact]
        public void connection_裸设备路径放行()
        {
            // connection 豁免绝对路径（P14）：裸串 /dev/ttyUSB0 与 JSON 内路径均放行；双点仍拦
            Assert.Null(CliParamSanitizer.Validate("connection", "/dev/ttyUSB0"));
            Assert.NotNull(CliParamSanitizer.Validate("connection", "/dev/../ttyUSB0"));
        }

        [Fact]
        public void 路径类_纯双点拒绝()
        {
            // P14 边界：value 恰为 ".."（无分隔符场景）
            Assert.NotNull(CliParamSanitizer.Validate("path", ".."));
            Assert.NotNull(CliParamSanitizer.Validate("output", ".."));
            Assert.NotNull(CliParamSanitizer.Validate("file", ".."));
        }

        // ── 标识符类 ──

        [Theory]
        [InlineData("name", "a..b")]
        [InlineData("name", "a/b")]
        [InlineData("screen", "x\\y")]
        [InlineData("tag", "a..b")]
        [InlineData("widget", "a/b")]
        [InlineData("new-name", "..")]
        [InlineData("user-name", "a\\b")]
        [InlineData("mode", "a..b")]
        public void 标识符类_拦双点或分隔符(string key, string value)
        {
            Assert.NotNull(CliParamSanitizer.Validate(key, value));
        }

        [Theory]
        [InlineData("name", "Tank1_Temp")]
        [InlineData("screen", "主画面")]
        [InlineData("widget", "button_1")]
        [InlineData("ip", "192.168.1.100")]
        [InlineData("mode", "copy")]
        public void 标识符类_正常标识符放行(string key, string value)
        {
            Assert.Null(CliParamSanitizer.Validate(key, value));
        }

        // ── 自由文本类 ──

        [Fact]
        public void 自由文本_含双点拒绝()
        {
            Assert.NotNull(CliParamSanitizer.Validate("description", "路径 ..\\x"));
            Assert.NotNull(CliParamSanitizer.Validate("message", "升级.. 失败"));
            Assert.NotNull(CliParamSanitizer.Validate("items", "a|..\\b"));
            Assert.NotNull(CliParamSanitizer.Validate("base-value", ".."));
        }

        [Fact]
        public void 自由文本_含分隔符放行()
        {
            Assert.Null(CliParamSanitizer.Validate("description", "图片 C:\\img\\a.png 正常"));
            Assert.Null(CliParamSanitizer.Validate("items", "C:\\a.png|D:\\b.png"));
        }

        // ── 未知 key（颜色/数值等静态分类不覆盖）──

        [Theory]
        [InlineData("color", "#FF0000")]
        [InlineData("color", "..")]      // 未知 key 不在任何分类白名单 → 放行（与既有行为一致：静态分类无法覆盖）
        [InlineData("threshold", "25.5")]
        [InlineData("x", "1/2\\3..4")]
        public void 未知key_按自由文本语义放行(string key, string value)
        {
            Assert.Null(CliParamSanitizer.Validate(key, value));
        }

        // ── 错误消息包含参数名与原始值 ──

        [Fact]
        public void 错误消息_含参数名与原始值()
        {
            var err = CliParamSanitizer.Validate("name", "a..b");
            Assert.NotNull(err);
            Assert.Contains("--name", err);
            Assert.Contains("a..b", err);
        }

        // ── J-5: null 守卫（I-3 cli-optval-null-nre 教训——未提供参数直通不崩）──

        [Theory]
        [InlineData("path")]
        [InlineData("project")]
        [InlineData("name")]
        [InlineData("description")]
        [InlineData("connection")]
        public void null值_放行不崩(string key)
        {
            Assert.Null(CliParamSanitizer.Validate(key, null));   // null 未提供参数 → 直通（不 NRE）
        }
    }
}
