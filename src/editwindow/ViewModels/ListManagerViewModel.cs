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

        /// <summary>预设值（图片路径：去引号 + 统一正斜杠——双通道（手动输入/选择器）在 setter 层单源规范化，设备端 Linux 兼容）。</summary>
        public string Value
        {
            get => _owner.Items[_index];
            set
            {
                var cleaned = _isImage ? StripQuotes(value).Replace('\\', '/') : value;
                if (_owner.Items[_index] == cleaned) return;
                _owner.Items[_index] = cleaned;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PathValid));
                OnPropertyChanged(nameof(PreviewImagePath));
                _onValueChanged?.Invoke();
            }
        }

        /// <summary>是否图片列表项（显示路径校验列）。</summary>
        public bool IsImage => _isImage;

        /// <summary>图片路径是否有效（文件存在；空项视为有效占位）。目录动态读取：工程未保存返回 true 不校验。</summary>
        public bool PathValid => !_isImage || CheckImagePath(_owner.Items[_index], _projectDirProvider());

        /// <summary>回车确认后重新校验路径（刷新标红状态与预览）。</summary>
        public void NotifyPathRecheck()
        {
            OnPropertyChanged(nameof(PathValid));
            OnPropertyChanged(nameof(PreviewImagePath));
        }

        /// <summary>
        /// 预览图片完整路径（相对工程目录解析；空项/目录未定/文件不存在 → null）。
        /// 供列表管理面板「预览」列绑定（PathToImageSourceConverter 加载）。
        /// </summary>
        public string? PreviewImagePath
        {
            get
            {
                if (!_isImage) return null;
                var full = ResolveImageFullPath(_owner.Items[_index], _projectDirProvider());
                return full != null && File.Exists(full) ? full : null;
            }
        }

        /// <summary>去除首尾空格及成对的双/单引号（支持 "path" / 'path'）。</summary>
        public static string StripQuotes(string s)
        {
            var t = s.Trim();
            if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
                return t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        /// <summary>图片路径解析：去引号 → 相对工程目录解析为完整路径（未保存/空项返回 null）。</summary>
        public static string? ResolveImageFullPath(string raw, string projectDir)
        {
            var p = StripQuotes(raw);
            if (p.Length == 0) return null;
            if (string.IsNullOrEmpty(projectDir)) return null;   // 工程未保存：无从解析
            return Path.IsPathRooted(p) ? p : Path.Combine(projectDir, p);
        }

        /// <summary>图片路径有效性：解析后文件存在（空项/未保存视为有效——不误标红）。</summary>
        public static bool CheckImagePath(string raw, string projectDir)
        {
            var full = ResolveImageFullPath(raw, projectDir);
            return full == null || File.Exists(full);   // 空项/未保存 → 有效占位；有路径 → 文件必须存在
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
            {
                // AI 后台线程触发命令时封送到 UI 线程（ObservableCollection 变更须在 UI 线程）
                if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
                    RefreshLists();
                else
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshLists);
            }
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
            // 删除确认（与变量/设备删除一致；被控件引用的列表由 delete_list 命令拒绝）
            var confirm = System.Windows.MessageBox.Show(
                $"确定删除列表 \"{list.Name}\" 吗？\n删除后不可恢复（被控件引用的列表会被拒绝删除）。", "删除列表",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            var result = CommandService.Execute("delete_list", new Dictionary<string, object?> { ["name"] = list.Name });
            if (!result.Success)
            {
                System.Windows.MessageBox.Show(result.ErrorMessage ?? "删除列表失败", "删除列表",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            // 成功：OnCommandExecuted → RefreshLists 已处理选中置空
        }


        /// <summary>批量删除选中列表（多选删除按钮/右键）：逐个走 delete_list 命令（被引用拒绝），失败汇总提示。</summary>

        // ═══ 列表项撤销/重做（P3-6：增删改进撤销栈，Ctrl+Z 恢复） ═══
        private readonly List<Dictionary<ListDef, string[]>> _listUndoStack = new();
        private readonly List<Dictionary<ListDef, string[]>> _listRedoStack = new();
        public bool HasListUndo => _listUndoStack.Count > 0;
        public bool HasListRedo => _listRedoStack.Count > 0;

        /// <summary>操作前快照：记录全部列表的 Items 内容（撤销粒度=单次操作）。</summary>
        private void PushListSnapshot()
        {
            var snap = Project.Lists.ToDictionary(l => l, l => l.Items.ToArray());
            _listUndoStack.Add(snap);
            if (_listUndoStack.Count > 50) _listUndoStack.RemoveAt(0);   // 上限防膨胀
            _listRedoStack.Clear();
        }

        private void ApplySnapshot(Dictionary<ListDef, string[]> snap)
        {
            foreach (var (list, items) in snap)
            {
                list.Items.Clear();
                list.Items.AddRange(items);
            }
            RefreshLists();
            RebuildTextItems();
            RebuildImageItems();
        }

        /// <summary>撤销列表项操作（Ctrl+Z；返回是否执行）。</summary>
        public bool UndoList()
        {
            if (_listUndoStack.Count == 0) return false;
            var snap = _listUndoStack[^1];
            _listUndoStack.RemoveAt(_listUndoStack.Count - 1);
            _listRedoStack.Add(Project.Lists.ToDictionary(l => l, l => l.Items.ToArray()));
            ApplySnapshot(snap);
            return true;
        }

        /// <summary>重做列表项操作（Ctrl+Y；返回是否执行）。</summary>
        public bool RedoList()
        {
            if (_listRedoStack.Count == 0) return false;
            var snap = _listRedoStack[^1];
            _listRedoStack.RemoveAt(_listRedoStack.Count - 1);
            _listUndoStack.Add(Project.Lists.ToDictionary(l => l, l => l.Items.ToArray()));
            ApplySnapshot(snap);
            return true;
        }
        public void DeleteLists(IReadOnlyList<ListDef> lists)
        {
            if (lists.Count == 0) return;
            var names = string.Join("、", lists.Select(l => $"\"{l.Name}\""));
            var confirm = System.Windows.MessageBox.Show(
                $"确定删除列表 {names} 吗？\n删除后不可恢复（被控件引用的列表会被拒绝删除）。", "删除列表",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var list in lists)
            {
                var result = CommandService.Execute("delete_list", new Dictionary<string, object?> { ["name"] = list.Name });
                if (!result.Success) failed.Add($"{list.Name}: {result.ErrorMessage}");
            }
            if (failed.Count > 0)
                System.Windows.MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "删除列表",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        private void AddItem(ListType type)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            if (list == null)
            {
                System.Windows.MessageBox.Show("请先选择或新建一个列表", "添加列表项", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            PushListSnapshot();   // P3-6 撤销快照（guard 后：仅真实操作入栈）
            list.Items.Add("");
            CommitItems(list);
            if (type == ListType.Text) RebuildTextItems(); else RebuildImageItems();
        }

        private void RemoveItem(ListType type)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            var item = type == ListType.Text ? SelectedTextItem : SelectedImageItem;
            if (list == null || item == null) return;
            PushListSnapshot();   // P3-6 撤销快照（guard 后：仅真实操作入栈）
            list.Items.RemoveAt(item.Number - 1);
            CommitItems(list);
            if (type == ListType.Text) RebuildTextItems(); else RebuildImageItems();
        }


        /// <summary>批量删除选中列表项（多选删除按钮/Delete 键；按 Number 降序删防序号错位）。</summary>
        public void RemoveItems(ListType type, IReadOnlyList<ListItemVM> items)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            if (list == null || items.Count == 0) return;
            PushListSnapshot();   // P3-6 撤销快照（guard 后：仅真实操作入栈）
            foreach (var item in items.OrderByDescending(i => i.Number))
            {
                if (item.Number >= 1 && item.Number <= list.Items.Count)
                    list.Items.RemoveAt(item.Number - 1);
            }
            CommitItems(list);
            if (type == ListType.Text) RebuildTextItems(); else RebuildImageItems();
        }

        /// <summary>粘贴列表项（P3-5 复制粘贴）：添加新项并复制值。</summary>
        public void PasteItem(ListType type, string value)
        {
            var list = type == ListType.Text ? SelectedTextList : SelectedImageList;
            if (list == null)
            {
                System.Windows.MessageBox.Show("请先选择或新建一个列表", "粘贴列表项", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            PushListSnapshot();   // P3-6 撤销快照（guard 后：仅真实操作入栈）
            list.Items.Add(value);
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
