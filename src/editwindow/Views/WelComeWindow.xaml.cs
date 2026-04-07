using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using ProtoBuf;

namespace NavigatorHMI.Views
{
    public partial class WelComeWindow : Window
    {
        public event EventHandler<ProjectEventArgs> OpenProjectRequested;


        #region 加载欢迎页面
        public WelComeWindow()
        {
            InitializeComponent();

        }

        // 打开历史记录中的工程
        public void OpenRecentProject(string filePath)
        {
            if (File.Exists(filePath))
            {
                this.LoadRecentProjects(filePath);
            }
            else
            {
                MessageBox.Show($"工程文件不存在：{filePath}", "提示",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                // 可选：从历史记录中移除无效路径
                //_recentManager.RemoveInvalidPath(filePath);
            }
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

        private void WelComeWindowLoaded(object sender, RoutedEventArgs e)
        {
            RecentProjectsListBox.ItemsSource = RecentProjectManager.Instance.RecentOpenedProject;
        }

        private void LoadRecentProjects(string file_path)
        {
            // 加载最近项目列表
            //ToDo: 接下来做这个，点击列表中的工程，如果工程存在则打开，不存在则变灰

        }
        #endregion

        #region 打开工程
        // 打开工程按钮点击事件
        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            // 1. 创建打开文件对话框
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Title = "选择工程文件";
            dialog.Filter = "组态工程文件|*.hmiproj";
            dialog.DefaultExt = ".hmiproj";
            dialog.CheckFileExists = true;

            // 2. 显示对话框，判断用户是否点击“打开”
            if (dialog.ShowDialog() == true)
            {
                string filePath = dialog.FileName;

                try
                {
                    // 3. 反序列化加载工程对象（使用 protobuf-net）
                    HMIProject project;
                    using (var fs = new FileStream(filePath, FileMode.Open))
                    {
                        project = Serializer.Deserialize<HMIProject>(fs);
                    }

                    // 更新工程路径和修改时间
                    project.ProjectFilePath = filePath;
                    project.LastModifiedTime = DateTime.Now;

                    // 4. 添加到最近打开列表（假设 App.RecentManager 是全局单例）
                    RecentProjectManager.Instance.AddRecentProject(filePath);

                    // 5. 打开编辑窗口（假设 EditWindow 可接收工程对象）
                    var editWindow = new EditWindow();
                    // var viewModel = new EditWindow(project);  // 需要提前定义 EditorViewModel
                    // editWindow.DataContext = viewModel;
                    editWindow.Show();

                    // 6. 关闭当前欢迎窗口
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开工程失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region 新建工程
        // 新建工程按钮点击事件
        private void CreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            // 隐藏当前欢迎窗口
            this.Hide();

            var dialog = new NewProjectDialog();
            // 假设 NewProjectDialog 内部点击“确定”时会设置 DialogResult = true
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                // 用户确认创建了工程 → 彻底关闭欢迎窗口（或打开编辑窗口后关闭）
                // 例如：打开主编辑窗口
                var editor = new EditWindow();
                editor.Show();
                this.Close(); // 关闭欢迎窗口
            }
            else
            {
                // 用户取消 → 重新显示欢迎窗口
                this.Show();
            }
        }
        #endregion

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
