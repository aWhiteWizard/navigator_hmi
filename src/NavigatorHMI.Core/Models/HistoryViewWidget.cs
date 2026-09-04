using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 历史记录控件（P-5，2026-09-02；v1.1-design §5.3 ③）。
    /// 显示选定变量的历史记录列表（替代导航页变量历史 tab）：变量列表（多变量——用户拍板运行时下拉切换）
    /// + 数据库路径（可配置，默认设备端 navihmi_history.db）。
    /// FW 数据源：DataLogger.queryTagHistory。
    /// </summary>
    [ProtoContract]
    public class HistoryViewWidget : Widget
    {
        private List<string> _tags = new();
        /// <summary>变量列表（多变量——FW 运行时下拉切换选变量看历史）。</summary>
        [ProtoMember(1)]
        public List<string> Tags { get => _tags; set { _tags = value; OnPropertyChanged(); } }

        private string _dbPath = "navihmi_history.db";
        /// <summary>数据库路径（可配置；默认设备端 navihmi_history.db）。</summary>
        [ProtoMember(2)]
        public string DbPath { get => _dbPath; set { _dbPath = value; OnPropertyChanged(); } }

        private List<string> _titles = new();
        /// <summary>Q-6（2026-09-04 用户 Check）：各变量的列显示名（平行于 <see cref="Tags"/>——Titles[i] 对应 Tags[i]；
        /// 空/缺省 = 显示变量名本身（老工程无 titles 兼容）。PC 属性表格 tag+title 两列编辑。</summary>
        [ProtoMember(3)]
        public List<string> Titles { get => _titles; set { _titles = value; OnPropertyChanged(); } }
    }
}
