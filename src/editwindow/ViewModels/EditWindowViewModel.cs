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
            UserActive = false;
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
            UserActive = false;
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
            UserActive = false;
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
            UserActive = false;
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
            UserActive = false;
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
            UserActive = false;
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
            UserActive = false;
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
            UserActive = false;
            RefreshTreeCurrentStatus();
        }

        /// <summary>关闭报警配置 Tab（若当前激活则切回当前画面，树高亮恢复画面节点）。</summary>
        public void CloseAlarmTab()
        {
            if (AlarmActive) AlarmActive = false;
            AlarmTabOpen = false;
            RefreshTreeCurrentStatus();
        }

        // ═══════════════════════════════════════════
        // W3 用户面板（用户名设置/用户组策略/用户安全设置三子页，与变量/通讯/列表/报警互斥）
        // ═══════════════════════════════════════════

        private bool _userTabOpen;
        /// <summary>用户面板 Tab 是否打开。</summary>
        public bool UserTabOpen
        {
            get => _userTabOpen;
            set { if (_userTabOpen != value) { _userTabOpen = value; OnPropertyChanged(); } }
        }

        private bool _userActive;
        /// <summary>当前内容是否为用户面板。</summary>
        public bool UserActive
        {
            get => _userActive;
            set { if (_userActive != value) { _userActive = value; OnPropertyChanged(); } }
        }

        private int _userPanelPage;
        /// <summary>用户面板子页（0=用户名设置 1=用户组策略 2=用户安全设置）。</summary>
        public int UserPanelPage
        {
            get => _userPanelPage;
            set { if (_userPanelPage != value) { _userPanelPage = value; OnPropertyChanged(); RefreshTreeCurrentStatus(); } }   // 子页切换同步树叶子高亮（与 ListManagerSelectedIndex 对齐）
        }

        private void OpenUserPanelInternal(int page)
        {
            UserTabOpen = true;
            UserActive = true;   // 互斥关完后置位（统一 Replace 安全）
            UserPanelPage = page;
            VariableManagerActive = false;
            CommunicationActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            RefreshUserPanel();   // W3b：打开面板同步用户/组/策略数据
            RefreshTreeCurrentStatus();
        }

        /// <summary>打开用户面板-用户名设置（用户 CRUD）。</summary>
        public void OpenUserPanel() => OpenUserPanelInternal(0);

        /// <summary>打开用户面板-用户组策略（组权限）。</summary>
        public void OpenUserGroupPanel() => OpenUserPanelInternal(1);

        /// <summary>打开用户面板-用户安全设置（密码策略）。</summary>
        public void OpenUserSecurityPanel() => OpenUserPanelInternal(2);

        /// <summary>激活用户面板（Tab 已开时点击标签栏——不重复开，刷新数据）。</summary>
        public void ActivateUser()
        {
            if (!UserTabOpen) UserTabOpen = true;
            VariableManagerActive = false;
            CommunicationActive = false;
            ListManagerActive = false;
            AlarmActive = false;
            UserActive = true;
            RefreshUserPanel();
            RefreshTreeCurrentStatus();
        }

        /// <summary>关闭用户面板 Tab。</summary>
        public void CloseUserTab()
        {
            if (UserActive) UserActive = false;
            UserTabOpen = false;
            RefreshTreeCurrentStatus();
        }

        // ═══ W3b 用户面板数据（用户名设置/用户组策略/用户安全设置） ═══

        /// <summary>用户账户列表（同步工程 project.Users；打开面板时刷新）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<UserAccount> UserAccounts { get; } = new();

        /// <summary>用户组列表（同步 project.Groups）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<UserGroup> UserGroups { get; } = new();

        /// <summary>安全设置（绑定密码策略表单）。</summary>
        public SecuritySettings Security
        {
            get => CurrentProject.Security;
            set { CurrentProject.Security = value; OnPropertyChanged(); }
        }

        private UserAccount? _selectedUser;
        public UserAccount? SelectedUser
        {
            get => _selectedUser;
            set { _selectedUser = value; OnPropertyChanged(); }
        }

        private string _newUserName = "";
        public string NewUserName { get => _newUserName; set { _newUserName = value; OnPropertyChanged(); } }

        private string _newUserPassword = "";
        public string NewUserPassword { get => _newUserPassword; set { _newUserPassword = value; OnPropertyChanged(); } }

        private string _newUserGroup = "访客";
        public string NewUserGroup { get => _newUserGroup; set { _newUserGroup = value; OnPropertyChanged(); } }

        /// <summary>刷新用户面板数据（打开面板时调用）。</summary>
        public void RefreshUserPanel()
        {
            UserAccounts.Clear();
            foreach (var u in CurrentProject.Users) UserAccounts.Add(u);
            UserGroups.Clear();
            foreach (var g in CurrentProject.Groups) UserGroups.Add(g);
            OnPropertyChanged(nameof(Security));
        }

        /// <summary>添加用户（走命令层 create_user——校验/哈希/组存在性统一）。</summary>
        public void AddUserCommand()
        {
            var r = CommandService.Execute("create_user", new Dictionary<string, object?>
            {
                ["user_name"] = _pendingUserName.Trim(),
                ["password"] = _pendingUserPassword,
                ["group_name"] = _pendingUserGroup,
            });
            if (r.Success) RefreshUserPanel();
            System.Windows.MessageBox.Show(r.Success ? $"已创建用户 {_pendingUserName.Trim()}" : $"创建失败 [{r.ErrorCode}]: {r.ErrorMessage}",
                "用户管理", System.Windows.MessageBoxButton.OK,
                r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        private string _pendingUserName = "";
        private string _pendingUserPassword = "";
        private string _pendingUserGroup = "访客";

        /// <summary>P2-12 创建用户（弹窗收集 → 命令层 create_user）。</summary>
        public void AddUserCommand(string name, string password, string group)
        {
            _pendingUserName = name; _pendingUserPassword = password; _pendingUserGroup = group;
            PushUserSnapshot();
            AddUserCommand();
        }

        /// <summary>P2-12 更新用户（改用户名/密码/组；密码留空=不改）。</summary>
        public void UpdateUserCommand(string oldName, string newName, string newPassword, string newGroup)
        {
            PushUserSnapshot();
            var r = CommandService.Execute("update_user", new Dictionary<string, object?>
            {
                ["user_name"] = oldName,
                ["new_user_name"] = newName == oldName ? "" : newName,
                ["new_password"] = newPassword,
                ["new_group_name"] = newGroup,
            });
            if (r.Success) RefreshUserPanel();
            System.Windows.MessageBox.Show(r.Success ? $"已更新用户 {newName}" : $"更新失败 [{r.ErrorCode}]: {r.ErrorMessage}",
                "用户管理", System.Windows.MessageBoxButton.OK,
                r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>P2-12/任务12 复制用户（多选：用户名+组集合；密码不复制——副本需重设密码）。</summary>
        public void CopyUsersCommand(List<UserAccount> users)
        {
            if (users.Count == 0) return;
            _copiedUsers = users.Select(u => (u.UserName, u.GroupName)).ToList();
            System.Windows.MessageBox.Show($"已复制 {users.Count} 个用户（粘贴创建副本，密码需重设）", "用户管理",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>P2-12/任务12 粘贴用户（多副本：原名+副本 编号递增；密码随机生成并提示）。</summary>
        public void PasteUsersCommand()
        {
            if (_copiedUsers == null || _copiedUsers.Count == 0)
            {
                System.Windows.MessageBox.Show("剪贴板没有用户（请先复制）", "用户管理",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            PushUserSnapshot();   // 批量一次快照，整体可撤销（对齐 DeleteGroupsBatch）
            var created = new List<string>();
            var failed = new List<string>();
            foreach (var (name, group) in _copiedUsers)
            {
                var baseName = name;
                var n = 1;
                while (CurrentProject.Users.Any(x => x.UserName == baseName + "副本" + (n > 1 ? n.ToString() : ""))) n++;
                var newName = baseName + "副本" + (n > 1 ? n.ToString() : "");
                var randomPwd = System.Guid.NewGuid().ToString("N")[..8];
                var r = CommandService.Execute("create_user", new Dictionary<string, object?>
                {
                    ["user_name"] = newName,
                    ["password"] = randomPwd,
                    ["group_name"] = group,
                });
                if (r.Success) created.Add($"{newName}（密码 {randomPwd}）");
                else failed.Add($"{newName}（{r.ErrorCode}）");
            }
            RefreshUserPanel();
            var msg = created.Count > 0 ? $"已创建用户副本：{string.Join("、", created.Take(5))}{(created.Count > 5 ? $" 等 {created.Count} 个" : "")}（建议编辑改密）" : "";
            if (failed.Count > 0) msg += (msg.Length > 0 ? "；" : "") + $"失败：{string.Join("、", failed)}";
            System.Windows.MessageBox.Show(msg, "用户管理",
                System.Windows.MessageBoxButton.OK,
                failed.Count > 0 ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
        }

        private List<(string UserName, string GroupName)>? _copiedUsers;   // 任务12：多选复制集合

        // ═══ P2-12 用户/组撤销栈（快照 Users+Groups；GUI 操作 + AI 操作统一入栈） ═══
        private readonly System.Collections.Generic.Stack<List<UserAccount>> _usersUndo = new();
        private readonly System.Collections.Generic.Stack<List<UserAccount>> _usersRedo = new();
        private readonly System.Collections.Generic.Stack<List<UserGroup>> _groupsUndo = new();
        private readonly System.Collections.Generic.Stack<List<UserGroup>> _groupsRedo = new();

        /// <summary>P2-12 操作前快照（用户/组；任何增删改复制/AI 用户操作前调用）。</summary>
        public void PushUserSnapshot()
        {
            _usersUndo.Push(SnapshotUsers());
            _groupsUndo.Push(SnapshotGroups());
            _usersRedo.Clear();
            _groupsRedo.Clear();
        }

        /// <summary>P2-12 撤销用户/组操作（Ctrl+Z 用户面板焦点；恢复快照）。</summary>
        public bool UndoUser()
        {
            if (_usersUndo.Count == 0) return false;
            _usersRedo.Push(SnapshotUsers());
            _groupsRedo.Push(SnapshotGroups());
            RestoreUsers(_usersUndo.Pop());
            RestoreGroups(_groupsUndo.Pop());
            RefreshUserPanel();
            return true;
        }

        /// <summary>P2-12 重做用户/组操作（Ctrl+Y）。</summary>
        public bool RedoUser()
        {
            if (_usersRedo.Count == 0) return false;
            _usersUndo.Push(SnapshotUsers());
            _groupsUndo.Push(SnapshotGroups());
            RestoreUsers(_usersRedo.Pop());
            RestoreGroups(_groupsRedo.Pop());
            RefreshUserPanel();
            return true;
        }

        private List<UserAccount> SnapshotUsers() => CurrentProject.Users.Select(u => new UserAccount { UserName = u.UserName, PasswordHash = u.PasswordHash, GroupName = u.GroupName, MustChangePassword = u.MustChangePassword }).ToList();
        private List<UserGroup> SnapshotGroups() => CurrentProject.Groups.Select(g => new UserGroup { Name = g.Name, Permissions = { } }).Select(g => { g.Permissions.AddRange(CurrentProject.Groups.First(x => x.Name == g.Name).Permissions); return g; }).ToList();

        private void RestoreUsers(List<UserAccount> list)
        {
            CurrentProject.Users.Clear();
            foreach (var u in list) CurrentProject.Users.Add(u);
        }

        private void RestoreGroups(List<UserGroup> list)
        {
            CurrentProject.Groups.Clear();
            foreach (var g in list) CurrentProject.Groups.Add(g);
        }

        /// <summary>删除选中用户（走命令层 delete_user——最后管理员保护）。</summary>
        public void DeleteUserCommand()
        {
            if (SelectedUser == null) return;
            var uname = SelectedUser.UserName;
            // 问题 4：删除确认框
            if (System.Windows.MessageBox.Show($"确定删除用户「{uname}」吗？", "用户管理",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;
            PushUserSnapshot();   // P2-12：删除可撤销
            var r = CommandService.Execute("delete_user", new Dictionary<string, object?> { ["user_name"] = uname });
            if (r.Success) { RefreshUserPanel(); SelectedUser = null; }
            System.Windows.MessageBox.Show(r.Success ? $"已删除用户 {uname}" : $"删除失败 [{r.ErrorCode}]: {r.ErrorMessage}",
                "用户管理", System.Windows.MessageBoxButton.OK,
                r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>补 #2：批量删除用户（DELETE 键多选——一次确认已在 View；命令层最后管理员保护，失败汇总；对齐 DeleteGroupsBatch 模式）。</summary>
        public void DeleteUsersBatch(List<string> names)
        {
            if (names.Count == 0) return;
            PushUserSnapshot();   // 批量一次快照可整体撤销
            var failed = new List<string>();
            foreach (var name in names)
            {
                var r = CommandService.Execute("delete_user", new Dictionary<string, object?> { ["user_name"] = name });
                if (!r.Success) failed.Add($"{name}（{r.ErrorCode}）");
            }
            RefreshUserPanel();
            SelectedUser = null;
            if (failed.Count > 0)
                System.Windows.MessageBox.Show($"部分用户删除失败：{string.Join("、", failed)}", "用户管理",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            else
                System.Windows.MessageBox.Show($"已删除 {names.Count} 个用户", "用户管理",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>P2-1 新建组（弹窗收集 → 命令层 create_group）。</summary>
        public void AddGroupCommand(string name, List<UserPermission> perms)
        {
            PushUserSnapshot();   // P2-12：组操作可撤销
            var r = CommandService.Execute("create_group", new Dictionary<string, object?>
            {
                ["group_name"] = name,
                ["permissions"] = string.Join(",", perms.Select(x => x.ToString())),
            });
            if (r.Success) RefreshUserPanel();
            System.Windows.MessageBox.Show(r.Success ? $"已创建组 {name}" : $"创建失败 [{r.ErrorCode}]: {r.ErrorMessage}",
                "用户组", System.Windows.MessageBoxButton.OK,
                r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>P2-1 更新组（改名/权限）。</summary>
        public void UpdateGroupCommand(string oldName, string newName, List<UserPermission> perms)
        {
            PushUserSnapshot();   // P2-12：组操作可撤销
            var r = CommandService.Execute("update_group", new Dictionary<string, object?>
            {
                ["group_name"] = oldName,
                ["new_group_name"] = newName == oldName ? "" : newName,
                ["permissions"] = string.Join(",", perms.Select(x => x.ToString())),
            });
            if (r.Success) RefreshUserPanel();
            System.Windows.MessageBox.Show(r.Success ? $"已更新组 {newName}" : $"更新失败 [{r.ErrorCode}]: {r.ErrorMessage}",
                "用户组", System.Windows.MessageBoxButton.OK,
                r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>P2-1 删除组（预设组命令层 BLOCKED）。</summary>
        public void DeleteGroupCommand(string name)
        {
            // 问题 4：删除确认框
            if (System.Windows.MessageBox.Show($"确定删除用户组「{name}」吗？", "用户组",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
                return;
            PushUserSnapshot();   // P2-12：组操作可撤销
            var r = CommandService.Execute("delete_group", new Dictionary<string, object?> { ["group_name"] = name });
            if (r.Success) RefreshUserPanel();
            System.Windows.MessageBox.Show(r.Success ? $"已删除组 {name}" : $"删除失败 [{r.ErrorCode}]: {r.ErrorMessage}",
                "用户组", System.Windows.MessageBoxButton.OK,
                r.Success ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }

        /// <summary>任务 10：批量删除组（DELETE 键多选——一次确认已在 View；预设组 BLOCKED 跳过并汇总）。</summary>
        public void DeleteGroupsBatch(List<string> names)
        {
            if (names.Count == 0) return;
            PushUserSnapshot();   // 批量一次快照可整体撤销
            var failed = new List<string>();
            foreach (var name in names)
            {
                var r = CommandService.Execute("delete_group", new Dictionary<string, object?> { ["group_name"] = name });
                if (!r.Success) failed.Add($"{name}（{r.ErrorCode}）");
            }
            RefreshUserPanel();
            if (failed.Count > 0)
                System.Windows.MessageBox.Show($"部分组删除失败：{string.Join("、", failed)}", "用户组",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            else
                System.Windows.MessageBox.Show($"已删除 {names.Count} 个用户组", "用户组",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>P2-1/任务12 复制组（多选：组名+权限集合；预设组也可复制）。</summary>
        public void CopyGroupsCommand(List<UserGroup> groups)
        {
            if (groups.Count == 0) return;
            _copiedGroups = groups.Select(g => (g.Name, g.Permissions.ToList())).ToList();
            System.Windows.MessageBox.Show($"已复制 {groups.Count} 个组（可粘贴创建副本）", "用户组",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>P2-1/任务12 粘贴组（多副本：原名+副本 编号递增；一次快照整体撤销）。</summary>
        public void PasteGroupsCommand()
        {
            if (_copiedGroups == null || _copiedGroups.Count == 0)
            {
                System.Windows.MessageBox.Show("剪贴板没有组（请先复制）", "用户组",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            PushUserSnapshot();   // 批量一次快照，整体可撤销（对齐 DeleteGroupsBatch）
            var failed = new List<string>();
            foreach (var (name, perms) in _copiedGroups)
            {
                var baseName = name;
                var n = 1;
                while (CurrentProject.Groups.Any(x => x.Name == baseName + "副本" + (n > 1 ? n.ToString() : ""))) n++;
                var newName = baseName + "副本" + (n > 1 ? n.ToString() : "");
                var r = CommandService.Execute("create_group", new Dictionary<string, object?>
                {
                    ["group_name"] = newName,
                    ["permissions"] = string.Join(",", perms.Select(x => x.ToString())),
                });
                if (!r.Success) failed.Add($"{newName}（{r.ErrorCode}）");
            }
            RefreshUserPanel();
            if (failed.Count > 0)
                System.Windows.MessageBox.Show($"部分组创建失败：{string.Join("、", failed)}", "用户组",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            else
                System.Windows.MessageBox.Show($"已创建 {_copiedGroups.Count} 个组副本", "用户组",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private List<(string Name, List<UserPermission> Permissions)>? _copiedGroups;   // 任务12：多选复制集合

        /// <summary>保存安全设置（写 project.Security + 标脏；负值拒绝）。</summary>
        public void SaveSecurityCommand()
        {
            var s = CurrentProject.Security;
            if (s.MinPasswordLength < 0 || s.PasswordMaxAgeDays < 0 || s.FailedLoginLockout < 0 || s.LockMinutes < 0)
            {
                System.Windows.MessageBox.Show("密码策略数值不能为负", "用户安全设置", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            OnPropertyChanged(nameof(Security));
            ProjectDirtyRequested?.Invoke();
        }

        /// <summary>激活画面：退出变量管理器/通讯/列表管理视图并切换画面（即使 CurrentScreen 未变也生效）。</summary>
        public void ActivateScreen(Screen screen)
        {
            VariableManagerActive = false;
            CommunicationActive = false;
            ListManagerActive = false;
            UserActive = false;
            AlarmActive = false;
            CurrentScreen = screen;   // 可能短路（值未变），短路时树高亮靠下方 RefreshTreeCurrentStatus 兜底
            RefreshTreeCurrentStatus();
        }

        private Screen _currentScreen;
        /// <summary>当前画面是否为世界地图（地图底图显示/鼠标穿透）。</summary>
        public bool IsWorldMapActive => _currentScreen?.Type == ScreenType.WorldMap;

        public Screen CurrentScreen
        {
            get => _currentScreen;
            set
            {
                if (_currentScreen == value) return;

                var oldScreen = _currentScreen;
                _currentScreen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWorldMapActive));   // 世界地图画面：地图底图可见性

                // E11：画面切换同步 current_screen 命令的运行时画面（AI 感知当前画面）
                CommandService.CurrentScreenName = value?.Name;

                // 切到画面时自动退出变量管理器/通讯/列表管理视图（标签栏高亮同步）
                VariableManagerActive = false;
                CommunicationActive = false;
                ListManagerActive = false;
                AlarmActive = false;
                UserActive = false;

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
                UpdateNodeRecursive(root, CurrentScreen, VariableManagerActive, CommunicationActive, ListManagerActive, AlarmActive, UserActive, UserPanelPage, ListManagerListType);
            }
        }

        private static void UpdateNodeRecursive(ProjectTreeViewModel node, Screen currentScreen, bool variableManagerActive, bool communicationActive, bool listManagerActive, bool alarmActive, bool userActive, int userPanelPage, ListType listManagerListType)
        {
            if (node is ScreenItemNode screenNode)
            {
                screenNode.IsCurrent = !variableManagerActive && !communicationActive && !listManagerActive && !alarmActive && !userActive && (screenNode.Screen == currentScreen);
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
            else if (node is UserNameNode unNode)
            {
                unNode.IsCurrent = userActive && userPanelPage == 0;
            }
            else if (node is UserGroupNode ugNode)
            {
                ugNode.IsCurrent = userActive && userPanelPage == 1;
            }
            else if (node is UserSecurityNode usNode)
            {
                usNode.IsCurrent = userActive && userPanelPage == 2;
            }
            foreach (var child in node.Children)
            {
                UpdateNodeRecursive(child, currentScreen, variableManagerActive, communicationActive, listManagerActive, alarmActive, userActive, userPanelPage, listManagerListType);
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

            // 用户/组命令：刷新用户面板（AI 添加用户即时显示——问题 2）
            if (cmdName is "create_user" or "update_user" or "delete_user" or "create_group" or "update_group" or "delete_group")
                RefreshUserPanel();
            // X-1c：外部（非拖拽）update_tag 改基准值 → 清 Tag redo（防 Redo 用旧快照覆盖手动编辑的新值）
            if (cmdName == "update_tag" && result.Success)
                _undoManager.ClearTagRedo();

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
            TreeRoots.Add(BuildAlarmRootNode());
            TreeRoots.Add(BuildUserRootNode());
        }

        /// <summary>构建「通信变量」根节点（含「变量」/「通讯」子节点，双击在画布位置打开对应 Tab）。</summary>
        private CommunicationRootNode BuildCommunicationRootNode()
        {
            var node = new CommunicationRootNode();
            node.OnVariableManagerSelected += OpenVariableManager;
            node.OnDeviceConfigSelected += OpenCommunication;
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

        /// <summary>W3 构建「报警」根节点（从通信变量移出；双击打开报警配置 Tab）。</summary>
        private AlarmRootNode BuildAlarmRootNode()
        {
            var node = new AlarmRootNode();
            node.OnAlarmConfigSelected += OpenAlarm;
            return node;
        }

        /// <summary>W3 构建「用户」根节点（三子节点：用户名设置/用户组策略/用户安全设置——打开对应面板）。</summary>
        private UserRootNode BuildUserRootNode()
        {
            var node = new UserRootNode();
            node.OnUserNameSelected += OpenUserPanel;
            node.OnUserGroupSelected += OpenUserGroupPanel;
            node.OnUserSecuritySelected += OpenUserSecurityPanel;
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
        /// <summary>A12：列表撤销优先回调（工具栏撤销/Ctrl+Z/菜单统一走此——列表栈非空时先撤列表项操作；View 初始化时赋值）。</summary>
        public ListManagerViewModel? ListManager { get; set; }

        public void PushUndoSnapshot()
        {
            if (CurrentScreen != null)
            {
                _undoManager.PushSnapshot(CurrentScreen);
            }
        }

        /// <summary>C12-2：世界地图数据变更（地图加点/清除等）前快照——WorldMap 配置独立于 Widgets 快照（UndoManager 双栈）。</summary>
        public void PushWorldMapUndoSnapshot()
        {
            if (CurrentScreen?.Type == ScreenType.WorldMap && CurrentProject?.WorldMap != null)
                _undoManager.PushWorldMapSnapshot(CurrentScreen, CurrentProject.WorldMap);
        }

        /// <summary>X-1c：变量基准值变更前快照（拖拽绑变量点更新 BaseValue 用；Tags 全局单栈）。</summary>
        public void PushTagSnapshot()
        {
            if (CurrentProject?.Tags != null) _undoManager.PushTagSnapshot(CurrentProject.Tags);
        }
        /// <summary>X-1c：变量撤销/重做后触发变量表格与地图点刷新（EditWindow 订阅）。</summary>
        public Action? TagUndoRequested { get; set; }
        public Action? TagRedoRequested { get; set; }
        public bool HasTagUndo => _undoManager.HasTagUndo;
        public bool HasTagRedo => _undoManager.HasTagRedo;

        /// <summary>WorldMap 撤销/重做后触发地图与表格刷新（EditWindow 订阅：UpdateAllGeoWidgets + 作业点/范围点表格重载）。</summary>
        public Action? WorldMapUndoRequested { get; set; }

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
            TreeRoots.Add(BuildAlarmRootNode());
            TreeRoots.Add(BuildUserRootNode());

            // 默认选中世界地图画面（旧工程缺 WorldMap 时兜底退回全局画面——FirstOrDefault 防抛异常）
            CurrentScreen = project.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap)
                ?? project.Screens.FirstOrDefault(s => s.Type == ScreenType.Template);

            UndoCommand = new RelayCommand(
                () =>
                {
                    // A12：列表栈非空时优先撤销列表项操作（列表操作粒度小、频率高，按钮/Ctrl+Z/菜单一致）
                    if (ListManager?.HasListUndo == true) { ListManager.UndoList(); return; }
                    // X-1c should-fix：Tag/WorldMap/Widgets 三栈在"非空栈"中选版本最大者（LIFO 全局顺序；栈空的不参与比较——防撤销后 LatestVersion 仍指向空栈卡死）
                    int best = -1;
                    if (CurrentProject != null && _undoManager.HasTagUndo) best = Math.Max(best, _undoManager.TagLatestVersion);
                    if (CurrentScreen != null && _undoManager.HasWorldMapUndo(CurrentScreen)) best = Math.Max(best, _undoManager.WorldMapLatestVersion);
                    if (CurrentScreen != null && _undoManager.HasUndo(CurrentScreen)) best = Math.Max(best, _undoManager.WidgetsLatestVersion);
                    if (CurrentProject != null && _undoManager.HasTagUndo && best == _undoManager.TagLatestVersion)
                    {
                        if (_undoManager.UndoTags(CurrentProject.Tags))
                        {
                            TagUndoRequested?.Invoke();
                            ProjectDirtyRequested?.Invoke();
                            return;
                        }
                    }
                    if (CurrentScreen == null) return;
                    // C12-2：世界地图画面优先撤销 WorldMap 数据（作业点/范围点；Widgets 快照不含 WorldMap）
                    if (CurrentScreen.Type == ScreenType.WorldMap && CurrentProject?.WorldMap != null
                        && _undoManager.HasWorldMapUndo(CurrentScreen) && best == _undoManager.WorldMapLatestVersion)
                    {
                        if (_undoManager.UndoWorldMap(CurrentScreen, CurrentProject.WorldMap))
                        {
                            WorldMapUndoRequested?.Invoke();
                            ProjectDirtyRequested?.Invoke();
                            return;
                        }
                    }
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
                    // A12：列表栈非空时优先重做列表项操作
                    if (ListManager?.HasListRedo == true) { ListManager.RedoList(); return; }
                    // X-1c should-fix（复审）：与 Undo 对称——非空 redo 栈中"最近撤销版本"最大者优先（后撤先重做，LIFO 互逆）
                    int best = -1;
                    if (CurrentProject != null && _undoManager.HasTagRedo) best = Math.Max(best, _undoManager.TagRedoLatestVersion);
                    if (CurrentScreen != null && _undoManager.HasWorldMapRedo(CurrentScreen)) best = Math.Max(best, _undoManager.WorldMapRedoLatestVersion);
                    if (CurrentScreen != null && _undoManager.HasRedo(CurrentScreen)) best = Math.Max(best, _undoManager.WidgetsRedoLatestVersion);
                    if (CurrentProject != null && _undoManager.HasTagRedo && best == _undoManager.TagRedoLatestVersion)
                    {
                        if (_undoManager.RedoTags(CurrentProject.Tags))
                        {
                            TagRedoRequested?.Invoke();
                            ProjectDirtyRequested?.Invoke();
                            return;
                        }
                    }
                    if (CurrentScreen == null) return;
                    // C12-2：世界地图画面优先重做 WorldMap 数据
                    if (CurrentScreen.Type == ScreenType.WorldMap && CurrentProject?.WorldMap != null
                        && _undoManager.HasWorldMapRedo(CurrentScreen) && best == _undoManager.WorldMapRedoLatestVersion)   // X-1c 三审：与 Undo 侧对称，缺版本条件会抢先重做错序
                    {
                        if (_undoManager.RedoWorldMap(CurrentScreen, CurrentProject.WorldMap))
                        {
                            WorldMapUndoRequested?.Invoke();
                            ProjectDirtyRequested?.Invoke();
                            return;
                        }
                    }
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
                _aiCts?.Dispose();
                _aiCts = new CancellationTokenSource();
                var token = _aiCts.Token;
                try
                {
                    EnsureAiAgent();
                    if (_aiAgent == null)
                        throw new InvalidOperationException("本地模型加载中，请稍候再发送…");
                    var agent = _aiAgent;   // 捕获引用：Task.Run 执行时不再重读（防切换设置竞态 NRE）
                    PushUndoSnapshot();   // AI 操作前快照当前画面（Ctrl+Z 可整体撤销 AI 的画面改动；变量/列表/报警用对应管理器删）
                PushUserSnapshot();   // P2-12：AI 用户/组操作也可撤销（快照 Users+Groups）
                    // 网络推理放后台线程；await 后经 WPF SynchronizationContext 回 UI 线程更新消息
                    var result = await Task.Run(() => agent!.ChatAsync(text, token), token);
                    ReplaceThinking("ai", result);
                    // 操作清单：展示 AI 本次执行了什么（可据此手动撤销单项）；撤销指引按操作类型分类（A4）
                    var ops = agent.LastOperations.Where(o => o.Success).ToList();
                    if (ops.Count > 0)
                    {
                        var lines = new List<string> { $"本次执行 {ops.Count} 项操作：" };
                        lines.AddRange(ops.Select((o, i) => $"{i + 1}. {o.CommandName}（{o.ArgsSummary}）"));
                        lines.Add(UndoGuidanceFor(ops));
                        AiMessages.Add(new AiChatEntry("status", string.Join("\n", lines)));
                    }
                }
                catch (OperationCanceledException)
                {
                    ReplaceThinking("status", "已停止（用户终止当前任务）。");
                }
                catch (Exception ex)
                {
                    ReplaceThinking("status", ex.Message);
                }
                finally
                {
                    IsAiThinking = false;
                    _aiCts?.Dispose();
                    _aiCts = null;
                }
            });
            // 思考中：发送按钮变「停止」，点击取消当前任务（CancellationToken）
            CancelAiCommand = new RelayCommand(() => _aiCts?.Cancel());
            AiSendOrCancelCommand = new RelayCommand(() =>
            {
                if (IsAiThinking) CancelAiCommand.Execute(null);
                else AiSendCommand.Execute(null);
            });
        }

        // ── A4：AI 操作撤销指引分类（按命令域提示正确的撤销入口）──

        /// <summary>用户/组域命令：撤销入口在【用户管理】页面（页面级 Ctrl+Z，A3）。</summary>
        private static readonly HashSet<string> UndoUserDomainCommands = new()
        {
            "create_user", "update_user", "delete_user",
            "create_group", "update_group", "delete_group",
        };

        /// <summary>画面域命令：当前画面 Ctrl+Z 撤销。</summary>
        private static readonly HashSet<string> UndoScreenDomainCommands = new()
        {
            "create_screen", "delete_screen", "rename_screen", "copy_screen", "paste_screen", "set_default_font",
            "add_widget", "move_widget", "resize_widget", "delete_widget", "set_property",
            "bring_to_front", "bring_forward", "send_backward", "send_to_back",
            "align_widgets", "array_layout", "copy_widget", "paste_widget",
            "bind_event", "add_event", "remove_event", "update_event", "bind_tag",
        };

        /// <summary>无需撤销指引的命令（只读/持久化/编译——归第三类时不能提示「手动删除」）。</summary>
        private static readonly HashSet<string> UndoNoGuidanceCommands = new()
        {
            "current_screen", "save_project", "compile", "list_users", "scan_devices",
            "connect", "deploy_project", "deploy_firmware",
        };

        /// <summary>按本次成功操作涉及的域生成撤销指引文案（只列出现过的域；其余默认归「对应管理器查看/处理」）。</summary>
        private static string UndoGuidanceFor(List<AIAgent.AiOperation> ops)
        {
            var hints = new List<string>();
            if (ops.Any(o => UndoScreenDomainCommands.Contains(o.CommandName)))
                hints.Add("切回画面页后 Ctrl+Z 撤销画面改动");
            if (ops.Any(o => UndoUserDomainCommands.Contains(o.CommandName)))
                hints.Add("切到【用户管理】页面后 Ctrl+Z 撤销用户/组改动");
            if (ops.Any(o => !UndoScreenDomainCommands.Contains(o.CommandName)
                             && !UndoUserDomainCommands.Contains(o.CommandName)
                             && !UndoNoGuidanceCommands.Contains(o.CommandName)))
                hints.Add("其余操作可在对应管理器查看/处理");
            return "撤销指引：" + string.Join("；", hints) + "。";
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

        /// <summary>推理深度：映射单次指令的最大模型调用轮次（快速 4 / 均衡 8 / 深度 16；见 EnsureAiAgent 两处 MaxIterations 映射，注释与代码 2026-08-26 已对齐）。</summary>
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
            var cfg = AiConfigStore.Load();
            _aiApiKey = cfg.ApiKey;
            _aiLocalModelPath = cfg.LocalModelPath;   // P3-7：模型路径移设置窗后一并刷新（否则 Agent 用旧路径）
            OnPropertyChanged(nameof(AiApiKey));
            OnPropertyChanged(nameof(AiLocalModelPath));
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

        private CancellationTokenSource? _aiCts;   // AI 任务取消令牌（发送后变「停止」可终止）
        private bool _isAiThinking;
        /// <summary>AI 是否正在推理（发送中禁用输入/按钮）。</summary>
        public bool IsAiThinking { get => _isAiThinking; set { _isAiThinking = value; OnPropertyChanged(); } }

        /// <summary>展开/收起 AI 侧边栏。</summary>
        public ICommand ToggleAiPanelCommand { get; private set; } = null!;
        /// <summary>发送自然语言指令给 AI（DeepSeek 语义 Agent，多轮会话）。</summary>
        public ICommand AiSendCommand { get; private set; } = null!;
        public ICommand CancelAiCommand { get; private set; } = null!;
        public ICommand AiSendOrCancelCommand { get; private set; } = null!;
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
