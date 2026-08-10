using System.Windows;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>
    /// 世界地图 viewport 屏幕坐标 ↔ 经纬度换算（批 4）。
    /// Mapsui 5.1 的 Viewport 无公开 ScreenToWorld/WorldToScreen API（已探针实证），
    /// 自实现标准 Web Mercator 无旋转换算。纯静态数学，可单测。
    /// </summary>
    public static class MapViewportMath
    {
        /// <summary>屏幕坐标 → 经纬度（viewport 参数取自 Map.Navigator.Viewport：center 为 Web Mercator 米坐标）。</summary>
        public static GeoPoint ScreenToGeo(double centerX, double centerY, double resolution, double viewportWidth, double viewportHeight, Point screenPos)
        {
            double worldX = (screenPos.X - viewportWidth / 2) * resolution + centerX;
            double worldY = (viewportHeight / 2 - screenPos.Y) * resolution + centerY;
            var (lon, lat) = Mapsui.Projections.SphericalMercator.ToLonLat(worldX, worldY);
            return new GeoPoint(lon, lat);
        }

        /// <summary>经纬度 → 屏幕坐标（与 <see cref="ScreenToGeo"/> 互逆）。</summary>
        public static Point GeoToScreen(double centerX, double centerY, double resolution, double viewportWidth, double viewportHeight, GeoPoint geo)
        {
            var (worldX, worldY) = Mapsui.Projections.SphericalMercator.FromLonLat(geo.Longitude, geo.Latitude);
            return new Point((worldX - centerX) / resolution + viewportWidth / 2,
                             (centerY - worldY) / resolution + viewportHeight / 2);
        }
    }
}
