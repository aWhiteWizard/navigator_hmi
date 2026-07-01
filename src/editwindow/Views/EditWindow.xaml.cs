using System;
using System.ComponentModel;
using System.IO;
using System.Linq.Expressions;
using System.Security.RightsManagement;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using ProtoBuf;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class EditWindow : Window
    {
        // 私有字段：保存 ItemsControl 的引用
        #region ItemsControl 的引用

        private ItemsControl _myItemsControl;

        #endregion

        #region EditWindow私有变量

        private EditWindowViewModel _viewModel;
        private bool _isAddButtonMode = false;
        private HMIProject currentProject;
        private bool isProjectDirty;

        #endregion

        #region 拖拽变量

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private ButtonWidget _draggingWidget = null;
        private double _dragStartX;
        private double _dragStartY;
        private const double DRAG_THRESHOLD = 5;

        #endregion

        public EditWindow(HMIProject project)
        {
            InitializeComponent();

            // 1. 先创建 ViewModel 并设置 DataContext
            _viewModel = new EditWindowViewModel(project);
            this.DataContext = _viewModel;

            // 2. 保存项目引用
            currentProject = project;
            this.Title = project.ProjectFilePath;

            // 3. 订阅事件
            WeakReferenceMessenger.Default.Register<ScreenAddedMessage>(this, OnScreenAdded);

            // 4. 在 Loaded 事件中初始化 UI
            this.Loaded += EditWindow_Loaded;

            // 5. 订阅 ViewModel 事件
            _viewModel.CanvasReloadRequested += LoadCanvas;
            _viewModel.RefreshCanvasRequested += () => LoadCanvas(_viewModel.CurrentScreen);

            // 6. 初始加载
            LoadCanvas(_viewModel.CurrentScreen);

            isProjectDirty = false;
            this.CheckBinding();
        }

        private void EditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保 ItemsControl 在 Canvas 中
            EnsureItemsControlInCanvas();

            // 订阅 ViewModel 的 PropertyChanged
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            // 诊断输出
            System.Diagnostics.Debug.WriteLine($"✅ EditWindow 加载完成");
            System.Diagnostics.Debug.WriteLine($"   CurrentScreen: {_viewModel?.CurrentScreen?.Name}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {_viewModel?.CurrentScreen?.Widgets?.Count}");
        }

        private bool skipClosingCheck = false;
        private void EditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 如果是因为菜单关闭而触发的，直接放行
            if (skipClosingCheck)
            {
                skipClosingCheck = false;
                return;
            }

            // 应用退出时的检查
            if (!TryCloseProject(true))
            {
                e.Cancel = true;   // 用户取消，阻止窗口关闭（应用不退出）
            }
        }

        #region 添加ItemsControl
        /// <summary>
        /// 创建 ItemsControl
        /// </summary>
        private ItemsControl CreateItemsControl(Screen screen)
        {
            var itemsControl = new ItemsControl();
            itemsControl.Name = "MyItemsControl";

            // 设置 ItemsPanel
            var panelTemplate = new ItemsPanelTemplate();
            var factory = new FrameworkElementFactory(typeof(Canvas));
            panelTemplate.VisualTree = factory;
            itemsControl.ItemsPanel = panelTemplate;

            // 设置 ItemContainerStyle
            var style = new Style(typeof(ContentPresenter));
            style.Setters.Add(new Setter(Canvas.LeftProperty, new Binding("X") { Mode = BindingMode.TwoWay }));
            style.Setters.Add(new Setter(Canvas.TopProperty, new Binding("Y") { Mode = BindingMode.TwoWay }));
            itemsControl.ItemContainerStyle = style;

            // 设置 ItemTemplate
            var dataTemplate = new DataTemplate();
            var buttonFactory = new FrameworkElementFactory(typeof(Button));
            buttonFactory.SetBinding(Button.ContentProperty, new Binding("Text"));
            buttonFactory.SetBinding(Button.WidthProperty, new Binding("Width"));
            buttonFactory.SetBinding(Button.HeightProperty, new Binding("Height"));
            buttonFactory.SetBinding(SelectorHelper.IsSelectedProperty, new Binding("IsSelected") { Mode = BindingMode.TwoWay });
            // 添加事件
            buttonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(Button_Click));
            buttonFactory.AddHandler(Button.MouseLeftButtonDownEvent, new MouseButtonEventHandler(Button_MouseLeftButtonDown), true);
            buttonFactory.AddHandler(Button.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Button_PreviewMouseLeftButtonDown));
            buttonFactory.AddHandler(Button.MouseMoveEvent, new MouseEventHandler(Button_MouseMove), true);
            buttonFactory.AddHandler(Button.MouseLeftButtonUpEvent, new MouseButtonEventHandler(Button_MouseLeftButtonUp), true);

            dataTemplate.VisualTree = buttonFactory;
            itemsControl.ItemTemplate = dataTemplate;

            return itemsControl;
        }

        private void EnsureItemsControlInCanvas()
        {
            var existingItemsControl = DrawingCanvas.Children.OfType<ItemsControl>().FirstOrDefault();
            if (existingItemsControl == null)
            {
                // 如果没有 ItemsControl，重新加载当前画面
                if (_viewModel?.CurrentScreen != null)
                {
                    LoadCanvas(_viewModel.CurrentScreen);
                }
            }
        }

        private void CheckBinding()
        {
            var vm = this.DataContext as EditWindowViewModel;
            if (vm?.CurrentScreen == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ CurrentScreen 为 null");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"✅ CurrentScreen 存在, Widgets 数量: {vm.CurrentScreen.Widgets.Count}");
            }
        }

        private void ClearAllSelection()
        {
            var vm = this.DataContext as EditWindowViewModel;
            if (vm?.CurrentScreen?.Widgets == null) return;

            // 清除数据模型的选中状态
            foreach (var widget in vm.CurrentScreen.Widgets)
            {
                widget.IsSelected = false;
            }

            // 清除 UI 上的装饰器
            foreach (UIElement child in DrawingCanvas.Children)
            {
                if (child is Button btn)
                {
                    SelectorHelper.SetIsSelected(btn, false);
                }
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditWindowViewModel.CurrentScreen))
            {
                // 画面切换时，清除选中状态
                ClearAllSelection();
                System.Diagnostics.Debug.WriteLine("✅ 画面切换，已清除选中状态");

                // ItemsControl 会自动更新，因为绑定已经处理了
            }
        }
        #endregion

        private void OnScreenAdded(object recipient, ScreenAddedMessage message)
        {
            // 如果需要在 UI 线程上操作（比如改变标题），Dispatcher 是安全的
            Dispatcher.Invoke(() =>
            {
                isProjectDirty = true;
                this.Title = currentProject.ProjectFilePath + "*";
            });
        }

        private void TreeViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem;
            var node = item?.DataContext as ProjectTreeViewModel;
            node?.DoubleClickCommand?.Execute(null);
        }

        #region 画布
        //加载画面
        private void LoadCanvas(Screen screen)
        {
            // 清除所有子元素
            DrawingCanvas.Children.Clear();

            if (screen == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ LoadCanvas: screen 为 null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ LoadCanvas: {screen.Name}, Widgets 数量: {screen.Widgets.Count}");

            // 创建 ItemsControl
            var itemsControl = CreateItemsControl(screen);

            // 添加到画布
            DrawingCanvas.Children.Add(itemsControl);
            Canvas.SetLeft(itemsControl, 0);
            Canvas.SetTop(itemsControl, 0);
            Panel.SetZIndex(itemsControl, 999);

            // 设置尺寸
            var vm = this.DataContext as EditWindowViewModel;
            itemsControl.Width = screen.Width > 0 ? screen.Width : (vm?.DeviceWidth ?? 800);
            itemsControl.Height = screen.Height > 0 ? screen.Height : (vm?.DeviceHeight ?? 600);

            // 绑定 ItemsSource
            itemsControl.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CurrentScreen.Widgets"));

            // 保存引用
            _myItemsControl = itemsControl;

            // 清除选中状态
            ClearAllSelection();
            SelectorHelper.ClearAllAdorners();

            System.Diagnostics.Debug.WriteLine($"✅ ItemsControl 已创建并添加到 Canvas");
            System.Diagnostics.Debug.WriteLine($"   尺寸: {itemsControl.Width}x{itemsControl.Height}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {screen.Widgets.Count}");
        }

        // 画布点击事件：在点击位置添加按钮
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. 判断点击的是否是按钮或其子元素
            var source = e.OriginalSource as DependencyObject;
            var clickedButton = FindVisualParent<Button>(source);

            if (clickedButton != null)
            {
                // 点击的是按钮，让 Button_Click 处理选中逻辑
                // 这里不处理，直接返回
                return;
            }

            // 2. 点击的是空白区域 → 清除所有选中状态
            ClearAllSelection();

            // 3. 如果不在添加模式，不执行添加
            if (!_isAddButtonMode) return;

            // 4. 添加新按钮
            if (_viewModel?.CurrentScreen == null) return;

            Point pos = e.GetPosition(DrawingCanvas);
            var newButton = new ButtonWidget
            {
                X = pos.X - 40,
                Y = pos.Y - 15,
                Width = 80,
                Height = 30,
                Text = "新按钮"
            };

            _viewModel.CurrentScreen.Widgets.Add(newButton);
            isProjectDirty = true;
            this.Title = currentProject.ProjectFilePath + "*";

            ToggleAddButtonMode(null, null);
        }
        #endregion

        private void SaveCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            SaveProject(currentProject, currentProject.ProjectFilePath);
        }

        private void CloseCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            // 检查未保存修改（仅关闭工程，不是应用退出）
            if (!TryCloseProject(false))
                return; // 用户取消了，不关闭工程

            // 清空工程相关数据
            isProjectDirty = false;

            // 打开欢迎窗口
            WelComeWindow welcome = new WelComeWindow();
            welcome.Show();

            // 关闭当前 EditWindow（注意：会触发 Closing 事件）
            skipClosingCheck = true;   // 设置跳过标志，防止 Closing 中重复检查
            this.Close();
        }

        // 公共方法：检查未保存更改，返回 true 表示可以继续关闭，false 表示用户取消
        private bool TryCloseProject(bool isAppClosing)
        {
            // 如果没有打开任何工程或没有未保存修改，直接允许
            if (string.IsNullOrEmpty(currentProject.ProjectFilePath) || !isProjectDirty)
                return true;

            // 弹出询问对话框
            MessageBoxResult result = MessageBox.Show(
                "当前工程有未保存的修改，是否保存？",
                "提示",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveProject(currentProject, currentProject.ProjectFilePath);
                return true;
            }
            else if (result == MessageBoxResult.No)
            {
                return true;       // 不保存，丢弃更改
            }
            else // Cancel
            {
                return false;      // 用户取消，不关闭
            }
        }

        // 保存工程方法
        private void SaveProject(HMIProject project, string filePath)
        {
            // 确保目录存在
            string directory = System.IO.Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var fs = File.Create(filePath))
            {
                Serializer.Serialize(fs, project);
            }

            // 保存成功后，更新工程对象中的路径和修改时间
            project.ProjectFilePath = filePath;
            project.LastModifiedTime = DateTime.Now;
            isProjectDirty = false;
            this.Title = project.ProjectFilePath;
        }

        private void ToggleAddButtonMode(object sender, RoutedEventArgs e)
        {
            _isAddButtonMode = !_isAddButtonMode;
            if (_isAddButtonMode)
            {
                DrawingCanvas.Cursor = Cursors.Cross;
                AddButtonModeBtn.Content = "Adding Button";
            }
            else
            {
                DrawingCanvas.Cursor = Cursors.Arrow;
                AddButtonModeBtn.Content = "Button";
            }
        }

        private Button FindVisualParent<Button>(DependencyObject child) where Button : DependencyObject
        {
            while (child != null)
            {
                if (child is Button button) return button;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
        #region 拖拽事件
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var widget = btn.DataContext as ButtonWidget;
            if (widget == null) return;

            var vm = this.DataContext as EditWindowViewModel;
            if (vm?.CurrentScreen?.Widgets == null) return;

            // 清除所有 Widget 的选中状态
            foreach (var w in vm.CurrentScreen.Widgets)
            {
                w.IsSelected = false;
            }
            widget.IsSelected = true;

            // 更新 UI 装饰器
            foreach (UIElement child in DrawingCanvas.Children)
            {
                if (child is Button b)
                {
                    var w = b.DataContext as ButtonWidget;
                    if (w != null)
                    {
                        SelectorHelper.SetIsSelected(b, w.IsSelected);
                    }
                }
            }
        }

        /// <summary>
        /// 预览鼠标按下：记录起始位置
        /// </summary>
        private void Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var button = sender as Button;
            if (button != null)
            {
                _dragStartPoint = e.GetPosition(DrawingCanvas);
            }
        }

        /// <summary>
        /// 鼠标按下：开始拖拽
        /// </summary>
        private void Button_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var widget = btn.DataContext as ButtonWidget;
            if (widget == null) return;
            // 如果按钮没有被选中，先选中它
            if (!widget.IsSelected)
            {
                SelectButton(widget);
            }

            // 开始拖拽
            _isDragging = true;
            _draggingWidget = widget;
            _dragStartX = widget.X;
            _dragStartY = widget.Y;

            // 设置光标为十字箭头
            this.Cursor = Cursors.SizeAll;

            // 捕获鼠标
            btn.CaptureMouse();

            System.Diagnostics.Debug.WriteLine($"🔄 开始拖拽: {widget.Text}");
        }

        /// <summary>
        /// 鼠标移动：拖拽过程中更新位置
        /// </summary>
        private void Button_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggingWidget == null) return;

            // 确保光标是十字（防止被其他元素改变）
            this.Cursor = Cursors.SizeAll;

            Point currentPos = e.GetPosition(DrawingCanvas);

            double offsetX = currentPos.X - _dragStartPoint.X;
            double offsetY = currentPos.Y - _dragStartPoint.Y;

            double newX = _dragStartX + offsetX;
            double newY = _dragStartY + offsetY;

            // 限制在画布范围内
            var vm = this.DataContext as EditWindowViewModel;
            if (vm?.CurrentScreen != null)
            {
                newX = Math.Max(0, Math.Min(newX, vm.CurrentScreen.Width - _draggingWidget.Width));
                newY = Math.Max(0, Math.Min(newY, vm.CurrentScreen.Height - _draggingWidget.Height));
            }

            // 更新数据模型（UI 会自动更新）
            _draggingWidget.X = newX;
            _draggingWidget.Y = newY;

            // 标记工程已修改
            if (!isProjectDirty)
            {
                isProjectDirty = true;
                this.Title = currentProject.ProjectFilePath + "*";
            }
        }

        /// <summary>
        /// 鼠标释放：结束拖拽
        /// </summary>
        private void Button_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;

            var button = sender as Button;
            if (button != null)
            {
                button.ReleaseMouseCapture();
            }

            // 恢复默认光标
            this.Cursor = Cursors.Arrow;

            // 判断是否真的拖拽了
            Point currentPos = e.GetPosition(DrawingCanvas);
            double distance = Math.Sqrt(
                Math.Pow(currentPos.X - _dragStartPoint.X, 2) +
                Math.Pow(currentPos.Y - _dragStartPoint.Y, 2)
            );

            if (distance < DRAG_THRESHOLD && _draggingWidget != null)
            {
                // 点击操作，选中按钮（如果还没选中的话）
                if (!_draggingWidget.IsSelected)
                {
                    SelectButton(_draggingWidget);
                }
            }
            else if (_draggingWidget != null)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 结束拖拽: {_draggingWidget.Text}, 从{currentPos.X}, {currentPos.Y} 到 ({_draggingWidget.X}, {_draggingWidget.Y})");
            }

            _isDragging = false;
            _draggingWidget = null;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 选中指定的按钮
        /// </summary>
        private void SelectButton(ButtonWidget widget)
        {
            var vm = this.DataContext as EditWindowViewModel;
            if (vm?.CurrentScreen?.Widgets == null) return;

            // 清除所有选中
            foreach (var w in vm.CurrentScreen.Widgets)
            {
                w.IsSelected = false;
            }
            widget.IsSelected = true;

            // 更新 UI
            UpdateSelectionUI();

            System.Diagnostics.Debug.WriteLine($"✅ 选中按钮: {widget.Text}");
        }

        /// <summary>
        /// 遍历 <see cref="DrawingCanvas"/> 中的 <see cref="ItemsControl"/>，
        /// 将每个按钮的选中视觉效果同步到对应 <see cref="ButtonWidget.IsSelected"/> 数据状态。
        /// </summary>
        /// <remarks>
        /// 由于 WPF 的 <see cref="ItemsControl"/> 使用虚拟化容器生成 UI 元素，
        /// 数据模型上的 <c>IsSelected</c> 属性变更不会自动刷新按钮的附加属性。
        /// 此方法手动遍历所有已生成的项容器，通过 <see cref="SelectorHelper.SetIsSelected"/>
        /// 将数据层的选中状态同步到 UI 层，确保选中高亮与实际数据一致。
        /// 调用时机：<see cref="SelectButton(ButtonWidget)"/> 修改数据模型选中状态之后。
        /// </remarks>
        private void UpdateSelectionUI()
        {
            // 从 Canvas 子元素中查找 ItemsControl（按钮列表的宿主控件）
            var itemsControl = DrawingCanvas.Children.OfType<ItemsControl>().FirstOrDefault();
            if (itemsControl == null) return;

            // 遍历所有数据项对应的 UI 容器，逐一同步选中状态
            for (int i = 0; i < itemsControl.Items.Count; i++)
            {
                // 通过 ItemContainerGenerator 获取第 i 个数据项对应的 ContentPresenter
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container != null)
                {
                    // ContentPresenter 的第一个视觉子元素即为数据模板生成的 Button
                    var button = VisualTreeHelper.GetChild(container, 0) as Button;
                    if (button != null)
                    {
                        // 从 Button 的 DataContext 获取对应的数据模型
                        var widget = button.DataContext as ButtonWidget;
                        if (widget != null)
                        {
                            // 将数据模型的 IsSelected 同步到按钮的附加属性，触发选中高亮样式
                            SelectorHelper.SetIsSelected(button, widget.IsSelected);
                        }
                    }
                }
            }
        }
        #endregion
    }
}