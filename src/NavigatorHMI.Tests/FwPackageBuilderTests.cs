using System.Security.Cryptography;
using System.Text;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// D 循环批 2 D1（2026-08-30）：FwPackageBuilder .fw 打包器 round-trip 自检——
    /// magic/version/组件表/sha256 定长字段布局校验（两端互读契约：PC 打包器 ↔ Docker pack_fw.py ↔ FW otaupdater）。
    /// 注：并入「设备连接」collection（🟡3 集成测试改 FirmwareFolderService 全局根——串行防并行污染）。
    /// </summary>
    [Collection("设备连接")]
    public class FwPackageBuilderTests : IDisposable
    {
        private readonly string _dir;

        public FwPackageBuilderTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_fw_" + Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string MakeFile(string name, byte[] content)
        {
            var p = Path.Combine(_dir, name);
            File.WriteAllBytes(p, content);
            return p;
        }

        [Fact]
        public void 打包_单app组件_产物合法()
        {
            var app = MakeFile("navigatorhmi-fw", new byte[] { 1, 2, 3, 4, 5 });

            var result = FwPackageBuilder.Build("1.1.0", _dir,
                new FwPackageBuilder.Component { Name = "navigatorhmi-fw", Type = FwPackageBuilder.ComponentType.App, Target = "/usr/bin/navigatorhmi-fw", FilePath = app });

            Assert.True(File.Exists(result.Path));
            Assert.Equal("NavigatorHMI_v1.1.0.fw", Path.GetFileName(result.Path));
            Assert.Equal(1, result.ComponentCount);
            var bytes = File.ReadAllBytes(result.Path);
            Assert.Equal(FwPackageBuilder.HeaderSize + FwPackageBuilder.ComponentEntrySize + 5, bytes.Length);
            Assert.Equal("NHFW", Encoding.ASCII.GetString(bytes, 0, 4));   // magic
            Assert.Equal("1.1.0".PadRight(16), Encoding.ASCII.GetString(bytes, 4, 16));   // version 定长
            // payload sha（header: magic4+version16+timestamp8+count4=32 → sha 偏移 32）
            var sha = Encoding.ASCII.GetString(bytes, 32, 64).Trim();
            var calc = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4, 5 })).ToLowerInvariant();
            Assert.Equal(calc, sha);
        }

        [Fact]
        public void 打包_三组件_组件表与payload对齐()
        {
            var app = MakeFile("app.bin", new byte[] { 1, 2, 3 });
            var kernel = MakeFile("boot.img", new byte[64]);   // 64B 内核占位
            var rootfs = MakeFile("rootfs.img", new byte[128]);

            var result = FwPackageBuilder.Build("1.2.0", _dir,
                new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app },
                new FwPackageBuilder.Component { Name = "kernel", Type = "kernel", Target = "/dev/block/by-name/boot", FilePath = kernel },
                new FwPackageBuilder.Component { Name = "rootfs", Type = "rootfs", Target = "/dev/block/by-name/rootfs", FilePath = rootfs });

            var bytes = File.ReadAllBytes(result.Path);
            Assert.Equal(3, result.ComponentCount);
            // 组件表 3 项：name/type/target 定长校验（app 首项）
            var tableOffset = FwPackageBuilder.HeaderSize;
            Assert.Equal("app".PadRight(32), Encoding.ASCII.GetString(bytes, tableOffset, 32));
            Assert.Equal("app".PadRight(16), Encoding.ASCII.GetString(bytes, tableOffset + 32, 16));
            Assert.Equal("/usr/bin/navigatorhmi-fw".PadRight(48), Encoding.ASCII.GetString(bytes, tableOffset + 48, 48));
            // payload 顺序 = 表序（app 3B → kernel 64B → rootfs 128B）
            var payloadOffset = FwPackageBuilder.HeaderSize + 3 * FwPackageBuilder.ComponentEntrySize;
            Assert.Equal(1, bytes[payloadOffset]);                     // app 内容首字节（{1,2,3}）
            Assert.Equal(0, bytes[payloadOffset + 3]);                 // kernel 占位首字节（全 0）
            Assert.Equal(0, bytes[payloadOffset + 3 + 64]);            // rootfs 占位首字节（全 0）
            Assert.Equal(payloadOffset + 3 + 64 + 128, bytes.Length);  // 总长 = header + 表 + 三 payload
            // payload sha256 与 header 一致（header: magic4+version16+timestamp8+count4=32 → sha 偏移 32）
            var headerSha = Encoding.ASCII.GetString(bytes, 32, 64).Trim();
            var payloadBytes = bytes.Skip(payloadOffset).ToArray();
            var calcSha = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
            Assert.Equal(calcSha, headerSha);
        }

        [Fact]
        public void 打包_篡改payload_校验失败()
        {
            var app = MakeFile("app.bin", new byte[] { 1, 2, 3 });
            var result = FwPackageBuilder.Build("1.0.0", _dir,
                new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app });
            var bytes = File.ReadAllBytes(result.Path);
            // 篡改 payload 首字节
            var payloadOffset = FwPackageBuilder.HeaderSize + FwPackageBuilder.ComponentEntrySize;
            bytes[payloadOffset] ^= 0xFF;
            // header sha 偏移 32（magic4+version16+timestamp8+count4）——审查 🔴 修复：原 28 读到 count 尾部恒不匹配（测试恒真）
            var headerSha = Encoding.ASCII.GetString(bytes, 32, 64).Trim();
            var calcSha = Convert.ToHexString(SHA256.HashData(bytes.Skip(payloadOffset).ToArray())).ToLowerInvariant();
            Assert.NotEqual(calcSha, headerSha);   // 篡改后 sha256 不匹配（真实验证，非恒真）
        }

        [Fact]
        public void 打包_组件文件缺失_抛异常()
        {
            Assert.Throws<FileNotFoundException>(() =>
                FwPackageBuilder.Build("1.0.0", _dir,
                    new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = Path.Combine(_dir, "不存在.bin") }));
        }

        [Fact]
        public void 打包_无组件_抛异常()
        {
            Assert.Throws<ArgumentException>(() => FwPackageBuilder.Build("1.0.0", _dir));
        }

        [Fact]
        public void 打包_非法类型_抛异常()
        {
            var app = MakeFile("app.bin", new byte[] { 1 });
            Assert.Throws<ArgumentException>(() =>
                FwPackageBuilder.Build("1.0.0", _dir,
                    new FwPackageBuilder.Component { Name = "app", Type = "uboot", Target = "/usr/bin/x", FilePath = app }));
        }

        [Fact]
        public void 打包_非ASCII字段_抛异常()
        {
            var app = MakeFile("app.bin", new byte[] { 1 });
            // 审查 🟡 修复：非 ASCII（中文）字段静默 '?' 会破坏 pack_fw.py 互读——必须显式拒绝
            Assert.Throws<ArgumentException>(() =>
                FwPackageBuilder.Build("1.0.0", _dir,
                    new FwPackageBuilder.Component { Name = "固件", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app }));
        }

        [Fact]
        public void 打包_version含路径穿越字符_抛异常()
        {
            var app = MakeFile("app.bin", new byte[] { 1 });
            // 审查复审 🟡：version 注入 "..\x" 会拼入 fwName 穿越 outputDir——必须拒绝
            Assert.Throws<ArgumentException>(() =>
                FwPackageBuilder.Build("..\\x", _dir,
                    new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app }));
        }

        [Fact]
        public void 打包_version超长_抛异常()
        {
            var app = MakeFile("app.bin", new byte[] { 1 });
            // version 超 16B（header 定长字段）拒绝
            Assert.Throws<ArgumentException>(() =>
                FwPackageBuilder.Build("1.0.0.0.0.0.0.0.0.0", _dir,
                    new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app }));
        }

        [Fact]
        public void 带尺寸打包_产物命名7inch_可被尺寸扫描命中()
        {
            // 🟡3 集成契约：4 参 Build（sizeInch）→ NavigatorHMI_7inch_v<版本>.fw → FirmwareFolderService.ListForSize("7寸") 可扫到
            var app = MakeFile("app.bin", new byte[] { 1 });
            var result = FwPackageBuilder.Build("1.1.3", "7寸", _dir,
                new FwPackageBuilder.Component { Name = "app", Type = "app", Target = "/usr/bin/navigatorhmi-fw", FilePath = app });
            Assert.Equal("NavigatorHMI_7inch_v1.1.3.fw", Path.GetFileName(result.Path));

            // 以 _dir 为固件库根扫描（改全局根前保存/还原——并入「设备连接」collection 串行防并行污染）
            var prevRoot = NavigatorHMI.Core.Services.FirmwareFolderService.DefaultRoot;
            try
            {
                NavigatorHMI.Core.Services.FirmwareFolderService.Initialize(_dir);
                var found = NavigatorHMI.Core.Services.FirmwareFolderService.FindLatestForSize("7寸");
                Assert.NotNull(found);
                Assert.Equal(result.Path, found);
                Assert.Null(NavigatorHMI.Core.Services.FirmwareFolderService.FindLatestForSize("4寸"));   // 异尺寸不混入
            }
            finally
            {
                NavigatorHMI.Core.Services.FirmwareFolderService.Initialize(prevRoot);
            }
        }
    }
}
