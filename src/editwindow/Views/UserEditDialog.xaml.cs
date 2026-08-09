using NavigatorHMI.Common;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NavigatorHMI.Views;

/// <summary>P2-12 用户编辑对话框（新建/编辑复用：用户名 + 密码 + 所属组下拉）。</summary>
public partial class UserEditDialog : Window, INotifyPropertyChanged
{
    public UserEditDialog(IEnumerable<UserGroup> groups)
    {
        InitializeComponent();
        DataContext = this;
        foreach (var g in groups) GroupCombo.Items.Add(g.Name);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private string _userName = "";
    public string UserName { get => _userName; set { if (_userName != value) { _userName = value; Notify(); } } }

    private string _password = "";
    /// <summary>密码（编辑时留空=不改）。</summary>
    public string Password => _password;

    public string SelectedGroup => GroupCombo.SelectedItem?.ToString() ?? "";

    private void PassBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _password = ((System.Windows.Controls.PasswordBox)sender).Password;

    /// <summary>预填（编辑模式）。</summary>
    public void Prefill(string name, string groupName)
    {
        UserName = name;
        GroupCombo.SelectedItem = groupName;
        if (GroupCombo.SelectedItem == null && GroupCombo.Items.Count > 0)
            GroupCombo.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            MessageBox.Show("用户名不能为空", "用户", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (GroupCombo.SelectedItem == null)
        {
            MessageBox.Show("请选择所属组", "用户", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}
