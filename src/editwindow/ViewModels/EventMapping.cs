using System;
using System.Collections.Generic;
using System.Linq;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>控件类型 → 可用事件映射（DESIGN-EVENTS.md §2 定稿）+ 事件/动作中文名。</summary>
    public static class EventMapping
    {
        /// <summary>事件类型中文名（对话框/事件栏显示）。</summary>
        public static readonly IReadOnlyDictionary<EventType, string> EventNames = new Dictionary<EventType, string>
        {
            [EventType.onClick] = "点击",
            [EventType.onPress] = "按下",
            [EventType.onRelease] = "释放",
            [EventType.onValueChange] = "值变化",
            [EventType.onAlarmTrigger] = "报警触发",
            [EventType.onAlarmAck] = "报警确认",
            [EventType.onAlarmClear] = "报警恢复",
            [EventType.onScreenLoad] = "画面加载",
            [EventType.onScreenUnload] = "画面切走",
            [EventType.onTimer] = "周期触发",
            [EventType.onSystemStart] = "设备启动",
            [EventType.onSystemShutdown] = "设备关机",
            [EventType.onInput] = "输入完成",
            [EventType.onOn] = "开关打开",
            [EventType.onOff] = "开关关闭",
            [EventType.onProgressComplete] = "进度完成（100%）",
        };

        /// <summary>动作类型中文名（对话框函数列表显示）。</summary>
        public static readonly IReadOnlyDictionary<ActionType, string> ActionNames = new Dictionary<ActionType, string>
        {
            [ActionType.tag_write] = "设置变量值",
            [ActionType.tag_add] = "变量值增加",
            [ActionType.tag_subtract] = "变量值减少",
            [ActionType.tag_toggle] = "变量翻转",
            [ActionType.set_bit] = "变量置位",
            [ActionType.reset_bit] = "变量复位",
            [ActionType.screen_switch] = "切换画面",
            [ActionType.screen_prev] = "上一个画面",
            [ActionType.screen_next] = "下一个画面",
            [ActionType.show_popup] = "弹窗/详情",
            [ActionType.set_property] = "修改控件属性",
            [ActionType.send_notification] = "发送消息(MQTT)",
            [ActionType.run_command] = "执行命令",
            [ActionType.set_datetime] = "设置日期时间",
            [ActionType.get_datetime] = "读取日期时间",
            [ActionType.acknowledge_alarm] = "确认报警",
            [ActionType.set_system_time] = "修改系统时间",
        };

        /// <summary>控件类型（Type 名）→ 可用事件列表（DESIGN §2；图形类/Label 无事件；进度条 onProgressComplete）。</summary>
        private static readonly Dictionary<string, EventType[]> Map = new()
        {
            ["ButtonWidget"] = new[] { EventType.onClick, EventType.onPress, EventType.onRelease },
            ["ImageWidget"] = new[] { EventType.onClick, EventType.onValueChange },
            ["NumericDisplayWidget"] = new[] { EventType.onValueChange },
            ["FrameWidget"] = new[] { EventType.onClick, EventType.onPress, EventType.onValueChange, EventType.onRelease },
            ["SwitchWidget"] = new[] { EventType.onClick, EventType.onOn, EventType.onOff, EventType.onRelease },
            ["IOFieldWidget"] = new[] { EventType.onClick, EventType.onPress, EventType.onInput, EventType.onValueChange, EventType.onRelease },
            ["CheckBoxWidget"] = new[] { EventType.onClick, EventType.onValueChange, EventType.onRelease },
            ["TextListWidget"] = new[] { EventType.onClick, EventType.onPress, EventType.onValueChange, EventType.onRelease },
            ["ProgressBarWidget"] = new[] { EventType.onProgressComplete },
        };

        /// <summary>控件类型是否支持事件（属性面板事件栏可见性）。</summary>
        public static bool SupportsEvents(string widgetTypeName) => Map.ContainsKey(widgetTypeName);

        /// <summary>控件可用事件列表（按 Map 顺序）。</summary>
        public static IReadOnlyList<EventType> GetEvents(string widgetTypeName)
            => Map.TryGetValue(widgetTypeName, out var evts) ? evts : Array.Empty<EventType>();

        /// <summary>控件事件中文名列表（事件栏显示）。</summary>
        public static string EventName(EventType e) => EventNames.TryGetValue(e, out var n) ? n : e.ToString();
    }

    /// <summary>事件栏行 VM：单个事件的状态行（事件名 + 已配置函数数 + 可配置）。</summary>
    public class EventRowVM
    {
        public EventType Type { get; }
        public string DisplayName => EventMapping.EventName(Type);
        public int ActionCount { get; set; }
        public string Status => ActionCount > 0 ? $"已配置 {ActionCount} 个函数" : "未配置";
        public bool Configured => ActionCount > 0;

        public EventRowVM(EventType type) { Type = type; }
    }
}
