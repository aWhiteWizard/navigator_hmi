using System.Windows;
using System.Windows.Controls;
using NavigatorHMI.Common;

namespace NavigatorHMI.Views.Helpers
{
    /// <summary>W4 窗口控件设计态预览（UserView/AlarmView/RobotList 三类型——按 WindowWidget.Type 切换内容）。</summary>
    public partial class WindowPreview : UserControl
    {
        public static readonly DependencyProperty WindowWidgetProperty =
            DependencyProperty.Register(nameof(WindowWidget), typeof(WindowWidget), typeof(WindowPreview),
                new PropertyMetadata(null, OnWindowWidgetChanged));

        public WindowWidget? WindowWidget
        {
            get => (WindowWidget?)GetValue(WindowWidgetProperty);
            set => SetValue(WindowWidgetProperty, value);
        }

        private static void OnWindowWidgetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (WindowPreview)d;
            c.DataContext = c.WindowWidget;   // DP 绑定路径不调 CLR wrapper——显式设 DataContext（绑定 "." 挂 WindowWidget 属性）
            if (e.OldValue is WindowWidget old) old.PropertyChanged -= c.OnWidgetChanged;
            if (c.WindowWidget != null) c.WindowWidget.PropertyChanged += c.OnWidgetChanged;
            c.NotifyTypeChanged();
        }

        private void OnWidgetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 封送：命令层/后台线程（AI Task.Run）改模型可能后台触发——跨线程 SetValue 抛异常
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnWidgetChanged(sender, e)));
                return;
            }
            if (e.PropertyName == nameof(WindowWidget.Type)) NotifyTypeChanged();
        }

        private void NotifyTypeChanged()
        {
            var type = WindowWidget?.Type ?? WindowType.UserView;
            SetValue(IsUserViewProperty, type == WindowType.UserView);
            SetValue(IsAlarmViewProperty, type == WindowType.AlarmView);
            SetValue(IsRobotListProperty, type == WindowType.RobotList);
            SetValue(IsAlarmViewEmptyProperty, false);   // 设计态恒显示示例（空提示仅运行时）
        }

        // 可见性 DP（XAML RelativeSource Self 绑定；Type 变更时 NotifyTypeChanged 刷新）
        public static readonly DependencyProperty IsUserViewProperty = DependencyProperty.Register(nameof(IsUserView), typeof(bool), typeof(WindowPreview), new PropertyMetadata(false));
        public bool IsUserView => (bool)GetValue(IsUserViewProperty);
        public static readonly DependencyProperty IsAlarmViewProperty = DependencyProperty.Register(nameof(IsAlarmView), typeof(bool), typeof(WindowPreview), new PropertyMetadata(false));
        public bool IsAlarmView => (bool)GetValue(IsAlarmViewProperty);
        public static readonly DependencyProperty IsRobotListProperty = DependencyProperty.Register(nameof(IsRobotList), typeof(bool), typeof(WindowPreview), new PropertyMetadata(false));
        public bool IsRobotList => (bool)GetValue(IsRobotListProperty);
        public static readonly DependencyProperty IsAlarmViewEmptyProperty = DependencyProperty.Register(nameof(IsAlarmViewEmpty), typeof(bool), typeof(WindowPreview), new PropertyMetadata(false));
        public bool IsAlarmViewEmpty => false;   // 设计态恒显示示例（空提示仅运行时）

        public WindowPreview()
        {
            InitializeComponent();
        }
    }
}
