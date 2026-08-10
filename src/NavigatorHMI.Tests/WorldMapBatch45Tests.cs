using NavigatorHMI.Common;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>世界地图批 4/5：视口换算数学 + 批 3a 遗留逻辑测试。</summary>
    public class WorldMapBatch45Tests
    {
        // 参考 z6 viewport：center=(0,0)（3857 原点），res=156543.03392804097/64，800×600
        private const double ResZ6 = 156543.03392804097 / 64;
        private const double Cx = 0, Cy = 0;
        private const double W = 800, H = 600;

        // ── 换算数学（批 4 核心）──

        [Fact]
        public void 屏幕中心_映射到视口中心经纬度()
        {
            var geo = MapViewportMath.ScreenToGeo(Cx, Cy, ResZ6, W, H, new System.Windows.Point(W / 2, H / 2));
            Assert.Equal(0, geo.Longitude, 5);   // 中心=世界原点=经纬度 (0,0)
            Assert.Equal(0, geo.Latitude, 5);
        }

        [Fact]
        public void 屏幕坐标_往返一致()
        {
            var src = new System.Windows.Point(123.4, 456.7);
            var geo = MapViewportMath.ScreenToGeo(Cx, Cy, ResZ6, W, H, src);
            var back = MapViewportMath.GeoToScreen(Cx, Cy, ResZ6, W, H, geo);
            Assert.Equal(src.X, back.X, 3);
            Assert.Equal(src.Y, back.Y, 3);
        }

        [Fact]
        public void 已知经纬度_换算到屏幕()
        {
            // 北京 (116.4, 39.9)：应落在视口中心偏右上方（经度>0 东侧、纬度>0 北侧=屏幕上方）
            var scr = MapViewportMath.GeoToScreen(Cx, Cy, ResZ6, W, H, new GeoPoint(116.4, 39.9));
            Assert.True(scr.X > W / 2, $"经度正 → 屏幕右半（实际 X={scr.X}）");
            Assert.True(scr.Y < H / 2, $"纬度正 → 屏幕上半（实际 Y={scr.Y}）");
            // 往返回原经纬度
            var geo = MapViewportMath.ScreenToGeo(Cx, Cy, ResZ6, W, H, scr);
            Assert.Equal(116.4, geo.Longitude, 3);
            Assert.Equal(39.9, geo.Latitude, 3);
        }

        [Fact]
        public void 负经纬度_换算到屏幕左下()
        {
            var scr = MapViewportMath.GeoToScreen(Cx, Cy, ResZ6, W, H, new GeoPoint(-60, -20));
            Assert.True(scr.X < W / 2);
            Assert.True(scr.Y > H / 2);
        }

        [Fact]
        public void 非零中心_往返一致()
        {
            // 生产路径 center 必非 0：以中国区域 3857 坐标为中心，z8 分辨率；往返一致为核（src 非视口中心，经纬度断言无意义）
            var (cx, cy) = Mapsui.Projections.SphericalMercator.FromLonLat(104.06, 30.67);
            const double resZ8 = 156543.03392804097 / 256;
            var src = new System.Windows.Point(234.5, 123.7);
            var geo = MapViewportMath.ScreenToGeo(cx, cy, resZ8, W, H, src);
            var back = MapViewportMath.GeoToScreen(cx, cy, resZ8, W, H, geo);
            Assert.Equal(src.X, back.X, 3);
            Assert.Equal(src.Y, back.Y, 3);
            // 屏幕左上（123.7 在 600 中心 300 上方）→ 纬度应高于中心纬度 30.67
            Assert.True(geo.Latitude > 30.67);
            Assert.True(geo.Longitude < 104.06);
        }

        [Fact]
        public void 高缩放级别_已知点往返()
        {
            // z16 分辨率（街道级）：已知点为中心 → 在视口内，往返精度（高缩放放大换算误差）
            var (cx, cy) = Mapsui.Projections.SphericalMercator.FromLonLat(116.404, 39.915);
            const double resZ16 = 156543.03392804097 / 65536;
            var geo = new GeoPoint(116.404, 39.915);
            var scr = MapViewportMath.GeoToScreen(cx, cy, resZ16, W, H, geo);
            Assert.Equal(W / 2, scr.X, 3);   // 已知点=中心 → 屏幕中心
            Assert.Equal(H / 2, scr.Y, 3);
            var back = MapViewportMath.ScreenToGeo(cx, cy, resZ16, W, H, scr);
            Assert.Equal(geo.Longitude, back.Longitude, 6);
            Assert.Equal(geo.Latitude, back.Latitude, 6);
        }

        // ── 批 3a 遗留：PolygonWidgetCreator 空列表防御 ──

        [Fact]
        public void PolygonCreator_空列表抛异常()
        {
            var creator = new PolygonWidgetCreator();
            Assert.Throws<ArgumentException>(() => creator.Create(new System.Windows.Point[0], new Screen { Name = "s" }));
            Assert.Throws<ArgumentException>(() => creator.Create(null!, new Screen { Name = "s" }));
        }

        [Fact]
        public void PolygonCreator_顶点转相对坐标()
        {
            var creator = new PolygonWidgetCreator();
            var pts = new[] { new System.Windows.Point(100, 100), new System.Windows.Point(200, 100), new System.Windows.Point(150, 200) };
            var poly = creator.Create(pts, new Screen { Name = "s" });
            Assert.Equal(100, poly.X);
            Assert.Equal(100, poly.Y);
            Assert.Equal(100, poly.Width);
            Assert.Equal(100, poly.Height);
            Assert.Equal(3, poly.Points.Count);
            Assert.Equal(0, poly.Points[0].X);   // 首个顶点 = 相对原点
            Assert.Equal(50, poly.Points[2].X);
        }

        // ── 批 3a 遗留：PointWidget 通知 DisplayText ──

        [Fact]
        public void PointWidget_标签变化通知DisplayText()
        {
            var pt = new PointWidget();
            var notified = false;
            pt.PropertyChanged += (_, e2) => { if (e2.PropertyName == nameof(PointWidget.DisplayText)) notified = true; };
            pt.Label = "1号泵站";
            Assert.True(notified);
        }

        [Fact]
        public void PointWidget_固定经纬度变化通知DisplayText()
        {
            var pt = new PointWidget();
            var notified = false;
            pt.PropertyChanged += (_, e2) => { if (e2.PropertyName == nameof(PointWidget.DisplayText)) notified = true; };
            pt.FixedPoint = new GeoPoint(104.0583, 30.6722);
            Assert.True(notified);
            Assert.Equal("E104°3'30\", N30°40'20\"", pt.DisplayText);   // 无标签时显示经纬度 DMS
        }
    }
}
