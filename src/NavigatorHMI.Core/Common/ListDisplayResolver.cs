using System.Globalization;
using System.IO;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 列表显示解析（设计态预览 + 渲染共用单一来源）。
    /// 控件（Image/Frame/TextList）绑定列表（ListRef）后，按「绑定变量数值 = 索引」显示列表项：
    /// 索引 = 绑定变量基准值（数值解析）→ 否则缺省值 DefaultIndex；越界/无效 → 不显示。
    /// </summary>
    public static class ListDisplayResolver
    {
        /// <summary>去除首尾空格及成对的双/单引号（支持 "path" / 'path'）。</summary>
        public static string StripQuotes(string s)
        {
            var t = s.Trim();
            if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
                return t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        /// <summary>相对路径按工程目录解析为完整路径（去引号；空/未保存返回 null）。</summary>
        public static string? ResolveFullPath(string raw, string projectDir)
        {
            var p = StripQuotes(raw);
            if (p.Length == 0) return null;
            if (string.IsNullOrEmpty(projectDir)) return null;
            return Path.IsPathRooted(p) ? p : Path.Combine(projectDir, p);
        }

        /// <summary>工程目录（ProjectFilePath 所在目录，空 = 未保存）。</summary>
        public static string ProjectDir => TagResolver.CurrentProject != null
            ? Path.GetDirectoryName(TagResolver.CurrentProject.ProjectFilePath) ?? ""
            : "";

        /// <summary>
        /// 解析显示索引：绑定变量基准值（数值）优先，否则缺省值；钳制 [0, count-1]，无效返回 -1。
        /// 整数解析失败时尝试 FLOAT 截断取整（变量类型允许 FLOAT 绑索引，基准值 "3.5" 设计态与运行时一致）。
        /// </summary>
        public static int ResolveIndex(string? boundTag, int defaultIndex, int count)
        {
            if (count <= 0) return -1;
            int idx = defaultIndex;
            if (!string.IsNullOrEmpty(boundTag))
            {
                var bv = TagResolver.ResolveBaseValue(boundTag);
                if (int.TryParse(bv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    idx = v;
                else if (double.TryParse(bv, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
                         && d >= int.MinValue && d <= int.MaxValue)
                    idx = (int)d;   // FLOAT 截断取整（显式范围判断，防超 int 范围饱和转换语义隐晦）
            }
            return (idx >= 0 && idx < count) ? idx : -1;
        }

        /// <summary>解析列表项文本（列表不存在/未绑列表/索引无效 → null）。</summary>
        public static string? ResolveListText(string listRef, string? boundTag, int defaultIndex)
        {
            var list = TagResolver.CurrentProject?.Lists.FirstOrDefault(l => l.Name == listRef);
            if (list == null) return null;
            var idx = ResolveIndex(boundTag, defaultIndex, list.Items.Count);
            return idx >= 0 ? list.Items[idx] : null;
        }

        /// <summary>解析列表项图片完整路径（列表不存在/未绑列表/索引无效 → null；文件不存在仍返回解析路径由渲染侧兜底）。</summary>
        public static string? ResolveListImagePath(string listRef, string? boundTag, int defaultIndex)
        {
            var text = ResolveListText(listRef, boundTag, defaultIndex);
            if (text == null) return null;
            return ResolveFullPath(text, ProjectDir) ?? text;
        }
    }
}
