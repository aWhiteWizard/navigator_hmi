// AB-6c（方案①）：PropDialogResources 的代码后置——只做事件转发，不含业务逻辑。
// 转发目标 = 该字典所服务的窗口实例（运行时由 sender 反查，不持有窗口引用：
//   Source= 字典由 WPF 按 URI 共享实例，用字段注入窗口会在多变窗口场景串台）。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NavigatorHMI.Views.Themes
{
    public partial class PropDialogResources : ResourceDictionary
    {
        public PropDialogResources()
        {
            InitializeComponent();
        }

        /// <summary>事件发送者所属的编辑窗口。
        /// 主路径：`Window.GetWindow(sender)` 沿逻辑树反查——**实测在 ContextMenu 打开期间亦可达**
        /// （2026-09-16 内存 WPF 探针：菜单 Click 处理器内 `Window.GetWindow(MenuItem)` = Window）。
        /// 兜底：`Application.MainWindow`（且必须是 EditWindow），仅防御性——欢迎流程下主窗口是欢迎窗，
        /// 该兜底可能为空，**不得作为依赖**；两者都无 ⇒ 返回 null，转发方法空操作（不抛异常、也不猜窗口）。</summary>
        private static EditWindow? TargetWindow(object sender)
        {
            if (sender is DependencyObject d && Window.GetWindow(d) is EditWindow w)
                return w;
            // 兜底收紧（AB-6c 复审 🟡）：只在"应用主窗口就是编辑窗口"时命中——多窗口场景宁可不转发，也不猜窗口。
            return Application.Current?.MainWindow as EditWindow;
        }

        private void AiMsgCopy_Click(object sender, RoutedEventArgs e) =>
            TargetWindow(sender)?.AiMsgCopy_Click(sender, e);

        private void ListNameEditor_LostFocus(object sender, RoutedEventArgs e) =>
            TargetWindow(sender)?.ListNameEditor_LostFocus(sender, e);

        private void ListNameEditor_PreviewKeyDown(object sender, KeyEventArgs e) =>
            TargetWindow(sender)?.ListNameEditor_PreviewKeyDown(sender, e);
    }
}
