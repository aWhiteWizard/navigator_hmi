using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer
{
    /// <summary>
    /// Command Service 实现。注册所有命令处理器并统一调度执行。
    /// 供 GUI ViewModel、CLI 和 AI Agent 统一调用——所有操作走同一入口，无特权路径。
    /// </summary>
    /// <remarks>
    /// 线程安全: lock(_lock) 保证 Execute() 内所有 Handler 调用串行化，ReplaceProject 和 IsConnected 同步。
    /// IDisposable: 当 connect 建立设备连接时需实现 IDisposable 管理生命周期。
    /// </remarks>
    public class CommandService : ICommandService
    {
        private HMIProject _project;
        private readonly Dictionary<string, ICommandHandler> _handlers;
        private readonly object _lock = new();

        /// <summary>命令执行成功后触发（命令名, 参数, 结果）。供 ViewModel 做智能刷新。</summary>
        public event Action<string, Dictionary<string, object?>, CommandResult>? CommandExecuted;

        /// <summary>设备连接状态。仅通过 ConnectHandler 成功执行后设为 true。</summary>
        public bool IsConnected { get; private set; } = false;

        /// <summary>替换当前工程引用（open_project 等命令使用）。</summary>
        public void ReplaceProject(HMIProject newProject) { lock (_lock) { _project = newProject; } }

        /// <summary>设置设备连接状态（仅供内部 connect 命令执行后调用）。</summary>
        internal void SetConnected(bool connected) { lock (_lock) { IsConnected = connected; } }

        /// <summary>
        /// 初始化 CommandService 并注册全部 30 个命令处理器。
        /// </summary>
        /// <param name="project">当前工程对象</param>
        public CommandService(HMIProject project)
        {
            _project = project;
            _handlers = new()
            {
                // ── 工程操作 ──
                ["create_project"]  = new CreateProjectHandler(),
                ["open_project"]    = new OpenProjectHandler(),
                ["save_project"]    = new SaveProjectHandler(),
                ["compile"]         = new CompileHandler(),

                // ── 画面操作 ──
                ["create_screen"]   = new CreateScreenHandler(),
                ["delete_screen"]   = new DeleteScreenHandler(),
                ["rename_screen"]   = new RenameScreenHandler(),

                // ── 控件操作 ──
                ["add_widget"]      = new AddWidgetHandler(),
                ["move_widget"]     = new MoveWidgetHandler(),
                ["resize_widget"]   = new ResizeWidgetHandler(),
                ["delete_widget"]   = new DeleteWidgetHandler(),
                ["set_property"]    = new SetPropertyHandler(),

                // ── 控件层级 ──
                ["bring_to_front"]  = new BringToFrontHandler(),
                ["bring_forward"]   = new BringForwardHandler(),
                ["send_backward"]   = new SendBackwardHandler(),
                ["send_to_back"]    = new SendToBackHandler(),

                // ── 布局 ──
                ["align_widgets"]   = new AlignWidgetsHandler(),
                ["array_layout"]    = new ArrayLayoutHandler(),

                // ── 事件绑定 ──
                ["bind_event"]      = new BindEventHandler(),

                // ── 剪贴板 ──
                ["copy_widget"]     = new CopyWidgetHandler(),
                ["paste_widget"]    = new PasteWidgetHandler(),

                // ── 默认字体 ──
                ["set_default_font"] = new SetDefaultFontHandler(),

                // ── 变量操作 ──
                ["create_tag"]      = new CreateTagHandler(),
                ["bind_tag"]        = new BindTagHandler(),

                // ── 报警操作 ──
                ["create_alarm"]    = new CreateAlarmHandler(),

                // ── 设备操作 ──
                ["configure_device"] = new ConfigureDeviceHandler(),
                ["connect"]          = new ConnectHandler(),
                ["scan_devices"]     = new ScanDevicesHandler(),
                ["deploy_project"]   = new DeployProjectHandler(),
                ["deploy_firmware"]  = new DeployFirmwareHandler(),
            };
        }

        /// <summary>
        /// 执行指定命令。调用链: 查找Handler → Validate → 连接检查 → Execute。
        /// </summary>
        /// <param name="commandName">命令名</param>
        /// <param name="parameters">参数字典</param>
        /// <returns>执行结果</returns>
        public CommandResult Execute(string commandName, Dictionary<string, object?> parameters)
        {
            lock (_lock)
            {
            if (!_handlers.TryGetValue(commandName, out var handler))
                return CommandResult.Fail("UNKNOWN_COMMAND", $"未知命令: {commandName}");

            var validation = handler.Validate(parameters);
            if (!validation.IsValid)
                return CommandResult.Fail("INVALID_PARAM", validation.Error!);

            if (handler.Definition.RequiresConnection && !IsConnected)
                return CommandResult.Fail("NOT_CONNECTED", "请先连接设备 (connect 或 scan)");

            var result = handler.Execute(_project, parameters);
            // 骨架模式：connect 成功后先置连接状态（否则 RequiresConnection 命令永远 NOT_CONNECTED；
            // 且须在 CommandExecuted 事件触发前置位，订阅者刷新连接状态 UI 时读到最新值）
            if (result.Success && commandName == "connect") IsConnected = true;
            if (result.Success) CommandExecuted?.Invoke(commandName, parameters, result);
            return result;
            }
        }

        /// <summary>
        /// 获取所有已注册命令的定义列表（AI Agent 发现用）。
        /// </summary>
        public List<CommandDefinition> GetAvailableCommands()
            => _handlers.Values.Select(h => h.Definition).ToList();
    }
}
