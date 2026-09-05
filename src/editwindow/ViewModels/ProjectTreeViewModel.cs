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
            // W3：报警配置移出为独立根节点（AlarmRootNode）
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

    /// <summary>「列表」根节点（与「通信变量」平级）：展开显示「文本列表」「图片列表」「视频源列表」，双击子节点打开列表管理面板对应页。</summary>
    public class ListRootNode : ProjectTreeViewModel
    {
        public ListRootNode()
        {
            Name = "列表";
            Children.Add(new TextListRootNode(this));
            Children.Add(new ImageListRootNode(this));
            Children.Add(new VideoListRootNode(this));   // S-4
        }

        /// <summary>「文本列表」子节点被选中时触发（上层打开列表管理 Tab 文本页）。</summary>
        public event Action? OnTextListSelected;

        /// <summary>「图片列表」子节点被选中时触发（上层打开列表管理 Tab 图片页）。</summary>
        public event Action? OnImageListSelected;

        /// <summary>「视频源列表」子节点被选中时触发（S-4：上层打开列表管理 Tab 视频源列表页）。</summary>
        public event Action? OnVideoListSelected;

        /// <summary>供子节点调用的内部入口。</summary>
        internal void NotifyTextListSelected() => OnTextListSelected?.Invoke();

        /// <summary>供子节点调用的内部入口。</summary>
        internal void NotifyImageListSelected() => OnImageListSelected?.Invoke();

        /// <summary>供子节点调用的内部入口。</summary>
        internal void NotifyVideoListSelected() => OnVideoListSelected?.Invoke();
    }

    /// <summary>「文本列表」叶子节点：双击打开列表管理面板（文本页）。</summary>
    public class TextListRootNode : ProjectTreeViewModel
    {
        private readonly ListRootNode _parent;

        public TextListRootNode(ListRootNode parent)
        {
            _parent = parent;
            Name = "文本列表";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyTextListSelected());
        }
    }

    /// <summary>「图片列表」叶子节点：双击打开列表管理面板（图片页）。</summary>
    public class ImageListRootNode : ProjectTreeViewModel
    {
        private readonly ListRootNode _parent;

        public ImageListRootNode(ListRootNode parent)
        {
            _parent = parent;
            Name = "图片列表";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyImageListSelected());
        }
    }

    /// <summary>「视频源列表」叶子节点（S-4）：双击打开列表管理面板（视频源列表页）。</summary>
    public class VideoListRootNode : ProjectTreeViewModel
    {
        private readonly ListRootNode _parent;

        public VideoListRootNode(ListRootNode parent)
        {
            _parent = parent;
            Name = "视频源列表";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyVideoListSelected());
        }
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

    /// <summary>「报警」叶子节点：双击打开报警配置（画布 Tab）。</summary>
    public class AlarmConfigNode : ProjectTreeViewModel
    {
        private readonly AlarmRootNode _parent;

        public AlarmConfigNode(AlarmRootNode parent)
        {
            _parent = parent;
            Name = "报警";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyAlarmConfigSelected());
        }
    }

    /// <summary>W3「报警」根节点：从通信变量移出（DESIGN-WINDOWS.md 项目树定稿），含「报警」子节点。</summary>
    public class AlarmRootNode : ProjectTreeViewModel
    {
        public AlarmRootNode()
        {
            Name = "报警";
            Children.Add(new AlarmConfigNode(this));
        }

        /// <summary>「报警」子节点被选中时触发（上层打开报警配置 Tab）。</summary>
        public event Action? OnAlarmConfigSelected;

        internal void NotifyAlarmConfigSelected() => OnAlarmConfigSelected?.Invoke();
    }

    /// <summary>W3「用户」根节点：用户名设置/用户组策略/用户安全设置三子节点（DESIGN-WINDOWS.md §用户节点）。</summary>
    public class UserRootNode : ProjectTreeViewModel
    {
        public UserRootNode()
        {
            Name = "用户";
            Children.Add(new UserNameNode(this));
            Children.Add(new UserGroupNode(this));
            Children.Add(new UserSecurityNode(this));
        }

        public event Action? OnUserNameSelected;
        public event Action? OnUserGroupSelected;
        public event Action? OnUserSecuritySelected;
        internal void NotifyUserNameSelected() => OnUserNameSelected?.Invoke();
        internal void NotifyUserGroupSelected() => OnUserGroupSelected?.Invoke();
        internal void NotifyUserSecuritySelected() => OnUserSecuritySelected?.Invoke();
    }

    /// <summary>「用户名设置」叶子：打开用户管理面板（用户 CRUD）。</summary>
    public class UserNameNode : ProjectTreeViewModel
    {
        private readonly UserRootNode _parent;
        public UserNameNode(UserRootNode parent)
        {
            _parent = parent;
            Name = "用户名设置";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyUserNameSelected());
        }
    }

    /// <summary>「用户组策略」叶子：打开用户组权限面板。</summary>
    public class UserGroupNode : ProjectTreeViewModel
    {
        private readonly UserRootNode _parent;
        public UserGroupNode(UserRootNode parent)
        {
            _parent = parent;
            Name = "用户组策略";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyUserGroupSelected());
        }
    }

    /// <summary>「用户安全设置」叶子：打开密码策略面板。</summary>
    public class UserSecurityNode : ProjectTreeViewModel
    {
        private readonly UserRootNode _parent;
        public UserSecurityNode(UserRootNode parent)
        {
            _parent = parent;
            Name = "用户安全设置";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyUserSecuritySelected());
        }
    }

    /// <summary>
    /// 「设备管理」根节点（O 轮批 C C-1 重构，N+31 v2 方案：容器父节点，双击不打开单面板）——
    /// 展开四子节点：设备连接 / 远程控制 / 设备升级 / 工程下载，各双击打开独立子页（对齐用户节点模式）。
    /// </summary>
    public class DeviceRootNode : ProjectTreeViewModel
    {
        public DeviceRootNode()
        {
            Name = "设备管理";
            Children.Add(new DeviceConnectNode(this));
            Children.Add(new RemoteControlNode(this));
            Children.Add(new DeviceUpgradeNode(this));
            Children.Add(new ProjectDownloadNode(this));
        }

        public event Action? OnDeviceConnectSelected;
        public event Action? OnRemoteControlSelected;
        public event Action? OnDeviceUpgradeSelected;
        public event Action? OnProjectDownloadSelected;
        internal void NotifyDeviceConnectSelected() => OnDeviceConnectSelected?.Invoke();
        internal void NotifyRemoteControlSelected() => OnRemoteControlSelected?.Invoke();
        internal void NotifyDeviceUpgradeSelected() => OnDeviceUpgradeSelected?.Invoke();
        internal void NotifyProjectDownloadSelected() => OnProjectDownloadSelected?.Invoke();
    }

    /// <summary>「设备连接」叶子节点：双击打开设备连接子页（搜索/连接测试/断开/会话状态/闪烁确认）。</summary>
    public class DeviceConnectNode : ProjectTreeViewModel
    {
        private readonly DeviceRootNode _parent;
        public DeviceConnectNode(DeviceRootNode parent)
        {
            _parent = parent;
            Name = "设备连接";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyDeviceConnectSelected());
        }
    }

    /// <summary>「远程控制」叶子节点：双击打开远程控制子页（VNC 开关/查看器/闪烁）。</summary>
    public class RemoteControlNode : ProjectTreeViewModel
    {
        private readonly DeviceRootNode _parent;
        public RemoteControlNode(DeviceRootNode parent)
        {
            _parent = parent;
            Name = "远程控制";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyRemoteControlSelected());
        }
    }

    /// <summary>「设备升级」叶子节点：双击打开设备升级子页（固件文件夹/版本前置/进度/重启确认）。</summary>
    public class DeviceUpgradeNode : ProjectTreeViewModel
    {
        private readonly DeviceRootNode _parent;
        public DeviceUpgradeNode(DeviceRootNode parent)
        {
            _parent = parent;
            Name = "设备升级";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyDeviceUpgradeSelected());
        }
    }

    /// <summary>「工程下载」叶子节点：双击打开工程下载子页（下载 + D-B4 进度条）。</summary>
    public class ProjectDownloadNode : ProjectTreeViewModel
    {
        private readonly DeviceRootNode _parent;
        public ProjectDownloadNode(DeviceRootNode parent)
        {
            _parent = parent;
            Name = "工程下载";
            DoubleClickCommand = new RelayCommand(() => _parent.NotifyProjectDownloadSelected());
        }
    }
}
 
