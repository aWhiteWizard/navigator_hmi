using System;
using System.IO;
using System.Linq.Expressions;
using System.Security.RightsManagement;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using ProtoBuf;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class EditWindow : Window
    {
        public HMIProject currentProject;
        // 记录工程是否被编辑过（有未保存的更改）
        private bool isProjectDirty = false;
        // 跳过关闭检查（用于菜单关闭）
        private bool skipClosingCheck = false;

        public EditWindow(HMIProject project)
        {
            InitializeComponent();
            WeakReferenceMessenger.Default.Register<ScreenAddedMessage>(this, OnScreenAdded);
            this.Title = project.ProjectFilePath;
            currentProject = project;
            EditWindowViewModel editWindowViewModel = new EditWindowViewModel(currentProject);
            this.DataContext = editWindowViewModel;
            // 监听当前画面的变化
            // 订阅画面切换时的刷新
            editWindowViewModel.CanvasReloadRequested += LoadCanvas;
            // 订阅同一画面内的刷新（例如添加控件）
            editWindowViewModel.RefreshCanvasRequested += () => LoadCanvas(editWindowViewModel.CurrentScreen);
            LoadCanvas(editWindowViewModel.CurrentScreen);
            isProjectDirty = false;
        }

        private void OnScreenAdded(object recipient, ScreenAddedMessage message)
        {
            // 如果需要在 UI 线程上操作（比如改变标题），Dispatcher 是安全的
            Dispatcher.Invoke(() =>
            {
                isProjectDirty = true;
                this.Title = currentProject.ProjectFilePath + "*";
            });
        }

        private bool _isAddButtonMode = false;   // 是否处于添加按钮模式
        private void EditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 如果是因为菜单关闭而触发的，直接放行
            if (skipClosingCheck)
            {
                skipClosingCheck = false;
                return;
            }

            // 应用退出时的检查
            if (!TryCloseProject(true))
            {
                e.Cancel = true;   // 用户取消，阻止窗口关闭（应用不退出）
            }
        }
        private void TreeViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem;
            var node = item?.DataContext as ProjectTreeViewModel;
            node?.DoubleClickCommand?.Execute(null);
        }
        //加载画面
        private void LoadCanvas(Screen screen)
        {
            DrawingCanvas.Children.Clear();
            if (screen == null) return;
            // 根据 screen.Widgets 动态创建控件并添加到 DrawingCanvas
            foreach (var widget in screen.Widgets)
            {
                // 示例：添加按钮控件
                if (widget is ButtonWidget btnWidget)
                {
                    var btn = new Button
                    {
                        Content = btnWidget.Text,
                        Width = btnWidget.Width,
                        Height = btnWidget.Height
                    };
                    Canvas.SetLeft(btn, btnWidget.X);
                    Canvas.SetTop(btn, btnWidget.Y);
                    DrawingCanvas.Children.Add(btn);
                }
            }
        }

        private void SaveCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            SaveProject(currentProject, currentProject.ProjectFilePath);
        }

        private void CloseCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            // 检查未保存修改（仅关闭工程，不是应用退出）
            if (!TryCloseProject(false))
                return; // 用户取消了，不关闭工程

            // 清空工程相关数据
            isProjectDirty = false;

            // 打开欢迎窗口
            WelComeWindow welcome = new WelComeWindow();
            welcome.Show();

            // 关闭当前 EditWindow（注意：会触发 Closing 事件）
            skipClosingCheck = true;   // 设置跳过标志，防止 Closing 中重复检查
            this.Close();
        }

        // 公共方法：检查未保存更改，返回 true 表示可以继续关闭，false 表示用户取消
        private bool TryCloseProject(bool isAppClosing)
        {
            // 如果没有打开任何工程或没有未保存修改，直接允许
            if (string.IsNullOrEmpty(currentProject.ProjectFilePath) || !isProjectDirty)
                return true;

            // 弹出询问对话框
            MessageBoxResult result = MessageBox.Show(
                "当前工程有未保存的修改，是否保存？",
                "提示",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveProject(currentProject, currentProject.ProjectFilePath);
                return true;
            }
            else if (result == MessageBoxResult.No)
            {
                return true;       // 不保存，丢弃更改
            }
            else // Cancel
            {
                return false;      // 用户取消，不关闭
            }
        }

        // 保存工程方法
        private void SaveProject(HMIProject project, string filePath)
        {
            // 确保目录存在
            string directory = System.IO.Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var fs = File.Create(filePath))
            {
                Serializer.Serialize(fs, project);
            }

            // 保存成功后，更新工程对象中的路径和修改时间
            project.ProjectFilePath = filePath;
            project.LastModifiedTime = DateTime.Now;
            isProjectDirty = false;
            this.Title = project.ProjectFilePath;
        }

        private void ToggleAddButtonMode(object sender, RoutedEventArgs e)
        {
            _isAddButtonMode = !_isAddButtonMode;
            if (_isAddButtonMode)
            {
                DrawingCanvas.Cursor = Cursors.Cross;
                AddButtonModeBtn.Content = "Adding Button";
            }
            else
            {
                DrawingCanvas.Cursor = Cursors.Arrow;
                AddButtonModeBtn.Content = "Button";
            }
        }

        // 画布点击事件：在点击位置添加按钮
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isAddButtonMode) return;

            // 获取点击位置相对于画布的坐标
            Point pos = e.GetPosition(DrawingCanvas);

            // 创建数据模型
            var newButton = new ButtonWidget
            {
                X = pos.X,
                Y = pos.Y,
                Width = 80,    // 默认宽度
                Height = 30,   // 默认高度
                Text = "新按钮"
            };

            // 添加到当前画面的 Widgets 列表
            var vm = this.DataContext as EditWindowViewModel;
            vm.CurrentScreen.Widgets.Add(newButton);
            isProjectDirty = true;
            this.Title = currentProject.ProjectFilePath + "*";

            vm.NotifyCanvasRefreshNeeded();
            // 可选：自动退出添加模式（如果需要一次性放置，可以取消注释下面两行）

            ToggleAddButtonMode(null, null);
        }

        private void ProjectTreeView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                var selectedItem = ProjectTreeView.SelectedItem as ScreenItemNode;
                if (selectedItem != null && selectedItem.DeleteCommand.CanExecute(null))
                {
                    selectedItem.DeleteCommand.Execute(null);
                    e.Handled = true;
                    isProjectDirty = true;
                    this.Title = currentProject.ProjectFilePath + "*";
                }
            }
        }

    }
}