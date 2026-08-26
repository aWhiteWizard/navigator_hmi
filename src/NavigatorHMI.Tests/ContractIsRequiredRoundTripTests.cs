using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>2026-08-26 代码规范整改：默认 true bool 契约 IsRequired 修复后的 round-trip 回读测试（防 protobuf-net 省略 false 丢值回归）。</summary>
    public class ContractIsRequiredRoundTripTests
    {
        private static byte[] Serialize<T>(T obj)
        {
            using var ms = new System.IO.MemoryStream();
            ProtoBuf.Serializer.Serialize(ms, obj);
            return ms.ToArray();
        }

        private static T Deserialize<T>(byte[] bytes)
        {
            return ProtoBuf.Serializer.Deserialize<T>(new System.IO.MemoryStream(bytes));
        }

        [Fact]
        public void AlarmRule_AckRequired_false_回读保持false()
        {
            var rule = new AlarmRule { Name = "报警1", AckRequired = false };
            var back = Deserialize<AlarmRule>(Serialize(rule));
            Assert.False(back.AckRequired);   // IsRequired=true 强制写 false，回读不丢
        }

        [Fact]
        public void AlarmRule_AckRequired_缺省true_回读true()
        {
            var rule = new AlarmRule { Name = "报警2" };   // 缺省 true
            var back = Deserialize<AlarmRule>(Serialize(rule));
            Assert.True(back.AckRequired);
        }

        [Fact]
        public void HMIProject_ShowNavigationBar_false_回读保持false()
        {
            var p = new HMIProject { Name = "工程", ShowNavigationBar = false };
            var back = Deserialize<HMIProject>(Serialize(p));
            Assert.False(back.ShowNavigationBar);
        }

        [Fact]
        public void Screen_ShowInNav_false_回读保持false()
        {
            var s = new Screen { Name = "画面1", ShowInNav = false };
            var back = Deserialize<Screen>(Serialize(s));
            Assert.False(back.ShowInNav);
        }

        [Fact]
        public void DateTimeWidget_序列化round_trip_Text与Format完整()
        {
            var w = new DateTimeWidget { Text = "2026-01-02 03:04:05", Format = "yyyy/MM/dd" };
            var back = Deserialize<DateTimeWidget>(Serialize(w));
            Assert.Equal("2026-01-02 03:04:05", back.Text);
            Assert.Equal("yyyy/MM/dd", back.Format);
        }

        [Fact]
        public void DTO_ShowTitleBar_false_回读保持false()
        {
            var dto = new NavihmiWidget { ObjectName = "窗口1", ShowTitleBar = false };
            var back = Deserialize<NavihmiWidget>(Serialize(dto));
            Assert.False(back.ShowTitleBar);
        }
    }
}
