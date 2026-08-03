using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 列表项视图模型。一行 = 列表的一个预设值项（有序，0-based 存储，显示 1-based 序号）。
    /// 图片列表项：路径存储时去引号（支持 "path" / 'path'），校验文件是否存在（相对工程目录）。
    /// 值变更后通过回调提交 update_list 命令（与 Command Layer 保持同一入口）。
    /// </summary>
    public class ListItemVM : INotifyPropertyChanged
    {
        private readonly ListDef _owner;
        private readonly int _index;              // 0-based 存储索引
        private readonly bool _isImage;
        private readonly Func<string> _projectDirProvider;  // 工程目录动态读取（保存前为空 → 不校验）
        private readonly Action _onValueChanged;

        public ListItemVM(ListDef owner, int index, bool isImage, Func<string> projectDirProvider, Action onValueChanged)
        {
            _owner = owner;
            _index = index;
            _isImage = isImage;
            _projectDirProvider = projectDirProvider;
            _onValueChanged = onValueChanged;
        }

        /// <summary>显示序号（1-based，列表内容按 1234 排布）。</summary>
        public int Number => _index + 1;

        /// <summary>预设值（图片路径存储为去引号后的干净路径）。</summary>
        public string Value
        {
            get => _owner.Items[_index];
            set
            {
                var cleaned = _isImage ? StripQuotes(value) : value;
                if (_owner.Items[_index] == cleaned) return;
                _owner.Items[_index] = cleaned;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PathValid));
                _onValueChanged?.Invoke();
            }
        }

        /// <summary>是否图片列表项（显示路径校验列）。</summary>
        public bool IsImage => _isImage;

        /// <summary>图片路径是否有效（文件存在；空项视为有效占位）。目录动态读取：工程未保存返回 true 不校验。</summary>
        public bool PathValid => !_isImage || CheckImagePath(_owner.Items[_index], _projectDirProvider());

        /// <summary>回车确认后重新校验路径（刷新标红状态）。</summary>
        public void NotifyPathRecheck() => OnPropertyChanged(nameof(PathValid));

        /// <summary>去除首尾空格及成对的双/单引号（支持 "path" / 'path'）。</summary>
        public static string StripQuotes(string s)
        {
            var t = s.Trim();
            if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
                return t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        /// <summary>图片路径有效性：相对工程目录解析后文件存在（绝对路径直接校验）。工程未保存（目录为空）时不校验。</summary>
        public static bool CheckImagePath(string raw, string projectDir)
        {
            var p = StripQuotes(raw);
            if (p.Length == 0) return true;   // 空项允许（占位/待填）
            if (string.IsNullOrEmpty(projectDir)) return true;   // 工程未保存：无从解析，不误标红（保存后重新校验）
            var full = Path.IsPathRooted(p) ? p : Path.Combine(projectDir, p);
            return File.Exists(full);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 列表管理面板 ViewModel（文本列表/图片列表）。展示工程全部 ListDef，支持新建/删除列表、
    /// 重命名（级联控件 ListRef）、编辑各项预设值。所有修改走 CommandService（GUI/CLI/AI 同一入口）。
    /// </summary>
    public class ListManagerViewModel : INotifyPropertyChanged
    {
        public HMIProject Project { get; }
        public CommandService CommandService { get; }

        public ListManagerViewModel(HMIProject project, CommandService commandService)
        {
            Project = project;
            CommandService = commandService;

            NewTextListCommand = new RelayCommand(() => CreateList(ListType.Text));
            DeleteTextListCommand = new RelayCommand(() => DeleteList(ListType.Text));
            AddTextItemCommand = new RelayCommand(() => AddItem(ListType.Text));
            RemoveTextItemCommand = new RelayCommand(() => RemoveItem(ListType.Text));
            NewImageListCommand = new RelayCommand(() => CreateList(ListType.Image));
            DeleteImageListCommand = new RelayCommand(() => DeleteList(ListType.Image));
            AddImageItemCommand = new RelayCommand(() => AddItem(ListType.Image));
            RemoveImageItemCommand = new RelayCommand(() => RemoveItem(ListType.Image));

            CommandService.CommandExecuted += OnCommandExecuted;
            RefreshLists();
        }

        /// <summary>工程目录（图片路径相对解析基准）。</summary>
        public string ProjectDir => Path.GetDirectoryName(Project.ProjectFilePath) ?? "";

        // ═══ 列表显示集合（增量同步，保持 ListBox 选中与编辑焦点） ═══

        public ObservableCollection<ListDef> TextLists { get; } = new();
        public ObservableCollection<ListDef> ImageLists { get; } = new();

        private void OnCommandExecuted(string cmdName, Dictionary<string, object?> parameters, CommandResult result)
        {
            if (result.Success && cmdName is "create_list" or "update_list" or "delete_list")
                RefreshLists();
        }

        /// <summary>增量同步列表集合：按名称保序，缺失补充/多余移除，不重建对象（防选中/编辑焦点丢失）。</summary>
        public void RefreshLists()
        {
            SyncCollection(TextLists, Project.Lists.Where(l => l.Type == ListType.Text));
            SyncCollection(ImageLists, Project.Lists.Where(l => l.Type == ListType.Image));

            // 选中列表被删除时置空并清空项编辑区
            if (SelectedTextList != null && !Project.Lists.Contains(SelectedTextList))
            {
                SelectedTextList = null;
                RebuildTextItems();
            }
            if (SelectedImageList != null && !Project.Lists.Contains(SelectedImageList))
            {
                SelectedImageList = null;
                RebuildImageItems();
            }
        }

        /// <summary>增量同步列表集合：逐位引用对齐（前缀不动），仅尾部增删——不 Clear 重建，
        /// 保 ListBox 选中与 DataGrid 编辑焦点不丢失（wpf-combobox-style 集合重建陷阱）。</summary>
        private static void SyncCollection(ObservableCollection<ListDef> target, IEnumerable<ListDef> source)
        {
            var src = source.ToList();
            // 从前往后找首个失配点（前缀一致段保持不动）
            int i = 0;
            while (i < target.Count && i < src.Count && ReferenceEquals(target[i], src[i])) i++;
            // 失配点起截断尾部多余
            while (target.Count > i) target.RemoveAt(target.Count - 1);
            // 补上缺失
            for (; i < src.Count; i++) target.Add(src[i]);
        }

        // ═══ 选中与项编辑区 ═══

        private ListDef? _selectedTextList;
        public ListDef? SelectedTextList
        {
            get => _selectedTextList;
            set
            {
                if (_selectedTextList != value)
                {
                    _selectedTextList = value;
                    OnPropertyChanged();
                    RebuildTextItems();
                    _syncingName = true;
                    try { TextListName = value?.Name ?? ""; }
                    finally { _syncingName = false; }
                }
            }
        }

        private ListDef? _selectedImageList;
        public ListDef? SelectedImageList
        {
            get => _selectedImageList;
            set
            {
                if (_selectedImageList != value)
                {
                    _selectedImageList = value;
                    OnPropertyChanged();
                    RebuildImageItems();
                    _syncingName = true;
                    try { ImageListName = value?.Name ?? ""; }
                    finally { _syncingName = false; }
                }
            }
        }

        public ObservableCollection<ListItemVM> TextItems { get; } = new();
        public ObservableCollection<ListItemVM> ImageItems { get; } = new();
        public ListItemVM? SelectedTextItem { get; set; }
        public ListItemVM? SelectedImageItem { get; set; }

        private void RebuildTextItems() => RebuildItems(TextItems, SelectedTextList, isImage: false);
        private void RebuildImageItems() => RebuildItems(ImageItems, SelectedImageList, isImage: true);

        private void RebuildItems(ObservableCollection<ListItemVM> target, ListDef? list, bool isImage)
        {
            target.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Items.Count; i++)
                target.Add(new ListItemVM(list, i, isImage, () => ProjectDir, () => CommitItems(list)));
        }

        /// <summary>保存/另存成功后重新校验图片路径（工程目录可能刚生效，重算标红状态）。</summary>
        public void RecheckImagePaths()
        {
            foreach (var item in ImageItems)
                item.NotifyPathRecheck();
        }

        /// <summary>提交列表项全量（update_list 命令），值变更/增删项后调用。</summary>
        private void CommitItems(ListDef list)
        {
            CommandService.Execute("update_list", new Dictionary<string, object?>
            {
                ["name"] = list.Name,
                ["items"] = list.Items,
            });
        }

        // ═══ 新建/删除列表 ═══

        private string _newTextListName = "";
        public string NewTextListName
        {
            get => _newTextListName;
            set { if (_newTextListName != value) { _newTextListName = value; OnPropertyChanged(); } }
        }

        private string _newImageListName = "";
        public string NewImageListName
        {
            get => _newImageListName;
            set { if (_newImageListName != value) { _newImageListName = value; OnPropertyChanged(); } }
        }

        public ICommand NewTextListCommand { get; }
        public ICommand DeleteTextListCommand { get; }
        public ICommand AddTextItemCommand { get; }
        public ICommand RemoveTextItemCommand { get; }
        public ICommand NewImageListCommand { get; }
        public ICommand DeleteImageListCommand { get; }
        public ICommand AddImageItemCommand { get; }
        public ICommand RemoveImageItemCommand { get; }

        private void CreateList(ListType type)
        {
            var name = (type == ListType.Text ? NewTextListName : NewImageListName).Trim();
            if (name.Length == 0)
            {
                System.Windows.MessageBox.Show("请输入列表名称", "新建列表", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            var result = CommandService.Execute("create_list", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["type"] = type.ToString(),
            });
            if (result.Success)
            {
                if (type == ListType.Text) NewTextListName = ""; else NewImageListName = "";
                var created = Project.Lists.FirstOrDefault(l => l.Name == name);
                if (type == ListType.Text) SelectedTextList = created; else SelectedImageList = created;
            }
            else
            {
                System.Windows.MessageBox.Show(result.ErrorMessage ?? "创建列表失败", "新建列表",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void DeleteList(ListType type)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            if (list == null) return;
            var result = CommandService.Execute("delete_list", new Dictionary<string, object?> { ["name"] = list.Name });
            if (!result.Success)
            {
                System.Windows.MessageBox.Show(result.ErrorMessage ?? "删除列表失败", "删除列表",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            // 成功：OnCommandExecuted → RefreshLists 已处理选中置空
        }

        private void AddItem(ListType type)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            if (list == null)
            {
                System.Windows.MessageBox.Show("请先选择或新建一个列表", "添加列表项", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            list.Items.Add("");
            CommitItems(list);
            if (type == ListType.Text) RebuildTextItems(); else RebuildImageItems();
        }

        private void RemoveItem(ListType type)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            var item = type == ListType.Text ? SelectedTextItem : SelectedImageItem;
            if (list == null || item == null) return;
            list.Items.RemoveAt(item.Number - 1);
            CommitItems(list);
            if (type == ListType.Text) RebuildTextItems(); else RebuildImageItems();
        }

        // ═══ 列表名重命名（级联同步控件 ListRef） ═══

        private bool _syncingName;
        private string _textListName = "";
        public string TextListName
        {
            get => _textListName;
            set
            {
                if (_textListName != value)
                {
                    _textListName = value;
                    OnPropertyChanged();
                    if (!_syncingName) RenameList(SelectedTextList, value);
                }
            }
        }

        private string _imageListName = "";
        public string ImageListName
        {
            get => _imageListName;
            set
            {
                if (_imageListName != value)
                {
                    _imageListName = value;
                    OnPropertyChanged();
                    if (!_syncingName) RenameList(SelectedImageList, value);
                }
            }
        }

        private void RenameList(ListDef? list, string newName)
        {
            if (list == null) return;
            newName = newName.Trim();
            if (newName.Length == 0 || newName == list.Name) return;
            var result = CommandService.Execute("update_list", new Dictionary<string, object?>
            {
                ["name"] = list.Name,
                ["new_name"] = newName,
            });
            if (!result.Success)
            {
                System.Windows.MessageBox.Show(result.ErrorMessage ?? "重命名失败", "重命名列表",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                // 失败回滚输入框
                _syncingName = true;
                try
                {
                    if (list == SelectedTextList) TextListName = list.Name;
                    if (list == SelectedImageList) ImageListName = list.Name;
                }
                finally { _syncingName = false; }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
