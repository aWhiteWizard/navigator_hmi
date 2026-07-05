using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Microsoft.WindowsAPICodePack.Shell;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// NewProjectDialog.xaml 的交互逻辑
    /// </summary>
    public partial class NewProjectDialog : Window
    {
        public NewProjectDialog()
        {
            InitializeComponent();
            this.DataContext = new DeviceConfigViewModel();
        }

        private void OpenPathSelectDialog_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog openFileDialog = new FolderBrowserDialog();

            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.Title = "请选择一个文件夹";
                dialog.IsFolderPicker = true; // 关键：设置为文件夹选择模式
                                              // 可选：限制用户只能在此目录及其子目录中选择
                                              // dialog.InitialDirectory = @"C:\Your\Base\Path";
                                              // dialog.EnsurePathExists = true;

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    string selectedPath = dialog.FileName;
                    ProjectPathTextBox.Text = selectedPath;
                    this.ValidateAndProcessPath(selectedPath = selectedPath);
                }
            }
        }
        private void ValidateAndProcessPath(string selectedPath)
        {
            // 这里可以添加路径验证逻辑
            if (!System.IO.Directory.Exists(selectedPath))
            {
                System.Windows.MessageBox.Show("选择的文件夹不存在！",
                                "警告",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
            }
            else
            {
                // 可选：检查文件夹是否为空、是否有权限等
                try
                {
                    var files = System.IO.Directory.GetFiles(selectedPath);
                    var directories = System.IO.Directory.GetDirectories(selectedPath);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Windows.MessageBox.Show("无法访问该文件夹，权限不足！",
                                    "错误",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                    ProjectPathTextBox.Clear();
                }
            }
        }

        private void CreateProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            // 在选择的路径下创建工程同名的文件夹
            string projectFolder = System.IO.Path.Combine(ProjectPathTextBox.Text, ProjectNameTextBox.Text);
            System.IO.Directory.CreateDirectory(projectFolder);

            // 工程文件路径改为放在该文件夹下
            string projectFilePath = System.IO.Path.Combine(projectFolder, ProjectNameTextBox.Text + ".hmiproj");

            // 把文件夹路径设回 ViewModel 的 ProjectPath（原本是父目录，现在改为子目录）
            var vm = this.DataContext as DeviceConfigViewModel;
            if (vm != null)
            {
                vm.ProjectPath = projectFolder;  // 关键：让 ViewModel 知道目标文件夹
            }

            RecentProjectManager.Instance.AddRecentProject(projectFilePath);
            DialogResult = true;
            this.Close();
        }



        private void CreateProjectCancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;  // 关键：设置为 false
            this.Close();
        }  
    }
}
