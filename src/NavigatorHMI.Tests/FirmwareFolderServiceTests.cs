using System;
using System.IO;
using System.Linq;
using NavigatorHMI.Core.Services;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// O 轮批 C C-4 固件文件夹方案测试（2026-09）——FirmwareFolderService：
    /// 专门固件目录按尺寸子目录组织（firmware/&lt;尺寸&gt;/）；语义版本取最新（v1.10.0 &gt; v1.9.0 非字典序）；
    /// 目录不存在/空 → 空列表不抛。
    /// </summary>
    public class FirmwareFolderServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _prevRoot;

        public FirmwareFolderServiceTests()
        {
            _prevRoot = FirmwareFolderService.DefaultRoot;
            _dir = Path.Combine(Path.GetTempPath(), "navihmi_firmware_" + Guid.NewGuid().ToString("N")[..6]);
            FirmwareFolderService.Initialize(_dir);
        }

        public void Dispose()
        {
            FirmwareFolderService.Initialize(null);
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string MakeFw(string sizeInch, string version)
        {
            var dir = Path.Combine(_dir, sizeInch);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"NavigatorHMI_v{version}.fw");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        [Fact]
        public void 尺寸子目录_语义最新_非字典序()
        {
            MakeFw("7寸", "1.2.0");
            var newest = MakeFw("7寸", "1.10.0");
            MakeFw("7寸", "1.9.0");
            MakeFw("4寸", "2.0.0");   // 其它尺寸隔离

            var found = FirmwareFolderService.FindLatestForSize("7寸");
            Assert.NotNull(found);
            Assert.Equal(newest, found);   // 语义 v1.10.0（字典序会误选 v1.9.0）
        }

        [Fact]
        public void 目录不存在_返回空列表()
        {
            Assert.Empty(FirmwareFolderService.ListForSize("不存在尺寸"));
            Assert.Null(FirmwareFolderService.FindLatestForSize("不存在尺寸"));
        }

        [Fact]
        public void 列表_语义降序()
        {
            MakeFw("7寸", "1.0.0");
            MakeFw("7寸", "1.10.0");
            MakeFw("7寸", "1.2.0");
            var list = FirmwareFolderService.ListForSize("7寸");
            Assert.Equal(3, list.Count);
            Assert.Contains("v1.10.0", list[0]);   // 最新在前
            Assert.Contains("v1.2.0", list[1]);
            Assert.Contains("v1.0.0", list[2]);
        }

        [Fact]
        public void 空尺寸_unknown目录兜底()
        {
            Assert.Equal(Path.Combine(FirmwareFolderService.DefaultRoot, "unknown"),
                FirmwareFolderService.DirForSize(null));
        }
    }
}
