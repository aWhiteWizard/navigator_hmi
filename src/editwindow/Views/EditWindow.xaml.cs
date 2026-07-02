using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Behaviors;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using ProtoBuf;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// EditWindow 的交互逻辑。作为 WPF 窗口的 code-behind，
    /// 负责响应 UI 事件并将业务逻辑委托给拆分后的专职类。
    /// </summary>
    public partial class EditWindow : Window
    {
        #region 私有字段

        private ItemsControl _myItemsControl;
        private EditWindowViewModel _viewModel;
        /// <summary>当前激活的 Widget 创建策略，null 表示不在添加模式</summary>
        private IWidgetCreator? _currentWidgetCreator = null;
        private HMIProject currentProject;
        private bool isProjectDirty;
        private bool skipClosingCheck = false;

        // widget的专职类
        private readonly WidgetSelectionManager _selectionManager;
        private readonly WidgetDragBehavior _dragBehavior;

        #endregion

        #region 构造函数 & 初始化

        public EditWindow(HMIProject project)
        {
            InitializeComponent();

            // 1. 先创建 ViewModel 并设置 DataContext
            _viewModel = new EditWindowViewModel(project);
            this.DataContext = _viewModel;

            // 2. 保存项目引用
            currentProject = project;
            this.Title = project.ProjectFilePath;

            // 3. 初始化widget的专职类
            _selectionManager = new WidgetSelectionManager(
                DrawingCanvas,
                () => _viewModel);

            _dragBehavior = new WidgetDragBehavior(
                DrawingCanvas,
                () => _viewModel,
                MarkProjectDirty,
                cursor => this.Cursor = cursor,
                _selectionManager);

            // 4. 订阅事件
            WeakReferenceMessenger.Default.Register<ScreenAddedMessage>(this, OnScreenAdded);

            // 5. 在 Loaded 事件中初始化 UI
            this.Loaded += EditWindow_Loaded;

            // 6. 订阅 ViewModel 事件
            _viewModel.CanvasReloadRequested += LoadCanvas;
            _viewModel.RefreshCanvasRequested += () => LoadCanvas(_viewModel.CurrentScreen);

            // 7. 初始加载
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

        #endregion

        #region 辅助方法

        /// <summary>
        /// 标记工程已修改（脏标记），并更新窗口标题显示星号。
        /// 拖拽行为和添加控件操作均通过此回调触发。
        /// </summary>
        private void MarkProjectDirty()
        {
            if (!isProjectDirty)
            {
                isProjectDirty = true;
                this.Title = currentProject.ProjectFilePath + "*";
            }
        }

        #endregion

        #region ItemsControl & 画布

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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EditWindowViewModel.CurrentScreen))
            {
                // 画面切换时，清除选中状态
                _selectionManager.ClearAllSelection();
                System.Diagnostics.Debug.WriteLine("✅ 画面切换，已清除选中状态");
            }
        }

        /// <summary>
        /// 加载画面：使用 <see cref="WidgetItemsControlFactory"/> 创建 ItemsControl，
        /// 并通过 <see cref="WidgetDragBehavior"/> 附加拖拽事件。
        /// </summary>
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

            // 使用工厂创建 ItemsControl（不再在 code-behind 中手写模板）
            var itemsControl = WidgetItemsControlFactory.Create(  
                screen,  
                _dragBehavior.OnButtonClick,  
                _dragBehavior.OnPreviewMouseLeftButtonDown,  
                _dragBehavior.OnMouseLeftButtonDown,  
                _dragBehavior.OnMouseMove,  
                _dragBehavior.OnMouseLeftButtonUp);  

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

            // 清除选中状态和装饰器
            _selectionManager.ClearAllSelection();
            SelectorHelper.ClearAllAdorners();

            System.Diagnostics.Debug.WriteLine($"✅ ItemsControl 已创建并添加到 Canvas");
            System.Diagnostics.Debug.WriteLine($"   尺寸: {itemsControl.Width}x{itemsControl.Height}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {screen.Widgets.Count}");
        }

        /// <summary>
        /// 画布点击事件：在点击位置创建 Widget（添加模式下），或清除选中状态。
        /// </summary>
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 1. 判断点击的是否是按钮或其子元素
            var source = e.OriginalSource as DependencyObject;
            var clickedButton = FindVisualParent<Button>(source);

            if (clickedButton != null)
            {
                // 点击的是按钮，让拖拽/选中逻辑处理，这里直接返回
                return;
            }

            // 2. 点击的是空白区域 → 清除所有选中状态
            _selectionManager.ClearAllSelection();

            // 3. 如果不在添加模式，不执行添加
            if (_currentWidgetCreator == null) return;

            // 4. 创建 Widget（通过策略模式委托给 IWidgetCreator）
            if (_viewModel?.CurrentScreen == null) return;

            Point pos = e.GetPosition(DrawingCanvas);
            var widget = _currentWidgetCreator.Create(pos);

            _viewModel.CurrentScreen.Widgets.Add(widget);
            MarkProjectDirty();

            // 添加完成后退出添加模式
            _currentWidgetCreator = null;
            DrawingCanvas.Cursor = Cursors.Arrow;
            AddButtonModeBtn.Content = "Button";
        }

        #endregion

        #region 添加模式

        private void ToggleAddButtonMode(object sender, RoutedEventArgs e)
        {
            // 切换：进入 Button 添加模式 / 退出添加模式
            if (_currentWidgetCreator == null)
            {
                _currentWidgetCreator = new ButtonWidgetCreator();
                DrawingCanvas.Cursor = Cursors.Cross;
                AddButtonModeBtn.Content = "Adding Button";
            }
            else
            {
                _currentWidgetCreator = null;
                DrawingCanvas.Cursor = Cursors.Arrow;
                AddButtonModeBtn.Content = "Button";
            }
        }

        #endregion

        #region 视觉树查找

        private Button FindVisualParent<Button>(DependencyObject child) where Button : DependencyObject
        {
            while (child != null)
            {
                if (child is Button button) return button;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        #endregion

        #region 项目保存 & 关闭（保留在 code-behind，方案 B）

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

        /// <summary>
        /// 检查未保存更改，返回 true 表示可以继续关闭，false 表示用户取消。
        /// </summary>
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

        /// <summary>
        /// 保存工程到文件。
        /// </summary>
        private void SaveProject(HMIProject project, string filePath)
        {
            ProjectFileService.Save(project, filePath);
            isProjectDirty = false;
            this.Title = project.ProjectFilePath;
        }

        #endregion

        #region 树形视图 & 消息

        private void OnScreenAdded(object recipient, ScreenAddedMessage message)
        {
            // 如果需要在 UI 线程上操作（比如改变标题），Dispatcher 是安全的
            Dispatcher.Invoke(() =>
            {
                MarkProjectDirty();
            });
        }

        private void TreeViewItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem;
            var node = item?.DataContext as ProjectTreeViewModel;
            node?.DoubleClickCommand?.Execute(null);
        }

        #endregion
    }
}
