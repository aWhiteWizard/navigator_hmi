using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>世界地图批 1：GeoPoint 经纬度 DMS/小数度转换与解析测试。</summary>
    public class GeoPointTests
    {
        // ── 小数度 → DMS 格式化 ──

        [Fact]
        public void 小数度转DMS_东经北纬()
        {
            var p = new GeoPoint(104.0583, 30.6722);
            Assert.Equal("E104°3'29.88\", N30°40'19.92\"", p.ToDmsString());
        }

        [Fact]
        public void 小数度转DMS_西经南纬()
        {
            var p = new GeoPoint(-104.0583, -30.6722);
            Assert.Equal("W104°3'29.88\", S30°40'19.92\"", p.ToDmsString());
        }

        [Fact]
        public void 小数度转DMS_零点()
        {
            var p = new GeoPoint(0, 0);
            Assert.Equal("E0°0'0.00\", N0°0'0.00\"", p.ToDmsString());
        }

        [Fact]
        public void 小数度转DMS_秒进位()
        {
            // W-2a：秒保留 2 位小数——104.0599 ≈ 104°3'35.64"（不再舍入到整数 36）
            var p = new GeoPoint(104.0599, 30);
            Assert.Equal("E104°3'35.64\", N30°0'0.00\"", p.ToDmsString());
            // 分进位：30.9999 ≈ 30°59'59.64" → 59.64 < 60 不进位（旧整数舍入到 60' 才进 31°）
            var p2 = new GeoPoint(104, 30.9999);
            Assert.Equal("E104°0'0.00\", N30°59'59.64\"", p2.ToDmsString());
            // W-2a 补测：分小数 0.99996 → 秒 59.9976" → Round(,2)=60.00 → 触发 sec>=60 进位 → 104°2'0.00"
            var p3 = new GeoPoint(104.0333326667, 0);
            Assert.Equal("E104°2'0.00\", N0°0'0.00\"", p3.ToDmsString());
        }

        [Fact]
        public void 负数小数解析_西经南纬()
        {
            // W-2a 补测：TryParseCoord 接受带负号纯小数（V-5a 合法输入 -104.06 / -30.55）
            Assert.True(GeoPoint.TryParseCoord("-104.06", true, out var lng));
            Assert.Equal(-104.06, lng, 6);
            Assert.True(GeoPoint.TryParseCoord("-30.55", false, out var lat));
            Assert.Equal(-30.55, lat, 6);
            Assert.False(GeoPoint.TryParseCoord("104.06", false, out _));   // 经度值放纬度位：超 ±90 拒绝
        }

        // ── DMS 解析 → 小数度 ──

        [Fact]
        public void 解析DMS_带前缀()
        {
            Assert.True(GeoPoint.TryParse("E104°3'30\", N30°40'20\"", out var p));
            Assert.NotNull(p);
            Assert.Equal(104 + 3.0 / 60 + 30.0 / 3600, p!.Longitude, 5);
            Assert.Equal(30 + 40.0 / 60 + 20.0 / 3600, p.Latitude, 5);
        }

        [Fact]
        public void 解析DMS_西经南纬前缀()
        {
            Assert.True(GeoPoint.TryParse("W104°3'30\", S30°40'20\"", out var p));
            Assert.NotNull(p);
            Assert.Equal(-(104 + 3.0 / 60 + 30.0 / 3600), p!.Longitude, 5);
            Assert.Equal(-(30 + 40.0 / 60 + 20.0 / 3600), p.Latitude, 5);
        }

        [Fact]
        public void 解析基准值_括号包裹()
        {
            Assert.True(GeoPoint.TryParse("(E0°0'0\", N0°0'0\")", out var p));
            Assert.NotNull(p);
            Assert.Equal(0, p!.Longitude);
            Assert.Equal(0, p.Latitude);
        }

        [Fact]
        public void 解析基准值_往返一致()
        {
            var p = new GeoPoint(104.0583, 30.6722);
            var baseValue = p.ToBaseValue();
            Assert.Equal("(E104°3'29.88\", N30°40'19.92\")", baseValue);
            Assert.True(GeoPoint.TryParse(baseValue, out var back));
            // DMS 2 位小数秒精度：往返值为 DMS 精确值（非原始小数度）
            Assert.Equal(104 + 3.0 / 60 + 29.88 / 3600, back!.Longitude, 5);
            Assert.Equal(30 + 40.0 / 60 + 19.92 / 3600, back.Latitude, 5);
        }

        // ── 小数度输入 ──

        [Fact]
        public void 解析小数度_无前缀()
        {
            Assert.True(GeoPoint.TryParse("104.0583, 30.6722", out var p));
            Assert.NotNull(p);
            Assert.Equal(104.0583, p!.Longitude);
            Assert.Equal(30.6722, p.Latitude);
        }

        [Fact]
        public void 解析小数度_带前缀()
        {
            Assert.True(GeoPoint.TryParse("E104.0583, N30.6722", out var p));
            Assert.NotNull(p);
            Assert.Equal(104.0583, p!.Longitude);
            Assert.Equal(30.6722, p.Latitude);
        }

        [Fact]
        public void 解析_中文逗号分隔()
        {
            Assert.True(GeoPoint.TryParse("104.0583，30.6722", out var p));
            Assert.NotNull(p);
            Assert.Equal(104.0583, p!.Longitude);
        }

        [Fact]
        public void 解析_仅度省略分秒()
        {
            Assert.True(GeoPoint.TryParse("E104°, N30°", out var p));
            Assert.NotNull(p);
            Assert.Equal(104, p!.Longitude);
            Assert.Equal(30, p.Latitude);
        }

        // ── 范围校验 ──

        [Theory]
        [InlineData("200, 30")]        // 经度超范围
        [InlineData("104, 95")]        // 纬度超范围
        [InlineData("-181, 30")]       // 经度下限
        [InlineData("E200°, N30°")]    // DMS 超范围
        public void 解析_超范围拒绝(string text)
        {
            Assert.False(GeoPoint.TryParse(text, out _));
        }

        // ── 非法输入 ──

        [Theory]
        [InlineData("")]
        [InlineData("abc, 30")]
        [InlineData("104")]
        [InlineData("104, 30, 20")]
        [InlineData("E104°3'70\", N30°")]   // 秒 ≥ 60
        public void 解析_非法输入拒绝(string text)
        {
            Assert.False(GeoPoint.TryParse(text, out _));
        }

        // ── 边界合法值 ──

        [Fact]
        public void 解析_边界值通过()
        {
            Assert.True(GeoPoint.TryParse("180, 90", out var p));
            Assert.NotNull(p);
            Assert.Equal(180, p!.Longitude);
            Assert.Equal(90, p.Latitude);
        }

        // ── 前缀-位置错配 ──

        [Theory]
        [InlineData("N104°, E30°")]    // 经度位 N、纬度位 E
        [InlineData("S104°, W30°")]    // 经度位 S、纬度位 W
        [InlineData("E30°, N104°")]    // 经度位纬度前缀、纬度位经度前缀
        public void 解析_前缀位置错配拒绝(string text)
        {
            Assert.False(GeoPoint.TryParse(text, out _));
        }

        // ── 秒舍入 AwayFromZero（0.5″ 进位而非银行家舍入）──

        [Fact]
        public void 秒舍入_半点进位()
        {
            // W-2a：秒保留 2 位小数——30.0001389 ≈ N30°0'0.5"（0.5 不再 AwayFromZero 进位到 1）
            var p = new GeoPoint(0, 30.0001388889);
            Assert.Equal("E0°0'0.00\", N30°0'0.50\"", p.ToDmsString());
        }
    }
}
