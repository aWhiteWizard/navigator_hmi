using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 世界地图配置。世界地图是一个特殊画面类型（<see cref="ScreenType.WorldMap"/>），不是普通控件。
    /// 每个工程最多一个世界地图画面，默认不可删除。
    /// </summary>
    [ProtoContract]
    public class WorldMapConfig
    {
        /// <summary>显示区域最小纬度</summary>
        [ProtoMember(1)]
        public double LatMin { get; set; }

        /// <summary>显示区域最大纬度</summary>
        [ProtoMember(2)]
        public double LatMax { get; set; }

        /// <summary>显示区域最小经度</summary>
        [ProtoMember(3)]
        public double LngMin { get; set; }

        /// <summary>显示区域最大经度</summary>
        [ProtoMember(4)]
        public double LngMax { get; set; }

        /// <summary>地图瓦片来源："offline"（离线瓦片）| "amap"（高德地图）| 其他自定义源</summary>
        [ProtoMember(5)]
        public string TileSource { get; set; } = "offline";

        /// <summary>默认缩放级别（值越大越详细）</summary>
        [ProtoMember(6)]
        public int ZoomLevel { get; set; } = 10;

        /// <summary>
        /// 是否在世界地图上叠加全局画面控件。
        /// 世界地图默认关闭全局叠加（避免遮挡地图），其他画面类型强制叠加不可配置。
        /// </summary>
        [ProtoMember(7)]
        public bool ShowGlobalOverlay { get; set; } = false;

        /// <summary>作业点列表（绑经纬度变量或固定值；视口自适应按这些点包围盒计算）。</summary>
        [ProtoMember(8)]
        public List<MapWorkPoint> WorkPoints { get; set; } = new();

        /// <summary>作业范围点列表（围栏顶点，无名称；绑经纬度变量或固定值——二选一）。</summary>
        [ProtoMember(9)]
        public List<WorkRangePoint> WorkRangePoints { get; set; } = new();

        /// <summary>点击切换画面事件（运行时点击地图空白处触发，首个匹配事件执行）。</summary>
        [ProtoMember(10)]
        public List<WidgetEvent> Events { get; set; } = new();

        /// <summary>锁定预览：运行时禁平移/缩放；点击切换由 FW 无条件执行，不受锁影响（配合组态侧 ZoomToBox 一次性框选视口）。
        /// N-4（2026-08-30）：注释同步新语义——ViewLocked 只禁平移缩放，不拦点击切换（M 循环已修 FW 行为）。</summary>
        [ProtoMember(11)]
        public bool ViewLocked { get; set; }
    }

    /// <summary>
    /// 世界地图作业范围点（围栏顶点）。绑经纬度变量（GPS 类型，动态移动）或写固定值（<see cref="FixedPoint"/>）——二选一；无名称。
    /// </summary>
    [ProtoContract]
    public class WorkRangePoint
    {
        /// <summary>经纬度固定值（组态写死；有 BoundTag 时以变量动态值为准）</summary>
        [ProtoMember(1)]
        public GeoPoint? FixedPoint { get; set; }

        /// <summary>绑定的经纬度变量名（GPS 类型；为空 = 用 FixedPoint 固定值）</summary>
        [ProtoMember(2)]
        public string BoundTag { get; set; } = "";
    }

    /// <summary>
    /// 世界地图作业点。绑经纬度变量（GPS 类型，动态移动）或写固定值（<see cref="FixedPoint"/>）——二选一。
    /// </summary>
    [ProtoContract]
    public class MapWorkPoint
    {
        /// <summary>作业点名称（如 "1号泵站"）</summary>
        [ProtoMember(1)]
        public string Name { get; set; } = "";

        /// <summary>经纬度固定值（组态写死；有 BoundTag 时以变量动态值为准）</summary>
        [ProtoMember(2)]
        public GeoPoint? FixedPoint { get; set; }

        /// <summary>绑定的经纬度变量名（GPS 类型；为空 = 用 FixedPoint 固定值）</summary>
        [ProtoMember(3)]
        public string BoundTag { get; set; } = "";
    }
}
