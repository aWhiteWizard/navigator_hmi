using System.Linq;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    // ═══════════════════════════════════════════
    // P-5 历史记录控件属性（HistoryViewWidget，2026-09-02）
    // partial 拆分（P-2d 铺垫）：HistoryView 专属属性独立文件
    // 多变量用逗号分隔文本编辑（TagsText ↔ 模型 List<string> Tags）
    // ═══════════════════════════════════════════
    public partial class PropertyViewModel
    {
        private string _historyTagsText = "";

        /// <summary>历史记录变量列表（多变量，逗号分隔显示编辑——运行时 FW 下拉切换选变量看历史）。</summary>
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

        /// <summary>P-5：HistoryViewWidget 装载（SelectedWidget setter 内调用；_syncingFromModel 保护防回写）。</summary>
        private void LoadHistoryViewProperties(HistoryViewWidget hv)
        {
            HistoryTagsText = string.Join(", ", hv.Tags);
            HistoryDbPath = string.IsNullOrEmpty(hv.DbPath) ? "navihmi_history.db" : hv.DbPath;
        }
    }
}
