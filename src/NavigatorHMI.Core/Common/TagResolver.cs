using System.Linq;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 设计态变量解析器：绑定变量的控件在画布上显示变量的基准值（BaseValue）。
    /// EditWindow 加载工程时设置 CurrentProject（单工程应用）。
    /// </summary>
    public static class TagResolver
    {
        /// <summary>当前工程引用（设计态渲染用，运行时由设备端实时数据库代替）。</summary>
        public static HMIProject? CurrentProject { get; set; }

        /// <summary>按变量名解析 Tag（不存在返回 null）。</summary>
        public static Tag? Resolve(string tagName)
            => string.IsNullOrEmpty(tagName) ? null : CurrentProject?.Tags.FirstOrDefault(t => t.Name == tagName);

        /// <summary>取变量基准值（无变量/无基准值时返回空串）。</summary>
        public static string ResolveBaseValue(string tagName)
            => Resolve(tagName)?.BaseValue ?? "";
    }
}
