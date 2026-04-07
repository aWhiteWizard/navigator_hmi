using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.Models;
using NavigatorHMI.Views;
using ProtoBuf;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace NavigatorHMI.ViewModels
{
    public class DeviceConfigViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private HMIProject _current_project;
        public HMIProject CurrentProject
        {
            get => _current_project;
            set
            {
                _current_project = value;
                OnPropertyChanged("CurrentProject");
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
                OnPropertyChanged("ProjectPath");
            }
        }
        public string ProjectName
        {
            get => _projectName;
            set
            {
                _projectName = value;
                OnPropertyChanged("ProjectName");
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
            // 创建设备模型
            var navigatorHmi_v1 = new DeviceModel
            {
                Name = "NavigatorHMI_V1",
                SupportedSizes = new ObservableCollection<string>
                {
                    "1024x600",
                    "720x720",
                }
            };

            var testReserved = new DeviceModel
            {
                Name = "测试",
                SupportedSizes = new ObservableCollection<string>
                {
                    "0x0"
                }
            };

            // 添加到集合
            DeviceModels.Add(navigatorHmi_v1);
            DeviceModels.Add(testReserved);

            // 默认选择第一个设备
            if (DeviceModels.Count > 0)
                SelectedDeviceModel = DeviceModels[0];
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
            string ProjectName = this.ProjectName;
            string ProjectPath = this.ProjectPath;

            string fullPath = Path.Combine(ProjectPath, ProjectName + ".hmiproj");

            if (File.Exists(fullPath))
            {
                var result = MessageBox.Show("工程文件已存在，是否覆盖？", "提示",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            var newProject = new HMIProject
            {
                Name = ProjectName,
                CreateTime = DateTime.Now,
                Version = "1.0",
                LastModifiedTime = DateTime.Now,
                Screens = new List<Screen>(),
                ProjectFilePath = fullPath
            };

            SaveProject(newProject, fullPath);

            CurrentProject = newProject;

            MessageBox.Show("新项目已创建！");

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
                OnPropertyChanged("SelectedDeviceModel");
                UpdateSizes();
            }
        }

        // 选中的尺寸（仍然是字符串）
        private string _selectedSize;
        public string SelectedSize
        {
            get => _selectedSize;
            set
            {
                _selectedSize = value;
                OnPropertyChanged("SelectedSize");
            }
        }

        // 设备模型集合（DeviceModel对象）
        public ObservableCollection<DeviceModel> DeviceModels { get; } = new ObservableCollection<DeviceModel>();

        // 可用尺寸集合
        public ObservableCollection<string> AvailableSizes { get; } = new ObservableCollection<string>();
        private void UpdateSizes()
        {
            AvailableSizes.Clear();

            if (SelectedDeviceModel != null && SelectedDeviceModel.SupportedSizes != null)
            {
                // 从选中的设备模型中获取支持的尺寸
                foreach (var size in SelectedDeviceModel.SupportedSizes)
                {
                    AvailableSizes.Add(size);
                }
            }

            // 自动选择第一个尺寸
            if (AvailableSizes.Count > 0)
                SelectedSize = AvailableSizes[0];
            else
                SelectedSize = null;
        }

        private void SaveProject(HMIProject project, string filePath)
        {
            // 确保目录存在
            string directory = Path.GetDirectoryName(filePath);
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
    }
}
