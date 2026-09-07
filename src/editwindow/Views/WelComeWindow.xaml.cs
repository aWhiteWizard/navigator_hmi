using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views
{
    public partial class WelComeWindow : Window
    {
        /// <summary>W-B：打开工程进行中标志——防加载期间重复点击进入（竞态护栏）。</summary>
        private bool _isOpening;

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
                // W-B：异步打开完整流程（后台反序列化 + 进度遮罩；成功打开/失败回退均在内部处理）
                _ = OpenProjectAsync(selected.Path);
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
                // W-B：异步打开完整流程
                _ = OpenProjectAsync(dialog.FileName);
            }
        }

        /// <summary>
        /// W-B 后台加载：反序列化在后台线程（ProjectFileService.LoadAsync + 进度回调），
        /// UI 线程显示进度遮罩（防重复操作）；成功 → OpenProject；失败 → 错误提示并停留在欢迎页（回退不残留）。
        /// </summary>
        private async Task OpenProjectAsync(string filePath)
        {
            if (_isOpening) return;   // 竞态护栏：加载中不重复进入
            _isOpening = true;
            try
            {
                // 进度回调封送回 UI 线程更新遮罩文本（Progress<T> 捕获当前 SynchronizationContext）
                var progress = new Progress<string>(s => LoadingProgressText.Text = s);
                LoadingOverlay.Visibility = Visibility.Visible;

                var project = await ProjectFileService.LoadAsync(filePath, progress);
                if (!IsVisible) return;   // 加载期间窗口被关闭——中止，不再打开编辑器

                LoadingOverlay.Visibility = Visibility.Collapsed;
                this.OpenProject(project);   // UI 线程继续（Hide → EditWindow.Show → Close）
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                if (IsVisible)
                    MessageBox.Show($"打开工程失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                // 停留欢迎页——加载失败不动当前状态（事务性纪律，回退不残留）
            }
            finally
            {
                _isOpening = false;
            }
        }

        private void OpenProject(HMIProject project)
        {
            try
            {
                // 先隐藏幻影页面，准备新工程信息
                this.Hide();
                // 添加到最近打开列表（假设 App.RecentManager 是全局单例）
                RecentProjectManager.Instance.AddRecentProject(project.ProjectFilePath);
                // 打开编辑窗口（假设 EditWindow 可接收工程对象）
                var editWindow = new EditWindow(project);
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
                this.Close(); // 关闭欢迎窗口
            }
            else
            {
                // 用户取消 → 重新显示欢迎窗口
                this.Show();
            }
        }
        #endregion

        /// <summary>P9：欢迎窗自动触发「新建工程」（EditWindow 菜单「新建」打开欢迎窗后 Dispatcher 延迟调用）。
        /// 复用 CreateNewProject_Click 现有流转（Hide → NewProjectDialog.ShowDialog → 成功关窗/取消 Show 回），不易丢对象。</summary>
        public void AutoCreateNewProject()
        {
            if (!IsVisible) return;   // 判活：延迟委托执行前用户已关闭/切换欢迎窗（打开最近工程等）→ 不再弹新建对话框
            CreateNewProject_Click(this, new RoutedEventArgs());
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
