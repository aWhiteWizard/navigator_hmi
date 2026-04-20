using System;
using System.IO;
using System.Linq.Expressions;
using System.Security.RightsManagement;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
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
        public EditWindow(HMIProject project)
        {
            InitializeComponent();
            currentProject = project;
            EditWindowViewModel editWindowViewModel = new EditWindowViewModel(currentProject);
            this.DataContext = editWindowViewModel;
            // 监听当前画面的变化
            // 订阅画面切换时的刷新
            editWindowViewModel.CanvasReloadRequested += LoadCanvas;
            // 订阅同一画面内的刷新（例如添加控件）
            editWindowViewModel.RefreshCanvasRequested += () => LoadCanvas(editWindowViewModel.CurrentScreen);
            LoadCanvas(editWindowViewModel.CurrentScreen);
        }
        private bool _isAddButtonMode = false;   // 是否处于添加按钮模式
        private void EditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 检查是否有未保存的更改
            if (HasUnsavedChanges())
            {
                var result = MessageBox.Show("工程有未保存的更改，是否保存？",
                    "保存更改",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SaveProject(currentProject, currentProject.ProjectFilePath);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
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
        private void EditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化控件引用
            InitializeControls();

            // 检查是否是从文件打开的
            // if (ProjectData != null)
            // {
                // IsFromFile = !string.IsNullOrEmpty(ProjectFilePath);

                // 设置窗口标题
                // string titlePrefix = IsFromFile ? "编辑" : "新建";
                // Title = $"{titlePrefix}工程 - {ProjectData.ProjectName}";

                // if (_titleText != null)
                // {
                //     _titleText.Text = ProjectData.ProjectName;
                // }

                // 加载数据到UI
                // LoadProjectDataToUI();

                // 更新菜单状态
                // UpdateMenuStates();

                // 如果是新建的工程，可能需要初始化一些默认值
                // if (!IsFromFile)
                // {
                //     InitializeNewProject();
                // }
            //  }
        }

        private void SaveCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            SaveProject(currentProject, currentProject.ProjectFilePath);
        }

        private void InitializeControls()
        {
            // 获取标题文本控件（假设在XAML中有这个控件）
           // _titleText = FindName("TitleText") as TextBlock;

            // 获取菜单项
            if (FindName("MainMenu") is Menu mainMenu)
            {
                // 查找文件菜单
                foreach (MenuItem item in mainMenu.Items)
                {
                    if (item.Header.ToString() == "文件(_F)")
                    {
                        foreach (MenuItem subItem in item.Items)
                        {
                            if (subItem.Name == "SaveMenuItem")
                            { }
                                // _saveMenuItem = subItem;
                            else if (subItem.Name == "SaveAsMenuItem")
                            { }
                                // _saveAsMenuItem = subItem;
                        }
                        break;
                    }
                }
            }
        }

        private bool HasUnsavedChanges()
        {
            // 这里实现检查是否有未保存更改的逻辑
            // 可以比较当前UI状态与原始ProjectData
            return false; // 简化示例
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
            
            vm.NotifyCanvasRefreshNeeded();
            // 可选：自动退出添加模式（如果需要一次性放置，可以取消注释下面两行）

            ToggleAddButtonMode(null, null);
        }


    }
}