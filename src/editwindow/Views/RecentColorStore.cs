using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// 颜色选择器最近使用颜色存储（跨会话持久化，最多 20 个，存 %APPDATA%\NavigatorHMI\recent_colors.json）。
    /// 新颜色插入头部、去重、超限裁剪。
    /// </summary>
    public static class RecentColorStore
    {
        private const int MaxCount = 20;
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavigatorHMI", "recent_colors.json");
        private static List<string>? _cache;

        /// <summary>加载最近使用颜色（首个为最近使用）。</summary>
        public static IReadOnlyList<string> Load()
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(FilePath))
                    _cache = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath))
                             ?? new List<string>();
            }
            catch
            {
                _cache = new List<string>();   // 文件损坏/读取失败 → 空列表，不阻塞取色
            }
            return _cache ??= new List<string>();
        }

        /// <summary>记录一个已选颜色（头部插入 + 去重 + 裁剪 + 持久化）。</summary>
        public static void Add(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Equals("Transparent", StringComparison.OrdinalIgnoreCase))
                return;

            var list = Load().ToList();
            list.Remove(hex);
            list.Insert(0, hex);
            if (list.Count > MaxCount)
                list.RemoveRange(MaxCount, list.Count - MaxCount);
            _cache = list;

            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
            }
            catch
            {
                // 持久化失败不阻塞取色（下次启动丢失最近色，可接受）
            }
        }
    }
}
