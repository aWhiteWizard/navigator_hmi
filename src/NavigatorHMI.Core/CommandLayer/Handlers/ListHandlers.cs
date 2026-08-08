using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
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

    /// <summary>创建列表（文本/图片）。列表项为有序预设值，控件绑定数值变量按索引显示。</summary>
    public class CreateListHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "create_list", Description = "创建列表（文本/图片预设值，控件绑定数值变量按索引显示）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "列表名（工程内唯一）" },
                ["type"] = new() { Type = "enum", Required = true, EnumValues = new[] { "Text", "Image" }, Description = "列表类型" },
                // Required：AI compact schema 才保留 items（建列表必须指定项；图片列表为 | 分隔的图片路径）
                ["items"] = new() { Type = "string", Required = true, Description = "列表项，用 | 分隔（图片列表为图片路径）" },
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
            if (project.Lists.Any(l => l.Name == name))
                return CommandResult.Fail("DUPLICATE", $"列表 \"{name}\" 已存在（可直接使用，或 update-list 修改其内容）");
            if (!Enum.TryParse<ListType>(p["type"]!.ToString(), ignoreCase: true, out var type))
                return CommandResult.Fail("INVALID_PARAM", $"未知列表类型: {p["type"]}（Text/Image）");
            var items = ListItemsParser.Parse(p.GetValueOrDefault("items")) ?? new List<string>();
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
                ["new_name"] = new() { Type = "string", Description = "新列表名（重命名）" },
                ["items"] = new() { Type = "string", Description = "预设值列表，用 | 分隔（提供则整体替换）" },
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
            var list = project.Lists.FirstOrDefault(l => l.Name == name);
            if (list == null) return CommandResult.Fail("NOT_FOUND", $"列表 \"{name}\" 不存在");

            // 重命名：唯一性校验 + 级联同步控件 ListRef（反射遍历 Widget，兼容 ImageWidget/FrameWidget/TextListWidget）
            // ⚠️ 不能提前 return：items 替换分支须独立可达（cli-param-sanitize §12 显式清空语义）
            // new_name 无清空语义：仅判非 null，由内层 Trim + 长度守卫统一净化空白串
            if (p.TryGetValue("new_name", out var nn) && nn != null)
            {
                var newName = nn.ToString()!.Trim();
                if (newName.Length > 0 && newName != name)   // Trim 后空名/同名：跳过重命名，items 分支继续
                {
                    if (project.Lists.Any(l => l.Name == newName))
                        return CommandResult.Fail("DUPLICATE", $"列表 \"{newName}\" 已存在");
                    foreach (var screen in project.Screens)
                        foreach (var w in screen.Widgets)
                        {
                            var prop = w.GetType().GetProperty(ListRefPropertyName);
                            if (prop != null && prop.CanRead && prop.CanWrite && prop.GetValue(w) is string refName && refName == name)
                                prop.SetValue(w, newName);
                        }
                    list.Name = newName;
                }
            }

            // items 提供则整体替换（GUI 面板增删改项后提交全量）
            if (p.TryGetValue("items", out var items) && items != null)
            {
                var parsed = ListItemsParser.Parse(items);
                if (parsed != null)
                {
                    list.Items.Clear();
                    list.Items.AddRange(parsed);
                }
            }
            return CommandResult.Ok(new { list_name = list.Name });
        }
    }

    /// <summary>删除列表。若被控件 ListRef 引用则拒绝删除（引用保护，防止控件悬空引用）。</summary>
    public class DeleteListHandler : ICommandHandler
    {
        /// <summary>控件列表引用属性名（反射统一处理防字符串漂移）。</summary>
        private const string ListRefPropertyName = "ListRef";

        public CommandDefinition Definition => new()
        {
            Name = "delete_list", Description = "删除列表（被控件引用时拒绝）",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "列表名" },
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
            var list = project.Lists.FirstOrDefault(l => l.Name == name);
            if (list == null) return CommandResult.Fail("NOT_FOUND", $"列表 \"{name}\" 不存在");

            // 引用检查：控件 ListRef（反射遍历，兼容 ImageWidget/FrameWidget/TextListWidget）
            var widgetRefs = project.Screens
                .SelectMany(s => s.Widgets.Where(w =>
                        w.GetType().GetProperty(ListRefPropertyName) is { } prop
                        && prop.CanRead && prop.GetValue(w) is string refName && refName == name)
                    .Select(w => $"{s.Name}/{w.ObjectName}"))
                .ToList();
            if (widgetRefs.Count > 0)
                return CommandResult.Fail("IN_USE", $"列表 \"{name}\" 仍被控件引用（{string.Join(", ", widgetRefs)}），请先解除绑定");

            project.Lists.Remove(list);
            return CommandResult.Ok(new { list_name = name });
        }
    }
}
