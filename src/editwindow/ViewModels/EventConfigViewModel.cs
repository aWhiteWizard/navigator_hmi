using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;   // P8：BaseValueValidator（按 DataType 校验函数值）
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>C9 事件配置对话框 ViewModel：函数列表（1~N）+ 动态参数表单 + run_command 命令元数据驱动。
    /// C11 参数隔离：每个函数独立 ActionEditVM（各自 Parameters 字典），切换函数不串值。</summary>
    public class EventConfigViewModel : INotifyPropertyChanged
    {
        private readonly HMIProject _project;
        private readonly Widget? _widget;   // null = 世界地图级事件（WorldMapConfig.Events，P3 地图右键）
        private readonly EventType _eventType;

        /// <summary>本事件全部可用动作（添加函数下拉）。</summary>
        public IReadOnlyList<ActionType> AllActions { get; } = Enum.GetValues<ActionType>();

    // ═══ P3-7 函数两级选择（小类 → 函数） ═══
    public IReadOnlyList<string> FunctionGroupNames { get; } = new[] { "变量操作", "画面导航", "控件与界面", "通知与报警", "日期时间", "执行命令" };

    private static readonly Dictionary<string, ActionType[]> FunctionGroupActionMap = new()
    {
        ["变量操作"] = new[] { ActionType.tag_write, ActionType.tag_add, ActionType.tag_subtract, ActionType.tag_toggle, ActionType.set_bit, ActionType.reset_bit },
        ["画面导航"] = new[] { ActionType.screen_switch, ActionType.screen_prev, ActionType.screen_next },
        ["控件与界面"] = new[] { ActionType.set_property, ActionType.show_popup },
        ["通知与报警"] = new[] { ActionType.send_notification, ActionType.acknowledge_alarm },
        ["日期时间"] = new[] { ActionType.set_datetime, ActionType.get_datetime, ActionType.set_system_time },
        ["执行命令"] = new[] { ActionType.run_command },
    };

    private string _selectedFunctionGroupName = "变量操作";
    public string SelectedFunctionGroupName
    {
        get => _selectedFunctionGroupName;
        set
        {
            if (_selectedFunctionGroupName == value) return;
            _selectedFunctionGroupName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FunctionGroupActionNames));
            SelectedFunctionGroupActionName = FunctionGroupActionNames.FirstOrDefault();
        }
    }

    /// <summary>当前小类下可添加的函数（中文名列表）。</summary>
    public IReadOnlyList<string> FunctionGroupActionNames
        => FunctionGroupActionMap.TryGetValue(SelectedFunctionGroupName, out var list)
            ? list.Select(t => EventMapping.ActionNames.TryGetValue(t, out var n) ? n : t.ToString()).ToList()
            : Array.Empty<string>();

    private string _selectedFunctionGroupActionName = "";
    /// <summary>选中的函数（添加按钮用；名字 → ActionType 反查）。</summary>
    public string SelectedFunctionGroupActionName
    {
        get => _selectedFunctionGroupActionName;
        set
        {
            _selectedFunctionGroupActionName = value ?? "";
            OnPropertyChanged();
            SelectedFunctionGroupAction = ResolveActionType(_selectedFunctionGroupActionName);
        }
    }

    private ActionType? _selectedFunctionGroupAction;
    public ActionType? SelectedFunctionGroupAction { get => _selectedFunctionGroupAction; set { _selectedFunctionGroupAction = value; OnPropertyChanged(); } }

    private static ActionType? ResolveActionType(string name)
    {
        foreach (var t in Enum.GetValues<ActionType>())
            if ((EventMapping.ActionNames.TryGetValue(t, out var n) ? n : t.ToString()) == name) return t;
        return Enum.TryParse<ActionType>(name, ignoreCase: true, out var r) ? r : null;
    }

    // ═══ P3-7 命令两级选择（分类 → 命令） ═══
    public IReadOnlyList<string> CommandGroupNames { get; } = new[] { "创建类", "删除类", "设置/更新类", "绑定类", "其他" };

    public static string CommandGroupOf(string cmd) => cmd switch
    {
        var c when c.StartsWith("create_") || c.StartsWith("add_") => "创建类",
        var c when c.StartsWith("delete_") || c.StartsWith("remove_") => "删除类",
        var c when c.StartsWith("set_") || c.StartsWith("update_") => "设置/更新类",
        var c when c.StartsWith("bind_") || c.StartsWith("unbind_") => "绑定类",
        _ => "其他",
    };

    private string _selectedCommandGroupName = "创建类";
    public string SelectedCommandGroupName
    {
        get => _selectedCommandGroupName;
        set
        {
            if (_selectedCommandGroupName == value) return;
            _selectedCommandGroupName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredCommandNames));
        }
    }

    /// <summary>当前分类下的命令名（选中写入 SelectedFunction.CommandName）。</summary>
    public IReadOnlyList<string> FilteredCommandNames
        => Commands.Where(c => CommandGroupOf(c.Name) == SelectedCommandGroupName).Select(c => c.Name).ToList();

    private string _selectedCommandName = "";
    public string SelectedCommandName
    {
        get => _selectedCommandName;
        set
        {
            _selectedCommandName = value ?? "";
            OnPropertyChanged();
            if (SelectedFunction != null && !string.IsNullOrWhiteSpace(_selectedCommandName))
                SelectedFunction.CommandName = _selectedCommandName;   // 写回函数命令（触发参数表单重建）
        }
    }

        public ObservableCollection<ActionEditVM> Functions { get; } = new();
        private ActionEditVM? _selectedFunction;

        /// <summary>I-3 事件触发条件（如 "value > 80"；留空=无条件）。保存写回 WidgetEvent.Condition。</summary>
        private string _condition = "";
        public string Condition
        {
            get => _condition;
            set { if (_condition != value) { _condition = value ?? ""; OnPropertyChanged(); } }
        }
        public ActionEditVM? SelectedFunction
        {
            get => _selectedFunction;
            set
            {
                if (_selectedFunction != null) _selectedFunction.Commit();   // C11：切换前保存未提交值
                _selectedFunction = value;
                if (value != null) value.Load();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowCommandParams));
                OnPropertyChanged(nameof(ShowActionParams));
                OnPropertyChanged(nameof(NoParamsHint));
                // P3-7：run_command 函数切换时同步命令两级下拉（分类 + 命令），避免残留旧命令与表单矛盾
                if (value?.Type == ActionType.run_command && !string.IsNullOrWhiteSpace(value.CommandName))
                {
                    SelectedCommandGroupName = CommandGroupOf(value.CommandName);
                    SelectedCommandName = value.CommandName;
                }
            }
        }
        public bool HasSelection => _selectedFunction != null;
        public bool ShowCommandParams => _selectedFunction?.Type == ActionType.run_command;
        public bool ShowActionParams => _selectedFunction != null && _selectedFunction.Type != ActionType.run_command && _selectedFunction.ActionParams.Count > 0;
        public bool NoParamsHint => _selectedFunction != null && _selectedFunction.Type != ActionType.run_command && _selectedFunction.ActionParams.Count == 0;

        /// <summary>变量名 → 类型（tag-bool/tag-numeric 过滤用；不存在返回 BOOL 以免误含）。</summary>
        public TagDataType TagOf(string name)
            => _project.Tags.FirstOrDefault(t => t.Name == name)?.DataType ?? TagDataType.BOOL;

        /// <summary>可用命令列表（run_command 下拉；按 CommandService 注册全量）。</summary>
        public IReadOnlyList<CommandDefinition> Commands { get; }

        /// <summary>画面名列表（screen_switch 下拉；排除全局画面 Template）。</summary>
        public IReadOnlyList<string> ScreenNames { get; }

        /// <summary>同画面控件名列表（set_property 控件下拉）。</summary>
        public IReadOnlyList<string> WidgetNames { get; }

        /// <summary>变量名列表。</summary>
        public IReadOnlyList<string> TagNames { get; }

        /// <summary>报警名列表。</summary>
        public IReadOnlyList<string> AlarmNames { get; }

        public EventConfigViewModel(HMIProject project, Widget? widget, EventType evt, ICommandService commands)
        {
            _project = project;
            _widget = widget;
            _eventType = evt;
            Commands = commands.GetAvailableCommands().OrderBy(c => c.Name).ToList();
            SelectedFunctionGroupActionName = FunctionGroupActionNames.FirstOrDefault();   // 初始选中第一个函数（tag_write）
            var screen = widget != null ? project.Screens.FirstOrDefault(s => s.Widgets.Contains(widget)) : null;
            ScreenNames = project.Screens.Where(s => !s.Name.Contains("全局")).Select(s => s.Name).ToList();
            WidgetNames = screen?.Widgets.Select(w => w.ObjectName).ToList() ?? new List<string>();
            TagNames = project.Tags.Select(t => t.Name).ToList();
            AlarmNames = project.Alarms.Select(a => a.Name).ToList();

            // 载入现有 Actions（同一事件的）；地图级事件存 WorldMapConfig.Events
            var events = widget != null ? widget.Events : (project.WorldMap ??= new WorldMapConfig()).Events;
            var we = events.FirstOrDefault(e => e.Type == evt);
            if (we != null)
            {
                _condition = we.Condition;   // I-3 载入事件条件
                foreach (var a in we.Actions)
                    Functions.Add(new ActionEditVM(a, this));
            }
        }

        /// <summary>添加函数（从下拉选中动作类型）。</summary>
        public void AddFunction(ActionType type)
        {
            var vm = new ActionEditVM(new EventAction { Type = type }, this);
            Functions.Add(vm);
            SelectedFunction = vm;
        }

        public void RemoveFunction(ActionEditVM vm)
        {
            var idx = Functions.IndexOf(vm);
            Functions.Remove(vm);
            if (SelectedFunction == vm) SelectedFunction = Functions.ElementAtOrDefault(Math.Max(0, idx - 1));
        }

        /// <summary>P8：全函数校验（保存前调用）：返回第一个错误或 null。</summary>
        public string? ValidateAll()
        {
            foreach (var f in Functions)
            {
                var err = f.Validate();
                if (err != null) return err;
            }
            return null;
        }

        /// <summary>保存：写回 widget.Events 或 WorldMapConfig.Events（替换同事件 Actions）。</summary>
        public void Save()
        {
            foreach (var f in Functions) f.Commit();
            var events = _widget != null ? _widget.Events : (_project.WorldMap ??= new WorldMapConfig()).Events;
            var we = events.FirstOrDefault(e => e.Type == _eventType);
            if (Functions.Count == 0)
            {
                if (we != null) events.Remove(we);
                return;
            }
            if (we == null)
            {
                we = new WidgetEvent { Type = _eventType };
                events.Add(we);
            }
            we.Condition = Condition;   // I-3 保存事件条件（留空=无条件）
            we.Actions.Clear();
            we.Actions.AddRange(Functions.Select(f => f.Build()));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>单个函数（动作）编辑 VM：C11 参数隔离——每个函数独立 Parameters 字典 + 独立表单字段。</summary>
    public class ActionEditVM : INotifyPropertyChanged
    {
        public ActionType Type { get; }
        public string TypeName => EventMapping.ActionNames.TryGetValue(Type, out var n) ? n : Type.ToString();
        private readonly EventAction _action;
        private readonly EventConfigViewModel _owner;

        /// <summary>函数参数（除 run_command 外由静态表驱动；run_command 由命令元数据动态驱动）。</summary>
        public ObservableCollection<ParamFieldVM> ActionParams { get; } = new();
        public ObservableCollection<ParamFieldVM> CommandParams { get; } = new();

        /// <summary>run_command 选择的命令名。</summary>
        private string _commandName = "";
        public string CommandName
        {
            get => _commandName;
            set
            {
                if (_commandName == value) return;
                _commandName = value;
                RebuildCommandParams();
                OnPropertyChanged();
                OnPropertyChanged(nameof(Summary));
            }
        }
        public IReadOnlyList<string> CommandNames { get; }

        /// <summary>参数摘要（函数列表行显示）。</summary>
        public string Summary
        {
            get
            {
                var parts = Type == ActionType.run_command
                    ? new[] { _commandName }.Concat(CommandParams.Select(f => $"{f.Key}={f.Value}")).Where(s => !string.IsNullOrEmpty(s))
                    : ActionParams.Select(f => $"{f.Key}={f.Value}");
                var arr = parts.ToArray();
                return arr.Length > 0 ? string.Join(", ", arr) : "无参数";
            }
        }

        public ActionEditVM(EventAction action, EventConfigViewModel owner)
        {
            _action = action;
            _owner = owner;
            Type = action.Type;
            CommandNames = owner.Commands.Select(c => c.Name).ToList();
            if (Type == ActionType.run_command)
            {
                _commandName = action.Parameters.TryGetValue("command_name", out var cn) ? cn : "";
                foreach (var kv in action.Parameters.Where(kv => kv.Key != "command_name"))
                    CommandParams.Add(new ParamFieldVM(kv.Key, kv.Value, null, owner.Commands.FirstOrDefault(c => c.Name == _commandName)?.Parameters.GetValueOrDefault(kv.Key)?.Type ?? "string"));
                OnPropertyChanged(nameof(CommandName));   // 仅通知 UI（_commandName 已直接赋字段；表单已按旧命令参数加载）
            }
            else
            {
                BuildActionParams(action.Parameters);
            }
        }

        /// <summary>构建非 run_command 的参数表单（按 ActionType 静态表）。</summary>
        private void BuildActionParams(Dictionary<string, string> existing)
        {
            var fields = ActionParamSchema.Get(Type);
            foreach (var (key, label, kind, opts) in fields)
            {
                var value = existing.TryGetValue(key, out var v) ? v : "";
                var opts2 = kind == "screen" ? _owner.ScreenNames : kind == "widget" ? _owner.WidgetNames
                    : kind == "tag" ? _owner.TagNames : kind == "tag-bool" ? _owner.TagNames.Where(n => _owner.TagOf(n) == TagDataType.BOOL).ToList()
                    : kind == "tag-numeric" ? _owner.TagNames.Where(n => TagCompatibility.IsNumericCompatible(_owner.TagOf(n))).ToList()
                    : kind == "alarm" ? _owner.AlarmNames : opts;
                var pf = new ParamFieldVM(key, value, label, kind, opts2);
                pf.PropertyChanged += (_, _) => OnActionParamChanged();
                ActionParams.Add(pf);
            }
        }

        /// <summary>P8：参数表单任一字段变化 → 刷新摘要 + 全字段实时校验（tag_write 的 value 依赖 tag_name 目标类型）。</summary>
        private void OnActionParamChanged()
        {
            OnPropertyChanged(nameof(Summary));
            foreach (var f in ActionParams) RevalidateField(f);
        }

        /// <summary>P8：按字段语义校验——tag_write 的 value 按目标变量 DataType 校验（复用 BaseValueValidator）；
        /// tag_add/tag_subtract 的 delta 必须为数值；其余字段放行。</summary>
        private void RevalidateField(ParamFieldVM f)
        {
            if (Type == ActionType.tag_write && f.Key == "value")
            {
                var tagName = ActionParams.FirstOrDefault(x => x.Key == "tag_name")?.Value ?? "";
                var dt = _owner.TagOf(tagName);   // TagOf 未知变量默认 BOOL——非法 tag_name 由变量下拉约束，此处仅防格式垃圾
                f.ErrorText = BaseValueValidator.Check(f.Value, dt) ?? "";
            }
            else if (f.Key == "delta" && Type is ActionType.tag_add or ActionType.tag_subtract)
            {
                f.ErrorText = string.IsNullOrWhiteSpace(f.Value) ? ""
                    : double.TryParse(f.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
                        ? "" : "增量必须是数值";
            }
            else f.ErrorText = "";
        }

        /// <summary>P8：整体校验（保存前调用）：先实时重校验全部字段，再检查必填语义（带 tag_name 的动作未选变量）。返回错误描述或 null。</summary>
        public string? Validate()
        {
            foreach (var f in ActionParams) RevalidateField(f);
            if (Type is ActionType.tag_write or ActionType.tag_add or ActionType.tag_subtract
                or ActionType.tag_toggle or ActionType.set_bit or ActionType.reset_bit)
            {
                var tagName = ActionParams.FirstOrDefault(f => f.Key == "tag_name")?.Value;
                if (string.IsNullOrWhiteSpace(tagName)) return $"{TypeName} 未选择变量";
            }
            var bad = ActionParams.FirstOrDefault(f => f.HasError);
            return bad != null ? $"{TypeName} 参数「{bad.Key}」：{bad.ErrorText}" : null;
        }

        /// <summary>run_command：按命令元数据重建参数表单（清空重填——DESIGN C12 防串错）。</summary>
        private void RebuildCommandParams()
        {
            CommandParams.Clear();
            var def = _owner.Commands.FirstOrDefault(c => c.Name == _commandName);
            if (def == null) return;
            foreach (var (key, pd) in def.Parameters)
            {
                // DESIGN C12：切命令清空——只用 DefaultValue，不从旧 Parameters 取（防同 key 串值）；初始载入的旧参数由构造器单独恢复
                var prev = pd.DefaultValue?.ToString() ?? "";
                var opts = pd.Type == "enum" ? pd.EnumValues : null;
                var f = new ParamFieldVM(key, prev, null, pd.Type, opts, pd.Required);
                f.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
                CommandParams.Add(f);
            }
        }

        /// <summary>提交：表单值写回 Parameters（C11 切换/保存前调用）。</summary>
        public void Commit()
        {
            _action.Parameters.Clear();
            if (Type == ActionType.run_command)
            {
                if (!string.IsNullOrEmpty(_commandName)) _action.Parameters["command_name"] = _commandName;
                foreach (var f in CommandParams) _action.Parameters[f.Key] = f.Value;
            }
            else
            {
                foreach (var f in ActionParams) _action.Parameters[f.Key] = f.Value;
            }
        }

        /// <summary>加载：从 Parameters 刷新表单（切换函数时）。</summary>
        public void Load()
        {
            if (Type == ActionType.run_command)
            {
                foreach (var f in CommandParams)
                    if (_action.Parameters.TryGetValue(f.Key, out var v)) f.Value = v;
                OnPropertyChanged(nameof(Summary));
            }
            else
            {
                foreach (var f in ActionParams)
                    if (_action.Parameters.TryGetValue(f.Key, out var v)) f.Value = v;
                OnPropertyChanged(nameof(Summary));
            }
        }

        public EventAction Build()
        {
            Commit();
            return _action;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>参数表单字段 VM（文本框/下拉/数字；Required 标 *）。</summary>
    public class ParamFieldVM : INotifyPropertyChanged
    {
        public string Key { get; }
        public string Label { get; }
        public string Kind { get; }       // string/int/double/bool/enum/screen/widget/tag/alarm
        public IReadOnlyList<string>? Options { get; }
        public bool Required { get; }
        public string DisplayLabel => Required ? Key + " *" : Key;

        private string _value = "";
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); }
        }
        public bool IsEmpty => string.IsNullOrEmpty(_value);
        public bool IsCombo => Options != null && Options.Count > 0;
        public bool IsNumber => Kind is "int" or "double";

        /// <summary>P8：字段值校验错误（实时校验，非空则红字提示 + 保存拦截）。</summary>
        private string _errorText = "";
        public string ErrorText
        {
            get => _errorText;
            set { if (_errorText != value) { _errorText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); } }
        }
        public bool HasError => !string.IsNullOrEmpty(_errorText);

        public ParamFieldVM(string key, string value, string? label, string kind = "string", IReadOnlyList<string>? options = null, bool required = false)
        {
            Key = key; _value = value; Label = label ?? key; Kind = kind; Options = options; Required = required;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>动作参数静态表：ActionType → (key, 中文标签, 控件类型, 选项)。</summary>
    internal static class ActionParamSchema
    {
        internal static List<(string key, string label, string kind, string[]? opts)> Get(ActionType t) => t switch
        {
            ActionType.tag_write => new() { ("tag_name", "变量", "tag", null), ("value", "值", "string", null) },
            ActionType.tag_add => new() { ("tag_name", "变量(数值)", "tag-numeric", null), ("delta", "增量", "string", null) },
            ActionType.tag_subtract => new() { ("tag_name", "变量(数值)", "tag-numeric", null), ("delta", "减量", "string", null) },
            ActionType.tag_toggle => new() { ("tag_name", "变量(BOOL)", "tag-bool", null) },
            ActionType.set_bit => new() { ("tag_name", "变量(BOOL)", "tag-bool", null) },
            ActionType.reset_bit => new() { ("tag_name", "变量(BOOL)", "tag-bool", null) },
            ActionType.screen_switch => new() { ("target_screen", "目标画面", "screen", null) },
            ActionType.screen_prev => new(),
            ActionType.screen_next => new(),
            ActionType.show_popup => new() { ("content", "内容/画面", "string", null) },
            ActionType.set_property => new() { ("widget_name", "控件", "widget", null), ("key", "属性", "enum", SetPropertyKeys), ("value", "值", "string", null) },
            ActionType.send_notification => new() { ("topic", "主题", "string", null), ("message", "消息", "string", null) },
            ActionType.set_datetime => new() { ("datetime", "日期时间(格式 yyyy-MM-dd HH:mm:ss)", "string", null) },
            ActionType.get_datetime => new(),
            ActionType.acknowledge_alarm => new() { ("alarm_name", "报警", "alarm", null) },
                ActionType.set_system_time => new() { ("datetime", "日期时间(格式 yyyy-MM-dd HH:mm:ss)", "string", null) },
            _ => new(),
        };

        internal static readonly string[] SetPropertyKeys = {
            "text", "x", "y", "width", "height", "visible", "enabled", "backColor", "foreColor", "borderColor",
            "borderWidth", "fontSize", "imagePath", "listRef", "defaultIndex", "base_value", "hAlign", "vAlign",
            "minValue", "maxValue", "stepValue", "unit", "inputType", "switchOnColor", "switchOffColor", "frameIndex", "repeat"
        };
    }
}
