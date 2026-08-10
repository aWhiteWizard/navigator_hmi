using NavigatorHMI.CommandLayer;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>世界地图批 2：GPS 变量命令层（create_tag 校验 / TagCompatibility / add_widget 新控件）。</summary>
    public class WorldMapBatch2Tests
    {
        private static (CommandService svc, NavigatorHMI.Common.HMIProject project, NavigatorHMI.Common.Screen screen) Create()
        {
            var p = new NavigatorHMI.Common.HMIProject { Name = "P", DeviceWidth = 800, DeviceHeight = 480 };
            var s = new NavigatorHMI.Common.Screen { Name = "主", Width = 800, Height = 480 };
            p.Screens.Add(s);
            var svc = new CommandService(p);
            return (svc, p, s);
        }

        // ═══ add_widget 新控件（polygon/point）═══

        [Fact]
        public void addWidget_polygon创建成功()
        {
            var (svc, p, s) = Create();
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "polygon" });
            Assert.True(r.Success);
            Assert.IsType<NavigatorHMI.Common.PolygonWidget>(s.Widgets[0]);
        }

        [Fact]
        public void addWidget_point带标签创建成功()
        {
            var (svc, p, s) = Create();
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "point", ["label"] = "1号泵站" });
            Assert.True(r.Success);
            var pw = Assert.IsType<NavigatorHMI.Common.PointWidget>(s.Widgets[0]);
            Assert.Equal("1号泵站", pw.Label);
        }

        // ═══ GPS 变量创建与基准值校验（create_tag）═══

        [Fact]
        public void createTag_GPS合法基准值通过()
        {
            var (svc, p, s) = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "位置", ["data_type"] = "GPS",
                ["base_value"] = "(E104°3'30\", N30°40'20\")"
            });
            Assert.True(r.Success);
            Assert.Equal(NavigatorHMI.Common.TagDataType.GPS, p.Tags[0].DataType);
        }

        [Fact]
        public void createTag_GPS非法基准值拒绝()
        {
            var (svc, p, s) = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "GPS", ["base_value"] = "E999°, N30°" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void createTag_GPS小数度基准值归一DMS()
        {
            var (svc, p, s) = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "GPS", ["base_value"] = "104.0583, 30.6722" });
            Assert.True(r.Success);
            // 需求：输入小数度自动转 DMS 存储（基准值归一为 DMS 括号格式）
            Assert.Equal("(E104°3'30\", N30°40'20\")", p.Tags[0].BaseValue);
        }

        // ═══ TagCompatibility：图形类可绑 GPS，其余禁止 ═══

        [Theory]
        [InlineData("line")]
        [InlineData("circle")]
        [InlineData("polygon")]
        [InlineData("point")]
        public void 图形类控件_绑GPS通过(string type)
        {
            var (svc, p, s) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "GPS" });
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = type, ["bound_tag"] = "位置" });
            Assert.True(r.Success);
        }

        [Theory]
        [InlineData("button")]
        [InlineData("text")]
        [InlineData("numeric")]
        [InlineData("switch")]
        [InlineData("rectangle")]
        public void 非图形类控件_绑GPS拒绝(string type)
        {
            var (svc, p, s) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "GPS" });
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = type, ["bound_tag"] = "位置" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_TYPE", r.ErrorCode);
        }

        [Fact]
        public void 图形类控件_绑非GPS拒绝()
        {
            var (svc, p, s) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "温度", ["data_type"] = "FLOAT" });
            var r = svc.Execute("add_widget", new Dictionary<string, object?> { ["screen_name"] = "主", ["widget_type"] = "point", ["bound_tag"] = "温度" });
            Assert.False(r.Success);
            Assert.Equal("INVALID_TYPE", r.ErrorCode);
        }

        // ═══ 改类型到 GPS 的旧值归一（双入口一致：create 归一 / update 改类型归一）═══

        [Fact]
        public void updateTag_改类型到GPS_旧小数度归一DMS()
        {
            var (svc, p, s) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "STRING", ["base_value"] = "104.0583, 30.6722" });
            var r = svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "位置", ["data_type"] = "GPS" });
            Assert.True(r.Success);
            Assert.Equal(NavigatorHMI.Common.TagDataType.GPS, p.Tags[0].DataType);
            Assert.Equal("(E104°3'30\", N30°40'20\")", p.Tags[0].BaseValue);   // 与 create_tag 归一格式一致
        }

        // ═══ AI 入口可达：create_tag/update_tag 的 data_type EnumValues 含 GPS ═══

        [Fact]
        public void createUpdateTag_EnumValues含GPS()
        {
            foreach (var def in new[]
            {
                new NavigatorHMI.CommandLayer.Handlers.CreateTagHandler().Definition,
                new NavigatorHMI.CommandLayer.Handlers.UpdateTagHandler().Definition,
            })
            {
                var ev = def.Parameters["data_type"].EnumValues;
                Assert.Contains("GPS", ev, StringComparer.Ordinal);
            }
        }
    }
}
