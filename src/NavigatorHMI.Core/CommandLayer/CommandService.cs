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
        public void ReplaceProject(HMIProject newProject)
        {
            lock (_lock)
            {
                _project = newProject;
                EnsureDefaultGroups(_project);   // P1-10：替换路径（CLI open/SaveAs）也预置三组（幂等）
            }
        }

        /// <summary>P1-10：预置默认用户组（管理员全权/操作员报警确认·画面编辑/访客全禁）。创建工程与打开旧工程统一入口。</summary>
        public static void EnsureDefaultGroups(HMIProject project)
        {
            if (project.Groups.Count > 0) return;
            project.Groups.Add(new UserGroup { Name = "管理员", Permissions = { UserPermission.ScreenEdit, UserPermission.AlarmAck, UserPermission.UserManage, UserPermission.SystemSettings } });
            project.Groups.Add(new UserGroup { Name = "操作员", Permissions = { UserPermission.AlarmAck, UserPermission.ScreenEdit } });
            project.Groups.Add(new UserGroup { Name = "访客" });
        }

        /// <summary>设置设备连接状态（仅供内部 connect 命令执行后调用）。</summary>
        internal void SetConnected(bool connected) { lock (_lock) { IsConnected = connected; } }

        /// <summary>
        /// 初始化 CommandService 并注册全部命令处理器（工程/画面/控件/层级/布局/事件/剪贴板/字体/变量/报警/设备）。
        /// </summary>
        /// <param name="project">当前工程对象</param>
        public CommandService(HMIProject project)
        {
            _project = project;
            EnsureDefaultGroups(_project);   // P1-10：打开旧工程时 Groups 为空自动预置三组（创建工程路径 CreateProjectHandler 已预置，不重复）
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
                ["copy_screen"]     = new CopyScreenHandler(),
                ["paste_screen"]    = new PasteScreenHandler(),
                ["current_screen"]  = new CurrentScreenHandler(),

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
                ["add_event"]       = new AddEventHandler(),
                ["remove_event"]    = new RemoveEventHandler(),
                ["update_event"]    = new UpdateEventHandler(),

                // ── 剪贴板 ──
                ["copy_widget"]     = new CopyWidgetHandler(),
                ["paste_widget"]    = new PasteWidgetHandler(),

                // ── 默认字体 ──
                ["set_default_font"] = new SetDefaultFontHandler(),

                // ── 变量操作 ──
                ["create_tag"]      = new CreateTagHandler(),
                ["update_tag"]      = new UpdateTagHandler(),
                ["delete_tag"]      = new DeleteTagHandler(),
                ["bind_tag"]        = new BindTagHandler(),

                // ── 列表操作 ──
                ["create_list"]     = new CreateListHandler(),
                ["update_list"]     = new UpdateListHandler(),
                ["delete_list"]     = new DeleteListHandler(),

                // ── 报警操作 ──
                ["create_alarm"]    = new CreateAlarmHandler(),
                ["update_alarm"]    = new UpdateAlarmHandler(),
                ["delete_alarm"]    = new DeleteAlarmHandler(),
                // ── 用户系统（W2） ──
                ["create_user"]     = new CreateUserHandler(),
                ["update_user"]     = new UpdateUserHandler(),
                ["delete_user"]     = new DeleteUserHandler(),
                ["list_users"]      = new ListUsersHandler(),
                ["create_group"]    = new CreateGroupHandler(),
                ["update_group"]    = new UpdateGroupHandler(),
                ["delete_group"]    = new DeleteGroupHandler(),

                // ── 设备操作 ──
                ["configure_device"] = new ConfigureDeviceHandler(),
                ["update_device"]    = new UpdateDeviceHandler(),
                ["delete_device"]    = new DeleteDeviceHandler(),
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
            try
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
            if (result.Success) SafeInvokeCommandExecuted(commandName, parameters, result);
            return result;
            }
            catch (Exception ex)
            {
                // 防御：handler/校验/事件订阅者异常不崩溃（AI Agent 直调入口暴露面）；
                // 注：handler 中途抛异常时模型可能半更新——约定 handler 先验证后修改的原子性纪律
                System.Diagnostics.Trace.WriteLine($"[CommandService] {commandName} 异常: {ex}");
                return CommandResult.Fail("COMMAND_CRASH", $"命令 {commandName} 执行异常: {ex.Message}");
            }
            }
        }

        /// <summary>触发 CommandExecuted 事件并隔离订阅者异常（单个订阅者抛错不连累其他/崩溃）。</summary>
        private void SafeInvokeCommandExecuted(string name, Dictionary<string, object?> p, CommandResult r)
        {
            foreach (var d in CommandExecuted?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { ((Action<string, Dictionary<string, object?>, CommandResult>)d)(name, p, r); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[CommandService] CommandExecuted 订阅者异常: {ex}"); }
            }
        }

        /// <summary>
        /// 获取所有已注册命令的定义列表（AI Agent 发现用）。
        /// </summary>
        public List<CommandDefinition> GetAvailableCommands()
            => _handlers.Values.Select(h => h.Definition).ToList();

        public List<string> GetScreenNames()
        {
            lock (_lock) { return _project.Screens.Select(s => s.Name).ToList(); }
        }

        /// <summary>P2-3：当前工程用户组名列表（AI 创建用户时知道合法 group_name）。</summary>
        public List<string> GetGroupNames()
        {
            lock (_lock) { return _project.Groups.Select(g => g.Name).ToList(); }
        }

        /// <summary>B③：当前画面控件清单（ObjectName + 类型名；上限 50 条——AI 操作画面内既有控件时知道名字）。</summary>
        public List<(string Name, string Type)> GetCurrentScreenWidgets()
        {
            lock (_lock)
            {
                var name = _project.CurrentScreenName;
                var screen = _project.Screens.FirstOrDefault(s => s.Name == name);
                if (screen == null) return new List<(string, string)>();
                return screen.Widgets.Take(50).Select(w => (w.ObjectName, w.GetType().Name)).ToList();
            }
        }

        /// <summary>E11 当前画面名：代理到工程运行时字段（GUI 画面切换维护；current_screen 命令读取）。</summary>
        public string? CurrentScreenName
        {
            get { lock (_lock) { return _project.CurrentScreenName; } }
            set { lock (_lock) { _project.CurrentScreenName = value; } }
        }
    }
}
