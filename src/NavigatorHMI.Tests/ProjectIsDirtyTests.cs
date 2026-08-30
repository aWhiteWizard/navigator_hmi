using System.Collections.ObjectModel;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// K 循环：工程脏标记 IsDirty 模型级单点化 + 原子保存测试（2026-08-30）。
    /// 覆盖：标量/集合置脏、运行时字段不置脏、Save/Load 清脏、原子保存失败保护。
    /// N 循环 N-3（2026-08-30）：设备操作命令不标脏——谓词断言 + disconnect 零网络成功路径。
    /// 注：disconnect 测试触碰 DeviceConnectionService（static 单例）——与 DeviceConnectionTests 同 collection 串行。
    /// </summary>
    [Collection("设备连接")]
    public class ProjectIsDirtyTests
    {
        private static HMIProject NewProject()
            => new() { Name = "测试工程" };

        private static string TempFilePath(string name)
        {
            var dir = Path.Combine(Path.GetTempPath(), "navihmi_isdirty_test");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        [Fact]
        public void 新建空工程默认不脏()
        {
            var p = new HMIProject();
            Assert.False(p.IsDirty);
        }

        [Fact]
        public void 新建工程初始化属性即脏_未保存语义()
        {
            // 创建工程时初始化 Name/CreateTime 等 = 一次修改；未保存关闭应提示保存（脏标记合理）
            var p = NewProject();
            Assert.True(p.IsDirty);
        }

        [Fact]
        public void 标量属性修改置脏()
        {
            var p = NewProject();
            p.ClearDirty();
            p.Name = "改名";
            Assert.True(p.IsDirty);
        }

        [Fact]
        public void 设备尺寸修改置脏()
        {
            var p = NewProject();
            p.ClearDirty();
            p.DeviceWidth = 1024;
            Assert.True(p.IsDirty);
        }

        [Fact]
        public void 画面集合增删置脏()
        {
            var p = NewProject();
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            Assert.True(p.IsDirty);

            p.ClearDirty();
            p.Screens.RemoveAt(0);
            Assert.True(p.IsDirty);
        }

        [Fact]
        public void 变量集合增删置脏()
        {
            var p = NewProject();
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            Assert.True(p.IsDirty);

            p.ClearDirty();
            p.Tags.Clear();
            Assert.True(p.IsDirty);
        }

        [Fact]
        public void 运行时画面名修改不置脏()
        {
            var p = NewProject();
            p.ClearDirty();   // 清除初始化置脏，单独验证 CurrentScreenName 不置脏
            p.CurrentScreenName = "画面B";
            Assert.False(p.IsDirty);
        }

        [Fact]
        public void 保存成功后清脏_再修改又置脏()
        {
            var p = NewProject();
            var path = TempFilePath("clean.hmiproj");
            ProjectFileService.Save(p, path);
            Assert.False(p.IsDirty);
            Assert.False(File.Exists(path + ".tmp"), "原子保存不应残留 .tmp 临时文件");

            p.Name = "再改";
            Assert.True(p.IsDirty);
        }

        [Fact]
        public void 加载后清脏_含集合元素()
        {
            var p = NewProject();
            var path = TempFilePath("load2.hmiproj");
            p.Name = "保存内容";
            p.Screens.Add(new Screen { Name = "画面A", Type = ScreenType.Custom });
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            ProjectFileService.Save(p, path);

            var loaded = ProjectFileService.Load(path);
            Assert.False(loaded.IsDirty, "加载完成后不应处于脏状态（反序列化 setter/集合事件会置脏，须清）");
            Assert.Equal(1, loaded.Screens.Count);
            Assert.Equal(1, loaded.Tags.Count);
        }

        [Fact]
        public void 同路径二次保存走替换分支_内容更新无临时残留()
        {
            var p = NewProject();
            var path = TempFilePath("replace.hmiproj");
            ProjectFileService.Save(p, path);

            p.Name = "第二次保存";
            p.Tags.Add(new Tag { Name = "新变量", DataType = TagDataType.INT16 });
            ProjectFileService.Save(p, path);   // 目标已存在 → File.Replace 分支

            Assert.False(p.IsDirty);
            Assert.False(File.Exists(path + ".tmp"), "替换保存不应残留 .tmp");
            var loaded = ProjectFileService.Load(path);
            Assert.Equal("第二次保存", loaded.Name);
            Assert.Single(loaded.Tags);
        }

        [Fact]
        public void 原子保存失败_原文件完好_无临时残留()
        {
            var p = NewProject();
            var path = TempFilePath("atomic.hmiproj");
            // 先保存一份有效工程作为「原文件」
            ProjectFileService.Save(p, path);
            var originalBytes = File.ReadAllBytes(path);

            // 目标路径是已存在目录 → File.Create/File.Move 抛 IOException（模拟写入失败）
            var dirAsTarget = Path.Combine(Path.GetTempPath(), "navihmi_isdirty_fail_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dirAsTarget);
            try
            {
                var ex = Record.Exception(() => ProjectFileService.Save(p, dirAsTarget));
                Assert.IsType<IOException>(ex);
                Assert.False(File.Exists(dirAsTarget + ".tmp"), "保存失败应清理临时文件");
            }
            finally
            {
                Directory.Delete(dirAsTarget, true);
            }

            // 原文件完好（未被半截写入）
            var afterBytes = File.ReadAllBytes(path);
            Assert.Equal(originalBytes, afterBytes);
        }

        [Fact]
        public void 集合替换后置脏钩子自愈_增删仍置脏()
        {
            // 设计约束：Screens/Tags 整集合赋值时 setter 重挂 CollectionChanged（自愈，防丢置脏钩子）
            var p = NewProject();
            p.ClearDirty();
            p.Screens = new ObservableCollection<Screen>();
            p.Screens.Add(new Screen { Name = "画面X", Type = ScreenType.Custom });
            Assert.True(p.IsDirty, "整集合替换后新增元素应仍触发置脏（setter 重挂钩子）");

            p.ClearDirty();
            p.Tags = new ObservableCollection<Tag>();
            p.Tags.Add(new Tag { Name = "温度", DataType = TagDataType.FLOAT });
            Assert.True(p.IsDirty);
        }

        // ── N 循环 N-3（2026-08-30）：设备操作命令不标脏 ──

        [Fact]
        public void 设备操作命令清单_谓词断言_7运行时命令排除_3配置命令保留()
        {
            // 守卫 UI 排除逻辑（OnCommandExecuted 开头 IsDeviceRuntimeCommand 短路）：黑名单漂移即红
            string[] runtimeCommands = { "connect", "disconnect", "scan_devices", "deploy_project", "blink_device", "vnc", "deploy_firmware" };
            foreach (var cmd in runtimeCommands)
                Assert.True(EditWindowViewModel.IsDeviceRuntimeCommand(cmd), $"{cmd} 应为设备运行时命令（不标脏）");

            // 设备通信配置 = 工程数据，仍标脏（不在排除清单）
            string[] configCommands = { "configure_device", "update_device", "delete_device" };
            foreach (var cmd in configCommands)
                Assert.False(EditWindowViewModel.IsDeviceRuntimeCommand(cmd), $"{cmd} 为设备通信配置，应标脏（不排除）");
        }

        [Fact]
        public void disconnect成功路径_触发命令事件且工程不标脏()
        {
            // disconnect 零网络成功路径（RequiresConnection=false，幂等）：
            // 成功 → CommandExecuted 触发（真实链路中 OnCommandExecuted 会命中谓词排除短路）→ 工程 IsDirty 不受影响
            var p = NewProject();
            p.ClearDirty();
            var svc = new CommandService(p);

            string? fired = null;
            svc.CommandExecuted += (name, _, _) => fired = name;

            var result = svc.Execute("disconnect", new Dictionary<string, object?>());
            Assert.True(result.Success, $"{result.ErrorCode} {result.ErrorMessage}");
            Assert.Equal("disconnect", fired);        // 成功路径确实触发了统一标脏入口事件
            Assert.False(p.IsDirty, "disconnect 仅断开运行时会话，不应置脏工程");
        }
    }
}
