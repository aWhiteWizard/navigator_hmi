using System.Collections.Generic;
using System.Linq;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AH（2026-10-30 用户需求）：需求表里常写「小数位 N」，AI 直接传 decimals 即可——
    /// 命令层自动推导换算系数 scale = 10^-N（设备用放大整数传小数时成立）。
    /// 显式给了 scale 时以 scale 为准（decimals 只作显示精度）。
    /// </summary>
    public class TagScaleDecimalsTests
    {
        private static (CommandService svc, HMIProject project) Create()
        {
            var project = new HMIProject { Name = "换算/小数位" };
            return (new CommandService(project), project);
        }

        [Fact]
        public void 建变量时小数位1_自动推导scale_0点1()
        {
            var (svc, project) = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度",
                ["data_type"] = "FLOAT",
                ["source"] = "modbus://100/1",
                ["unit"] = "℃",
                ["decimals"] = 1,
            });
            Assert.True(r.Success, r.ErrorMessage);
            var t = project.Tags.Single();
            Assert.Equal(1, t.Decimals);
            Assert.Equal(0.1, t.Scale, 6);
        }

        [Theory]
        [InlineData(1, 0.1)]
        [InlineData(2, 0.01)]
        [InlineData(3, 0.001)]
        public void 小数位N_推导10的负N次(int decimals, double expectedScale)
        {
            var (svc, project) = Create();
            // AH（2026-10-31 用户裁决）：小数位只对 FLOAT 有效（整型换算后设备端取整）
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "v",
                ["data_type"] = "FLOAT",
                ["decimals"] = decimals,
            });
            Assert.Equal(expectedScale, project.Tags.Single().Scale, 8);
        }

        [Theory]
        [InlineData("UINT16")]
        [InlineData("INT16")]
        [InlineData("INT32")]
        [InlineData("BOOL")]
        public void 整型变量不接受小数位_报INVALID_PARAM(string dataType)
        {
            var (svc, project) = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度",
                ["data_type"] = dataType,
                ["decimals"] = 1,
            });
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
            Assert.Empty(project.Tags);   // 一个变量都没落库
        }

        [Fact]
        public void 整型变量仍可用scale换算_但小数位必须为0()
        {
            var (svc, project) = Create();
            var r = svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度",
                ["data_type"] = "UINT16",
                ["scale"] = 0.1,      // 允许：设备端换算后取整显示（211×0.1 ⇒ 21）
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(0.1, project.Tags.Single().Scale, 6);
            Assert.Equal(0, project.Tags.Single().Decimals);
        }

        [Fact]
        public void 显式scale优先于小数位推导()
        {
            var (svc, project) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "v",
                ["data_type"] = "FLOAT",
                ["scale"] = 10.0,     // 显式：×10 设备（raw 2.06 已小数）
                ["decimals"] = 1,     // 仅作显示精度
            });
            Assert.Equal(10.0, project.Tags.Single().Scale, 8);
            Assert.Equal(1, project.Tags.Single().Decimals);
        }

        [Fact]
        public void 更新变量时小数位1_推导scale_0点1()
        {
            var (svc, project) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度",
                ["data_type"] = "FLOAT",
                ["source"] = "modbus://100/1",
            });
            Assert.Equal(1.0, project.Tags.Single().Scale, 8);   // 初始不换算

            var r = svc.Execute("update_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度",
                ["decimals"] = 1,
            });
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(0.1, project.Tags.Single().Scale, 6);
        }

        [Fact]
        public void 换算生效_原始206变20点6()
        {
            var (svc, project) = Create();
            svc.Execute("create_tag", new Dictionary<string, object?>
            {
                ["name"] = "温度", ["data_type"] = "FLOAT", ["decimals"] = 1,
            });
            Assert.Equal(20.6, project.Tags.Single().ApplyScale(206), 6);
        }
    }
}
