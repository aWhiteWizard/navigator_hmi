using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavigatorHMI.CommandLayer.Handlers;

namespace NavigatorHMI.Core.Services
{
    /// <summary>
    /// 固件文件夹方案（O 轮批 C C-4 落点，N+29 用户定稿；2026-09 Check 标准修订）：
    /// **固件库根目录平铺存放**，文件名带尺寸标识——`NavigatorHMI_&lt;尺寸&gt;inch_v&lt;版本&gt;.fw`
    /// （7寸→7inch、4寸→4inch，ASCII 防编码问题；版本 x.y.z 纯数字语义）。无子目录。
    /// 例：NavigatorHMI_7inch_v1.1.3.fw / NavigatorHMI_4inch_v1.1.0.fw
    /// 连接设备尺寸（profile.SizeInch，如 "7寸"）→ inch 段映射 → 扫根目录该尺寸固件 → 语义版本取最新。
    /// 审查 🟡：版本解析**单一实现**——复用 <see cref="DeployFirmwareHandler.ParseFwVersion"/>。
    /// </summary>
    public static class FirmwareFolderService
    {
        /// <summary>固件库根目录（组态软件运行目录下 firmware/；可经 Initialize 覆盖——测试注入临时目录）。</summary>
        public static string DefaultRoot
        {
            get => _rootOverride ?? Path.Combine(AppContext.BaseDirectory, "firmware");
            private set => _rootOverride = value;
        }
        private static string? _rootOverride;

        /// <summary>测试/部署注入固件库根（null=还原默认运行目录 firmware/）。</summary>
        public static void Initialize(string? root = null) => _rootOverride = string.IsNullOrWhiteSpace(root) ? null : root;

        /// <summary>尺寸（profile.SizeInch，中文如 "7寸"/"4寸"）→ 文件名 inch 段（7inch/4inch）。
        /// 规范化：去全部空白、'寸'→'inch' 后缀、纯数字后补 inch（如 "7"→"7inch"）、已含 inch 归一小写。
        /// 结果白名单（🟡2 净化纪律）：仅允许 `数字+inch` 或 unknown——非法输入（含路径字符/字母尾巴）抛 ArgumentException
        /// （防尺寸段入文件名做路径穿越/脏名——打包器与扫描共用此映射）。</summary>
        public static string SizeToInchTag(string? sizeInch)
        {
            var s = (sizeInch ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return "unknown";
            s = s.Replace(" ", "").Replace("寸", "").ToLowerInvariant();   // "7 寸"→"7"、"7寸"→"7"
            if (s.EndsWith("inch", StringComparison.Ordinal))
                s = s[..^4];   // "7inch"→"7"（剥离后走数字校验）
            // 白名单：纯数字（1~3 位）+ 非空——profile 尺寸 7/4/10 等
            if (s.Length == 0 || s.Length > 3 || !s.All(char.IsAsciiDigit))
                throw new ArgumentException($"固件尺寸非法（仅允许纯数字寸如 \"7寸\"/\"7\"，实际 \"{sizeInch}\"）", nameof(sizeInch));
            return s + "inch";
        }

        /// <summary>固件库根目录下全部 .fw（文件名含 NavigatorHMI_ + 尺寸 inch 段 + _v：新标准
        /// NavigatorHMI_7inch_v1.1.3.fw；按**语义版本降序**——v1.10.0 &gt; v1.9.0；目录不存在/空 → 空列表）。</summary>
        public static List<string> ListAll(string? inchTag = null)
        {
            if (!Directory.Exists(DefaultRoot)) return new List<string>();
            var files = Directory.EnumerateFiles(DefaultRoot, "NavigatorHMI_*.fw");
            if (!string.IsNullOrEmpty(inchTag))
                files = files.Where(f => Path.GetFileName(f).Contains("_" + inchTag + "_v", StringComparison.OrdinalIgnoreCase));
            return files
                .OrderByDescending(f => DeployFirmwareHandler.ParseFwVersion(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>按尺寸取该尺寸固件列表（sizeInch 中文如 "7寸" → 匹配 7inch 段；空=全部尺寸）。</summary>
        public static List<string> ListForSize(string? sizeInch)
            => string.IsNullOrEmpty(sizeInch) ? ListAll() : ListAll(SizeToInchTag(sizeInch));

        /// <summary>按尺寸取语义最新 .fw（无 → null）。</summary>
        public static string? FindLatestForSize(string? sizeInch)
            => ListForSize(sizeInch).FirstOrDefault();

        /// <summary>日志/提示用目录显示（根目录本身——平铺无子目录）。</summary>
        public static string DirForSize(string? sizeInch) => DefaultRoot;
    }
}
