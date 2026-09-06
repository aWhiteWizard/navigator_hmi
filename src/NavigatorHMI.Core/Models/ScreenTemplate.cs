using System.Collections.ObjectModel;
using System.IO;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// W-A（P12 画面模板）：设计期复用库条目——保存常用画面控件布局（深拷贝快照，独立对象图，无共享引用）。
    /// 与全局画面（运行时叠加层）概念区分：模板是设计期复用（存常用布局、新建画面/已有画面一键应用），
    /// 不影响运行时叠加机制。应用时控件深拷贝载入目标画面——应用后可独立编辑（不联动模板源）。
    /// </summary>
    [ProtoContract]
    public class ScreenTemplate
    {
        /// <summary>模板名（工程内唯一；重名保存 = 覆盖更新）。</summary>
        [ProtoMember(1)]
        public string Name { get; set; } = "";

        /// <summary>控件布局深拷贝快照（保存时经 proto round-trip 生成独立对象图；应用时同样反序列化载入目标画面）。</summary>
        [ProtoMember(2)]
        public ObservableCollection<Widget> Widgets { get; set; } = new();

        /// <summary>深拷贝（proto round-trip——全新对象图，无共享引用，与 copy_screen 同机制）。</summary>
        public ScreenTemplate DeepClone()
        {
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, this);
            ms.Position = 0;
            return Serializer.Deserialize<ScreenTemplate>(ms);
        }

        /// <summary>控件深拷贝列表（应用模板到目标画面用；独立对象图）。</summary>
        public List<Widget> CloneWidgets()
        {
            var t = DeepClone();
            return t.Widgets.ToList();
        }
    }
}
