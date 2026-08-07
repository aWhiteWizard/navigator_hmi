using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.AiAgent;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;

namespace NavigatorHMI.ViewModels
{
    public class EditWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private HMIProject _currentProject;
        public HMIProject CurrentProject
        {
            get => _currentProject;
            set { _currentProject = value; OnPropertyChanged(); }
        }

        /// <summary>CommandService 实例，GUI/CLI/AI 统一入口。</summary>
        public CommandService CommandService { get; }

        public int DeviceHeight => _currentProject.DeviceHeight;
        public int DeviceWidth => _currentProject.DeviceWidth;

        /// <summary>当前工程 undo 版本号（单调递增，画布微移快照会话判断用；对跨屏/裁剪免疫）。</summary>
        public int UndoVersion => _undoManager.Version;

        /// <summary>画布顶部页面标签集合：已打开的画面（Custom/全局/世界地图），打开才显示，可关闭。</summary>
        public ObservableCollection<Screen> OpenScreens { get; } = new();

        /// <summary>把画面加入打开集合（打开/切换画面时调用）。</summary>
        private void EnsureScreenOpen(Screen screen)
        {
            if (screen == null || OpenScreens.Contains(screen)) return;
            OpenScreens.Add(screen);
        }

        /// <summary>关闭页面标签（仅非当前页可关闭；当前页先切换再关）。</summary>
        public void CloseScreenTab(Screen screen)
        {
            if (screen == null || !OpenScreens.Contains(screen)) return;
            if (ReferenceEquals(screen, CurrentScreen))
            {
                // 关闭当前页：先切换到其他已打开页或首个非 Custom 画面，再移除
                var target = OpenScreens.FirstOrDefault(s => !ReferenceEquals(s, screen))
                             ?? CurrentProject.Screens.FirstOrDefault(s => s.Type != ScreenType.Custom)
                             ?? CurrentProject.Screens.FirstOrDefault();
                if (target != null)
                {
                    CurrentScreen = target;
                    OpenScreens.Remove(screen);
                }
                // target 仍为 null（无任何画面）：保持当前，不关闭（防御）
            }
            else
            {
                OpenScreens.Remove(screen);
            }
            OnPropertyChanged(nameof(OpenScreens));
        }

        // ═══════════════════════════════════════════
        // 变量管理器 Tab（画布标签栏，与画面 Tab 并列切换）
        // ═══════════════════════════════════════════

        private bool _variableManagerTabOpen;

        /// <summary>变量管理器 Tab 是否打开（打开才在标签栏显示）。</summary>
        public bool VariableManagerTabOpen
        {
            get => _variableManagerTabOpen;
            set { if (_variableManagerTabOpen != value) { _variableManagerTabOpen = value; OnPropertyChanged(); } }
        }

        private bool _variableManagerActive;

        /// <summary>当前内容是否为变量管理器（true=显示变量管理器，false=显示画布）。</summary>
        public bool VariableManagerActive
        {
            get => _variableManagerActive;
            set { if (_variableManagerActive != value) { _variableManagerActive = value; OnPropertyChanged(); } }
        }

        /// <summary>打开变量管理器：显示 Tab 并切到变量管理器视图（树高亮同步到「变量」节点，与通讯互斥）。</summary>
        public void OpenVariableManager()
        {
            VariableManagerTabOpen = true;
            VariableManagerActive = true;
            CommunicationActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>激活变量管理器视图（Tab 已打开时点击标签栏）。</summary>
        public void ActivateVariableManager()
        {
            if (!VariableManagerTabOpen) VariableManagerTabOpen = true;
            VariableManagerActive = true;
            CommunicationActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>关闭变量管理器 Tab（若当前激活则切回当前画面，树高亮恢复画面节点）。</summary>
        public void CloseVariableManagerTab()
        {
            if (VariableManagerActive) VariableManagerActive = false;
            VariableManagerTabOpen = false;
            RefreshTreeCurrentStatus();
        }

        // ═══════════════════════════════════════════
        // 通讯配置 Tab（与变量管理器/画面互斥切换）
        // ═══════════════════════════════════════════

        private bool _communicationTabOpen;

        /// <summary>通讯配置 Tab 是否打开（打开才在标签栏显示）。</summary>
        public bool CommunicationTabOpen
        {
            get => _communicationTabOpen;
            set { if (_communicationTabOpen != value) { _communicationTabOpen = value; OnPropertyChanged(); } }
        }

        private bool _communicationActive;

        /// <summary>当前内容是否为通讯配置（true=显示通讯表格，false=其他）。</summary>
        public bool CommunicationActive
        {
            get => _communicationActive;
            set { if (_communicationActive != value) { _communicationActive = value; OnPropertyChanged(); } }
        }

        /// <summary>打开通讯配置：显示 Tab 并切到通讯视图（树高亮同步到「通讯」节点，与变量管理器互斥）。</summary>
        public void OpenCommunication()
        {
            CommunicationTabOpen = true;
            CommunicationActive = true;
            VariableManagerActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>激活通讯配置视图（Tab 已打开时点击标签栏）。</summary>
        public void ActivateCommunication()
        {
            if (!CommunicationTabOpen) CommunicationTabOpen = true;
            CommunicationActive = true;
            VariableManagerActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>关闭通讯配置 Tab（若当前激活则切回当前画面，树高亮恢复画面节点）。</summary>
        public void CloseCommunicationTab()
        {
            if (CommunicationActive) CommunicationActive = false;
            CommunicationTabOpen = false;
            RefreshTreeCurrentStatus();
        }

        // ═══════════════════════════════════════════
        // 列表管理 Tab（文本列表/图片列表，与变量/通讯/画面互斥切换）
        // ═══════════════════════════════════════════

        private bool _listManagerTabOpen;

        /// <summary>列表管理 Tab 是否打开（打开才在标签栏显示）。</summary>
        public bool ListManagerTabOpen
        {
            get => _listManagerTabOpen;
            set { if (_listManagerTabOpen != value) { _listManagerTabOpen = value; OnPropertyChanged(); } }
        }

        private bool _listManagerActive;

        /// <summary>当前内容是否为列表管理（true=显示列表管理，false=其他）。</summary>
        public bool ListManagerActive
        {
            get => _listManagerActive;
            set { if (_listManagerActive != value) { _listManagerActive = value; OnPropertyChanged(); } }
        }

        private ListType _listManagerListType = ListType.Text;

        /// <summary>列表管理当前激活子页（Text=文本列表页，Image=图片列表页）。</summary>
        public ListType ListManagerListType
        {
            get => _listManagerListType;
            set { if (_listManagerListType != value) { _listManagerListType = value; OnPropertyChanged(); } }
        }

        private int _listManagerSelectedIndex = 0;

        /// <summary>列表管理 TabControl 页索引（0=文本列表，1=图片列表）。与 ListManagerListType 单向同步：
        /// 树双击/激活设置索引 → TabControl 跟随；用户手动切页回写 → 树高亮同步。</summary>
        public int ListManagerSelectedIndex
        {
            get => _listManagerSelectedIndex;
            set
            {
                if (_listManagerSelectedIndex == value) return;
                _listManagerSelectedIndex = value;
                OnPropertyChanged();
                var type = value == 1 ? ListType.Image : ListType.Text;
                if (_listManagerListType != type)
                {
                    _listManagerListType = type;
                    OnPropertyChanged(nameof(ListManagerListType));
                }
                RefreshTreeCurrentStatus();
            }
        }

        /// <summary>打开列表管理：显示 Tab 并切到对应子页（树高亮同步到「文本/图片列表」节点，与变量/通讯互斥）。</summary>
        public void OpenListManager(ListType listType)
        {
            ListManagerTabOpen = true;
            ListManagerActive = true;
            ListManagerListType = listType;
            ListManagerSelectedIndex = listType == ListType.Image ? 1 : 0;
            VariableManagerActive = false;
            CommunicationActive = false;
            AlarmActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>激活列表管理视图（Tab 已打开时点击标签栏切换子页）。</summary>
        public void ActivateListManager(ListType listType)
        {
            if (!ListManagerTabOpen) ListManagerTabOpen = true;
            ListManagerActive = true;
            ListManagerListType = listType;
            ListManagerSelectedIndex = listType == ListType.Image ? 1 : 0;
            VariableManagerActive = false;
            CommunicationActive = false;
            AlarmActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>关闭列表管理 Tab（若当前激活则切回当前画面，树高亮恢复画面节点）。</summary>
        public void CloseListManagerTab()
        {
            if (ListManagerActive) ListManagerActive = false;
            ListManagerTabOpen = false;
            RefreshTreeCurrentStatus();
        }

        // ═══════════════════════════════════════════
        // 报警配置 Tab（与变量/通讯/列表/画面互斥切换）
        // ═══════════════════════════════════════════

        private bool _alarmTabOpen;

        /// <summary>报警配置 Tab 是否打开（打开才在标签栏显示）。</summary>
        public bool AlarmTabOpen
        {
            get => _alarmTabOpen;
            set { if (_alarmTabOpen != value) { _alarmTabOpen = value; OnPropertyChanged(); } }
        }

        private bool _alarmActive;

        /// <summary>当前内容是否为报警配置（true=显示报警表格，false=其他）。</summary>
        public bool AlarmActive
        {
            get => _alarmActive;
            set { if (_alarmActive != value) { _alarmActive = value; OnPropertyChanged(); } }
        }

        /// <summary>打开报警配置：显示 Tab 并切到报警视图（树高亮同步到「报警」节点，与变量/通讯/列表互斥）。</summary>
        public void OpenAlarm()
        {
            AlarmTabOpen = true;
            AlarmActive = true;
            VariableManagerActive = false;
            CommunicationActive = false;
            ListManagerActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>激活报警配置视图（Tab 已打开时点击标签栏）。</summary>
        public void ActivateAlarm()
        {
            if (!AlarmTabOpen) AlarmTabOpen = true;
            AlarmActive = true;
            VariableManagerActive = false;
            CommunicationActive = false;
            ListManagerActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>关闭报警配置 Tab（若当前激活则切回当前画面，树高亮恢复画面节点）。</summary>
        public void CloseAlarmTab()
        {
            if (AlarmActive) AlarmActive = false;
            AlarmTabOpen = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>激活画面：退出变量管理器/通讯/列表管理视图并切换画面（即使 CurrentScreen 未变也生效）。</summary>
        public void ActivateScreen(Screen screen)
        {
            VariableManagerActive = false;
            CommunicationActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            CurrentScreen = screen;   // 可能短路（值未变），短路时树高亮靠下方 RefreshTreeCurrentStatus 兜底
            RefreshTreeCurrentStatus();
        }

        private Screen _currentScreen;
        public Screen CurrentScreen
        {
            get => _currentScreen;
            set
            {
                if (_currentScreen == value) return;

                var oldScreen = _currentScreen;
                _currentScreen = value;
                OnPropertyChanged();

                // 切到画面时自动退出变量管理器/通讯/列表管理视图（标签栏高亮同步）
                VariableManagerActive = false;
                CommunicationActive = false;
                ListManagerActive = false;
                AlarmActive = false;

                // 打开画面 → 自动加入标签集合（打开才显示标签）
                EnsureScreenOpen(value);

                // 更新 Screen.IsCurrent（标签栏高亮）
                if (oldScreen != null) oldScreen.IsCurrent = false;
                if (value != null) value.IsCurrent = true;

                // 🆕 更新树节点的 IsCurrent 状态（含变量管理器激活时的「变量」节点高亮）
                RefreshTreeCurrentStatus();

                // 通知画布重新加载
                OnPropertyChanged(nameof(CurrentScreen.Widgets));
                CanvasReloadRequested?.Invoke(value);
            }
        }

        /// <summary>按当前激活状态重刷树节点高亮（切换画面/打开关闭工具 Tab 后调用）。</summary>
        private void RefreshTreeCurrentStatus()
        {
            foreach (var root in TreeRoots)
            {
                UpdateNodeRecursive(root, CurrentScreen, VariableManagerActive, CommunicationActive, ListManagerActive, AlarmActive, ListManagerListType);
            }
        }

        private static void UpdateNodeRecursive(ProjectTreeViewModel node, Screen currentScreen, bool variableManagerActive, bool communicationActive, bool listManagerActive, bool alarmActive, ListType listManagerListType)
        {
            if (node is ScreenItemNode screenNode)
            {
                screenNode.IsCurrent = !variableManagerActive && !communicationActive && !listManagerActive && !alarmActive && (screenNode.Screen == currentScreen);
            }
            else if (node is VariableManagerNode vmNode)
            {
                vmNode.IsCurrent = variableManagerActive;
            }
            else if (node is DeviceConfigNode deviceNode)
            {
                deviceNode.IsCurrent = communicationActive;
            }
            else if (node is AlarmConfigNode alarmNode)
            {
                alarmNode.IsCurrent = alarmActive;
            }
            else if (node is TextListRootNode tlNode)
            {
                tlNode.IsCurrent = listManagerActive && listManagerListType == ListType.Text;
            }
            else if (node is ImageListRootNode ilNode)
            {
                ilNode.IsCurrent = listManagerActive && listManagerListType == ListType.Image;
            }
            foreach (var child in node.Children)
            {
                UpdateNodeRecursive(child, currentScreen, variableManagerActive, communicationActive, listManagerActive, alarmActive, listManagerListType);
            }
        }

        public event Action<Screen> CanvasReloadRequested;
        // 新增：同一画面内数据变化时（如添加/删除控件）触发刷新
        public event Action RefreshCanvasRequested;

        /// <summary>批量命令抑制中间全量重建（批量拖拽等场景：调用方循环后统一刷新一次，防 N 次重建卡顿）。</summary>
        public bool SuppressRefresh { get; set; }

        /// <summary>CommandService 执行命令成功后，智能刷新 UI。</summary>
        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // 后台线程（AI Agent Task.Run）触发时跨线程刷新会崩并被事件总线 try/catch 静默吞掉 → 封送回 UI 线程
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => OnCommandExecuted(cmdName, parameters, result)));
                return;
            }
            // bind_tag 只影响运行时绑定，设计态无视觉/树/标签变化：跳过全量重建（防属性面板选中丢失），仅标脏
            if (cmdName == "bind_tag")
            {
                ProjectDirtyRequested?.Invoke();
                return;
            }
            // 批量抑制（如批量拖拽生成多控件）：跳过重建只标脏，调用方结束统一刷新一次
            if (SuppressRefresh)
            {
                ProjectDirtyRequested?.Invoke();
                return;
            }

            // 重建项目树
            RebuildProjectTree();
            // 确保自定义画面列表展开
            if (TreeRoots.Count >= 3 && TreeRoots[2] is CustomScreensRootNode customRoot)
                customRoot.IsExpanded = true;
            // 树重建后恢复当前画面高亮（新节点 IsCurrent 默认 false）；变量管理器激活时「变量」节点高亮不丢
            RefreshTreeCurrentStatus();
            // 刷新画布
            RefreshCanvasRequested?.Invoke();
            OnPropertyChanged(nameof(CurrentScreen));
            OnPropertyChanged(nameof(CurrentScreen.Widgets));
            OnPropertyChanged(nameof(OpenScreens));   // 画面增删后刷新页面标签
            // 标记工程已修改
            ProjectDirtyRequested?.Invoke();

            // 智能跳转：create_screen → 自动切换到新画面
            if (cmdName == "create_screen" && parameters.TryGetValue("name", out var nameObj))
            {
                var screen = CurrentProject.Screens.FirstOrDefault(s => s.Name == nameObj?.ToString());
                if (screen != null) CurrentScreen = screen;
            }
            // delete_screen/paste_screen：当前画面被删时切换，避免悬空引用（幽灵画面可编辑但保存丢失）
            if (cmdName is "delete_screen" or "paste_screen")
            {
                if (CurrentScreen != null && !CurrentProject.Screens.Contains(CurrentScreen))
                {
                    CurrentScreen = CurrentProject.Screens.FirstOrDefault(s => s.Type != ScreenType.Custom);
                    // CurrentScreen setter 内部已触发 CanvasReloadRequested，无需重复
                }
                if (cmdName == "delete_screen" && parameters.TryGetValue("name", out var dn))
                {
                    _undoManager.ClearByName(dn?.ToString() ?? "");   // 删除后清理该画面 undo 栈
                    // 删除画面 → 移除其标签（按名称匹配）
                    var removed = OpenScreens.FirstOrDefault(s => s.Name == dn?.ToString());
                    if (removed != null) OpenScreens.Remove(removed);
                }
            }
        }

        /// <summary>重建项目树节点（新建/删除画面后调用）。</summary>
        private void RebuildProjectTree()
        {
            TreeRoots.Clear();
            var globalNode = new ScreenItemNode(CurrentProject.Screens.First(s => s.Type == ScreenType.Template), CurrentProject);
            globalNode.OnSelected += s => ActivateScreen(s);
            var mapNode = new ScreenItemNode(CurrentProject.Screens.First(s => s.Type == ScreenType.WorldMap), CurrentProject);
            mapNode.OnSelected += s => ActivateScreen(s);
            var customRoot = new CustomScreensRootNode(CurrentProject);
            customRoot.OnScreenSelected += s => ActivateScreen(s);
            customRoot.OnScreenDeleted += (deletedScreen) =>
            {
                OpenScreens.Remove(deletedScreen);   // 删除画面 → 移除其标签
                if (CurrentScreen == deletedScreen)
                    CurrentScreen = CurrentProject.Screens.FirstOrDefault(s => s.Type != ScreenType.Custom);
                CanvasReloadRequested?.Invoke(CurrentScreen);
                OnPropertyChanged(nameof(OpenScreens));   // 删除画面后刷新页面标签
            };
            customRoot.OnScreenUndoCleared += name => _undoManager.ClearByName(name);   // 清理被删画面的 undo/redo 栈
            TreeRoots.Add(globalNode);
            TreeRoots.Add(mapNode);
            TreeRoots.Add(customRoot);
            TreeRoots.Add(BuildCommunicationRootNode());
            TreeRoots.Add(BuildListRootNode());
        }

        /// <summary>构建「通信变量」根节点（含「变量」/「通讯」子节点，双击在画布位置打开对应 Tab）。</summary>
        private CommunicationRootNode BuildCommunicationRootNode()
        {
            var node = new CommunicationRootNode();
            node.OnVariableManagerSelected += OpenVariableManager;
            node.OnDeviceConfigSelected += OpenCommunication;
            node.OnAlarmConfigSelected += OpenAlarm;
            return node;
        }

        /// <summary>构建「列表」根节点（与「通信变量」平级，含「文本列表」/「图片列表」子节点，双击打开列表管理对应页）。</summary>
        private ListRootNode BuildListRootNode()
        {
            var node = new ListRootNode();
            node.OnTextListSelected += () => OpenListManager(ListType.Text);
            node.OnImageListSelected += () => OpenListManager(ListType.Image);
            return node;
        }
        // 撤销操作执行后触发，用于通知 View 层标记工程已修改
        public event Action? ProjectDirtyRequested;
        // 当控件列表发生变化时调用这个方法
        public void NotifyCanvasRefreshNeeded()
        {
            RefreshCanvasRequested?.Invoke();
        }

        public ObservableCollection<ProjectTreeViewModel> TreeRoots { get; } = new ObservableCollection<ProjectTreeViewModel>();

        private readonly UndoManager _undoManager = new();
        public ICommand UndoCommand { get; private set; }

        /// <summary>重做命令。</summary>
        public ICommand RedoCommand { get; private set; }

        /// <summary>
        /// 在执行修改操作之前保存当前画面的 Undo 快照。
        /// </summary>
        public void PushUndoSnapshot()
        {
            if (CurrentScreen != null)
            {
                _undoManager.PushSnapshot(CurrentScreen);
            }
        }

        /// <summary>弹出最近一个撤销快照（命令失败时调用——防空快照污染撤销栈）。</summary>
        public void PopUndoSnapshot()
        {
            if (CurrentScreen != null)
                _undoManager.PopLastSnapshot(CurrentScreen);
        }

        public EditWindowViewModel(HMIProject project)
        {
            CurrentProject = project;
            CommandService = new CommandService(project);
            CommandService.CommandExecuted += OnCommandExecuted;
            // 构建树根：全局画面、地图画面、自定义画面列表根
            // 注意：树节点选中一律走 ActivateScreen（当前画面未变时也能退出变量管理器视图）
            var globalNode = new ScreenItemNode(project.Screens.First(s => s.Type == ScreenType.Template), project);
            globalNode.OnSelected += s => ActivateScreen(s);
            var mapNode = new ScreenItemNode(project.Screens.First(s => s.Type == ScreenType.WorldMap), project);
            mapNode.OnSelected += s => ActivateScreen(s);
            var customRoot = new CustomScreensRootNode(project);
            customRoot.OnScreenSelected += s => ActivateScreen(s);
            customRoot.OnScreenDeleted += (deletedScreen) =>
            {
                OpenScreens.Remove(deletedScreen);   // 删除画面 → 移除其标签
                if (CurrentScreen == deletedScreen)
                {
                    // 切换到其他可用画面（比如全局画面或地图画面）
                    CurrentScreen = project.Screens.FirstOrDefault(s => s.Type != ScreenType.Custom);
                    // 触发画布刷新
                    CanvasReloadRequested?.Invoke(CurrentScreen);
                }
                OnPropertyChanged(nameof(OpenScreens));   // 删除画面后刷新页面标签
            };
            customRoot.OnScreenUndoCleared += name => _undoManager.ClearByName(name);   // 清理被删画面的 undo/redo 栈

            TreeRoots.Add(globalNode);
            TreeRoots.Add(mapNode);
            TreeRoots.Add(customRoot);
            TreeRoots.Add(BuildCommunicationRootNode());
            TreeRoots.Add(BuildListRootNode());

            // 默认选中全局画面
            CurrentScreen = project.Screens.First(s => s.Type == ScreenType.WorldMap);

            UndoCommand = new RelayCommand(
                () =>
                {
                    if (CurrentScreen == null) return;
                    var restored = _undoManager.Undo(CurrentScreen);
                    if (restored != null)
                    {
                        CurrentScreen.Widgets.Clear();
                        foreach (var w in restored)
                            CurrentScreen.Widgets.Add(w);
                        RefreshCanvasRequested?.Invoke();
                        ProjectDirtyRequested?.Invoke();
                    }
                });

            RedoCommand = new RelayCommand(
                () =>
                {
                    if (CurrentScreen == null) return;
                    var restored = _undoManager.Redo(CurrentScreen);
                    if (restored != null)
                    {
                        CurrentScreen.Widgets.Clear();
                        foreach (var w in restored)
                            CurrentScreen.Widgets.Add(w);
                        RefreshCanvasRequested?.Invoke();
                        ProjectDirtyRequested?.Invoke();
                    }
                });

            // ── AI 侧边栏（DeepSeek 语义 Agent：多轮会话 + 模型/推理深度/上下文长度可切换 + 新建会话）──
            LoadAiConfig();   // 恢复上次的模型/深度/上下文/Key/模型路径（用户级配置）
            ToggleAiPanelCommand = new RelayCommand(() => IsAiPanelOpen = !IsAiPanelOpen);
            AiNewSessionCommand = new RelayCommand(() =>
            {
                _aiAgent?.Reset();   // 清历史保 system 规则
                AiMessages.Clear();
                AiMessages.Add(new AiChatEntry("ai", "新会话已开始。可以描述操作（如「创建一个画面叫温度监控」）、提问或闲聊。"));
            });
            AiSendCommand = new RelayCommand(async () =>
            {
                var text = AiInput?.Trim() ?? "";
                if (text.Length == 0 || IsAiThinking) return;
                AiInput = "";
                AiMessages.Add(new AiChatEntry("user", text));
                AiMessages.Add(new AiChatEntry("status", "思考中…"));
                IsAiThinking = true;
                try
                {
                    EnsureAiAgent();
                    if (_aiAgent == null)
                        throw new InvalidOperationException("本地模型加载中，请稍候再发送…");
                    var agent = _aiAgent;   // 捕获引用：Task.Run 执行时不再重读（防切换设置竞态 NRE）
                    // 网络推理放后台线程；await 后经 WPF SynchronizationContext 回 UI 线程更新消息
                    var result = await Task.Run(() => agent!.ChatAsync(text));
                    ReplaceThinking("ai", result);
                }
                catch (Exception ex)
                {
                    ReplaceThinking("status", ex.Message);
                }
                finally { IsAiThinking = false; }
            });
        }

        // ── AI 会话设置（Copilot 风格：模型 / 推理深度 / 上下文长度）──

        /// <summary>AI 选项（下拉项：Key 为内部值，Label 为显示名）。</summary>
        public record AiOption(string Key, string Label);

        /// <summary>可选模型。deepseek-reasoner 不支持函数调用（FC 语义 Agent 必需），启用无工具纯对话模式；local 为本地 Qwen GGUF（需配置模型路径）。</summary>
        public IReadOnlyList<AiOption> AiModels { get; } = new[]
        {
            new AiOption("deepseek-chat", "DeepSeek Chat（函数调用）"),
            new AiOption("deepseek-reasoner", "DeepSeek Reasoner（深度思考，无工具）"),
            new AiOption("local", "本地模型（Qwen2.5-7B GGUF）"),
        };

        /// <summary>推理深度：映射单次指令的最大模型调用轮次（快速 2 / 均衡 4 / 深度 8）。</summary>
        public IReadOnlyList<AiOption> AiDepths { get; } = new[]
        {
            new AiOption("fast", "快速"),
            new AiOption("balanced", "均衡"),
            new AiOption("deep", "深度"),
        };

        /// <summary>上下文长度：映射会话历史保留轮数（短 4 / 中 10 / 长 20）。</summary>
        public IReadOnlyList<AiOption> AiContexts { get; } = new[]
        {
            new AiOption("short", "短（4 轮）"),
            new AiOption("medium", "中（10 轮）"),
            new AiOption("long", "长（20 轮）"),
        };

        private AiOption _selectedAiModel = new("deepseek-chat", "DeepSeek Chat（函数调用）");
        /// <summary>当前模型。切换时重建 Agent（后端按模型重建；新会话生效）。</summary>
        public AiOption SelectedAiModel
        {
            get => _selectedAiModel;
            set
            {
                if (_selectedAiModel != value)
                {
                    _selectedAiModel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsLocalAiModel));
                    OnPropertyChanged(nameof(IsCloudAiModel));
                    SaveAiConfig();
                    RebuildAiAgent();
                }
            }
        }

        /// <summary>当前是否本地模型（显示本地模型路径配置行）。</summary>
        public bool IsLocalAiModel => SelectedAiModel.Key == "local";
        /// <summary>当前是否云端模型（显示 API Key 输入行）。</summary>
        public bool IsCloudAiModel => SelectedAiModel.Key != "local";

        private AiOption _selectedAiDepth = new("balanced", "均衡");
        /// <summary>当前推理深度（MaxIterations）。切换时重建 Agent。</summary>
        public AiOption SelectedAiDepth
        {
            get => _selectedAiDepth;
            set { if (_selectedAiDepth != value) { _selectedAiDepth = value; OnPropertyChanged(); SaveAiConfig(); RebuildAiAgent(); } }
        }

        private AiOption _selectedAiContext = new("medium", "中（10 轮）");
        /// <summary>当前上下文长度（MaxHistoryTurns）。切换时重建 Agent。</summary>
        public AiOption SelectedAiContext
        {
            get => _selectedAiContext;
            set { if (_selectedAiContext != value) { _selectedAiContext = value; OnPropertyChanged(); SaveAiConfig(); RebuildAiAgent(); } }
        }

        private string _aiApiKey = "";
        /// <summary>自定义云端 API Key（优先于环境变量 DEEPSEEK_API；保存到用户级配置，不随工程分发）。</summary>
        public string AiApiKey
        {
            get => _aiApiKey;
            set { if (_aiApiKey != value) { _aiApiKey = value ?? ""; OnPropertyChanged(); SaveAiConfig(); RebuildAiAgent(); } }
        }

        private string _aiLocalModelPath = "";
        /// <summary>本地模型 GGUF 路径（空 = 自动查找 models/ 目录）。</summary>
        public string AiLocalModelPath
        {
            get => _aiLocalModelPath;
            set { if (_aiLocalModelPath != value) { _aiLocalModelPath = value ?? ""; OnPropertyChanged(); SaveAiConfig(); } }
        }

        private void LoadAiConfig()
        {
            var cfg = AiConfigStore.Load();
            _aiApiKey = cfg.ApiKey;
            _aiLocalModelPath = cfg.LocalModelPath;
            if (!string.IsNullOrEmpty(cfg.Model))
                _selectedAiModel = AiModels.FirstOrDefault(o => o.Key == cfg.Model) ?? _selectedAiModel;
            if (!string.IsNullOrEmpty(cfg.Depth))
                _selectedAiDepth = AiDepths.FirstOrDefault(o => o.Key == cfg.Depth) ?? _selectedAiDepth;
            if (!string.IsNullOrEmpty(cfg.Context))
                _selectedAiContext = AiContexts.FirstOrDefault(o => o.Key == cfg.Context) ?? _selectedAiContext;
        }

        private void SaveAiConfig()
        {
            AiConfigStore.Save(new AiConfigStore.AiConfig
            {
                ApiKey = _aiApiKey,
                LocalModelPath = _aiLocalModelPath,
                Model = _selectedAiModel.Key,
                Depth = _selectedAiDepth.Key,
                Context = _selectedAiContext.Key,
            });
        }

        /// <summary>设置窗口保存后重读 API Key（AiConfigStore 已更新）并重建 Agent。</summary>
        public void ReloadAiKeyFromStore()
        {
            _aiApiKey = AiConfigStore.Load().ApiKey;
            OnPropertyChanged(nameof(AiApiKey));
            RebuildAiAgent();
        }

        /// <summary>AI 侧边栏消息（Role: user/ai/status）。</summary>
        public ObservableCollection<AiChatEntry> AiMessages { get; } = new();

        private string _aiInput = "";
        /// <summary>AI 输入框内容。</summary>
        public string AiInput { get => _aiInput; set { _aiInput = value; OnPropertyChanged(); } }

        private bool _isAiPanelOpen;
        /// <summary>AI 侧边栏是否展开。</summary>
        public bool IsAiPanelOpen { get => _isAiPanelOpen; set { _isAiPanelOpen = value; OnPropertyChanged(); } }

        private bool _isAiThinking;
        /// <summary>AI 是否正在推理（发送中禁用输入/按钮）。</summary>
        public bool IsAiThinking { get => _isAiThinking; set { _isAiThinking = value; OnPropertyChanged(); } }

        /// <summary>展开/收起 AI 侧边栏。</summary>
        public ICommand ToggleAiPanelCommand { get; private set; } = null!;
        /// <summary>发送自然语言指令给 AI（DeepSeek 语义 Agent，多轮会话）。</summary>
        public ICommand AiSendCommand { get; private set; } = null!;
        /// <summary>新建会话：清空历史（Agent Reset + 消息列表清空 + 欢迎提示）。</summary>
        public ICommand AiNewSessionCommand { get; private set; } = null!;

        private AIAgent? _aiAgent;
        private ILLMBackend? _aiBackend;
        private bool _aiLocalLoading;   // 本地模型异步加载中（防重复触发）
        private bool _disposed;         // 已释放（异步回调校验用，防关窗后赋值 4.4GB 模型滞留）

        /// <summary>命令执行封送到 UI 线程（AI 后台线程改 ObservableCollection 会触发 WPF CollectionView 跨线程异常）。</summary>
        private static readonly Action<Action> UiCommandDispatcher = action =>
            System.Windows.Application.Current.Dispatcher.Invoke(action);

        /// <summary>按当前设置重建 Agent（模型/深度/上下文切换时调用；旧 Agent 释放，新会话生效）。</summary>
        private void RebuildAiAgent()
        {
            _aiBackend?.Dispose();
            _aiBackend = null;
            _aiAgent = null;
            AiMessages.Add(new AiChatEntry("status", "已切换设置，新会话生效。"));
        }

        /// <summary>供 code-behind（如浏览选择本地模型路径后）触发重建 Agent。</summary>
        public void RebuildAiAgentPublic() => RebuildAiAgent();

        private void EnsureAiAgent()
        {
            if (_aiAgent != null) return;

            if (SelectedAiModel.Key == "local")
            {
                // 本地 Qwen GGUF：路径优先取设置，空则自动查找（仓库 models/ 目录向上搜索）
                var path = string.IsNullOrWhiteSpace(AiLocalModelPath)
                    ? LocalLLMBackend.DefaultModelPath
                    : AiLocalModelPath;
                var localBackend = new LocalLLMBackend(path);
                if (!localBackend.IsAvailable)
                    throw new InvalidOperationException($"本地模型不存在: {path}\n请在设置中配置 GGUF 路径（或放到仓库 models/ 目录）");
                if (_aiLocalLoading)
                    throw new InvalidOperationException("本地模型正在加载，请稍候…");
                _aiLocalLoading = true;
                AiMessages.Add(new AiChatEntry("status", "本地模型加载中…"));
                // 4.4GB 模型加载放后台线程（UI 不冻结）；完成后经 Dispatcher 回 UI 线程赋值
                Task.Run(() =>
                {
                    try
                    {
                        var ok = localBackend.Load();
                        var loadError = localBackend.LoadError;
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            // 校验配置快照仍有效（防加载期间切换模型/关窗的竞态）：模型仍是 local 且未被重建/释放才赋值
                            if (_disposed || SelectedAiModel.Key != "local" || _aiAgent != null)
                            {
                                localBackend.Dispose();
                                _aiLocalLoading = false;
                                if (_disposed) return;
                                AiMessages.Add(new AiChatEntry("status", "本地模型加载已取消（设置已变更）。"));
                                return;
                            }
                            _aiLocalLoading = false;
                            if (!ok)
                            {
                                AiMessages.Add(new AiChatEntry("status", $"本地模型加载失败: {loadError}"));
                                return;
                            }
                            _aiBackend = localBackend;
                            _aiAgent = new AIAgent(CommandService, localBackend, enableTools: true, UiCommandDispatcher)
                            {
                                MaxIterations = SelectedAiDepth.Key switch { "fast" => 4, "deep" => 16, _ => 8 },
                                MaxHistoryTurns = SelectedAiContext.Key switch { "short" => 4, "long" => 20, _ => 10 },
                            };
                            AiMessages.Add(new AiChatEntry("status", "本地模型就绪。"));
                        });
                    }
                    catch
                    {
                        // 后台异常兜底：复位标志（防 _aiLocalLoading 永久卡住）
                        System.Windows.Application.Current.Dispatcher.Invoke(() => _aiLocalLoading = false);
                    }
                });
                return;
            }

            // 云端：自定义 Key 优先，否则环境变量
            var enableTools = SelectedAiModel.Key == "deepseek-chat";
            _aiBackend = new CloudLLMBackend(
                apiKey: string.IsNullOrWhiteSpace(AiApiKey) ? null : AiApiKey,
                model: SelectedAiModel.Key);
            if (!_aiBackend.IsAvailable)
            {
                _aiBackend.Dispose();   // 释放 HttpClient，防重复重试泄漏
                _aiBackend = null;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(AiApiKey)
                        ? "未配置云端 API Key：请在设置中填写，或设置环境变量 NAVIGATOR_HMI_AI_KEY / DEEPSEEK_API"
                        : "云端 API 不可用：请检查 API Key 与网络");
            }
            _aiAgent = new AIAgent(CommandService, _aiBackend, enableTools, UiCommandDispatcher)
            {
                MaxIterations = SelectedAiDepth.Key switch { "fast" => 4, "deep" => 16, _ => 8 },
                MaxHistoryTurns = SelectedAiContext.Key switch { "short" => 4, "long" => 20, _ => 10 },
            };
            if (!enableTools)
                AiMessages.Add(new AiChatEntry("status", "深度思考模式不支持函数调用：命令类指令不会执行，仅对话/分析。"));
        }

        /// <summary>把末尾的"思考中"条目替换为最终结果（成功=ai 角色，失败=status 角色）。</summary>
        private void ReplaceThinking(string role, string content)
        {
            if (AiMessages.Count > 0 && AiMessages[^1].Role == "status")
                AiMessages[^1] = new AiChatEntry(role, content);
            else
                AiMessages.Add(new AiChatEntry(role, content));
        }

        /// <summary>释放 AI 后端（CloudLLMBackend 持有 HttpClient/Authorization）。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _aiBackend?.Dispose();
            _aiBackend = null;
            _aiAgent = null;
            GC.SuppressFinalize(this);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


}
