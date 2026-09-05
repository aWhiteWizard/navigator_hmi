using System.ComponentModel;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>列表类型：文本列表（运行时显示字符串项）/ 图片列表（运行时显示图片项）/ 视频源列表（视频源地址项——S-4 2026-09-05）。</summary>
    public enum ListType
    {
        /// <summary>文本列表</summary>
        Text,
        /// <summary>图片列表</summary>
        Image,
        /// <summary>视频源列表（S-4：项=视频源地址——本地文件路径随工程打包 / RTSP/网络 URL 原样直连；Frame 视频源列表按索引切换用）</summary>
        Video
    }

    /// <summary>
    /// 列表定义。一组有序预设值（Items[0]=第1项），控件绑定数值变量按索引显示对应项。
    /// 图片列表的项为图片路径（相对工程目录），编译时资源编入工程；视频源列表的项为视频源地址
    /// （本地路径打包入 media / RTSP 网络 URL 原样——S-4）。
    /// Name 变更带 INPC 通知——列表管理面板左侧 ListBox DisplayMemberPath 绑定 Name，改名后自动刷新。
    /// </summary>
    [ProtoContract]
    public class ListDef : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _name = "";

        /// <summary>列表名（工程内唯一，控件 ListRef 引用）</summary>
        [ProtoMember(1)]
        public string Name
        {
            get => _name;
            set
            {
                value ??= "";   // 防御：绕过命令层直接置 null 会破坏唯一性比较链
                if (_name != value) { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
            }
        }

        /// <summary>列表类型</summary>
        [ProtoMember(2)]
        public ListType Type { get; set; }

        /// <summary>有序预设值（Items[0]=第1项）。文本列表=字符串项；图片列表=图片路径。</summary>
        [ProtoMember(3)]
        public List<string> Items { get; set; } = new();
    }
}
