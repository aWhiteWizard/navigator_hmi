using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
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
                _currentScreen = value;
                OnPropertyChanged();
                // 通知画布重新加载
                CanvasReloadRequested?.Invoke(value);
            }
        }

        public event Action<Screen> CanvasReloadRequested;
        // 新增：同一画面内数据变化时（如添加/删除控件）触发刷新
        public event Action RefreshCanvasRequested;
        // 当控件列表发生变化时调用这个方法
        public void NotifyCanvasRefreshNeeded()
        {
            RefreshCanvasRequested?.Invoke();
        }

        public ObservableCollection<ProjectTreeViewModel> TreeRoots { get; } = new ObservableCollection<ProjectTreeViewModel>();

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
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


}
