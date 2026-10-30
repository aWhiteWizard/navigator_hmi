using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.Common;
using NavigatorHMI.Core.Services;
using NavigatorHMI.Common;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// AH 修复（2026-10-30 用户实测「AI Agent 不能取消世界地图的显示」）：
    /// 开关 `HMIProject.IncludeWorldMapOnDevice`（proto 23）在 P-3 就实现了，生成器会按它过滤
    /// WorldMap 画面与地图配置；但此前**只有 GUI 属性面板能改**，命令层无入口 ⇒ AI 只能去删受保护的画面。
    /// 本测试锁死新命令 `set_world_map_visible` 的行为与编译过滤效果。
    /// </summary>
    public class WorldMapVisibleCommandTests
    {
        private static (CommandService svc, HMIProject project) Create()
        {
            var project = new HMIProject { Name = "地图开关" };
            project.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template, Width = 1024, Height = 600 });
            project.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 1024, Height = 600 });
            project.Screens.Add(new Screen { Name = "画面1", Type = ScreenType.Custom, Width = 1024, Height = 600 });
            return (new CommandService(project), project);
        }

        private static CommandResult Run(CommandService svc, object? visible)
            => svc.Execute("set_world_map_visible", new Dictionary<string, object?> { ["visible"] = visible });

        [Fact]
        public void 关闭世界地图_开关置false()
        {
            var (svc, project) = Create();
            Assert.True(project.IncludeWorldMapOnDevice);   // 默认开
            var r = Run(svc, false);
            Assert.True(r.Success, r.ErrorMessage);
            Assert.False(project.IncludeWorldMapOnDevice);
        }

        [Fact]
        public void 重新打开世界地图_开关置true()
        {
            var (svc, project) = Create();
            Run(svc, false);
            Assert.True(Run(svc, true).Success);
            Assert.True(project.IncludeWorldMapOnDevice);
        }

        [Theory]
        [InlineData("false")]
        [InlineData("0")]
        [InlineData("否")]
        [InlineData("隐藏")]
        public void 宽松布尔解析_关闭语义(string raw)
        {
            var (svc, project) = Create();
            Assert.True(Run(svc, raw).Success);
            Assert.False(project.IncludeWorldMapOnDevice);
        }

        [Fact]
        public void 非法值_报INVALID_PARAM()
        {
            var (svc, _) = Create();
            var r = Run(svc, "也许吧");
            Assert.False(r.Success);
            Assert.Equal("INVALID_PARAM", r.ErrorCode);
        }

        [Fact]
        public void 关闭后编译_产物不含世界地图画面()
        {
            var (svc, project) = Create();
            project.ProjectFilePath = Path.Combine(Path.GetTempPath(), "wm_off_" + System.Guid.NewGuid().ToString("N")[..6] + ".hmiproj");
            Run(svc, false);

            var result = ProjectGenerator.Compile(project);
            // 关键：关闭后**不再要求**作业范围≥3 点（否则下面会 BUILD_FAILED）
            Assert.False(result.HasErrors, string.Join("; ", result.Errors));

            // 产物层校验：DTO 里没有 WorldMap 画面（生成器按开关过滤）
            var mi = typeof(ProjectGenerator).GetMethod("ToDto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var dto = (NavihmiProject)mi.Invoke(null, new object[] { project })!;
            Assert.DoesNotContain(dto.Screens, s => s.Type == ScreenType.WorldMap);
            Assert.Null(dto.WorldMap);
            Assert.False(dto.IncludeWorldMapOnDevice);
        }

        [Fact]
        public void 保持开启时_作业范围不足仍报编译错误()
        {
            var (svc, project) = Create();
            project.ProjectFilePath = Path.Combine(Path.GetTempPath(), "wm_on_" + System.Guid.NewGuid().ToString("N")[..6] + ".hmiproj");
            // 不关世界地图且不放范围点
            var result = ProjectGenerator.Compile(project);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Errors, e => e.Contains("作业范围"));
        }

        [Fact]
        public void 命令已注册且登记为世界地图变更域()
        {
            var (svc, _) = Create();
            Assert.Contains(svc.GetAvailableCommands(), c => c.Name == "set_world_map_visible");
            Assert.Equal(ChangeDomain.WorldMap, ProjectChangeTracker.DomainOf("set_world_map_visible"));
        }
    }
}
