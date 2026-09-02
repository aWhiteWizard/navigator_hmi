using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// D-B2（2026-08-30）：create_list/update_list 图片列表项路径校验——
    /// N+20「图片列表不显示」第二根因（AI create_list 绝对路径 → 打包白名单拒绝 → 设备端无图）。
    /// 规则：目录内绝对路径自动相对化（含子目录）；目录外绝对路径拒绝（INVALID_PARAM 早失败）；
    /// 相对路径归一化保留；文本列表不校验；update 先校验后应用（两段式防半应用）。
    /// </summary>
    public class ListHandlersTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        private string NewTempDir(string suffix)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"navihmi_list_{suffix}_{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(dir);
            _tempDirs.Add(dir);
            return dir;
        }

        private HMIProject ProjectWith(string projectDir)
            => new() { Name = "列表路径测试", ProjectFilePath = Path.Combine(projectDir, "list-test.hmiproj") };

        private static CommandResult ExecCreate(HMIProject p, string type, string items)
            => new CreateListHandler().Execute(p, new Dictionary<string, object?>
            {
                ["name"] = "图片列表", ["type"] = type, ["items"] = items
            });

        public void Dispose()
        {
            foreach (var d in _tempDirs)
            {
                try { Directory.Delete(d, true); } catch { /* 清理失败不阻塞测试结论 */ }
            }
        }

        [Fact]
        public void 图片列表_工程目录内绝对路径_自动相对化()
        {
            var dir = NewTempDir("in");
            var img = Path.Combine(dir, "welcome.PNG");
            File.WriteAllBytes(img, new byte[] { 1, 2, 3 });
            var p = ProjectWith(dir);

            var result = ExecCreate(p, "Image", img);

            Assert.True(result.Success);
            var list = p.Lists.Single(l => l.Name == "图片列表");
            Assert.Equal("welcome.PNG", list.Items[0]);   // 绝对路径 → 相对化（正斜杠）
        }

        [Fact]
        public void 图片列表_工程目录内子目录绝对路径_自动相对化()
        {
            var dir = NewTempDir("sub");
            var img = Path.Combine(dir, "images", "icon.PNG");
            Directory.CreateDirectory(Path.GetDirectoryName(img)!);
            File.WriteAllBytes(img, new byte[] { 1, 2, 3 });
            var p = ProjectWith(dir);

            var result = ExecCreate(p, "Image", img);

            Assert.True(result.Success);
            Assert.Equal("images/icon.PNG", p.Lists.Single().Items[0]);   // 子目录相对化 + 正斜杠
        }

        [Fact]
        public void 图片列表_工程目录外绝对路径_保留原值_打包时收集()
        {
            // O-A1（2026-08-30 用户语义修正）：目录外绝对路径不再拒绝——路径只是"找文件"线索，
            // 打包时 DeploymentPackageBuilder 收集文件本身入包（设备不关心 PC 路径）
            var dir = NewTempDir("out");
            var outside = Path.Combine(NewTempDir("outside"), "a.png");
            File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });
            var p = ProjectWith(dir);

            var result = ExecCreate(p, "Image", outside);

            Assert.True(result.Success);
            Assert.Equal(outside, p.Lists.Single().Items[0]);   // 目录外绝对路径保留原值入库
        }

        [Fact]
        public void 图片列表_相对路径_原样保留()
        {
            var dir = NewTempDir("rel");
            var p = ProjectWith(dir);

            var result = ExecCreate(p, "Image", "images/foo.png");

            Assert.True(result.Success);
            Assert.Equal("images/foo.png", p.Lists.Single().Items[0]);
        }

        [Fact]
        public void 图片列表_相对路径含上级段_归一化()
        {
            var dir = NewTempDir("dotdot");
            var p = ProjectWith(dir);

            // "../outside.png" 从工程目录解析 → 工程目录的上级 → 不在工程目录内 → 拒绝（防绕过白名单）
            var result = ExecCreate(p, "Image", "../outside.png");

            Assert.False(result.Success);
            Assert.Equal("INVALID_PARAM", result.ErrorCode);
        }

        [Fact]
        public void 文本列表_不校验路径()
        {
            var dir = NewTempDir("txt");
            var p = ProjectWith(dir);

            var result = ExecCreate(p, "Text", "D:\\随便\\绝对\\文本项");

            Assert.True(result.Success);
            Assert.Equal("D:\\随便\\绝对\\文本项", p.Lists.Single().Items[0]);   // 文本项原样
        }

        [Fact]
        public void 图片列表_update_目录外绝对路径_保留替换()
        {
            // O-A1：update_list 目录外绝对路径同样保留（打包时收集）
            var dir = NewTempDir("upd");
            var p = ProjectWith(dir);
            Assert.True(ExecCreate(p, "Image", "images/ok.png").Success);
            var outside = Path.Combine(NewTempDir("outside_upd"), "b.png");
            File.WriteAllBytes(outside, new byte[] { 1, 2, 3 });

            var result = new UpdateListHandler().Execute(p, new Dictionary<string, object?>
            {
                ["name"] = "图片列表", ["items"] = outside
            });

            Assert.True(result.Success);
            Assert.Equal(outside, p.Lists.Single().Items[0]);   // 目录外绝对路径替换成功
        }

        [Fact]
        public void 图片列表_update_rename加非法items_两段式不半应用()
        {
            var dir = NewTempDir("atomic");
            var p = ProjectWith(dir);
            Assert.True(ExecCreate(p, "Image", "images/ok.png").Success);

            // rename + 非法 items（../ 目录逃逸，O-A1 仍拒绝）同传：两段式应先校验 items → 失败 → rename 不落库（防半应用）
            var result = new UpdateListHandler().Execute(p, new Dictionary<string, object?>
            {
                ["name"] = "图片列表", ["new_name"] = "改名列表", ["items"] = "../outside.png"
            });

            Assert.False(result.Success);
            Assert.Equal("INVALID_PARAM", result.ErrorCode);
            var list = p.Lists.Single();
            Assert.Equal("图片列表", list.Name);                    // rename 未应用
            Assert.Equal("images/ok.png", list.Items[0]);           // items 未替换
        }
    }
}
