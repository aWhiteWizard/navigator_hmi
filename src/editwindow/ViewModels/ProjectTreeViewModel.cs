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
using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    public abstract class ProjectTreeViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ObservableCollection<ProjectTreeViewModel> Children { get; } = new ObservableCollection<ProjectTreeViewModel>();
        public ICommand DoubleClickCommand { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    OnPropertyChanged(nameof(IsCurrent));
                }
            }
        }
        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                }
            }
        }

        private string _editName;
        public string EditName
        {
            get => _editName;
            set
            {
                if (_editName != value)
                {
                    _editName = value;
                    OnPropertyChanged(nameof(EditName));
                }
            }
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }
        }

        /// <summary>重命名命令：进入编辑模式</summary>
        public ICommand StartRenameCommand { get; set; }

        /// <summary>确认重命名命令：按回车提交</summary>
        public ICommand ConfirmRenameCommand { get; set; }


    }

    public class ScreenItemNode : ProjectTreeViewModel
    {
        public Screen Screen { get; set; }
        public ICommand DeleteCommand { get; }
        private readonly HMIProject _project;

        public ScreenItemNode(Screen screen, HMIProject project, Action<ScreenItemNode> deleteCallback=null)
        {
            Screen = screen;
            _project = project;
            Name = screen.Name;
            // 监听 Screen.Name 变更，同步更新树节点名称
            screen.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Screen.Name))
                {
                    Name = screen.Name;
                    OnPropertyChanged(nameof(Name));
                }
            };
            DoubleClickCommand = new RelayCommand(() => OnSelected?.Invoke(screen));
            // 只有当 deleteCallback 不为 null 且画面不是 Template/WorldMap 时，才启用删除命令
            bool canDelete = deleteCallback != null &&
                             screen.Type != ScreenType.Template &&
                             screen.Type != ScreenType.WorldMap;
            DeleteCommand = new RelayCommand(() => deleteCallback?.Invoke(this), () => canDelete);
            // 进入编辑模式
            StartRenameCommand = new RelayCommand(() =>
            {
                EditName = Name;       // 预填当前名称
                IsEditing = true;
            });

            // 确认重命名（与 CLI rename-screen 同规则：重名校验 + Template/WorldMap 保护）
            ConfirmRenameCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrWhiteSpace(EditName))
                {
                    var newName = EditName.Trim();
                    bool duplicate = _project?.Screens.Any(s => s.Name == newName && !ReferenceEquals(s, Screen)) ?? false;
                    bool protectedScreen = Screen.Type is ScreenType.Template or ScreenType.WorldMap;
                    if (!duplicate && !protectedScreen)
                    {
                        Screen.Name = newName;
                        // 更新节点名称并通知 UI
                        Name = newName;
                        OnPropertyChanged(nameof(Name));
                    }
                    else
                    {
                        // 重名/受保护：回退显示原名
                        EditName = Name;
                    }
                }
                IsEditing = false;
            });
        }
        public event Action<Screen> OnSelected;
    }

    public class CustomScreensRootNode : ProjectTreeViewModel
    {
        private HMIProject _project;

        public event Action<Screen> OnScreenDeleted;  // 通知外部画面被删除
        public event Action<string>? OnScreenUndoCleared;  // 通知外部清理该画面 undo/redo 栈
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
            var node = new ScreenItemNode(screen, _project, DeleteScreenNode);
            node.OnSelected += (s) => OnScreenSelected?.Invoke(s);
            // 插入到“添加画面”节点之前
            Children.Insert(Children.Count - 1, node);
        }

        private void DeleteScreenNode(ScreenItemNode node)
        {
            // 从工程中移除 Screen
            _project.Screens.Remove(node.Screen);
            // 清理该画面的 undo/redo 栈（防内存泄漏）
            OnScreenUndoCleared?.Invoke(node.Screen.Name);
            // 从树中移除节点
            Children.Remove(node);
            // 如果删除的是当前选中的画面，需要通知上层清除 CurrentScreen
            OnScreenDeleted?.Invoke(node.Screen);
        }

        public void AddNewScreen()
        {
            // 🆕 从所有 "画面N" 中提取编号，找最大编号 + 1，避免编号重复
            var existingNums = _project.Screens
                .Where(s => s.Type == ScreenType.Custom)
                .Select(s => {
                    // 尝试从名称 "画面N" 中提取数字 N
                    if (s.Name.StartsWith("画面") && int.TryParse(s.Name.Substring(2), out int n))
                        return n;
                    return 0;
                })
                .Where(n => n > 0)
                .ToList();

            int nextNum = existingNums.Count > 0 ? existingNums.Max() + 1 : 1;
            string newName = $"画面{nextNum}";

            var newScreen = new Screen { Name = newName, Type = ScreenType.Custom, Height = _project.DeviceHeight, Width = _project.DeviceWidth, Widgets = new ObservableCollection<Widget>() };
            _project.Screens.Add(newScreen);
            AddScreenNodeInternal(newScreen);
            // 发送消息：告诉所有订阅者，“新画面已添加”
            WeakReferenceMessenger.Default.Send(new ScreenAddedMessage());
        }

        public event Action<Screen> OnScreenSelected;
    }

    public class ScreenAddedMessage
    {
        // 可以留空，不需要任何属性
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

    /// <summary>「通信变量」根节点：展开显示「变量」「通讯」子节点，双击子节点在画布位置打开对应 Tab。</summary>
    public class CommunicationRootNode : ProjectTreeViewModel
    {
        public CommunicationRootNode()
        {
            Name = "通信变量";
            Children.Add(new VariableManagerNode(this));
            Children.Add(new DeviceConfigNode(this));
        }

        /// <summary>「变量」子节点被选中时触发（上层打开变量管理器 Tab）。</summary>
        public event Action? OnVariableManagerSelected;

        /// <summary>「通讯」子节点被选中时触发（上层打开通讯配置 Tab）。</summary>
        public event Action? OnDeviceConfigSelected;

        /// <summary>供子节点调用的内部入口。</summary>
        internal void NotifyVariableManagerSelected() => OnVariableManagerSelected?.Invoke();

        /// <summary>供子节点调用的内部入口。</summary>
        internal void NotifyDeviceConfigSelected() => OnDeviceConfigSelected?.Invoke();
    }

    /// <summary>「变量」叶子节点：双击打开变量管理器（画布 Tab）。</summary>
    public class VariableManagerNode : ProjectTreeViewModel
    {
        private readonly CommunicationRootNode _parent;

        public VariableManagerNode(CommunicationRootNode parent)
        {
            _parent = parent;
            Name = "变量";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyVariableManagerSelected());
        }
    }

    /// <summary>「通讯」叶子节点：双击打开通讯配置（画布 Tab）。</summary>
    public class DeviceConfigNode : ProjectTreeViewModel
    {
        private readonly CommunicationRootNode _parent;

        public DeviceConfigNode(CommunicationRootNode parent)
        {
            _parent = parent;
            Name = "通讯";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyDeviceConfigSelected());
        }
    }
}
 