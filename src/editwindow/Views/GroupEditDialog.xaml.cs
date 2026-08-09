using NavigatorHMI.Common;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NavigatorHMI.Views;

/// <summary>P2-1 用户组编辑对话框（新建/编辑复用：组名 + 权限 CheckBox）。</summary>
public partial class GroupEditDialog : Window, INotifyPropertyChanged
{
    public GroupEditDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private string _groupName = "";
    public string GroupName { get => _groupName; set { if (_groupName != value) { _groupName = value; Notify(); } } }

    private bool _screenEdit, _alarmAck, _userManage, _systemSettings;
    public bool ScreenEdit { get => _screenEdit; set { _screenEdit = value; Notify(); } }
    public bool AlarmAck { get => _alarmAck; set { _alarmAck = value; Notify(); } }
    public bool UserManage { get => _userManage; set { _userManage = value; Notify(); } }
    public bool SystemSettings { get => _systemSettings; set { _systemSettings = value; Notify(); } }

    /// <summary>预填（编辑模式）：组名 + 权限。</summary>
    public void Prefill(string name, IEnumerable<UserPermission> permissions)
    {
        GroupName = name;
        ScreenEdit = permissions.Contains(UserPermission.ScreenEdit);
        AlarmAck = permissions.Contains(UserPermission.AlarmAck);
        UserManage = permissions.Contains(UserPermission.UserManage);
        SystemSettings = permissions.Contains(UserPermission.SystemSettings);
    }

    /// <summary>收集勾选权限（ListBox 顺序固定）。</summary>
    public List<UserPermission> GetPermissions()
    {
        var r = new List<UserPermission>();
        if (ScreenEdit) r.Add(UserPermission.ScreenEdit);
        if (AlarmAck) r.Add(UserPermission.AlarmAck);
        if (UserManage) r.Add(UserPermission.UserManage);
        if (SystemSettings) r.Add(UserPermission.SystemSettings);
        return r;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            MessageBox.Show("组名不能为空", "用户组", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
