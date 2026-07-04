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

        public int DeviceHeight => _currentProject.DeviceHeight;
        public int DeviceWidth => _currentProject.DeviceWidth;

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
            // 构建树根：全局画面、地图画面、自定义画面列表根
            var globalNode = new ScreenItemNode(project.Screens.First(s => s.Type == ScreenType.Template));
            globalNode.OnSelected += s => CurrentScreen = s;
            var mapNode = new ScreenItemNode(project.Screens.First(s => s.Type == ScreenType.WorldMap));
            mapNode.OnSelected += s => CurrentScreen = s;
            var customRoot = new CustomScreensRootNode(project);
            customRoot.OnScreenSelected += s => CurrentScreen = s;
            customRoot.OnScreenDeleted += (deletedScreen) =>
            {
                if (CurrentScreen == deletedScreen)
                {
                    // 切换到其他可用画面（比如全局画面或地图画面）
                    CurrentScreen = project.Screens.FirstOrDefault(s => s.Type != ScreenType.Custom);
                    // 或者设置为 null，并让画布显示空白
                    // CurrentScreen = null;
                    // 触发画布刷新
                    CanvasReloadRequested?.Invoke(CurrentScreen);
                }
            };

            TreeRoots.Add(globalNode);
            TreeRoots.Add(mapNode);
            TreeRoots.Add(customRoot);

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
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


}
