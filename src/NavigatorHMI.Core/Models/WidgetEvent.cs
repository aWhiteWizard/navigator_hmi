using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 控件事件类型。定义控件的触发条件。
    /// </summary>
    public enum EventType
    {
        /// <summary>点击/触摸</summary>
        onClick,
        /// <summary>长按（≥500ms）</summary>
        onPress,
        /// <summary>释放（从按下状态松开）</summary>
        onRelease,
        /// <summary>绑定变量的值发生变化</summary>
        onValueChange,
        /// <summary>关联变量的报警被触发</summary>
        onAlarmTrigger,
        /// <summary>报警被操作员确认</summary>
        onAlarmAck,
        /// <summary>报警条件消除，报警恢复</summary>
        onAlarmClear,
        /// <summary>控件所在画面被加载显示</summary>
        onScreenLoad,
        /// <summary>控件所在画面被切走</summary>
        onScreenUnload,
        /// <summary>周期触发（间隔由参数指定，单位 ms）</summary>
        onTimer,
        /// <summary>设备启动完成后触发</summary>
        onSystemStart,
        /// <summary>设备关机前触发</summary>
        onSystemShutdown,
        /// <summary>输入完成（IOField 输入框）</summary>
        onInput,
        /// <summary>开关打开（Switch）</summary>
        onOn,
        /// <summary>开关关闭（Switch）</summary>
        onOff
    }

    /// <summary>
    /// 动作类型。事件触发后执行的操作类型。
    /// </summary>
    public enum ActionType
    {
        /// <summary>写值到指定变量</summary>
        tag_write,
        /// <summary>切换到指定画面</summary>
        screen_switch,
        /// <summary>修改控件属性（颜色/可见性/文本等）</summary>
        set_property,
        /// <summary>执行 Command Layer 中任意命令</summary>
        run_command,
        /// <summary>弹出对话框/详情面板</summary>
        show_popup,
        /// <summary>推送通知到 MQTT</summary>
        send_notification,
        /// <summary>上一个画面（运行时导航栈回退）</summary>
        screen_prev,
        /// <summary>下一个画面（运行时导航栈前进）</summary>
        screen_next,
        /// <summary>变量值增加</summary>
        tag_add,
        /// <summary>变量值减少</summary>
        tag_subtract,
        /// <summary>BOOL 变量翻转</summary>
        tag_toggle,
        /// <summary>BOOL 变量置位</summary>
        set_bit,
        /// <summary>BOOL 变量复位</summary>
        reset_bit,
        /// <summary>设置日期时间</summary>
        set_datetime,
        /// <summary>读取日期时间</summary>
        get_datetime,
        /// <summary>确认报警</summary>
        acknowledge_alarm
    }

    /// <summary>
    /// 事件-动作绑定。每个控件可配置零到多个事件，每个事件触发一或多个动作。
    /// 动作按列表顺序依次执行。
    /// </summary>
    [ProtoContract]
    public class WidgetEvent
    {
        /// <summary>事件类型（触发条件）</summary>
        [ProtoMember(1)]
        public EventType Type { get; set; }

        /// <summary>可选触发条件表达式（如 "value > 80"），空表示无条件触发</summary>
        [ProtoMember(2)]
        public string Condition { get; set; } = "";

        /// <summary>触发后执行的动作列表（按顺序执行）</summary>
        [ProtoMember(3)]
        public List<EventAction> Actions { get; set; } = new();
    }

    /// <summary>
    /// 单个动作定义。描述事件触发后执行的一次具体操作。
    /// </summary>
    [ProtoContract]
    public class EventAction
    {
        /// <summary>动作类型</summary>
        [ProtoMember(1)]
        public ActionType Type { get; set; }

        /// <summary>动作参数，键值对（如 {tag_name: "Motor_Enable", value: "1"}）</summary>
        [ProtoMember(2)]
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
}
