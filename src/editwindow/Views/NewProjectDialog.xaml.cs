using Microsoft.Win32;
using NavigatorHMI.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// NewProjectDialog.xaml 的交互逻辑
    /// </summary>
    public partial class NewProjectDialog : Window
    {
        public string ProjectName;
        public event EventHandler<ProjectEventArgs> NewProjectRequested;

        public NewProjectDialog()
        {
            InitializeComponent();
            this.DataContext = new DeviceConfigViewModel();
        }

        private void OpenPathSelectDialog_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.ValidateNames = false;
            openFileDialog.CheckFileExists = false;
            openFileDialog.CheckPathExists = true;
            openFileDialog.FileName = "选择文件夹";

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedPath = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
                ProjectPathTextBox.Text = selectedPath;
                ValidateAndProcessPath(selectedPath);
            }
        }
        private void ValidateAndProcessPath(string selectedPath)
        {
            // 这里可以添加路径验证逻辑
            if (!System.IO.Directory.Exists(selectedPath))
            {
                MessageBox.Show("选择的文件夹不存在！",
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

                    if (files.Length > 0 || directories.Length > 0)
                    {
                        var result = MessageBox.Show(
                            "文件夹不为空，是否继续使用此路径？",
                            "提示",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.No)
                        {
                            ProjectPathTextBox.Clear();
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("无法访问该文件夹，权限不足！",
                                    "错误",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Error);
                    ProjectPathTextBox.Clear();
                }
            }
        }

        private void CreateProjectBtn_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CreateProjectCancelBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }  
    }
}
