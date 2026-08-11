using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using ProtoBuf;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>P10：WorldMap 契约审计——C# ProtoMember 字段号与 fw/proto/navihmi.proto 对齐（防漂移）。</summary>
    public class WorldMapContractAuditTests
    {
        private static Dictionary<string, int> FieldNumbers(Type t) => t.GetProperties()
            .Select(pi => (pi, attr: pi.GetCustomAttribute<ProtoMemberAttribute>()))
            .Where(x => x.attr != null)
            .ToDictionary(x => x.pi.Name, x => x.attr!.Tag);

        [Fact]
        public void WorldMapConfig_字段号与proto对齐()
        {
            var fields = FieldNumbers(typeof(WorldMapConfig));
            // proto: lat_min=1 lat_max=2 lng_min=3 lng_max=4 tile_source=5 zoom_level=6 show_global_overlay=7 work_points=8 work_range_points=9 events=10 view_locked=11
            Assert.Equal(1, fields[nameof(WorldMapConfig.LatMin)]);
            Assert.Equal(2, fields[nameof(WorldMapConfig.LatMax)]);
            Assert.Equal(3, fields[nameof(WorldMapConfig.LngMin)]);
            Assert.Equal(4, fields[nameof(WorldMapConfig.LngMax)]);
            Assert.Equal(5, fields[nameof(WorldMapConfig.TileSource)]);
            Assert.Equal(6, fields[nameof(WorldMapConfig.ZoomLevel)]);
            Assert.Equal(7, fields[nameof(WorldMapConfig.ShowGlobalOverlay)]);
            Assert.Equal(8, fields[nameof(WorldMapConfig.WorkPoints)]);
            Assert.Equal(9, fields[nameof(WorldMapConfig.WorkRangePoints)]);
            Assert.Equal(10, fields[nameof(WorldMapConfig.Events)]);
            Assert.Equal(11, fields[nameof(WorldMapConfig.ViewLocked)]);
            Assert.Equal(11, fields.Count);   // 无多无少
        }

        [Fact]
        public void MapWorkPoint_字段号与proto对齐()
        {
            var fields = FieldNumbers(typeof(MapWorkPoint));
            Assert.Equal(1, fields[nameof(MapWorkPoint.Name)]);
            Assert.Equal(2, fields[nameof(MapWorkPoint.FixedPoint)]);
            Assert.Equal(3, fields[nameof(MapWorkPoint.BoundTag)]);
            Assert.Equal(3, fields.Count);
        }

        [Fact]
        public void WorkRangePoint_字段号与proto对齐()
        {
            var fields = FieldNumbers(typeof(WorkRangePoint));
            Assert.Equal(1, fields[nameof(WorkRangePoint.FixedPoint)]);
            Assert.Equal(2, fields[nameof(WorkRangePoint.BoundTag)]);
            Assert.Equal(2, fields.Count);
        }

        [Fact]
        public void GeoPoint_字段号与proto对齐()
        {
            var fields = FieldNumbers(typeof(GeoPoint));
            Assert.Equal(1, fields[nameof(GeoPoint.Longitude)]);
            Assert.Equal(2, fields[nameof(GeoPoint.Latitude)]);
        }

        [Fact]
        public void PolygonWidget_仅画面坐标无Geo字段()
        {
            // P1：GeoPoints 移除，多边形仅 points=64（画面坐标）
            var fields = FieldNumbers(typeof(PolygonWidget));
            Assert.DoesNotContain("GeoPoints", fields.Keys);
            Assert.Contains(nameof(PolygonWidget.Points), fields.Keys);
        }

        [Fact]
        public void 控件类型枚举_无Point()
        {
            // P1：PointWidget 整体移除——枚举名与 ProtoInclude 类型均不应再出现
            Assert.DoesNotContain("Point", Enum.GetNames<NavihmiWidgetType>());
            var includes = typeof(Widget).GetCustomAttributes<ProtoIncludeAttribute>().Select(a => a.KnownType);
            Assert.DoesNotContain(includes, t => t.Name == "PointWidget");
        }

        [Fact]
        public void addWidget_point拒绝()
        {
            // P3：命令层 add_widget 校验不再接受 "point"（P1 已删除 Point 控件，防残留复活）
            var p = new HMIProject { Name = "P" };
            p.Screens.Add(new Screen { Name = "主", Width = 800, Height = 480 });
            var svc = new CommandService(p);
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "point" });
            Assert.False(r.Success);
            Assert.Contains("未知控件类型", r.ErrorMessage);
        }
    }
}
