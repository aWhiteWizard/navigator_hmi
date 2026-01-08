using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views;
using System.Configuration;
using System.Data;
using System.Runtime.ConstrainedExecution;
using System.Windows;
using System.IO;
using System;
using NavigatorHMI.Common;


namespace NavigatorHMI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 解析命令行参数
            if (e.Args.Length > 0)
            {
                // 有参数：尝试作为工程文件处理
                string projectFile = e.Args[0];
                HandleProjectFileOpen(projectFile);
            }
            else
            {
                // 无参数：正常启动，显示欢迎窗口
                ShowWelcomeWindow();
            }
        }

        private void HandleProjectFileOpen(string filePath)
        {
            try
            {
                if (IsValidProjectFile(filePath))
                {
                    // 读取工程数据
                    // var projectData = LoadProjectFromFile(filePath);

                    // 直接打开编辑窗口projectData, 
                    OpenEditWindowDirectly(filePath);
                }
                else
                {
                    // 文件无效，显示错误并打开欢迎窗口
                    MessageBox.Show($"无法打开文件：{filePath}\n文件格式不支持或已损坏。",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShowWelcomeWindow();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开工程文件时出错：{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ShowWelcomeWindow();
            }
        }

        private bool IsValidProjectFile(string filePath)
        {
            // 检查文件扩展名
            string extension = Path.GetExtension(filePath).ToLower();
            if (extension != ".nproj" && extension != ".navigator")  // 你的工程文件扩展名
            {
                return false;
            }

            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                return false;
            }

            return true;
        }

        // private HMIProject LoadProjectFromFile(string filePath)
        private bool LoadProjectFromFile(string filePath)
        {
            // 这里实现从文件加载工程数据的逻辑
            // 你可以使用 JSON、XML 或其他格式

            // string json = File.ReadAllText(filePath);
            // var projectData = Newtonsoft.Json.JsonConvert.DeserializeObject<HMIProject>(json);

            // 设置文件路径
            // projectData.FilePath = filePath;

            return Path.Exists(filePath);
        }

        private void OpenEditWindowDirectly(string filePath)
        {
            // 创建编辑窗口并设置数据ProjectData projectData, 
            var editWindow = new EditWindow();
            // editWindow.ProjectData = projectData;
            // editWindow.ProjectFilePath = filePath;

            // 设置为主窗口
            MainWindow = editWindow;
            editWindow.Show();
        }

        private void ShowWelcomeWindow()
        {
            var welcomeWindow = new WelComeWindow();
            MainWindow = welcomeWindow;
            welcomeWindow.Show();
        }
    }
}
