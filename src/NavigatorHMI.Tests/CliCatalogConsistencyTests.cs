using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// V-5：CLI 声明式路由表（CliCatalog）与命令层 CommandDefinition 交叉一致性测试。
    /// 防漂移锁（负样本 gui-cli-param-map-missing 直接对治）：命令层改名/删参未同步 CLI → 此处红。
    /// </summary>
    public class CliCatalogConsistencyTests
    {
        /// <summary>构造真实 CommandService（注册全部 handler）→ 元数据全量。</summary>
        private static List<CommandDefinition> AllDefinitions()
        {
            var svc = new CommandService(new HMIProject());
            return svc.GetAvailableCommands();
        }

        [Fact]
        public void ValidateDefinitions_无错误()
        {
            var errors = CliCatalog.ValidateDefinitions(AllDefinitions());
            Assert.True(errors.Count == 0, "CLI 路由与命令层元数据漂移:\n" + string.Join("\n", errors));
        }

        /// <summary>每个非 custom 命令的 CommandName 都能在命令层注册表命中（ValidateDefinitions 已含，此处独立断言防测试退化）。</summary>
        [Fact]
        public void 元数据命令_命令层全部命中()
        {
            var names = AllDefinitions().Select(d => d.Name).ToHashSet();
            var missing = CliCatalog.All
                .Where(s => !s.IsCustom && !string.IsNullOrEmpty(s.CommandName))
                .Select(s => s.CommandName!)
                .Where(n => !names.Contains(n))
                .ToList();
            Assert.Empty(missing);
        }

        /// <summary>命令全集快照：防止重构误删命令（历史 RouteCommand switch 覆盖的命令都应保留在目录中）。</summary>
        [Fact]
        public void 命令全集_覆盖历史全命令()
        {
            var expected = new[]
            {
                // 工程
                "create-project", "open-project", "save-project", "compile",
                // 画面
                "create-screen", "delete-screen", "rename-screen", "current-screen", "copy-screen", "paste-screen",
                // 控件
                "add-widget", "move-widget", "resize-widget", "delete-widget", "set-property", "bind-robot-slot",
                // 层级
                "bring-to-front", "bring-forward", "send-backward", "send-to-back",
                // 事件
                "bind-event", "add-event", "remove-event", "update-event",
                // 世界地图
                "add-work-point", "add-work-range-point", "clear-work-range", "delete-work-point", "update-world-map",
                // 布局
                "align", "array",
                // 变量/用户
                "create-tag", "update-tag", "delete-tag", "bind-tag",
                "create-user", "update-user", "delete-user", "list-users",
                "create-group", "update-group", "delete-group",
                // 列表
                "create-list", "update-list", "delete-list",
                // 剪贴板/字体
                "copy-widget", "paste-widget", "set-default-font",
                // 报警
                "create-alarm", "update-alarm", "delete-alarm",
                // 设备
                "configure-device", "update-device", "delete-device",
                "connect", "disconnect", "scan", "deploy-project", "deploy-firmware", "blink-device", "vnc",
                // AI
                "ai",
            };
            var actual = CliCatalog.All.Select(s => s.CliName).ToHashSet();
            var missing = expected.Where(e => !actual.Contains(e)).ToList();
            Assert.True(missing.Count == 0, "目录缺失命令: " + string.Join(", ", missing));
        }

        /// <summary>custom 命令（有 CLI 专用执行器）在目录中被标记；元数据命令全部有 CommandName。</summary>
        [Fact]
        public void 命令角色标记_正确()
        {
            var custom = new[] { "create-project", "open-project", "save-project", "compile",
                "bind-event", "add-event", "remove-event", "update-event", "ai" };
            foreach (var c in CliCatalog.All)
            {
                if (custom.Contains(c.CliName))
                    Assert.True(c.IsCustom, $"{c.CliName} 应为 custom 命令");
                else
                {
                    Assert.False(c.IsCustom, $"{c.CliName} 不应为 custom");
                    Assert.False(string.IsNullOrEmpty(c.CommandName), $"{c.CliName} 缺 CommandName");
                }
            }
        }

        /// <summary>custom 命令的专用执行器（Handler）必须已声明且互不重复——防新增 custom 漏接执行器导致静默 UnknownCommand。</summary>
        [Fact]
        public void custom命令_执行器声明完备唯一()
        {
            var customs = CliCatalog.All.Where(c => c.IsCustom).ToList();
            Assert.NotEmpty(customs);
            var seen = new HashSet<CliCustomHandler>();
            foreach (var c in customs)
            {
                Assert.NotEqual(CliCustomHandler.None, c.Handler);
                Assert.True(seen.Add(c.Handler), $"Handler 重复: {c.Handler}");
            }
            // 枚举已定义的执行器应全部有对应命令（防枚举/目录双向漂移）
            var used = customs.Select(c => c.Handler).ToHashSet();
            foreach (var h in Enum.GetValues<CliCustomHandler>())
                if (h != CliCustomHandler.None)
                    Assert.True(used.Contains(h), $"枚举 {h} 无对应 custom 命令");
        }

        /// <summary>元数据命令帮助签名（Summary）中出现的 --键 必须是目录视图已声明的 CLI 键（防帮助承诺不存在的参数）。</summary>
        [Fact]
        public void 帮助签名键_全部在参数视图内()
        {
            foreach (var spec in CliCatalog.All.Where(s => !s.IsCustom))
            {
                if (spec.Views.Count == 0) continue;   // 无参命令（current-screen 等）
                var cliKeys = spec.Views.Select(v => v.CliKey).ToHashSet(StringComparer.Ordinal);
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(spec.Summary, @"--[a-z0-9-]+"))
                {
                    var key = m.Value[2..];
                    Assert.True(cliKeys.Contains(key),
                        $"[{spec.CliName}] 帮助签名含未声明参数 --{key}（目录 Views: {string.Join(", ", cliKeys.Select(k => "--" + k))}）");
                }
            }
        }

        /// <summary>帮助文本单源：--help 输出包含全部命令名与 set-property 键说明（无遗漏）。</summary>
        [Fact]
        public void 帮助文本_覆盖全部命令与属性键()
        {
            var help = CliCatalog.BuildHelpText();
            foreach (var c in CliCatalog.All)
                Assert.Contains(c.CliName, help);
            Assert.Contains("set-property 属性键", help);
            Assert.Contains("textColor", help);
        }

        /// <summary>视图语义约束：Required 参数不得带 Default（冲突）；CLI 键/CmdKey 非空；同命令 CLI 键唯一。</summary>
        [Fact]
        public void 参数视图_语义合法()
        {
            foreach (var spec in CliCatalog.All)
            {
                var cliKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in spec.Views)
                {
                    Assert.False(string.IsNullOrEmpty(v.CliKey), $"{spec.CliName} 参数缺 CliKey");
                    Assert.False(string.IsNullOrEmpty(v.CmdKey), $"{spec.CliName} --{v.CliKey} 缺 CmdKey");
                    Assert.True(cliKeys.Add(v.CliKey), $"{spec.CliName} CLI 键重复: --{v.CliKey}");
                    Assert.False(v.Required && v.Default != null, $"{spec.CliName} --{v.CliKey}: Required 与 Default 互斥");
                }
            }
        }
    }
}
