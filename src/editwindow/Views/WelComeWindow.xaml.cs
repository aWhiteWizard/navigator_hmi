using Microsoft.Win32;
using NavigatorHMI.Common;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NavigatorHMI.Views
{
    public partial class WelComeWindow : Window
    {

        public event EventHandler<ProjectEventArgs> NewProjectRequested;
        public event EventHandler<ProjectEventArgs> OpenProjectRequested;

        #region 加载欢迎页面
        public WelComeWindow()
        {
            InitializeComponent();
            Loaded += WelcomeWindow_Loaded;
            OpenProjectPath.AllowDrop = true;
            OpenProjectPath.PreviewDragOver += OpenProjectPath_PreviewDragOver;
            OpenProjectPath.Drop += OpenProjectPath_Drop;
        }
        
        private void OpenProjectPath_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void OpenProjectPath_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string filePath = files[0];
                    try
                    {
                        var project = LoadProject(filePath);
                        // OpenProjectRequested?.Invoke(this, new ProjectEventArgs(project));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"打开工程失败: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void WelcomeWindow_Loaded(object sender, RoutedEventArgs e)
        {
           LoadRecentProjects();
        }

        private void LoadRecentProjects()
        {
            // 加载最近项目列表
        }
        #endregion

        #region 打开工程
        // 打开工程按钮点击事件
        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            
        }

        // 加载工程文件
        private HMIProject LoadProject(string filePath)
        {
            // 这里实现工程文件加载逻辑
            // 简化版：直接创建工程对象
            return new HMIProject
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                CreateTime = DateTime.Now,
                LastModified = DateTime.Now
                // Pages = new System.Collections.ObjectModel.ObservableCollection<Page>()
            };
        }
        #endregion

        #region 新建工程
        // 新建工程按钮点击事件
        private void CreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            // 显示新建工程对话框
            var dialog = new NewProjectDialog();
            if (dialog.ShowDialog() == true)
            {
                // 创建新工程
                var project = CreateNewProject(dialog.ProjectName);

                // 触发事件，通知主窗口导航到编辑页面
                // NewProjectRequested?.Invoke(this, new ProjectEventArgs(project));
            }
        }

        // 创建新工程
        private HMIProject CreateNewProject(string projectName)
        {
            return new HMIProject
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                CreateTime = DateTime.Now,
                LastModified = DateTime.Now,
                // Pages = new System.Collections.ObjectModel.ObservableCollection<Page>
                //{
                //    new Page { Name = "主页面" }
                //}
            };
        }

        #endregion

        private void SelectProjectPath_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "HMI工程文件 (*.hmiproj)|*.hmiproj|所有文件 (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                OpenProjectPath.Text = openFileDialog.FileName;

                string selectedFilePath = openFileDialog.FileName;
                if (ValidateHmiProjectFile(selectedFilePath))
                {
                    OpenProjectPath.Text = selectedFilePath;
                    OnProjectPathSelected(selectedFilePath);
                }
                else
                {
                    MessageBox.Show(
                    "请选择有效的.hmiproj工程文件",
                    "文件类型错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                    );
                }
            }

        }

        // ============ 辅助方法 ============

        /// <summary>
        /// 验证HMI工程文件
        /// </summary>
        private bool ValidateHmiProjectFile(string filePath)
        {
            try
            {
                string extension = Path.GetExtension(filePath).ToLower();
                return extension == ".hmiproj" && File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// 文件选择后的处理
        /// </summary>
        private void OnProjectPathSelected(string filePath)
        {
            // 这里可以添加文件选择后的处理逻辑
            // 例如：更新UI、加载工程信息等
            Console.WriteLine($"已选择HMI工程文件: {filePath}");
        }
    }

    /// <summary>
    /// 页面相关事件参数
    /// </summary>
    public class ProjectEventArgs : EventArgs
    {
        public Guid ProjectId { get; }
        public ProjectEventArgs( Guid projectId)
        {
            ProjectId = projectId;
        }
    }
}
