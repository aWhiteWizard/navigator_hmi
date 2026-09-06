using System.IO;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>
    /// 图片列表项路径规范化（D-B2，2026-08-30 + O-A1 修正）——AI/CLI 传绝对路径入库是 N+20「图片列表不显示」第二根因：
    /// DeploymentPackageBuilder 白名单曾拒绝工程目录外路径 → 打包不入包 → 设备端无图。
    /// 规则（O-A1 用户语义修正 2026-08-30：图片路径只是"找文件"线索，打包时收集文件本身入包，设备不关心 PC 路径）：
    ///   绝对路径在工程目录内 → 自动相对化（Path.GetRelativePath，统一正斜杠）；
    ///   绝对路径在工程目录外 → **保留原值入库**（不再拒绝——打包 DeploymentPackageBuilder O-A1 收集文件本身入包 res/）；
    ///   相对路径逃逸（../ 归一化后越界）→ 仍拒绝（INVALID_PARAM，防目录穿越）；
    ///   相对路径工程内 → 归一化保留；工程未保存（无目录）→ 原样保留（GUI 同语义）。
    /// 注：GUI 浏览选择（EditWindow.xaml.cs L1299-1316）目录外保留绝对路径（预览用）——现与命令层语义一致（O-A1）。
    /// </summary>
    internal static class ImageListItemSanitizer
    {
        public static string? Sanitize(string raw, string projectDir, out string? error)
        {
            error = null;
            var p = ListDisplayResolver.StripQuotes(raw);
            if (p.Length == 0) return raw;
            if (string.IsNullOrEmpty(projectDir)) return raw;   // 工程未保存：无从校验/相对化
            string abs;
            try
            {
                // 统一解析为完整路径再判归属：相对路径也过 GetFullPath（去 .. 段归一化，防 ../ 绕过白名单）
                abs = Path.GetFullPath(Path.Combine(projectDir, p));
            }
            catch (Exception ex)   // 非法路径（含无效字符等）
            {
                error = $"图片路径 \"{raw}\" 无效: {ex.Message}";
                return null;
            }
            try
            {
                var rel = Path.GetRelativePath(projectDir, abs);
                if (rel.StartsWith("..") || Path.IsPathRooted(rel))
                {
                    // O-A1（2026-08-30 用户语义）：目录外不再拒绝——绝对路径是用户正常引用（文件对话框），
                    // 打包时收集文件本身入包（DeploymentPackageBuilder O-A1）；仅相对路径逃逸（../）仍拒绝（安全）
                    if (Path.IsPathRooted(p))
                        return p;   // 绝对路径目录外：保留原值入库（打包收集，设备不关心 PC 路径）
                    error = $"图片路径 \"{raw}\" 含目录逃逸（../），禁止引用工程外相对路径";
                    return null;
                }
                return rel.Replace('\\', '/');   // 统一正斜杠（与打包/设备端加载一致）
            }
            catch (Exception ex)   // 盘符差异/路径非法（GetRelativePath 可抛）
            {
                error = $"图片路径 \"{raw}\" 无效: {ex.Message}";
                return null;
            }
        }
    }

    /// <summary>解析 items 参数：支持 List&lt;string&gt;（GUI 传对象数组）或 "|" 分隔字符串（CLI），统一为 List&lt;string&gt;。</summary>
    internal static class ListItemsParser
    {
        public static List<string>? Parse(object? items)
        {
            // 🔴 必须返回拷贝：调用方（UpdateListHandler）会 Clear()+AddRange()，
            // 若直接返回原集合引用 → 清空自身后再加空集 → 列表项全部丢失（自引用 aliasing）
            if (items is List<string> list) return new List<string>(list);
            if (items is string s)
            {
                // 兼容多种分隔符（AI 模型可能用 | / ; / 逗号 / 顿号 / 换行 / 全角分号；Windows 路径不含这些字符，拆分安全）
                var parts = s.Split(new[] { '|', ';', ',', '，', '、', '\n', '\r', '；' }, StringSplitOptions.TrimEntries);
                return parts.Where(p => p.Length > 0).ToList();
            }
            if (items is System.Collections.IEnumerable en && items is not string)
            {
                var l = new List<string>();
                foreach (var o in en) if (o != null) l.Add(o.ToString()!);
                return l;
            }
            return null;
        }
    }

    /// <summary>U-1（2026-09-06 三类型命名空间独立）：控件列表引用按**消费类型**匹配/改写——
    /// Text 型 ← TextListWidget.ListRef；Image 型 ← ImageWidget/FrameWidget（ListRef 图模式）；Video 型 ← FrameWidget.VideoListRef。
    /// 跨类型同名列表各自只被对应消费方引用——rename/delete 级联保护不得全名匹配（防误级联/互拦）。</summary>
    internal static class ListTypeRefs
    {
        public static bool WidgetRefs(Widget w, string name, ListType type) => type switch
        {
            ListType.Text => w is TextListWidget tl && tl.ListRef == name,
            ListType.Image => w is ImageWidget im ? im.ListRef == name : w is FrameWidget fr && fr.ListRef == name,
            ListType.Video => w is FrameWidget fv && fv.VideoListRef == name,
            _ => false,
        };

        /// <summary>改写控件引用名（与 WidgetRefs 同匹配规则；不匹配不改）。</summary>
        public static void RenameRef(Widget w, string oldName, string newName, ListType type)
        {
            switch (type)
            {
                case ListType.Text when w is TextListWidget tl && tl.ListRef == oldName: tl.ListRef = newName; break;
                case ListType.Image when w is ImageWidget im && im.ListRef == oldName: im.ListRef = newName; break;
                case ListType.Image when w is FrameWidget fr && fr.ListRef == oldName: fr.ListRef = newName; break;
                case ListType.Video when w is FrameWidget fv && fv.VideoListRef == oldName: fv.VideoListRef = newName; break;
            }
        }
    }

    /// <summary>创建列表（文本/图片/视频源）。列表项为有序预设值，控件绑定数值变量按索引显示。</summary>
    public class CreateListHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "create_list", Description = "创建列表（文本/图片/视频源预设值，控件绑定数值变量按索引显示）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "列表名（工程内唯一）" },
                ["type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "Text", "Image", "Video" }, Description = "列表类型（Video=视频源列表，S-4：项=视频源地址——本地路径/RTSP URL）" },
                // Required：AI compact schema 才保留 items（建列表必须指定项；图片列表为 | 分隔的图片路径，视频列表为 | 分隔的源地址）
                ["items"] = new() { Type = "string", Required = true, Description = "列表项，用 | 分隔（图片=图片路径；视频=源地址本地路径/RTSP URL）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            if (!p.ContainsKey("type") || string.IsNullOrWhiteSpace(p["type"]?.ToString())) return ValidationResult.Fail("缺少必填参数: type");
            // 刻意不强制 items（与 Definition Required 不一致是有意的）：GUI 先建空列表后加项的工作流依赖；
            // Required 仅作用于 AI compact schema（模型必须传 items），Validate 兼容 GUI 空列表
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!.Trim();
            if (!Enum.TryParse<ListType>(p["type"]!.ToString(), ignoreCase: true, out var type))
                return CommandResult.Fail("INVALID_PARAM", $"未知列表类型: {p["type"]}（Text/Image/Video）");
            // U-1（2026-09-06 三类型命名空间独立）：查重限**同类型内**——文本/图片/视频列表可跨类型同名
            if (project.Lists.Any(l => l.Type == type && l.Name == name))
                return CommandResult.Fail("DUPLICATE", $"同类型列表 \"{name}\" 已存在（{type} 型；跨类型同名允许）");
            var items = ListItemsParser.Parse(p.GetValueOrDefault("items")) ?? new List<string>();

            // D-B2：图片列表项路径校验（绝对路径在工程目录内 → 相对化；目录外 → 拒绝早失败）
            if (type == ListType.Image)
            {
                var projectDir = Path.GetDirectoryName(project.ProjectFilePath) ?? "";
                var normalized = new List<string>(items.Count);
                foreach (var it in items)
                {
                    var ok = ImageListItemSanitizer.Sanitize(it, projectDir, out var err);
                    if (ok == null)
                        return CommandResult.Fail("INVALID_PARAM", err ?? "图片路径无效");
                    normalized.Add(ok);
                }
                items = normalized;
            }
            // S-4：视频源列表项清洗（去引号/trim——本地路径相对工程目录打包时解析；RTSP/网络 URL 原样；不做图片式存在校验——打包编译护栏统一）
            // 注：create 去空（CLI/AI 建表 items 为 | 串，Parser 已去空段，此处兜底）；update **保留空行**（GUI「＋添加项」占位/单元格清空——丢空则添加无效，见 update 分支注释）
            else if (type == ListType.Video)
            {
                items = items.Select(i => i.Trim().Trim('"', '\'')).Where(i => i.Length > 0).ToList();
            }

            project.Lists.Add(new ListDef { Name = name, Type = type, Items = items });
            return CommandResult.Ok(new { list_name = name, type = type.ToString(), item_count = items.Count });
        }
    }

    /// <summary>更新列表：重命名（级联同步控件 ListRef）或整体替换预设值 items。</summary>
    public class UpdateListHandler : ICommandHandler
    {
        /// <summary>控件列表引用属性名（ImageWidget/FrameWidget/TextListWidget 均有；反射统一处理防字符串漂移）。</summary>
        private const string ListRefPropertyName = "ListRef";

        public CommandDefinition Definition => new()
        {
            Name = "update_list", Description = "更新列表（重命名级联同步控件 ListRef）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "原列表名" },
                ["type"] = new() { Type = "enum", EnumValues = new[] { "Text", "Image", "Video" }, Description = "列表类型（U-1 跨类型同名时必传定位；缺省按唯一名匹配，同名多时要求指定）", KeepInCompact = true },
                ["new_name"] = new() { Type = "string", Description = "新列表名（重命名）", KeepInCompact = true },
                ["items"] = new() { Type = "string", Description = "预设值列表，用 | 分隔（提供则整体替换）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!.Trim();   // 与 create_list 对齐：查找前 Trim，防 CLI --name "l1 " NOT_FOUND
            // U-1：type 可选——提供则同类型定位（未命中=该类型列表不存在 → NOT_FOUND）；缺省按名唯一匹配（同名多报 AMBIGUOUS）
            ListType? typeFilter = null;
            if (p.TryGetValue("type", out var tp) && tp != null && tp.ToString()!.Length > 0)
            {
                if (!Enum.TryParse<ListType>(tp.ToString(), ignoreCase: true, out var t2))
                    return CommandResult.Fail("INVALID_PARAM", $"未知列表类型: {tp}（Text/Image/Video）");
                typeFilter = t2;
            }
            ListDef? list;
            if (typeFilter is { } tf)
            {
                list = project.Lists.FirstOrDefault(l => l.Type == tf && l.Name == name);
                if (list == null) return CommandResult.Fail("NOT_FOUND", $"列表 \"{name}\"（{tf} 型）不存在");
            }
            else
            {
                var matches = project.Lists.Where(l => l.Name == name).ToList();
                if (matches.Count == 0) return CommandResult.Fail("NOT_FOUND", $"列表 \"{name}\" 不存在");
                if (matches.Count > 1)
                    return CommandResult.Fail("AMBIGUOUS", $"列表 \"{name}\" 跨类型同名（Text/Image/Video），请用 type 参数指定");
                list = matches[0];
            }

            // 两段式（D-B2 审查修复）：先全量解析/校验 items（含图片路径规范化），全过再应用 rename+替换——
            // 否则 rename 先落库、items 校验失败 → 半应用（改名成功但内容未改，UI 显示不一致）
            List<string>? normalizedItems = null;
            if (p.TryGetValue("items", out var items) && items != null)
            {
                var parsed = ListItemsParser.Parse(items);
                if (parsed != null)
                {
                    normalizedItems = parsed;
                    if (list.Type == ListType.Image)
                    {
                        var projectDir = Path.GetDirectoryName(project.ProjectFilePath) ?? "";
                        var normalized = new List<string>(parsed.Count);
                        foreach (var it in parsed)
                        {
                            var ok = ImageListItemSanitizer.Sanitize(it, projectDir, out var err);
                            if (ok == null)
                                return CommandResult.Fail("INVALID_PARAM", err ?? "图片路径无效");
                            normalized.Add(ok);
                        }
                        normalizedItems = normalized;
                    }
                    else if (list.Type == ListType.Video)   // S-4：视频源项去引号（同 create_list）——**保留空占位行**
                    {
                        // 审查 🔴（2026-09-05）：不得丢空行——GUI「＋添加项」先插 "" 占位再提交，丢空则添加无效
                        // + 单元格清空提交致静默删行网格错位；对齐 Image 分支保留 "" 语义
                        normalizedItems = parsed.Select(i => i.Trim().Trim('"', '\'')).ToList();
                    }
                }
            }

            // 重命名：同类型唯一性校验 + 级联同步控件引用（ListTypeRefs 按消费类型匹配——U-1 同名跨类型不误级联）
            // ⚠️ 不能提前 return：items 替换分支须独立可达（cli-param-sanitize §12 显式清空语义）
            // new_name 无清空语义：仅判非 null，由内层 Trim + 长度守卫统一净化空白串
            if (p.TryGetValue("new_name", out var nn) && nn != null)
            {
                var newName = nn.ToString()!.Trim();
                if (newName.Length > 0 && newName != name)   // Trim 后空名/同名：跳过重命名，items 分支继续
                {
                    if (project.Lists.Any(l => l.Type == list.Type && l.Name == newName))
                        return CommandResult.Fail("DUPLICATE", $"同类型列表 \"{newName}\" 已存在（{list.Type} 型）");
                    foreach (var screen in project.Screens)
                        foreach (var w in screen.Widgets)
                            ListTypeRefs.RenameRef(w, name, newName, list.Type);
                    list.Name = newName;
                }
            }

            // items 提供则整体替换（GUI 面板增删改项后提交全量；已在上方两段式完成校验/规范化）
            if (normalizedItems != null)
            {
                list.Items.Clear();
                list.Items.AddRange(normalizedItems);
            }
            return CommandResult.Ok(new { list_name = list.Name });
        }
    }

    /// <summary>删除列表。若被控件 ListRef 引用则拒绝删除（引用保护，防止控件悬空引用）。</summary>
    public class DeleteListHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_list", Description = "删除列表（被控件引用时拒绝）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "列表名" },
                ["type"] = new() { Type = "enum", EnumValues = new[] { "Text", "Image", "Video" }, Description = "列表类型（U-1 跨类型同名时必传定位；缺省按唯一名匹配）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!.Trim();   // 与 create_list 对齐：查找前 Trim
            // U-1：type 可选——提供则同类型定位（未命中=该类型列表不存在 → NOT_FOUND）；缺省按名唯一匹配（同名多报 AMBIGUOUS）
            ListType? typeFilter = null;
            if (p.TryGetValue("type", out var tp) && tp != null && tp.ToString()!.Length > 0)
            {
                if (!Enum.TryParse<ListType>(tp.ToString(), ignoreCase: true, out var t2))
                    return CommandResult.Fail("INVALID_PARAM", $"未知列表类型: {tp}（Text/Image/Video）");
                typeFilter = t2;
            }
            ListDef? list;
            if (typeFilter is { } tf)
            {
                list = project.Lists.FirstOrDefault(l => l.Type == tf && l.Name == name);
                if (list == null) return CommandResult.Fail("NOT_FOUND", $"列表 \"{name}\"（{tf} 型）不存在");
            }
            else
            {
                var matches = project.Lists.Where(l => l.Name == name).ToList();
                if (matches.Count == 0) return CommandResult.Fail("NOT_FOUND", $"列表 \"{name}\" 不存在");
                if (matches.Count > 1)
                    return CommandResult.Fail("AMBIGUOUS", $"列表 \"{name}\" 跨类型同名（Text/Image/Video），请用 type 参数指定");
                list = matches[0];
            }

            // 引用检查：按消费类型匹配（U-1 ListTypeRefs——同名跨类型不互拦）
            var widgetRefs = project.Screens
                .SelectMany(s => s.Widgets.Where(w => ListTypeRefs.WidgetRefs(w, name, list.Type))
                    .Select(w => $"{s.Name}/{w.ObjectName}"))
                .ToList();
            if (widgetRefs.Count > 0)
                return CommandResult.Fail("IN_USE", $"列表 \"{name}\"（{list.Type} 型）仍被控件引用（{string.Join(", ", widgetRefs)}），请先解除绑定");

            project.Lists.Remove(list);
            return CommandResult.Ok(new { list_name = name });
        }
    }
}
