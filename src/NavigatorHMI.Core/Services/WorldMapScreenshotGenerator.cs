using System.IO;
using SkiaSharp;

namespace NavigatorHMI.Common;

/// <summary>
/// N-1 世界地图底图生成器（2026-08-30 用户定方案，替代瓦片铺贴）：设备端不能缩放（锁定视角），
/// 编译下载时用组态软件已下载的地图缓存（map-cache/）+ 联网补充，按 FW computeBounds 同口径视口
/// 拼单张底图 PNG（worldmap_bg.png）随工程下发；设备端显示底图 + 叠加作业点/范围点。
/// 无缓存瓦片且联网失败 → 返回缺失（调用方拒绝部署，用户定：不用模拟底图）。
/// </summary>
public static class WorldMapScreenshotGenerator
{
    /// <summary>高德街道图瓦片 URL（与 EditWindow.xaml.cs Mapsui POC 同源；s=子域 1~4，style=8 街道图）。</summary>
    public const string AmapTileUrlTemplate =
        "https://webrd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=8&x={x}&y={y}&z={z}";

    /// <summary>底图文件名（工程目录，随包下发）。</summary>
    public const string BackgroundFileName = "worldmap_bg.png";

    /// <summary>Web Mercator 地球半径（与 FW mercX/mercY 一致）。</summary>
    private const double EarthRadius = 6378137.0;

    /// <summary>生成底图 PNG 到工程目录。返回 null=成功，非 null=失败原因（拒绝部署）。</summary>
    /// <param name="project">当前工程（WorldMap 区域 + ZoomLevel 决定截图视口）</param>
    /// <param name="projectDir">工程目录（worldmap_bg.png 写于此）</param>
    /// <param name="cacheDirs">瓦片缓存目录（组态软件 map-cache/ 等；优先读取）</param>
    /// <param name="allowNetwork">是否允许联网下载（false=仅缓存）</param>
    /// <param name="log">日志回调</param>
    public static string? Generate(HMIProject project, string projectDir, IEnumerable<string> cacheDirs, bool allowNetwork, Action<string>? log = null)
    {
        log ??= s => System.Diagnostics.Trace.WriteLine($"[WorldMapScreenshot] {s}");
        var wm = project?.WorldMap;
        if (wm == null)
        {
            log("工程无 WorldMap，跳过底图生成");
            return null;
        }
        var (lngMin, lngMax, latMin, latMax) = ResolveRegion(project);
        if (lngMin == 0 && lngMax == 0 && latMin == 0 && latMax == 0)
        {
            log("WorldMap 无 bounds 且无有效作业点/范围点——跳过底图生成（设备端模拟底图）");
            return null;
        }
        int z = Math.Max(wm.ZoomLevel, 3);   // 高德瓦片有效下限 z3（截图用单级：设备端不缩放，锁定视角一张图）
        if (z > 19) z = 19;

        // 缓存目录列表（map-cache/ + 工程 tiles/ 等）；下载瓦片写入首个缓存目录（供后续无网复用）
        var cacheList = (cacheDirs ?? Enumerable.Empty<string>()).Where(d => !string.IsNullOrEmpty(d)).ToList();
        string? writeCacheTo = cacheList.FirstOrDefault();

        // 视口（与 FW computeBounds/toScreenX/Y 同口径：bounds/点包围盒 + 10% padding + 最小跨度 + resolution）
        double spanLng = Math.Max(lngMax - lngMin, 0.01);
        double spanLat = Math.Max(latMax - latMin, 0.01);
        double padLng = spanLng * 0.1, padLat = spanLat * 0.1;
        double vLngMin = lngMin - padLng, vLngMax = lngMax + padLng;
        double vLatMin = latMin - padLat, vLatMax = latMax + padLat;
        const int width = 1024, height = 600;
        double minX = MercX(vLngMin), maxX = MercX(vLngMax);
        double minY = MercY(vLatMin), maxY = MercY(vLatMax);
        double centerX = (minX + maxX) / 2, centerY = (minY + maxY) / 2;
        double resolution = Math.Max((maxX - minX) / (width * 0.9), (maxY - minY) / (height * 0.9));   // 同 FW L96-98

        // 视口四角经纬度 → 所需瓦片范围（同 FW rebuildTiles 屏幕四角反推——保证截图完整覆盖屏幕）
        int n = 1 << z;
        var (x0, yTop) = LonLatToTile(ScreenToLng(0, centerX, resolution, width), ScreenToLat(0, centerY, resolution, height), z);
        var (x1, yBottom) = LonLatToTile(ScreenToLng(width, centerX, resolution, width), ScreenToLat(height, centerY, resolution, height), z);

        // 收集瓦片（缓存优先 → 联网补充 → 缺失计数）
        int missing = 0;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        for (int tx = x0; tx <= x1; tx++)
        {
            for (int ty = yTop; ty <= yBottom; ty++)
            {
                byte[]? data = ReadCached(cacheList, z, tx, ty);
                if (data == null && allowNetwork)
                    data = DownloadTile(z, tx, ty, writeCacheTo, log);
                if (data == null)
                {
                    missing++;
                    continue;
                }
                using var tile = SKBitmap.Decode(data);
                if (tile == null) { missing++; continue; }
                // 瓦片世界坐标 → 屏幕位置 + 尺寸（同 FW tileScreenX/Y 与 tilePixelSize：
                // worldPerTile = 2πR/n，像素尺寸 = worldPerTile / resolution——审查 🔴 修正：
                // 原公式误乘 TilePixel*256 致瓦片画大 24 倍（z12）→ 底图地物错位 53%，叠加不对齐）
                double worldX = (tx / (double)n - 0.5) * 2 * Math.PI * EarthRadius;
                double worldY = (0.5 - ty / (double)n) * 2 * Math.PI * EarthRadius;
                double sx = (worldX - centerX) / resolution + width / 2;
                double sy = (centerY - worldY) / resolution + height / 2;
                double worldPerTile = 2 * Math.PI * EarthRadius / n;
                double tilePx = worldPerTile / resolution;
                canvas.DrawBitmap(tile, new SKRect((float)sx, (float)sy, (float)(sx + tilePx), (float)(sy + tilePx)));
            }
        }
        if (missing > 0)
        {
            log($"底图生成失败：{missing} 张瓦片无缓存且联网不可用——拒绝部署（用户定：不用模拟底图）");
            return $"瓦片缺失 {missing} 张（缓存+联网均不可用）——拒绝部署";
        }
        string outPath = Path.Combine(projectDir, BackgroundFileName);
        Directory.CreateDirectory(projectDir);
        using var img = SKImage.FromBitmap(bitmap);
        using var png = img.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(outPath);
        png.SaveTo(fs);
        log($"底图已生成: {outPath} ({new FileInfo(outPath).Length / 1024}KB, z{z}, 瓦片 {x0}~{x1}/{yTop}~{yBottom})");
        return null;
    }

    private static byte[]? ReadCached(List<string> cacheDirs, int z, int x, int y)
    {
        foreach (var dir in cacheDirs)
        {
            var path = Path.Combine(dir, z.ToString(), x.ToString(), $"{y}.png");
            if (File.Exists(path))
            {
                try { return File.ReadAllBytes(path); } catch { }
            }
        }
        return null;
    }

    private static byte[]? DownloadTile(int z, int x, int y, string? writeCacheTo, Action<string> log)
    {
        string url = AmapTileUrlTemplate
            .Replace("{s}", (new[] { "1", "2", "3", "4" })[(x + y + z) % 4])
            .Replace("{x}", x.ToString()).Replace("{y}", y.ToString()).Replace("{z}", z.ToString());
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NavigatorHMI/1.0");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.amap.com/");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var data = Task.Run(() => client.GetByteArrayAsync(url)).GetAwaiter().GetResult();   // 防 sync-over-async（K-5 先例）
                if (data.Length < 100 || data[0] != 0x89) return null;   // 非 PNG
                // 写入缓存目录（map-cache/，供后续无网复用）
                if (writeCacheTo != null)
                {
                    try
                    {
                        var p = Path.Combine(writeCacheTo, z.ToString(), x.ToString(), $"{y}.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                        File.WriteAllBytes(p, data);
                    }
                    catch { }
                }
                return data;
            }
            catch (Exception ex)
            {
                log($"z{z}/x{x}/y{y} 下载失败(第{attempt + 1}次): {ex.Message}");
                if (attempt < 2) Thread.Sleep(1500 * (attempt + 1));
            }
        }
        return null;
    }

    private static double MercX(double lng) => lng * EarthRadius * Math.PI / 180.0;
    private static double MercY(double lat) => EarthRadius * Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360));
    private static double ScreenToLng(double sx, double centerX, double resolution, double width)
        => ((sx - width / 2) * resolution + centerX) * 180.0 / (EarthRadius * Math.PI);
    private static double ScreenToLat(double sy, double centerY, double resolution, double height)
    {
        double y = centerY - (sy - height / 2) * resolution;
        return Math.Atan(Math.Sinh(y / EarthRadius)) * 180.0 / Math.PI;
    }
    private static (int X, int Y) LonLatToTile(double lng, double lat, int z)
    {
        int n = 1 << z;
        int x = (int)Math.Floor((lng + 180.0) / 360.0 * n);
        double latRad = lat * Math.PI / 180.0;
        int y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (x, y);
    }

    /// <summary>区域计算（bounds 优先；空则作业点/范围点包围盒 + 10% padding，同 FW computeBounds）。</summary>
    public static (double LngMin, double LngMax, double LatMin, double LatMax) ResolveRegion(HMIProject project)
    {
        var wm = project?.WorldMap;
        if (wm != null && wm.LatMax > wm.LatMin && wm.LngMax > wm.LngMin)
            return (wm.LngMin, wm.LngMax, wm.LatMin, wm.LatMax);
        double minLng = 1e9, maxLng = -1e9, minLat = 1e9, maxLat = -1e9;
        bool any = false;
        if (wm != null)
        {
            foreach (var wp in wm.WorkPoints)
                if (ResolveGeo(project, wp.BoundTag, wp.FixedPoint) is { } g && IsValid(g))
                {
                    minLng = Math.Min(minLng, g.Longitude); maxLng = Math.Max(maxLng, g.Longitude);
                    minLat = Math.Min(minLat, g.Latitude); maxLat = Math.Max(maxLat, g.Latitude);
                    any = true;
                }
            foreach (var rp in wm.WorkRangePoints)
                if (ResolveGeo(project, rp.BoundTag, rp.FixedPoint) is { } g && IsValid(g))
                {
                    minLng = Math.Min(minLng, g.Longitude); maxLng = Math.Max(maxLng, g.Longitude);
                    minLat = Math.Min(minLat, g.Latitude); maxLat = Math.Max(maxLat, g.Latitude);
                    any = true;
                }
        }
        if (!any) return (0, 0, 0, 0);
        double spanLng = Math.Max(maxLng - minLng, 0.01);
        double spanLat = Math.Max(maxLat - minLat, 0.01);
        return (minLng - spanLng * 0.1, maxLng + spanLng * 0.1, minLat - spanLat * 0.1, maxLat + spanLat * 0.1);
    }

    /// <summary>解析点坐标：绑 GPS 变量取基准值（DMS 或小数），否则固定值；无效返回 null（同 FW pointLng/pointLat 与 PC ResolveWorkPointGeo）。
    /// 审查 🟡 场景限定：PC 截图视口按 BaseValue（静态配置）口径——FW pointLng 取运行时变量值（加载时快照）；对齐前提 = 变量初值 = BaseValue
    /// （N-5 快照语义：点不随运行时移动；若运行时初值 ≠ BaseValue，视口/叠加会有漂移，属工程配置责任）。</summary>
    private static GeoPoint? ResolveGeo(HMIProject project, string boundTag, GeoPoint? fixedPoint)
    {
        if (!string.IsNullOrEmpty(boundTag))
        {
            var tag = project?.Tags.FirstOrDefault(t => t.Name == boundTag && t.DataType == TagDataType.GPS);
            if (tag != null && GeoPoint.TryParse(tag.BaseValue, out var g) && g != null && IsValid(g))
                return g;
        }
        return fixedPoint != null && IsValid(fixedPoint) ? fixedPoint : null;
    }

    private static bool IsValid(GeoPoint g)
        => g.Longitude is >= -180 and <= 180 && g.Latitude is >= -90 and <= 90
           && !(g.Longitude == 0 && g.Latitude == 0);
}
