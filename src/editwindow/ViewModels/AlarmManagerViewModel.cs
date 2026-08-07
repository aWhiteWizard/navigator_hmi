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
    /// 报警配置面板 ViewModel。展示工程全部报警规则（AlarmRule：高报/低报/变化率/偏差），
    /// 支持搜索过滤。新建/编辑/删除一律走 CommandService（GUI/CLI/AI 同一入口）。
    /// </summary>
    public class AlarmManagerViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        /// <summary>过滤后的报警显示集合（随 SearchText 重建）。</summary>
        public ObservableCollection<AlarmRule> Alarms { get; } = new();

        private string _searchText = "";

        /// <summary>搜索关键词（名称/变量/类型/等级模糊匹配），变化时重建列表。</summary>
        public string SearchText
        {
            get => _searchText;
            set { if (_searchText != value) { _searchText = value; OnPropertyChanged(); Refresh(); } }
        }

        private AlarmRule? _selectedAlarm;

        /// <summary>当前选中的报警（双击编辑用）。</summary>
        public AlarmRule? SelectedAlarm
        {
            get => _selectedAlarm;
            set { _selectedAlarm = value; OnPropertyChanged(); }
        }

        /// <summary>请求打开新建/编辑对话框（View 层处理，传 AlarmRule: null=新建, 非null=编辑）。</summary>
        public event Action<AlarmRule?>? AlarmEditRequested;

        /// <summary>请求打开删除确认对话框（View 层处理）。</summary>
        public event Action<AlarmRule>? AlarmDeleteRequested;

        public ICommand NewAlarmCommand { get; }
        public ICommand EditAlarmCommand { get; }
        public ICommand DeleteAlarmCommand { get; }

        public AlarmManagerViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;
            NewAlarmCommand = new RelayCommand(() => AlarmEditRequested?.Invoke(null));
            EditAlarmCommand = new RelayCommand(() => { if (SelectedAlarm != null) AlarmEditRequested?.Invoke(SelectedAlarm); });
            DeleteAlarmCommand = new RelayCommand(() => { if (SelectedAlarm != null) AlarmDeleteRequested?.Invoke(SelectedAlarm); });

            // 外部命令（CLI/AI）改动 Alarms 后自动同步列表
            CommandService.CommandExecuted += OnCommandExecuted;
            Refresh();
        }

        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            // AI 后台线程触发时跨线程改 ObservableCollection 会被吞 → 封送回 UI 线程
            if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => OnCommandExecuted(cmdName, parameters, result)));
                return;
            }
            if (result.Success && cmdName is "create_alarm" or "update_alarm" or "delete_alarm" or "update_tag")
                Refresh();
        }

        /// <summary>从工程模型重建过滤后的显示列表（按名称恢复选中）。</summary>
        public void Refresh()
        {
            var keep = SelectedAlarm;
            Alarms.Clear();
            var kw = _searchText.Trim();
            IEnumerable<AlarmRule> query = Project.Alarms;
            if (kw.Length > 0)
                query = Project.Alarms.Where(a =>
                    a.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || a.TagName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || a.Type.ToString().Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || a.Level.ToString().Contains(kw, StringComparison.OrdinalIgnoreCase));
            var filtered = query.ToList();
            foreach (var alarm in filtered)
                Alarms.Add(alarm);
            SelectedAlarm = filtered.FirstOrDefault(a => ReferenceEquals(a, keep))
                ?? (keep != null ? filtered.FirstOrDefault(a => a.Name == keep.Name) : null);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
