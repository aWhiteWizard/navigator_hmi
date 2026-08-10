using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>P9：世界地图 AI/CLI 命令（作业点/作业范围点/地图配置/地图级事件）。</summary>
    public class WorldMapHandlersTests
    {
        private static (CommandService svc, HMIProject project, Screen worldMap) Create()
        {
            var p = new HMIProject { Name = "P", DeviceWidth = 800, DeviceHeight = 480 };
            p.Screens.Add(new Screen { Name = "主", Width = 800, Height = 480 });
            var wm = new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 800, Height = 480 };
            p.Screens.Add(wm);
            p.WorldMap = new WorldMapConfig();
            var svc = new CommandService(p);
            return (svc, p, wm);
        }

        // ═══ add_work_point ═══

        [Fact]
        public void addWorkPoint_固定经纬度成功()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("add_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "1号泵站", ["lng_lat"] = "(E104.0583, N30.6722)" });
            Assert.True(r.Success);
            var wp = Assert.Single(p.WorldMap!.WorkPoints);
            Assert.Equal("1号泵站", wp.Name);
            Assert.Equal(104.0583, wp.FixedPoint!.Longitude, 4);
            Assert.Equal("", wp.BoundTag);
        }

        [Fact]
        public void addWorkPoint_绑定GPS变量成功()
        {
            var (svc, p, wm) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "GPS" });
            var r = svc.Execute("add_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "站点", ["bound_tag"] = "位置" });
            Assert.True(r.Success);
            var wp = Assert.Single(p.WorldMap!.WorkPoints);
            Assert.Equal("位置", wp.BoundTag);
            Assert.Null(wp.FixedPoint);
        }

        [Fact]
        public void addWorkPoint_双参数互斥拒绝()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("add_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "x", ["lng_lat"] = "(E104, N30)", ["bound_tag"] = "位置" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void addWorkPoint_绑非GPS变量拒绝()
        {
            var (svc, p, wm) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            var r = svc.Execute("add_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "x", ["bound_tag"] = "温度" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void addWorkPoint_非法经纬度拒绝()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("add_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "x", ["lng_lat"] = "abc" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void addWorkPoint_非世界地图画面拒绝()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("add_work_point", new Dictionary<string, object?> { ["screen_name"] = "主", ["name"] = "x", ["lng_lat"] = "(E104, N30)" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        // ═══ delete_work_point ═══

        [Fact]
        public void deleteWorkPoint_成功()
        {
            var (svc, p, wm) = Create();
            p.WorldMap!.WorkPoints.Add(new MapWorkPoint { Name = "A", FixedPoint = new GeoPoint(104, 30) });
            p.WorldMap.WorkPoints.Add(new MapWorkPoint { Name = "B", FixedPoint = new GeoPoint(105, 31) });
            var r = svc.Execute("delete_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "A" });
            Assert.True(r.Success);
            Assert.Single(p.WorldMap.WorkPoints);
            Assert.Equal("B", p.WorldMap.WorkPoints[0].Name);
        }

        [Fact]
        public void deleteWorkPoint_不存在报错()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("delete_work_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["name"] = "不存在" });
            Assert.False(r.Success);
            Assert.Equal("NOT_FOUND", r.ErrorCode);
        }

        // ═══ add_work_range_point / clear_work_range ═══

        [Fact]
        public void addWorkRangePoint_成功()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("add_work_range_point", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["lng_lat"] = "(E104, N30)" });
            Assert.True(r.Success);
            var rp = Assert.Single(p.WorldMap!.WorkRangePoints);
            Assert.Equal(104, rp.FixedPoint!.Longitude, 4);
        }

        [Fact]
        public void clearWorkRange_清空()
        {
            var (svc, p, wm) = Create();
            p.WorldMap!.WorkRangePoints.Add(new WorkRangePoint { FixedPoint = new GeoPoint(104, 30) });
            var r = svc.Execute("clear_work_range", new Dictionary<string, object?> { ["screen_name"] = "世界地图" });
            Assert.True(r.Success);
            Assert.Empty(p.WorldMap.WorkRangePoints);
        }

        // ═══ update_world_map ═══

        [Fact]
        public void updateWorldMap_配置写入()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("update_world_map", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["tile_source"] = "amap", ["zoom_level"] = "12", ["show_global_overlay"] = "true", ["view_locked"] = "true" });
            Assert.True(r.Success);
            Assert.Equal("amap", p.WorldMap!.TileSource);
            Assert.Equal(12, p.WorldMap.ZoomLevel);
            Assert.True(p.WorldMap.ShowGlobalOverlay);
            Assert.True(p.WorldMap.ViewLocked);
        }

        // ═══ 地图级事件（add_event 缺省 widget_name）═══

        [Fact]
        public void addEvent_地图级点击切换成功()
        {
            var (svc, p, wm) = Create();
            p.Screens.Add(new Screen { Name = "泵站1", Width = 800, Height = 480 });
            var r = svc.Execute("add_event", new Dictionary<string, object?> {
                ["screen_name"] = "世界地图",
                ["event_type"] = "onClick",
                ["action_type"] = "screen_switch",
                ["params"] = new Dictionary<string, string> { ["target_screen"] = "泵站1" }
            });
            Assert.True(r.Success);
            var evt = Assert.Single(p.WorldMap!.Events);
            Assert.Equal(EventType.onClick, evt.Type);
            Assert.Equal("泵站1", evt.Actions[0].Parameters["target_screen"]);
        }

        [Fact]
        public void addEvent_非世界地图缺省widget拒绝()
        {
            var (svc, p, wm) = Create();
            var r = svc.Execute("add_event", new Dictionary<string, object?> { ["screen_name"] = "主", ["event_type"] = "onClick", ["action_type"] = "screen_switch" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void removeEvent_地图级移除()
        {
            var (svc, p, wm) = Create();
            p.WorldMap!.Events.Add(new WidgetEvent { Type = EventType.onClick, Actions = { new EventAction { Type = ActionType.screen_switch } } });
            var r = svc.Execute("remove_event", new Dictionary<string, object?> { ["screen_name"] = "世界地图", ["event_type"] = "onClick", ["action_type"] = "screen_switch" });
            Assert.True(r.Success);
            Assert.Empty(p.WorldMap.Events);
        }
    }
}
