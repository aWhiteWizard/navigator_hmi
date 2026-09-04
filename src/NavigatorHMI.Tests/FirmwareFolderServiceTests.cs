using System;
using System.IO;
using System.Linq;
using NavigatorHMI.Core.Services;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// O 轮批 C C-4 固件文件夹方案测试（2026-09；Check 标准修订 2026-09）——FirmwareFolderService：
    /// **固件库根平铺** + 文件名带尺寸（NavigatorHMI_7inch_vX.Y.Z.fw）；按尺寸段过滤；语义版本取最新
    /// （v1.10.0 &gt; v1.9.0 非字典序）；旧命名 NavigatorHMI_vX.Y.Z.fw（无尺寸段）兼容；目录不存在/空 → 空列表不抛。
    /// 注：FirmwareFolderService 全局静态根（Initialize）——并入「设备连接」collection 与其它改全局根测试串行（防并行污染）。
    /// </summary>
    [Collection("设备连接")]
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

        /// <summary>平铺建固件（新标准命名：尺寸中文 → inch 段；null/空 → 旧命名无尺寸段）。</summary>
        private string MakeFw(string? sizeInch, string version)
        {
            Directory.CreateDirectory(_dir);   // 平铺无子目录——直接建根（FirmwareFolderService.ListAll 要求根存在）
            var name = string.IsNullOrEmpty(sizeInch)
                ? $"NavigatorHMI_v{version}.fw"
                : $"NavigatorHMI_{FirmwareFolderService.SizeToInchTag(sizeInch)}_v{version}.fw";
            var path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        [Fact]
        public void 按尺寸过滤_语义最新_非字典序()
        {
            MakeFw("7寸", "1.2.0");
            var newest = MakeFw("7寸", "1.10.0");
            MakeFw("7寸", "1.9.0");
            MakeFw("4寸", "2.0.0");   // 其它尺寸隔离（4inch 不混入 7inch 查询）

            var found = FirmwareFolderService.FindLatestForSize("7寸");
            Assert.NotNull(found);
            Assert.Equal(newest, found);   // 语义 v1.10.0（字典序会误选 v1.9.0）
        }

        [Fact]
        public void 该尺寸无固件_返回空列表()
        {
            MakeFw("7寸", "1.0.0");
            Assert.Empty(FirmwareFolderService.ListForSize("4寸"));   // 4inch 无 → 空
            Assert.Null(FirmwareFolderService.FindLatestForSize("4寸"));
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
        public void 尺寸映射_中文转inch()
        {
            Assert.Equal("7inch", FirmwareFolderService.SizeToInchTag("7寸"));
            Assert.Equal("4inch", FirmwareFolderService.SizeToInchTag("4寸"));
            Assert.Equal("7inch", FirmwareFolderService.SizeToInchTag("7"));      // 纯数字补 inch
            Assert.Equal("10inch", FirmwareFolderService.SizeToInchTag("10寸"));
            Assert.Equal("7inch", FirmwareFolderService.SizeToInchTag("7inch"));  // 已含 inch 幂等
            Assert.Equal("7inch", FirmwareFolderService.SizeToInchTag(" 7 寸 ")); // 去空白归一
            Assert.Equal("unknown", FirmwareFolderService.SizeToInchTag(null));   // 空兜底
            Assert.Equal("unknown", FirmwareFolderService.SizeToInchTag("  "));
        }

        [Fact]
        public void 尺寸映射_非法输入抛异常()
        {
            // 🟡2 净化：非纯数字（路径字符/字母尾巴/超长）→ 抛——防尺寸段入文件名路径穿越
            Assert.Throws<ArgumentException>(() => FirmwareFolderService.SizeToInchTag("../7"));
            Assert.Throws<ArgumentException>(() => FirmwareFolderService.SizeToInchTag("7寸/../x"));
            Assert.Throws<ArgumentException>(() => FirmwareFolderService.SizeToInchTag("abc"));
            Assert.Throws<ArgumentException>(() => FirmwareFolderService.SizeToInchTag("7.5寸"));
            Assert.Throws<ArgumentException>(() => FirmwareFolderService.SizeToInchTag("1234寸"));   // 超 3 位
        }

        [Fact]
        public void 空尺寸_列全部尺寸()
        {
            MakeFw("7寸", "1.0.0");
            MakeFw("4寸", "2.0.0");
            Assert.Equal(2, FirmwareFolderService.ListForSize(null).Count);   // null → 全部尺寸
            Assert.Equal(2, FirmwareFolderService.ListForSize("").Count);
        }

        [Fact]
        public void 旧命名无尺寸段_按尺寸查不到_全列可列出()
        {
            var legacy = MakeFw(null, "1.0.0");   // NavigatorHMI_v1.0.0.fw（旧命名兼容）
            Assert.Empty(FirmwareFolderService.ListForSize("7寸"));   // 无尺寸段 → 7inch 查不到
            Assert.Contains(legacy, FirmwareFolderService.ListAll());   // 全列可见（FindLatestFw 等全量路径可用）
        }

        [Fact]
        public void 调试命名_尺寸在版本后_归入尺寸列表()
        {
            // 2026-09-04 用户定：固件库检测 = NavigatorHMI 前缀 + 尺寸段（7inch）+ .fw 后缀，不限定尺寸在版本前。
            // 调试命名 NavigatorHMI_v1.1.0_7inch_20260904213035.fw（pack_fw --name-ts，尺寸段在版本后）应出现在 7寸 自动列表
            Directory.CreateDirectory(_dir);
            var dbg = Path.Combine(_dir, "NavigatorHMI_v1.1.0_7inch_20260904213035.fw");
            File.WriteAllBytes(dbg, new byte[] { 1, 2, 3 });
            MakeFw("4寸", "2.0.0");   // 异尺寸不混入

            var list = FirmwareFolderService.ListForSize("7寸");
            Assert.Contains(dbg, list);   // 调试命名自动显示（GUI OS Update 7寸 列表不再「无固件」）
            Assert.Single(list);          // 4inch 隔离
        }

        [Fact]
        public void 目录不存在_返回空列表()
        {
            FirmwareFolderService.Initialize(Path.Combine(Path.GetTempPath(), "navihmi_no_such_" + Guid.NewGuid().ToString("N")[..6]));
            Assert.Empty(FirmwareFolderService.ListForSize("7寸"));
            Assert.Empty(FirmwareFolderService.ListAll());
            Assert.Null(FirmwareFolderService.FindLatestForSize("7寸"));
        }
    }
}
