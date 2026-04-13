using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    public abstract class ProjectTreeViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ObservableCollection<ProjectTreeViewModel> Children { get; } = new ObservableCollection<ProjectTreeViewModel>();
        public ICommand DoubleClickCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ScreenItemNode : ProjectTreeViewModel
    {
        public Screen Screen { get; set; }
        public ScreenItemNode(Screen screen)
        {
            Screen = screen;
            Name = screen.Name;
            DoubleClickCommand = new RelayCommand(() => OnSelected?.Invoke(screen));
        }
        public event Action<Screen> OnSelected;
    }

    public class CustomScreensRootNode : ProjectTreeViewModel
    {
        private HMIProject _project;
        public CustomScreensRootNode(HMIProject project)
        {
            _project = project;
            Name = "自定义画面列表";
            // 添加“添加画面”按钮节点
            Children.Add(new AddScreenNode(this));
            // 加载已有自定义画面
            foreach (var screen in _project.Screens.Where(s => s.Type == ScreenType.Custom))
            {
                AddScreenNodeInternal(screen);
            }
        }

        private void AddScreenNodeInternal(Screen screen)
        {
            var node = new ScreenItemNode(screen);
            node.OnSelected += (s) => OnScreenSelected?.Invoke(s);
            // 插入到“添加画面”节点之前
            Children.Insert(Children.Count - 1, node);
        }

        public void AddNewScreen()
        {
            int nextNum = _project.Screens.Count(s => s.Type == ScreenType.Custom) + 1;
            string newName = $"画面{nextNum}";
            var newScreen = new Screen { Name = newName, Type = ScreenType.Custom, Widgets = new List<Widget>() };
            _project.Screens.Add(newScreen);
            AddScreenNodeInternal(newScreen);
        }

        public event Action<Screen> OnScreenSelected;
    }

    public class AddScreenNode : ProjectTreeViewModel
    {
        private CustomScreensRootNode _parent;
        public AddScreenNode(CustomScreensRootNode parent)
        {
            _parent = parent;
            Name = "➕添加画面";
            DoubleClickCommand = new RelayCommand(() => _parent.AddNewScreen());
        }
    }

}
 