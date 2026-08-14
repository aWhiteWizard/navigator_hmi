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
using Serilog;


namespace NavigatorHMI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>Serilog 日志目录（%APPDATA%\NavigatorHMI\logs\，与 ai-config.json 同根）。</summary>
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavigatorHMI", "logs");

        /// <summary>
        /// 初始化 Serilog 全局日志：滚动文件（按天），保留 31 天，输出时间/级别/消息/异常。
        /// 现场问题定位入口（现有 Trace/Debug 仅调试器可见，文件日志是唯一落盘通道）。
        /// </summary>
        private static void ConfigureLogging()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        Path.Combine(LogDirectory, "navihmi-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 31,
                        shared: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();
                Log.Information("NavigatorHMI 启动，日志目录: {LogDirectory}", LogDirectory);
            }
            catch (Exception ex)
            {
                // 日志初始化失败不能阻断主程序（磁盘满/权限异常等），降级为静默
                System.Diagnostics.Trace.WriteLine($"[App] 日志初始化失败: {ex.Message}");
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ConfigureLogging();
            // WPF 已知竞态兜底：窗口销毁期间顶部 MenuItem 的悬停定时器（SetTimerToOpenHierarchy）
            // 触发 FocusOrSelect → InputManager.PushMenuMode(menuSite=null) 抛 ArgumentNullException
            // （"关闭工程"= this.Close() 销毁 EditWindow 时，菜单子项处于悬停待展开状态即踩中）。
            // 窗口已在销毁，该 UI 操作可安全丢弃——精确匹配防掩盖其他 ArgumentNullException。
            DispatcherUnhandledException += (_, args) =>
            {
                var ex = args.Exception;
                if (ex is ArgumentNullException
                    && (ex.Message?.Contains("menuSite") == true
                        || ex.StackTrace?.Contains("PushMenuMode") == true))
                {
                    Log.Warning("忽略 WPF 菜单销毁期竞态异常: {Message}", ex.Message);
                    System.Diagnostics.Trace.WriteLine($"[App] 忽略 WPF 菜单销毁期竞态异常: {ex.Message}");
                    args.Handled = true;
                }
                else
                {
                    // 未处理 UI 线程异常：落盘后交给默认处理（不吞，保持既有崩溃行为）
                    Log.Error(ex, "未处理 UI 线程异常");
                }
            };
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
                    Log.Information("打开工程成功: {FilePath}", filePath);
                }
                else
                {
                    // 文件无效，显示错误并打开欢迎窗口
                    Log.Warning("工程文件无效（扩展名/不存在）: {FilePath}", filePath);
                    MessageBox.Show($"无法打开文件：{filePath}\n文件格式不支持或已损坏。",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ShowWelcomeWindow();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "打开工程文件异常: {FilePath}", filePath);
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
                Log.Error(ex, "反序列化工程失败: {FilePath}", filePath);
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

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("NavigatorHMI 退出");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
