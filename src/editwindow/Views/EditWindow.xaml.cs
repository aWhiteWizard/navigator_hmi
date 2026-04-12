using NavigatorHMI.Common;
using System;
using System.Linq.Expressions;
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

namespace NavigatorHMI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class EditWindow : Window
    {
        private HMIProject currentProject;
        public EditWindow(HMIProject project)
        {
            InitializeComponent();
            currentProject = project;
            Loaded += EditWindow_Loaded;
            Closing += EditWindow_Closing;
        }

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
                    SaveProject();
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
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
        private void SaveProject()
        {
            // 从UI更新ProjectData
            // UpdateProjectDataFromUI();

            // if (IsFromFile && !string.IsNullOrEmpty(ProjectFilePath))
            // {
                // 保存到现有文件
           //      SaveToFile(ProjectFilePath);
           //  }
           // else
           // {
                // 另存为
           //     SaveProjectAs();
           //  }
        }

        private void SaveToFile(string filePath)
        {
            /*
            try
            {
                ProjectData.LastModified = DateTime.Now;

                // 序列化为JSON
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                    ProjectData, Newtonsoft.Json.Formatting.Indented);

                File.WriteAllText(filePath, json);

                MessageBox.Show("工程保存成功！", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存工程时出错：{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            */
        }

        public void ScreenTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
        }

        private void CutScene_Click(object sender, RoutedEventArgs e)
        {
            // 暂不实现
        }

        private void CopyScene_Click(object sender, RoutedEventArgs e)
        {
            // 暂不实现
        }

        private void PasteScene_Click(object sender, RoutedEventArgs e)
        {
            // 暂不实现
        }

    }
}