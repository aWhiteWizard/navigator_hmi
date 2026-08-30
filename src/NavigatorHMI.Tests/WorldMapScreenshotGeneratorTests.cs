using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>
/// N 循环 N-1（2026-08-30 用户定方案，替代瓦片铺贴）：世界地图锁定视角底图生成器测试。
/// 覆盖：区域计算（bounds 优先/点包围盒/范围点/无点全 0）、缓存复用拼图、缺失拒绝。
/// 注：联网下载逻辑（DownloadTile）不测试网络面——用预置缓存瓦片走完整拼图路径。
/// </summary>
public class WorldMapScreenshotGeneratorTests : IDisposable
{
    private readonly string _dir;

    public WorldMapScreenshotGeneratorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "navihmi_bg_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static HMIProject NewProject()
    {
        var p = new HMIProject { Name = "底图测试", ProjectFilePath = Path.Combine(Path.GetTempPath(), "bg-test.hmiproj") };
        return p;
    }

    private static void WriteTile(string dir, int z, int x, int y)
    {
        // 真实可解码 PNG（SKBitmap 编码）——拼图 Decode 依据
        var full = Path.Combine(dir, z.ToString(), x.ToString(), $"{y}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var bmp = new SkiaSharp.SKBitmap(32, 32);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        canvas.Clear(SkiaSharp.SKColors.Green);
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var png = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(full);
        png.SaveTo(fs);
    }

    [Fact]
    public void 区域计算_bounds有效_优先用bounds()
    {
        var p = NewProject();
        p.WorldMap = new WorldMapConfig
        {
            LatMin = 30.0, LatMax = 31.0, LngMin = 103.0, LngMax = 105.0,
            WorkPoints = { new MapWorkPoint { Name = "点", FixedPoint = new GeoPoint(1, 1) } }
        };
        var (lngMin, lngMax, latMin, latMax) = WorldMapScreenshotGenerator.ResolveRegion(p);
        Assert.Equal(103.0, lngMin);
        Assert.Equal(105.0, lngMax);
        Assert.Equal(30.0, latMin);
        Assert.Equal(31.0, latMax);
    }

    [Fact]
    public void 区域计算_bounds全0_按点包围盒()
    {
        var p = NewProject();
        p.WorldMap = new WorldMapConfig
        {
            WorkPoints =
            {
                new MapWorkPoint { Name = "点1", FixedPoint = new GeoPoint(104.0, 30.0) },
                new MapWorkPoint { Name = "点2", FixedPoint = new GeoPoint(105.0, 31.0) }
            }
        };
        var (lngMin, lngMax, latMin, latMax) = WorldMapScreenshotGenerator.ResolveRegion(p);
        Assert.Equal(103.9, lngMin, 4);
        Assert.Equal(105.1, lngMax, 4);
        Assert.Equal(29.9, latMin, 4);
        Assert.Equal(31.1, latMax, 4);
    }

    [Fact]
    public void 区域计算_范围点参与包围盒()
    {
        var p = NewProject();
        p.WorldMap = new WorldMapConfig
        {
            WorkRangePoints =
            {
                new WorkRangePoint { FixedPoint = new GeoPoint(104.0, 30.0) },
                new WorkRangePoint { FixedPoint = new GeoPoint(103.0, 29.0) }
            }
        };
        var (lngMin, lngMax, latMin, latMax) = WorldMapScreenshotGenerator.ResolveRegion(p);
        Assert.True(lngMin < 103.0 && lngMax > 104.0 && latMin < 29.0 && latMax > 30.0);
    }

    [Fact]
    public void 区域计算_无bounds无点_返回全0()
    {
        var p = NewProject();
        p.WorldMap = new WorldMapConfig();
        var (lngMin, lngMax, latMin, latMax) = WorldMapScreenshotGenerator.ResolveRegion(p);
        Assert.Equal(0, lngMin);
        Assert.Equal(0, lngMax);
        Assert.Equal(0, latMin);
        Assert.Equal(0, latMax);
    }

    [Fact]
    public void 生成_缓存瓦片齐全_拼图成功()
    {
        // 预置缓存覆盖视口所需全部瓦片（z10，区域 104.0~104.1/30.0~30.1 视口四角 → x=807~808 y=422）
        // → 拼图成功（返回 null）+ PNG 生成；断言输出尺寸 1024x600 + 中心像素 ≈ 预置瓦片色（防绘制公式回归——审查 🔴）
        var p = NewProject();
        p.WorldMap = new WorldMapConfig
        {
            LatMin = 30.0, LatMax = 30.1, LngMin = 104.0, LngMax = 104.1,
            ZoomLevel = 10
        };
        var projectDir = Path.Combine(_dir, "proj");
        Directory.CreateDirectory(projectDir);
        var cache = Path.Combine(_dir, "cache");
        WriteTile(cache, 10, 807, 422);
        WriteTile(cache, 10, 808, 422);
        var err = WorldMapScreenshotGenerator.Generate(p, projectDir, new[] { cache }, allowNetwork: false);
        Assert.Null(err);
        var bgPath = Path.Combine(projectDir, "worldmap_bg.png");
        Assert.True(File.Exists(bgPath), "底图应生成");

        // 内容断言：输出 1024x600，中心像素 ≈ 预置绿色瓦片（SKColors.Green = RGB(0,128,0)——绘制尺寸公式回归捕获）
        using var bmp = SkiaSharp.SKBitmap.Decode(bgPath);
        Assert.NotNull(bmp);
        Assert.Equal(1024, bmp.Width);
        Assert.Equal(600, bmp.Height);
        var center = bmp.GetPixel(512, 300);
        Assert.True(Math.Abs(center.Green - 128) <= 30 && center.Red < 50,
            $"中心像素应为预置绿色瓦片，实际 RGB=({center.Red},{center.Green},{center.Blue})");
    }

    [Fact]
    public void 生成_缓存缺失_返回拒绝原因()
    {
        // 无缓存无网络 → 返回非 null（拒绝部署）
        var p = NewProject();
        p.WorldMap = new WorldMapConfig
        {
            LatMin = 30.0, LatMax = 30.1, LngMin = 104.0, LngMax = 104.1,
            ZoomLevel = 10
        };
        var projectDir = Path.Combine(_dir, "proj2");
        Directory.CreateDirectory(projectDir);
        var err = WorldMapScreenshotGenerator.Generate(p, projectDir, Enumerable.Empty<string>(), allowNetwork: false);
        Assert.NotNull(err);
        Assert.Contains("缺失", err);
    }

    [Fact]
    public void 生成_无WorldMap_跳过返回null()
    {
        var p = NewProject();   // 无 WorldMap
        var projectDir = Path.Combine(_dir, "proj3");
        Directory.CreateDirectory(projectDir);
        var err = WorldMapScreenshotGenerator.Generate(p, projectDir, Enumerable.Empty<string>(), allowNetwork: false);
        Assert.Null(err);
    }

    [Fact]
    public void 生成_无区域_跳过返回null()
    {
        var p = NewProject();
        p.WorldMap = new WorldMapConfig();   // 无 bounds 无点
        var projectDir = Path.Combine(_dir, "proj4");
        Directory.CreateDirectory(projectDir);
        var err = WorldMapScreenshotGenerator.Generate(p, projectDir, Enumerable.Empty<string>(), allowNetwork: false);
        Assert.Null(err);
    }
}
