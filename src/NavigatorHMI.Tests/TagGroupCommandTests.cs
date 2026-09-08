using System.Linq;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// Y-5a（2026-09-10 ④通信批）：变量分组 Tag.Group 命令层测试——
    /// create/update --group、--group "" 清空、list-tags --group 过滤（含「未分组」）、list-tag-groups 清单。
    /// 分组语义：运行时仅 PC 组态（UI 收起展开），FW 透传（Y-5 FW 半）；组无独立对象，重命名/删除 = 批量 update-tag。
    /// </summary>
    public class TagGroupCommandTests
    {
        private static CommandService NewService(out HMIProject project)
        {
            project = new HMIProject { Name = "分组测试" };
            project.Tags.Add(new Tag { Name = "电机A转速", DataType = TagDataType.FLOAT });
            project.Tags.Add(new Tag { Name = "电机A电流", DataType = TagDataType.FLOAT });
            project.Tags.Add(new Tag { Name = "泵站压力", DataType = TagDataType.FLOAT });
            return new CommandService(project);
        }

        [Fact]
        public void createTag_带group_落库()
        {
            var svc = NewService(out var p);
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "新变量", ["data_type"] = "FLOAT", ["group"] = "电机组",
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("电机组", p.Tags.Single(t => t.Name == "新变量").Group);
        }

        [Fact]
        public void updateTag_设置group与清空()
        {
            var svc = NewService(out var p);
            var ok = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "电机A转速", ["group"] = "电机组",
            });
            Assert.True(ok.Success, ok.ErrorMessage);
            Assert.Equal("电机组", p.Tags.Single(t => t.Name == "电机A转速").Group);
            // 显式空串 = 清空为未分组
            var clear = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "电机A转速", ["group"] = "",
            });
            Assert.True(clear.Success, clear.ErrorMessage);
            Assert.Equal("", p.Tags.Single(t => t.Name == "电机A转速").Group);
        }

        [Fact]
        public void updateTag_未提供group_不改动()
        {
            var svc = NewService(out var p);
            p.Tags.Single(t => t.Name == "电机A转速").Group = "电机组";
            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "电机A转速", ["unit"] = "rpm",   // 只改 unit，不带 group
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal("电机组", p.Tags.Single(t => t.Name == "电机A转速").Group);   // 保留现值
        }

        [Fact]
        public void listTags_全部与分组过滤()
        {
            var svc = NewService(out var p);
            svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "电机A转速", ["group"] = "电机组" });
            svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "电机A电流", ["group"] = "电机组" });
            svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "泵站压力", ["group"] = "泵站组" });

            var all = svc.Execute("list_tags", new Dictionary<string, object?>());
            var allJson = System.Text.Json.JsonSerializer.Serialize(all.Data);
            Assert.Contains("\"count\":3", allJson);

            var motor = svc.Execute("list_tags", new Dictionary<string, object?> { ["group"] = "电机组" });
            var motorRoot = System.Text.Json.JsonSerializer.SerializeToElement(motor.Data);
            var motorTags = motorRoot.GetProperty("tags");
            Assert.Equal(2, motorTags.GetArrayLength());
            var names = motorTags.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
            Assert.Contains("电机A转速", names);
            Assert.DoesNotContain("泵站压力", names);

            // 「未分组」特殊值 = 空分组变量
            var ungrouped = svc.Execute("list_tags", new Dictionary<string, object?> { ["group"] = "未分组" });
            var ugRoot = System.Text.Json.JsonSerializer.SerializeToElement(ungrouped.Data);
            Assert.Equal(0, ugRoot.GetProperty("tags").GetArrayLength());   // 本例全部分了组
        }

        [Fact]
        public void listTagGroups_分组清单与未分组计数()
        {
            var svc = NewService(out var p);
            svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "电机A转速", ["group"] = "电机组" });
            svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "电机A电流", ["group"] = "电机组" });
            svc.Execute("update_tag", new Dictionary<string, object?> { ["name"] = "泵站压力", ["group"] = "泵站组" });
            // 泵站压力后再留一个未分组变量
            p.Tags.Add(new Tag { Name = "游离变量", DataType = TagDataType.BOOL });

            var r = svc.Execute("list_tag_groups", new Dictionary<string, object?>());
            var root = System.Text.Json.JsonSerializer.SerializeToElement(r.Data);
            var groups = root.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).ToList();
            Assert.Contains("电机组", groups);
            Assert.Contains("泵站组", groups);
            Assert.Equal(2, root.GetProperty("count").GetInt32());
            Assert.Equal(1, root.GetProperty("ungrouped_count").GetInt32());
        }

        [Fact]
        public void Tag_Group_序列化roundTrip_保真()
        {
            // Tag.Group ProtoMember(10) round-trip（MqttContractTests 已有单值——这里走工程级 HMIProject 持久化路径）
            var p = new HMIProject { Name = "P" };
            p.Tags.Add(new Tag { Name = "V1", Group = "组A" });
            using var ms = new System.IO.MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, p);
            ms.Position = 0;
            var back = ProtoBuf.Serializer.Deserialize<HMIProject>(ms);
            Assert.Equal("组A", back.Tags.Single().Group);
        }

        [Theory]
        [InlineData("create_tag")]
        [InlineData("update_tag")]
        public void 组名未分组保留字_拒绝(string cmd)
        {
            // Y-5a reviewer 🟡：组名「未分组」为哨兵保留字（list-tags --group 过滤语义）——create/update 均拒绝
            var svc = NewService(out var p);
            var pDict = new Dictionary<string, object?>
            {
                ["name"] = "电机A转速", ["data_type"] = "FLOAT", ["group"] = "未分组",
            };
            if (cmd == "update_tag") pDict = new Dictionary<string, object?> { ["name"] = "电机A转速", ["group"] = "未分组" };
            var r = svc.Execute(cmd, pDict);
            Assert.False(r.Success);
            Assert.Contains("保留字", r.ErrorMessage);
        }
    }
}
