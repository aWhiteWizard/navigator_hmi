using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-6（2026-09-10 ④通信批）：delete/update_tag 引用检查扩展测试——
    /// ① 删除被 MQTT 映射（MqttBinding.TagName）引用 → IN_USE 拒绝；
    /// ② 删除被事件动作参数（tag_write 等 tag_name）引用 → IN_USE 拒绝（画面控件事件 + 世界地图事件）；
    /// ③ update_tag 重命名级联 MQTT 映射 + 事件动作参数；
    /// ④ 作业点名含 , | 分隔符 → 命令层拒绝（X 循环 NEED_RULE 并入）。
    /// </summary>
    public class DeleteTagRefMqttTests
    {
        private static HMIProject ProjectWithMqttBinding()
        {
            var p = new HMIProject { Name = "Y6 引用测试" };
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template });
            p.Tags.Add(new Tag { Name = "被引用变量", DataType = TagDataType.FLOAT });
            p.Tags.Add(new Tag { Name = "独立变量", DataType = TagDataType.FLOAT });
            p.MqttSettings = new MqttSettings
            {
                EnableMqtt = true,
                Topics = { new MqttTopic { Name = "t1", Direction = MqttTopicDirection.Publish, Topic = "a/b" } },
                Bindings = { new MqttBinding { TopicName = "t1", TagName = "被引用变量", FieldName = "val" } },
            };
            return p;
        }

        private static CommandService NewService(HMIProject p) => new(p);

        [Fact]
        public void 删除被MQTT映射引用变量_拒绝IN_USE()
        {
            var p = ProjectWithMqttBinding();
            var svc = NewService(p);
            var r = svc.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = "被引用变量" });
            Assert.False(r.Success);
            Assert.Equal("IN_USE", r.ErrorCode);
            Assert.Contains("MQTT", r.ErrorMessage);
            // 未被引用变量可删
            var ok = svc.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = "独立变量" });
            Assert.True(ok.Success, ok.ErrorMessage);
        }

        [Fact]
        public void 删除被事件动作参数引用变量_拒绝IN_USE()
        {
            var p = ProjectWithMqttBinding();
            p.Screens[0].Widgets.Add(new ButtonWidget
            {
                ObjectName = "btn1",
                Events = new List<WidgetEvent>
                {
                    new()
                    {
                        Type = EventType.onClick,
                        Actions = new List<EventAction>
                        {
                            new() { Type = ActionType.tag_write, Parameters = new() { ["tag_name"] = "被引用变量", ["value"] = "1" } }
                        }
                    }
                }
            });
            var svc = NewService(p);
            var r = svc.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = "被引用变量" });
            Assert.False(r.Success);
            Assert.Equal("IN_USE", r.ErrorCode);
            Assert.Contains("事件动作", r.ErrorMessage);
        }

        [Fact]
        public void 删除被世界地图事件引用变量_拒绝IN_USE()
        {
            var p = ProjectWithMqttBinding();
            p.Screens.Add(new Screen { Name = "地图", Type = ScreenType.WorldMap });
            p.WorldMap = new WorldMapConfig
            {
                Events = new List<WidgetEvent>
                {
                    new()
                    {
                        Type = EventType.onClick,
                        Actions = new List<EventAction>
                        {
                            new() { Type = ActionType.tag_write, Parameters = new() { ["tag_name"] = "被引用变量", ["value"] = "5" } }
                        }
                    }
                }
            };
            var svc = NewService(p);
            var r = svc.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = "被引用变量" });
            Assert.False(r.Success);
            Assert.Equal("IN_USE", r.ErrorCode);
            Assert.Contains("世界地图", r.ErrorMessage);
        }

        [Fact]
        public void 重命名级联MQTT映射与事件动作参数()
        {
            var p = ProjectWithMqttBinding();
            p.Screens[0].Widgets.Add(new ButtonWidget
            {
                ObjectName = "btn1",
                Events = new List<WidgetEvent>
                {
                    new()
                    {
                        Type = EventType.onClick,
                        Actions = new List<EventAction>
                        {
                            new() { Type = ActionType.tag_write, Parameters = new() { ["tag_name"] = "被引用变量", ["value"] = "1" } }
                        }
                    }
                }
            });
            var svc = NewService(p);
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "被引用变量", ["new_name"] = "新名字",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("新名字", p.MqttSettings!.Bindings.Single().TagName);   // MQTT 映射级联
            // 事件动作参数级联
            var actParams = p.Screens[0].Widgets[0].Events[0].Actions[0].Parameters;
            Assert.Equal("新名字", actParams["tag_name"]);
        }

        [Fact]
        public void 删除被地图作业点绑定GPS变量_拒绝IN_USE()
        {
            // Y-6 reviewer 🟡1：作业点 BoundTag 引用 GPS 变量 → 删除拒绝（防地图点悬空）
            var p = new HMIProject { Name = "地图引用" };
            p.Screens.Add(new Screen { Name = "地图", Type = ScreenType.WorldMap });
            p.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template });
            p.Tags.Add(new Tag { Name = "位置", DataType = TagDataType.GPS });
            p.WorldMap = new WorldMapConfig
            {
                WorkRangePoints =
                {
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.0, 30.0) },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.0) },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.1) },
                },
                WorkPoints = { new MapWorkPoint { Name = "作业点1", BoundTag = "位置" } },
            };
            var svc = NewService(p);
            var r = svc.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = "位置" });
            Assert.False(r.Success);
            Assert.Equal("IN_USE", r.ErrorCode);
            Assert.Contains("地图绑定", r.ErrorMessage);
        }

        [Fact]
        public void 重命名级联地图作业点BoundTag()
        {
            // Y-6 reviewer 🟡1：update_tag 重命名 → 地图作业点 BoundTag 级联
            var p = new HMIProject { Name = "地图级联" };
            p.Screens.Add(new Screen { Name = "地图", Type = ScreenType.WorldMap });
            p.Tags.Add(new Tag { Name = "位置", DataType = TagDataType.GPS });
            p.WorldMap = new WorldMapConfig
            {
                WorkPoints = { new MapWorkPoint { Name = "作业点1", BoundTag = "位置" } },
                WorkRangePoints =
                {
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.0, 30.0) },
                    new WorkRangePoint { BoundTag = "位置" },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.1) },
                }
            };
            var svc = NewService(p);
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "位置", ["new_name"] = "新位置",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("新位置", p.WorldMap!.WorkPoints.Single().BoundTag);
            Assert.Equal("新位置", p.WorldMap.WorkRangePoints.Single(x => x.BoundTag.Length > 0).BoundTag);
        }

        [Theory]
        [InlineData("作业点,1号")]
        [InlineData("作业点|1号")]
        public void 作业点名含分隔符_命令层拒绝(string name)
        {
            var p = new HMIProject { Name = "分隔符测试" };
            p.Screens.Add(new Screen { Name = "地图", Type = ScreenType.WorldMap });
            p.WorldMap = new WorldMapConfig
            {
                WorkRangePoints =
                {
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.0, 30.0) },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.0) },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.1) },
                }
            };
            var svc = NewService(p);
            var r = svc.Execute("add_work_point", new Dictionary<string, object?>
            {
                ["screen_name"] = "地图", ["name"] = name,
                ["lng_lat"] = "104.06,30.67",
            });
            Assert.False(r.Success);
            Assert.Contains("分隔符", r.ErrorMessage);
        }

        [Fact]
        public void 作业点名合法_通过()
        {
            var p = new HMIProject { Name = "分隔符测试" };
            p.Screens.Add(new Screen { Name = "地图", Type = ScreenType.WorldMap });
            p.WorldMap = new WorldMapConfig
            {
                WorkRangePoints =
                {
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.0, 30.0) },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.0) },
                    new WorkRangePoint { FixedPoint = new GeoPoint(104.1, 30.1) },
                }
            };
            var svc = NewService(p);
            var r = svc.Execute("add_work_point", new Dictionary<string, object?>
            {
                ["screen_name"] = "地图", ["name"] = "1号泵站",
                ["lng_lat"] = "104.06,30.67",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Single(p.WorldMap!.WorkPoints);
        }
    }
}
