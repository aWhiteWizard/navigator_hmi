using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.Models;
using NavigatorHMI.Views;
using MessageBox = System.Windows.MessageBox;
using Screen = NavigatorHMI.Common.Screen;

namespace NavigatorHMI.ViewModels
{
    public class DeviceConfigViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>退订全局事件（静态事件持有 VM 会泄漏——对话框关闭时调用）。</summary>
        public void Dispose() => DeviceProfileService.ProfilesChanged -= OnProfilesChanged;

        private HMIProject _currentProject;
        public HMIProject CurrentProject
        {
            get => _currentProject;
            set
            {
                _currentProject = value;
                OnPropertyChanged();
            }
        }

        private string _projectName;
        private string _projectPath;
        
        public string ProjectPath
        {
            get => _projectPath;
            set
            {
                _projectPath = value;
                OnPropertyChanged();
            }
        }
        public string ProjectName
        {
            get => _projectName;
            set
            {
                _projectName = value;
                OnPropertyChanged();
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand NewProject { get; }
        public ICommand OpenProject { get; }
        public ICommand BrowsePathCommand { get; }
        public Action CancelCreateProjectAction { get; set; }
        public Action CloseWindow { get; set; }
        public DeviceConfigViewModel()
        {
            SaveCommand = new RelayCommand(ExecuteSave, CanSave);
            NewProject = new RelayCommand(ExecuteCreateNewProject);
            OpenProject = new RelayCommand(ExecuteOpenProject);

            FilteredDeviceModels = new ObservableCollection<DeviceModel>();

            // K-7：设备模型/版本来自 device-profile 描述文件（加新型号只加文件，不改代码）+ 目录热重载
            DeviceProfileService.Initialize(Path.Combine(AppContext.BaseDirectory, "device-profiles"));
            DeviceProfileService.ProfilesChanged += OnProfilesChanged;
            RefreshProfiles();

            // 默认选择第一个版本
            if (DeviceVersions.Count > 0)
                SelectedDeviceVersion = DeviceVersions[0];

            FilterDeviceModels();
        }

        /// <summary>设备模型（K-7：device-profile 驱动——Test_HMI 已移除，仅真实型号）。</summary>
        public List<DeviceModel> DeviceModels { get; private set; } = new();

        /// <summary>可用版本（K-7：profile capability.compileVersion 去重——Test_Version 已移除）。</summary>
        public List<DeviceVersion> DeviceVersions { get; private set; } = new();

        /// <summary>刷新 profile 驱动的模型/版本列表（热重载时调用）；保持用户选中（版本按值重定位新实例、模型按 Name 保持）。</summary>
        private void RefreshProfiles()
        {
            var prevVersion = SelectedDeviceVersion?.Version;
            var prevModelName = SelectedDeviceModel?.Name;
            DeviceModels = DeviceProfileService.Profiles.Select(p => new DeviceModel
            {
                Name = p.Model,
                Version = p.Capability.CompileVersion,
                Width = p.Width.ToString(),
                Height = p.Height.ToString()
            }).ToList();
            DeviceVersions = DeviceProfileService.Profiles
                .Select(p => p.Capability.CompileVersion)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .Select(v => new DeviceVersion { Version = v! })
                .ToList();
            // 版本选中：按值重新定位新实例（旧实例不在新 ItemsSource，保留引用会致 SelectionBox 空白）
            if (prevVersion != null)
            {
                var match = DeviceVersions.FirstOrDefault(v => v.Version == prevVersion);
                SelectedDeviceVersion = match ?? DeviceVersions.FirstOrDefault();
            }
            else
            {
                SelectedDeviceVersion = DeviceVersions.FirstOrDefault();
            }
            OnPropertyChanged(nameof(DeviceModels));
            OnPropertyChanged(nameof(DeviceVersions));
            FilterDeviceModels(prevModelName);
        }

        /// <summary>FileSystemWatcher 事件在后台线程——UI 线程封送刷新。</summary>
        private void OnProfilesChanged()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(new Action(RefreshProfiles));
            else
                RefreshProfiles();
        }
        private void ExecuteSave()
        {
            // 这里添加保存项目的逻辑
            MessageBox.Show("项目已保存！");
        }

        private bool CanSave()
        {
            // 这里添加判断是否可以保存的逻辑
            return CurrentProject != null;
        }

        private void ExecuteCreateNewProject()
        {
            // 用户点击了确定按钮，vm.CurrentProject已经被设置
            string projectName = this.ProjectName;
            string projectPath = this.ProjectPath;

            string fullPath = Path.Combine(projectPath, projectName + ".hmiproj");

            if (File.Exists(fullPath))
            {
                var result = MessageBox.Show("工程文件已存在，是否覆盖？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            var newProject = new HMIProject
            {
                Name = projectName,
                CreateTime = DateTime.UtcNow,
                Version = SelectedDeviceVersion.Version,
                LastModifiedTime = DateTime.UtcNow,
                ProjectFilePath = fullPath,
                DeviceHeight = int.Parse(SelectedDeviceModel.Height),
                DeviceWidth = int.Parse(SelectedDeviceModel.Width)
            };
            newProject.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Height=newProject.DeviceHeight, Width=newProject.DeviceWidth });
            newProject.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template, Height = newProject.DeviceHeight, Width = newProject.DeviceWidth });
            SaveProject(newProject, fullPath);

            CurrentProject = newProject;

            MessageBox.Show("新项目已创建！");

            EditWindow editWindow = new EditWindow(CurrentProject);

            editWindow.Show();

            CloseWindow ?.Invoke();
            
        }

        private void ExecuteOpenProject()
        {
            // 这里添加打开项目的逻辑
            MessageBox.Show("打开项目功能尚未实现！");
        }

        // 选中的设备（现在是DeviceModel对象）
        private DeviceModel _selectedDeviceModel;
        public DeviceModel SelectedDeviceModel
        {
            get => _selectedDeviceModel;
            set
            {
                _selectedDeviceModel = value;
                OnPropertyChanged();
            }
        }

        private DeviceVersion _selectedDeviceVersion;
        public DeviceVersion SelectedDeviceVersion
        {
            get => _selectedDeviceVersion;
            set
            {
                _selectedDeviceVersion = value;
                OnPropertyChanged();
                FilterDeviceModels();  // 版本变化时筛选
            }
        }
        private ObservableCollection<DeviceModel> _filteredDeviceModels;
        public ObservableCollection<DeviceModel> FilteredDeviceModels
        {
            get => _filteredDeviceModels;
            set { 
                    _filteredDeviceModels = value;
                    OnPropertyChanged(); 
                }
        }
        private void FilterDeviceModels(string? keepName = null)
        {
            if (SelectedDeviceVersion == null)
            {
                FilteredDeviceModels.Clear();
                return;
            }

            var filtered = DeviceModels.Where(m => m.Version == SelectedDeviceVersion.Version).ToList();
            FilteredDeviceModels.Clear();
            foreach (var model in filtered)
                FilteredDeviceModels.Add(model);
            // 保持用户选中（热重载后按 Name 重定位）；无保留则选第一个
            var keep = keepName != null ? filtered.FirstOrDefault(m => m.Name == keepName) : null;
            SelectedDeviceModel = keep ?? FilteredDeviceModels.FirstOrDefault();
        }

        private void SaveProject(HMIProject project, string filePath)
        {
            ProjectManager.Save(project, filePath);
        }
    }
}
