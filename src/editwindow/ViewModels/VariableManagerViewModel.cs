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
    /// 变量管理器面板 ViewModel。展示工程全部变量（Tag），支持搜索过滤。
    /// 新建/编辑/删除一律走 CommandService（GUI/CLI/AI 同一入口）。
    /// 删除时由 delete_tag Handler 做引用保护（被控件/报警引用则拒绝）。
    /// </summary>
    public class VariableManagerViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        /// <summary>过滤后的变量显示集合（随 SearchText 重建）。</summary>
        public ObservableCollection<Tag> Tags { get; } = new();

        private string _searchText = "";

        /// <summary>搜索关键词（名称/来源/描述模糊匹配），变化时重建列表。</summary>
        public string SearchText
        {
            get => _searchText;
            set { if (_searchText != value) { _searchText = value; OnPropertyChanged(); Refresh(); } }
        }

        private Tag? _selectedTag;

        /// <summary>当前选中的变量（双击编辑 / 右键菜单用）。</summary>
        public Tag? SelectedTag
        {
            get => _selectedTag;
            set { _selectedTag = value; OnPropertyChanged(); }
        }

        /// <summary>变量总数（含过滤前）。</summary>
        public int TagCount => Project.Tags.Count;

        /// <summary>请求打开新建/编辑对话框（View 层处理，传 Tag: null=新建, 非null=编辑）。</summary>
        public event Action<Tag?>? TagEditRequested;

        /// <summary>请求打开删除确认对话框（View 层处理）。</summary>
        public event Action<Tag>? TagDeleteRequested;

        public ICommand NewTagCommand { get; }
        public ICommand EditTagCommand { get; }
        public ICommand DeleteTagCommand { get; }

        public VariableManagerViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;
            NewTagCommand = new RelayCommand(() => TagEditRequested?.Invoke(null));
            EditTagCommand = new RelayCommand(() => { if (SelectedTag != null) TagEditRequested?.Invoke(SelectedTag); });
            DeleteTagCommand = new RelayCommand(() => { if (SelectedTag != null) TagDeleteRequested?.Invoke(SelectedTag); });

            // 外部命令（CLI/AI）改动 Tags 后自动同步列表
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
            if (result.Success && cmdName is "create_tag" or "update_tag" or "delete_tag")
                Refresh();
        }

        /// <summary>从工程模型重建过滤后的显示列表（按名称恢复选中，搜索时不丢失选中项）。</summary>
        public void Refresh()
        {
            var keepName = SelectedTag?.Name;
            Tags.Clear();
            var kw = _searchText.Trim();
            IEnumerable<Tag> query = Project.Tags;
            if (kw.Length > 0)
                query = Project.Tags.Where(t =>
                    t.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || t.Source.Contains(kw, StringComparison.OrdinalIgnoreCase)
                 || t.Description.Contains(kw, StringComparison.OrdinalIgnoreCase));
            var filtered = query.ToList();   // 快照：过滤一次；避免 query.Contains 惰性重枚举（O(n²)）
            foreach (var tag in filtered)
                Tags.Add(tag);
            // 选中恢复（按名称匹配；变量被删或过滤掉时 SelectedTag 置空）
            SelectedTag = keepName != null
                ? filtered.FirstOrDefault(t => t.Name == keepName)
                : null;
            OnPropertyChanged(nameof(TagCount));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
