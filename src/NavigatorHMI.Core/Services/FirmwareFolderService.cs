using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavigatorHMI.CommandLayer.Handlers;

namespace NavigatorHMI.Core.Services
{
    /// <summary>
    /// 固件文件夹方案（O 轮批 C C-4 落点，N+29 用户定稿）：组态软件**专门固件文件夹**按尺寸组织——
    /// 根目录 <see cref="DefaultRoot"/>（默认 AppContext.BaseDirectory/firmware）下按尺寸子目录存放 .fw
    /// （firmware/7寸/、firmware/4寸/…）；升级时按连接设备尺寸（profile.SizeInch）定位子目录 → 语义版本取最新，
    /// 用户无需输入任何路径（--file 手动指定保留，命令层语义不变）。
    /// 审查 🟡（O 轮批 C）：版本解析**单一实现**——复用 <see cref="DeployFirmwareHandler.ParseFwVersion"/>（同程序集
    /// internal），不另写一套（防双实现静默漂移，B-4 v 前缀修复教训）。
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

        /// <summary>尺寸 → 子目录（firmware/&lt;sizeInch&gt;/；未知尺寸仍按原文拼——目录不存在返回空列表即可）。</summary>
        public static string DirForSize(string? sizeInch)
            => Path.Combine(DefaultRoot, string.IsNullOrWhiteSpace(sizeInch) ? "unknown" : sizeInch.Trim());

        /// <summary>该尺寸子目录全部 .fw（NavigatorHMI_v*.fw，按**语义版本降序**——v1.10.0 &gt; v1.9.0；目录不存在/空 → 空列表）。</summary>
        public static List<string> ListForSize(string? sizeInch)
        {
            var dir = DirForSize(sizeInch);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.EnumerateFiles(dir, "NavigatorHMI_v*.fw")
                .OrderByDescending(f => DeployFirmwareHandler.ParseFwVersion(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>该尺寸子目录语义最新 .fw（无 → null）。</summary>
        public static string? FindLatestForSize(string? sizeInch)
            => ListForSize(sizeInch).FirstOrDefault();
    }
}
