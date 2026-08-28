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
    public class DeviceConfigViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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

            // 默认选择第一个设备
            if (DeviceVersions.Count > 0)
                SelectedDeviceVersion = DeviceVersions[0];

            FilterDeviceModels();
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

        // 创建设备模型
        public List<DeviceModel> DeviceModels { get; } = new List<DeviceModel>
        {
            new DeviceModel { Name = "NavigatorHMI_4'",Version = "V1", Width = "720", Height = "720" },
            new DeviceModel { Name = "NavigatorHMI_7'", Version = "V1", Width = "1024", Height = "600" },
            new DeviceModel { Name = "Test_HMI", Version = "Test_Version", Width = "999", Height = "999" },
        };

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

        // 可用版本集合
        public List<DeviceVersion> DeviceVersions { get; } = new List<DeviceVersion>
        {
            new DeviceVersion { Version = "V1" },
            new DeviceVersion { Version = "Test_Version" },
        };
        // 选中的版本（现在是DeviceModel对象）
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
        private void FilterDeviceModels()
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
            if (FilteredDeviceModels.Count != 0)
                SelectedDeviceModel = FilteredDeviceModels[0];
        }

        private void SaveProject(HMIProject project, string filePath)
        {
            ProjectFileService.Save(project, filePath);
        }
    }
}
