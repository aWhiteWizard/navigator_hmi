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

        public EventConfigDialog(HMIProject project, Widget? widget, EventType evt, CommandLayer.ICommandService commands)
        {
            InitializeComponent();
            _vm = new EventConfigViewModel(project, widget, evt, commands);
            DataContext = _vm;
            Title = widget != null
                ? $"事件配置 - {EventMapping.EventName(evt)}（{widget.ObjectName}）"
                : $"事件配置 - {EventMapping.EventName(evt)}（世界地图点击切换）";
        }

        private void AddFunction_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedFunctionGroupAction is ActionType t)
                _vm.AddFunction(t);
        }

        private void RemoveFunction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ActionEditVM vm)
                _vm.RemoveFunction(vm);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // P8：保存前全参数校验（tag_write value 按目标变量类型/增量数值）——失败拦截并提示，不落库
            var err = _vm.ValidateAll();
            if (err != null)
            {
                System.Windows.MessageBox.Show(err, "参数校验未通过", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _vm.Save();
            DialogResult = true;
        }
    }
}
