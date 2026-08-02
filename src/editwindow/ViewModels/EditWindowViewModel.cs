using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;

namespace NavigatorHMI.ViewModels
{
    public class EditWindowViewModel : INotifyPropertyChanged
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

        /// <summary>打开变量管理器：显示 Tab 并切到变量管理器视图。</summary>
        public void OpenVariableManager()
        {
            VariableManagerTabOpen = true;
            VariableManagerActive = true;
        }

        /// <summary>激活变量管理器视图（Tab 已打开时点击标签栏）。</summary>
        public void ActivateVariableManager()
        {
            if (!VariableManagerTabOpen) VariableManagerTabOpen = true;
            VariableManagerActive = true;
        }

        /// <summary>关闭变量管理器 Tab（若当前激活则切回当前画面）。</summary>
        public void CloseVariableManagerTab()
        {
            if (VariableManagerActive) VariableManagerActive = false;
            VariableManagerTabOpen = false;
        }

        /// <summary>激活画面：退出变量管理器视图并切换画面（即使 CurrentScreen 未变也生效）。</summary>
        public void ActivateScreen(Screen screen)
        {
            VariableManagerActive = false;
            CurrentScreen = screen;
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

                // 切到画面时自动退出变量管理器视图（标签栏高亮同步）
                VariableManagerActive = false;

                // 打开画面 → 自动加入标签集合（打开才显示标签）
                EnsureScreenOpen(value);

                // 更新 Screen.IsCurrent（标签栏高亮）
                if (oldScreen != null) oldScreen.IsCurrent = false;
                if (value != null) value.IsCurrent = true;

                // 🆕 更新树节点的 IsCurrent 状态
                UpdateTreeNodeCurrentStatus(oldScreen, value);

                // 通知画布重新加载
                OnPropertyChanged(nameof(CurrentScreen.Widgets));
                CanvasReloadRequested?.Invoke(value);
            }
        }

        /// <summary>
        /// 更新树节点中画面的当前状态标记。
        /// 切换画面时，旧画面取消标记，新画面打上标记。
        /// </summary>
        private void UpdateTreeNodeCurrentStatus(Screen oldScreen, Screen newScreen)
        {
            foreach (var root in TreeRoots)
            {
                UpdateNodeRecursive(root, newScreen);
            }
        }

        private static void UpdateNodeRecursive(ProjectTreeViewModel node, Screen currentScreen)
        {
            if (node is ScreenItemNode screenNode)
            {
                screenNode.IsCurrent = (screenNode.Screen == currentScreen);
            }
            foreach (var child in node.Children)
            {
                UpdateNodeRecursive(child, currentScreen);
            }
        }

        public event Action<Screen> CanvasReloadRequested;
        // 新增：同一画面内数据变化时（如添加/删除控件）触发刷新
        public event Action RefreshCanvasRequested;

        /// <summary>CommandService 执行命令成功后，智能刷新 UI。</summary>
        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // 重建项目树
            RebuildProjectTree();
            // 确保自定义画面列表展开
            if (TreeRoots.Count >= 3 && TreeRoots[2] is CustomScreensRootNode customRoot)
                customRoot.IsExpanded = true;
            // 树重建后恢复当前画面高亮（新节点 IsCurrent 默认 false）
            if (CurrentScreen != null)
                UpdateTreeNodeCurrentStatus(null, CurrentScreen);
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
        }

        /// <summary>构建「通信变量」根节点（含「变量」子节点，点击在画布位置打开变量管理器 Tab）。</summary>
        private CommunicationRootNode BuildCommunicationRootNode()
        {
            var node = new CommunicationRootNode();
            node.OnVariableManagerSelected += OpenVariableManager;
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
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


}
