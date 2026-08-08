using System.Windows;
using System.Windows.Controls;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Views
{
    /// <summary>C9 事件配置对话框：函数列表（增删）+ 动态参数表单（run_command 由命令元数据驱动）+ C11 参数隔离。</summary>
    public partial class EventConfigDialog : Window
    {
        private readonly EventConfigViewModel _vm;

        public EventConfigDialog(HMIProject project, Widget widget, EventType evt, CommandLayer.ICommandService commands)
        {
            InitializeComponent();
            _vm = new EventConfigViewModel(project, widget, evt, commands);
            DataContext = _vm;
            Title = $"事件配置 - {EventMapping.EventName(evt)}（{widget.ObjectName}）";
            // 动作下拉：ActionType + 中文名
            foreach (var t in _vm.AllActions)
                ActionCombo.Items.Add(new ComboBoxItem { Content = EventMapping.ActionNames.TryGetValue(t, out var n) ? n : t.ToString(), Tag = t });
        }

        private void AddFunction_Click(object sender, RoutedEventArgs e)
        {
            if (ActionCombo.SelectedItem is ComboBoxItem item && item.Tag is ActionType t)
                _vm.AddFunction(t);
        }

        private void RemoveFunction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ActionEditVM vm)
                _vm.RemoveFunction(vm);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            DialogResult = true;
        }
    }
}
