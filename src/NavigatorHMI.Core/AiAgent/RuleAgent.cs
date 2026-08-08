using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NavigatorHMI.CommandLayer;

namespace NavigatorHMI.AiAgent
{
    /// <summary>
    /// 规则关键词映射 Agent：内置指令模板（正则 + 命名组参数提取）→ 对应命令执行。
    /// 离线、确定性、毫秒级；未匹配时提示支持范围。与 FC 链路（AIAgent）并行，供快速指令使用。
    /// </summary>
    public class RuleAgent
    {
        private readonly ICommandService _commands;

        /// <summary>模板：(正则, 命令名, 命名组→参数名映射, 默认参数)。组名"__value"表示整条输入作为该参数。</summary>
        private static readonly (string Pattern, string Command, Dictionary<string, string> Map, Dictionary<string, object?> Defaults)[] Templates =
        {
            // ── 画面 ──
            (@"^\s*(?:创建|新建|增加|添加)画面[:：]?(?<name>.+?)\s*$", "create_screen", Map("name"), null),
            (@"^\s*(?:创建|新建|增加|添加)(?:一个)?画面\s*$", "create_screen", new(), new() { ["name"] = "新画面" }),
            (@"^\s*(?:删除|移除)画面[:：]?(?<name>.+?)\s*$", "delete_screen", Map("name"), null),
            (@"^\s*(?:把|将)?画面(?<name>.+?)(?:重命名|改名为|改成|改名)(?:为)?[:：]?(?<new>.+?)\s*$", "rename_screen", new() { ["name"] = "name", ["new"] = "new_name" }, null),
            (@"^\s*(?:复制|拷贝)画面[:：]?(?<name>.+?)\s*$", "copy_screen", Map("name"), null),

            // ── 控件（type 关键词归一化；默认画面=全局画面）──
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>按钮|按键|button)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>文本|文字|标签)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>数值显示|数字显示)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>输入框|输入)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>进度条|进度)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>开关|switch)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>复选框|勾选框)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>图片|图像)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>列表|文本列表)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>矩形|方块)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>圆形|圆)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>椭圆)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>直线|线段)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>框架|frame)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:添加|增加|加|放|放置)(?:一个)?(?<type>IO域|IO)\s*$", "add_widget", new() { ["type"] = "widget_type" }, new() { ["screen_name"] = "全局画面" }),
            (@"^\s*(?:在|到)?(?<screen>.+?)(?:上|里|中|画面)?(?<center>中心)?(?:添加|增加|加|放|放置)(?:一个)?(?<type>按钮|按键|文本|文字|标签|数值显示|数字显示|进度条|开关|复选框|图片|列表|矩形|圆形|椭圆|直线|框架|IO域|IO|输入框|输入)\s*$",
                "add_widget", new() { ["screen"] = "screen_name", ["type"] = "widget_type" }, null),
            (@"^\s*(?:删除|移除)控件(?<widget>.+?)\s*$", "delete_widget", new() { ["widget"] = "widget_name" }, new() { ["screen_name"] = "全局画面" }),

            // ── 变量 ──
            (@"^\s*(?:创建|新建|增加|添加)变量[:：]?(?<name>.+?)(?:\s+(?:类型|type)\s*[:：]?\s*(?<data_type>BOOL|INT16|UINT16|INT32|FLOAT|STRING|DATETIME|日期时间))?\s*$",
                "create_tag", Map("name", "data_type"), new() { ["data_type"] = "FLOAT" }),   // 未指定类型默认 FLOAT（组匹配时覆盖）
            (@"^\s*(?:删除|移除)变量[:：]?(?<name>.+?)\s*$", "delete_tag", Map("name"), null),
            (@"^\s*把?(?<widget>.+?)绑定(?:到)?变量[:：]?(?<tag>.+?)\s*$", "bind_tag", new() { ["widget"] = "widget_name", ["tag"] = "tag_name" }, new() { ["screen_name"] = "全局画面" }),

            // ── 报警 ──
            (@"^\s*(?:创建|新建|增加|添加)报警[:：]?(?<name>.+?)(?:\s+关联变量[:：]?\s*(?<tag>.+?))?(?:\s+阈值\s*[:：]?\s*(?<threshold>\d+(?:\.\d+)?))?\s*$",
                "create_alarm", new() { ["name"] = "name", ["tag"] = "tag_name", ["threshold"] = "threshold" }, new() { ["type"] = "High" }),
            (@"^\s*(?:删除|移除)报警[:：]?(?<name>.+?)\s*$", "delete_alarm", Map("name"), null),

        };

        private static Dictionary<string, string> Map(params string[] names)
        {
            var d = new Dictionary<string, string>();
            foreach (var n in names) d[n] = n;
            return d;
        }

        public RuleAgent(ICommandService commands)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        /// <summary>处理指令：匹配模板 → 执行命令 → 返回结果摘要。未匹配返回支持范围提示。</summary>
        public string Process(string input)
        {
            var text = (input ?? "").Trim();
            if (text.Length == 0) return "请输入指令，例如：创建画面 温度监控 / 放一个按钮 / 创建变量 温度 类型 FLOAT";

            foreach (var t in Templates)
            {
                var m = Regex.Match(text, t.Pattern, RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                var args = new Dictionary<string, object?>();
                if (t.Defaults != null)
                    foreach (var kv in t.Defaults) args[kv.Key] = kv.Value;
                foreach (var kv in t.Map)
                {
                    if (m.Groups[kv.Key].Success && m.Groups[kv.Key].Length > 0)
                        args[kv.Value] = Normalize(kv.Value, m.Groups[kv.Key].Value.Trim());
                }
                // add_widget 必填 x/y：无坐标时默认 (100,100) 放置；模板命中「中心」后缀 → center=true（画面中心，忽略 x/y）
                if (t.Command == "add_widget")
                {
                    args.TryAdd("x", "100");
                    args.TryAdd("y", "100");
                    // 画面名歧义消解：正则的「画面」后缀可能吃掉画面名本身（如「全局画面」→「全局」）。
                    // 回退：screen 不在画面列表时，试 screen+"画面" 在列表 → 用全名（如「全局」→「全局画面」）
                    var screenName = args.TryGetValue("screen_name", out var s0) ? s0?.ToString() ?? "" : "";
                    var screens = _commands.GetScreenNames();
                    if (!screens.Contains(screenName, StringComparer.OrdinalIgnoreCase)
                        && screens.Contains(screenName + "画面", StringComparer.OrdinalIgnoreCase))
                    {
                        args["screen_name"] = screenName + "画面";
                        screenName = screenName + "画面";
                    }
                    if (m.Groups["center"].Success)
                    {
                        // 歧义消解：画面名含「中心」优先（如「数据中心」→ 画面名而非中心位置），否则视为中心位置
                        if (!screens.Contains(screenName, StringComparer.OrdinalIgnoreCase)
                            && screens.Contains(screenName + "中心", StringComparer.OrdinalIgnoreCase))
                            args["screen_name"] = screenName + "中心";
                        else
                            args["center"] = "true";
                    }
                }

                var result = _commands.Execute(t.Command, args);
                return result.Success
                    ? $"✓ 已执行 {t.Command}" + (result.Data != null ? $"：{System.Text.Json.JsonSerializer.Serialize(result.Data)}" : "")
                    : $"✗ {t.Command} 失败 [{result.ErrorCode}]: {result.ErrorMessage}";
            }

            return "未识别该指令。支持示例：\n" +
                   "· 创建画面 温度监控\n· 放一个按钮 / 在全局画面上放一个数值显示\n· 创建变量 温度 类型 FLOAT\n" +
                   "· 删除变量 温度\n· 创建报警 温度过高 关联变量 温度 阈值 80\n· 删除报警 温度过高\n· 配置设备 PLC1 协议 ModbusTCP";
        }

        /// <summary>控件类型关键词 → add_widget 的 widget_type 枚举值（与 WidgetHandlers 一致，小写）。</summary>
        private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["按钮"] = "button", ["按键"] = "button", ["button"] = "button",
            ["文本"] = "text", ["文字"] = "text", ["标签"] = "label",
            ["数值显示"] = "numeric", ["数字显示"] = "numeric",
            ["输入框"] = "iofield", ["输入"] = "iofield",
            ["进度条"] = "progressbar", ["进度"] = "progressbar",
            ["开关"] = "switch", ["switch"] = "switch",
            ["复选框"] = "checkbox", ["勾选框"] = "checkbox",
            ["图片"] = "image", ["图像"] = "image",
            ["列表"] = "textlist", ["文本列表"] = "textlist",
            ["日期时间"] = "datetime", ["时间"] = "datetime",
            ["矩形"] = "rectangle", ["方块"] = "rectangle",
            ["圆形"] = "circle", ["圆"] = "circle",
            ["椭圆"] = "ellipse",
            ["直线"] = "line", ["线段"] = "line",
            ["框架"] = "frame", ["frame"] = "frame",
            ["IO域"] = "iofield", ["IO"] = "iofield",
        };

        private static object? Normalize(string paramName, string value)
        {
            // type/widget_type：控件类型别名；data_type：变量类型别名（日期时间→DATETIME 等）
            if ((paramName is "type" or "widget_type" or "data_type") && TypeAliases.TryGetValue(value, out var mapped)) return mapped;
            if (paramName == "threshold" && double.TryParse(value, out var d)) return d;
            return value;
        }
    }
}
