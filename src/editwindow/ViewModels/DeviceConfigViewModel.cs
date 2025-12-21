using NavigatorHMI.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System;

namespace NavigatorHMI.ViewModels
{
    public class DeviceConfigViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        public DeviceConfigViewModel()
        {
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
    }
}
