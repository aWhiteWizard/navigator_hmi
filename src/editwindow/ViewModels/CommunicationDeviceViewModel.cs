using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 通讯配置面板 ViewModel。展示工程全部设备通信配置（DeviceConfig：ModbusRTU/ModbusTCP/MQTT），
    /// 支持搜索过滤。新建/编辑/删除一律走 CommandService（GUI/CLI/AI 同一入口）。
    /// </summary>
    public class CommunicationDeviceViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        /// <summary>过滤后的设备显示集合（随 SearchText 重建）。</summary>
        public ObservableCollection<DeviceConfig> Devices { get; } = new();

        private string _searchText = "";

        /// <summary>搜索关键词（名称/协议/连接信息模糊匹配），变化时重建列表。</summary>
        public string SearchText
        {
            get => _searchText;
            set { if (_searchText != value) { _searchText = value; OnPropertyChanged(); Refresh(); } }
        }

        private DeviceConfig? _selectedDevice;

        /// <summary>当前选中的设备（双击编辑用）。</summary>
        public DeviceConfig? SelectedDevice
        {
            get => _selectedDevice;
            set { _selectedDevice = value; OnPropertyChanged(); }
        }

        /// <summary>请求打开新建/编辑对话框（View 层处理，传 DeviceConfig: null=新建, 非null=编辑）。</summary>
        public event Action<DeviceConfig?>? DeviceEditRequested;

        public ICommand NewDeviceCommand { get; }
        public ICommand EditDeviceCommand { get; }

        public CommunicationDeviceViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;
            NewDeviceCommand = new RelayCommand(() => DeviceEditRequested?.Invoke(null));
            EditDeviceCommand = new RelayCommand(() => { if (SelectedDevice != null) DeviceEditRequested?.Invoke(SelectedDevice); });

            // 外部命令（CLI/AI）改动 Devices 后自动同步列表
            CommandService.CommandExecuted += OnCommandExecuted;
            Refresh();
        }

        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            if (result.Success && cmdName is "configure_device" or "update_device" or "delete_device")
                Refresh();
        }

        /// <summary>从工程模型重建过滤后的显示列表（按名称恢复选中）。</summary>
        public void Refresh()
        {
            var keepName = SelectedDevice?.Name;
            Devices.Clear();
            var kw = _searchText.Trim();
            IEnumerable<DeviceConfig> query = Project.Devices;
            if (kw.Length > 0)
                query = Project.Devices.Where(d =>
                    d.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || d.Protocol.ToString().Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || d.ConnectionInfo.Contains(kw, StringComparison.OrdinalIgnoreCase));
            var filtered = query.ToList();
            foreach (var device in filtered)
                Devices.Add(device);
            SelectedDevice = keepName != null
                ? filtered.FirstOrDefault(d => d.Name == keepName)
                : null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
