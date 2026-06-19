using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views;
using ProtoBuf;


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
                    HMIProject hmi_project = LoadProjectFromFile(filePath);
                    // 直接打开编辑窗口projectData, 
                    OpenEditWindowDirectly(hmi_project);
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
            if (extension != ".hmiproj")  // 你的工程文件扩展名
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

        private HMIProject LoadProjectFromFile(string filePath)
        {
            // 这里实现从文件加载工程数据的逻辑
            // 反序列化加载工程对象（使用 protobuf-net）
            HMIProject project = new HMIProject();
            try
            {

                using (var fs = new FileStream(filePath, FileMode.Open))
                {
                    project = Serializer.Deserialize<HMIProject>(fs);
                }

                // 更新工程路径和修改时间
                project.ProjectFilePath = filePath;
                project.LastModifiedTime = DateTime.Now;

            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开工程失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            // 得到工程对象文件
            return project;
        }

        private void OpenEditWindowDirectly(HMIProject project)
        {
            // 添加到最近打开列表（假设 App.RecentManager 是全局单例）
            RecentProjectManager.Instance.AddRecentProject(project.ProjectFilePath);
            // 打开编辑窗口
            var editWindow = new EditWindow(project);
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
