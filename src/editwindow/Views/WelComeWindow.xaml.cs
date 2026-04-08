using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using ProtoBuf;

namespace NavigatorHMI.Views
{
    public partial class WelComeWindow : Window
    {
        #region 加载欢迎页面
        public WelComeWindow()
        {
            InitializeComponent();

        }

        private void WelComeWindowLoaded(object sender, RoutedEventArgs e)
        {
            RecentProjectsListBox.ItemsSource = RecentProjectManager.Instance.RecentOpenedProject;
        }

        #endregion

        private void RecentProjectsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecentProjectsListBox.SelectedItem is RecentOpenedFileItems selected)
            {
                // 清除选中状态
                RecentProjectsListBox.SelectedItem = null;

                // 检查文件是否存在
                if (!selected.IsFileExists)
                {
                    // 提示用户文件不存在，并询问是否从列表中移除
                    var result = MessageBox.Show(
                        $"工程文件不存在：{selected.Path}\n\n是否从最近列表中移除该项？",
                        "文件不存在",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        // 从管理器中移除该项
                        RecentProjectManager.Instance.RemoveRecentProject(selected.Path);
                        // 刷新 ListBox 绑定
                        this.RefreshRecentList();
                    }
                    else
                    {
                        this.RefreshRecentList();
                    }
                    return;
                }

                // 打开工程（调用已有的 OpenProject 方法）
                this.OpenProject(selected.Path);
            }
        }
        private void RefreshRecentList()
        {
            // 简单刷新：重新绑定
            RecentProjectsListBox.ItemsSource = null;
            RecentProjectsListBox.ItemsSource = RecentProjectManager.Instance.RecentOpenedProject;
        }
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

                    this.OpenProject(project.ProjectFilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开工程失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenProject(string filePath)
        {
            try
            {
                // 先隐藏幻影页面，准备新工程信息
                this.Hide();
                // 添加到最近打开列表（假设 App.RecentManager 是全局单例）
                RecentProjectManager.Instance.AddRecentProject(filePath);
                // 打开编辑窗口（假设 EditWindow 可接收工程对象）
                var editWindow = new EditWindow();
                // var viewModel = new EditWindow(project);  // 需要提前定义 EditorViewModel
                // editWindow.DataContext = viewModel;
                editWindow.Show(); // ToDo: 暂时显示编译页面，需要添加读取工程信息逻辑
                // 关闭当前欢迎窗口
                this.Close();
            }
            catch (Exception ex)
            {
                this.Show();
                MessageBox.Show($"打开编辑页面失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void ClearInvalidProjects_Click(object sender, RoutedEventArgs e)
        {
            // 提示用户文件不存在，并询问是否从列表中移除
            var result = MessageBox.Show(
                "是否从最近列表中移除所有不存在的项？",
                "提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 从管理器中移除该项
                RecentProjectManager.Instance.RemoveAllInvalidProjects();
                // 刷新 ListBox 绑定
                this.RefreshRecentList();
            }
            else
            {
                // 不移除，刷新列表
                this.RefreshRecentList();
            }
        }
    }
}
