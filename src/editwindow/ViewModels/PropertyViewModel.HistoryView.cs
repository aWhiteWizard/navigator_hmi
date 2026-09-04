using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    // ═══════════════════════════════════════════
    // P-5 历史记录控件属性（HistoryViewWidget）+ Q-6 变量表格化（tag+title 两列，2026-09-04 用户 Check）
    // partial 拆分（P-2d 铺垫）：HistoryView 专属属性独立文件
    // Q-6：单输入框（逗号分隔）→ 表格（每行：变量下拉选择 + Title 列显示名，可加/删行）；
    //   模型 Tags/Titles 平行 List（Titles[i] 对应 Tags[i]；空 title = 显示变量名——老工程兼容）
    // ═══════════════════════════════════════════
    public partial class PropertyViewModel
    {
        /// <summary>Q-6：历史变量表格行（Tag 下拉选择 + Title 列名）。行变更 → CommitHistoryRows 重建模型（INPC 订阅链标脏）。</summary>
        public class HistoryTagRowVM : INotifyPropertyChanged
        {
            private readonly PropertyViewModel _owner;
            public HistoryTagRowVM(PropertyViewModel owner, string tag, string title)
            {
                _owner = owner;
                _tag = tag;
                _title = title;
            }
            private string _tag;
            public string Tag
            {
                get => _tag;
                set
                {
                    if (value == null) return;   // Q-6 审查 🔴：ComboBox SelectedValue 失配回写 null → 拦截（先例 RobotSlotRowVM.SelectedTag/TrendTagA；否则 CommitHistoryRows 把已配置行当未选变量跳过静默清空）
                    if (_tag != value) { _tag = value; OnPropertyChanged(); _owner.OnHistoryRowChanged(); }
                }
            }
            private string _title;
            public string Title
            {
                get => _title;
                set { if (_title != value) { _title = value; OnPropertyChanged(); _owner.OnHistoryRowChanged(); } }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>表格行集合（编辑源；装载/提交写模型）。</summary>
        public ObservableCollection<HistoryTagRowVM> HistoryTagRows { get; } = new();

        /// <summary>行下拉选项（工程全部变量——BindableTags 引用，RefreshBindableTags 联动增删）。</summary>
        public ObservableCollection<Tag> HistoryTagOptions => BindableTags;

        private bool _historyRowsSyncing;

        /// <summary>行变更（Tag/Title 编辑）→ 写回模型（重建 Tags/Titles List——Widget INPC 订阅链标脏）。</summary>
        internal void OnHistoryRowChanged()
        {
            if (_historyRowsSyncing || _selectedWidget is not HistoryViewWidget hv) return;
            CommitHistoryRows(hv);
        }

        private void CommitHistoryRows(HistoryViewWidget hv)
        {
            var tags = new System.Collections.Generic.List<string>();
            var titles = new System.Collections.Generic.List<string>();
            foreach (var row in HistoryTagRows)
            {
                if (string.IsNullOrWhiteSpace(row.Tag)) continue;   // 未选变量行不写入
                tags.Add(row.Tag.Trim());
                titles.Add((row.Title ?? "").Trim());
            }
            // 重设 List 触发 INPC（Widget 订阅链标脏）；仅变化时写防抖动
            if (!tags.SequenceEqual(hv.Tags) || !titles.SequenceEqual(hv.Titles))
            {
                BeforeModify?.Invoke();
                hv.Tags = tags;
                hv.Titles = titles;
            }
        }

        /// <summary>添加一行（空 tag——用户选变量 + 填 title）。</summary>
        public void AddHistoryTagRow()
        {
            if (_selectedWidget is not HistoryViewWidget hv) return;
            var row = new HistoryTagRowVM(this, "", "");
            _historyRowsSyncing = true;
            HistoryTagRows.Add(row);
            _historyRowsSyncing = false;
            CommitHistoryRows(hv);   // 空行 tag 被跳过 → 无模型变化不推快照（审查 🟡-1：显式 BeforeModify 推空快照丢 redo 链）
        }

        /// <summary>删除一行（行内删除按钮）。</summary>
        public void RemoveHistoryTagRow(HistoryTagRowVM row)
        {
            if (_selectedWidget is not HistoryViewWidget hv) return;
            _historyRowsSyncing = true;
            HistoryTagRows.Remove(row);
            _historyRowsSyncing = false;
            CommitHistoryRows(hv);   // 模型变化由 Commit 内 diff-guard 单推（审查 🟡-1：显式推送与 Commit 双推重叠）
        }

        private string _historyTagsText = "";

        /// <summary>历史记录变量列表（逗号分隔文本——Q-6 表格取代 UI 编辑；保留兼容内部/旧引用）。</summary>
        public string HistoryTagsText
        {
            get => _historyTagsText;
            set
            {
                if (_historyTagsText != value)
                {
                    _historyTagsText = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is HistoryViewWidget hv) hv.Tags = SplitTags(value); }
                }
            }
        }

        private string _historyDbPath = "navihmi_history.db";

        /// <summary>历史记录数据库路径（可配置；空=设备端默认）。</summary>
        public string HistoryDbPath
        {
            get => _historyDbPath;
            set
            {
                if (_historyDbPath != value)
                {
                    _historyDbPath = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is HistoryViewWidget hv) hv.DbPath = value; }
                }
            }
        }

        /// <summary>逗号分隔文本 → 变量列表（去空白/去空项）。</summary>
        private static System.Collections.Generic.List<string> SplitTags(string text)
            => (text ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries).ToList();

        /// <summary>P-5/Q-6：HistoryViewWidget 装载（SelectedWidget setter 内调用；_syncingFromModel 保护防回写）——
        /// 建表格行（Tags/Titles 平行——空 titles 行 title 空 = 显示变量名）。</summary>
        private void LoadHistoryViewProperties(HistoryViewWidget hv)
        {
            HistoryDbPath = string.IsNullOrEmpty(hv.DbPath) ? "navihmi_history.db" : hv.DbPath;
            HistoryTagsText = string.Join(", ", hv.Tags);   // 兼容保留（内部文本同步）
            _historyRowsSyncing = true;
            HistoryTagRows.Clear();
            for (int i = 0; i < hv.Tags.Count; ++i)
            {
                var title = (hv.Titles != null && i < hv.Titles.Count) ? hv.Titles[i] : "";
                HistoryTagRows.Add(new HistoryTagRowVM(this, hv.Tags[i], title));
            }
            _historyRowsSyncing = false;
        }
    }
}
