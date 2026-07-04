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

        // 树形视图和 Widget 的右键菜单处理器
        private readonly TreeViewContextMenuHandler _treeContextMenuHandler;
        private readonly WidgetContextMenuHandler _widgetContextMenuHandler;

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

            // 3. 先初始化 _selectionManager（_widgetContextMenuHandler 依赖它）
            _selectionManager = new WidgetSelectionManager(
                DrawingCanvas,
                () => _viewModel);

            // 4. 初始化右键菜单处理器（注入 Undo 快照回调）
            _treeContextMenuHandler = new TreeViewContextMenuHandler(
                TreeContextMenu,
                MarkProjectDirty,
                () => _viewModel.PushUndoSnapshot());

            _widgetContextMenuHandler = new WidgetContextMenuHandler(
                WidgetContextMenu,
                () => _viewModel,
                _selectionManager,
                MarkProjectDirty,
                () => _viewModel.NotifyCanvasRefreshNeeded(),
                () => _viewModel.PushUndoSnapshot());

            // 5. 初始化 _dragBehavior（注入 Undo 快照回调）
            _dragBehavior = new WidgetDragBehavior(
                DrawingCanvas,
                () => _viewModel,
                MarkProjectDirty,
                cursor => this.Cursor = cursor,
                _selectionManager,
                _widgetContextMenuHandler.Show,
                () => _viewModel.PushUndoSnapshot());

            // 6. 订阅事件
            WeakReferenceMessenger.Default.Register<ScreenAddedMessage>(this, OnScreenAdded);

            // 7. 在 Loaded 事件中初始化 UI
            this.Loaded += EditWindow_Loaded;

            // 8. 订阅 ViewModel 事件
            _viewModel.CanvasReloadRequested += LoadCanvas;
            _viewModel.RefreshCanvasRequested += () => LoadCanvas(_viewModel.CurrentScreen);
            _viewModel.ProjectDirtyRequested += MarkProjectDirty;

            // 9. 初始加载
            LoadCanvas(_viewModel.CurrentScreen);

            isProjectDirty = false;
            this.CheckBinding();

            // 全局点击监听：点击 Popup 外部时关闭菜单
            this.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (TreeContextMenu.IsOpen)
                {
                    var clicked = e.OriginalSource as DependencyObject;
                    if (clicked != null && !IsDescendantOf(clicked, TreeContextMenu.Child))
                    {
                        TreeContextMenu.IsOpen = false;
                    }
                }

                if (WidgetContextMenu.IsOpen)
                {
                    var clicked = e.OriginalSource as DependencyObject;
                    if (clicked != null && !IsDescendantOf(clicked, WidgetContextMenu.Child))
                    {
                        WidgetContextMenu.IsOpen = false;
                    }
                }
            };
        }

        /// <summary>
        /// 判断 child 是否是 parent 的视觉子树后代。
        /// </summary>
        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null) return false;
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void EditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureItemsControlInCanvas();

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            System.Diagnostics.Debug.WriteLine($"✅ EditWindow 加载完成");
            System.Diagnostics.Debug.WriteLine($"   CurrentScreen: {_viewModel?.CurrentScreen?.Name}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {_viewModel?.CurrentScreen?.Widgets?.Count}");
        }

        private void EditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (skipClosingCheck)
            {
                skipClosingCheck = false;
                return;
            }

            if (!TryCloseProject(true))
            {
                e.Cancel = true;
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
            DrawingCanvas.Children.Clear();

            if (screen == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ LoadCanvas: screen 为 null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ LoadCanvas: {screen.Name}, Widgets 数量: {screen.Widgets.Count}");

            var itemsControl = WidgetItemsControlFactory.Create(
                screen,
                _dragBehavior.OnButtonClick,
                _dragBehavior.OnPreviewMouseLeftButtonDown,
                _dragBehavior.OnMouseLeftButtonDown,
                _dragBehavior.OnMouseMove,
                _dragBehavior.OnMouseLeftButtonUp,
                _dragBehavior.OnPreviewMouseRightButtonDown,
                _dragBehavior.OnMouseRightButtonUp);

            DrawingCanvas.Children.Add(itemsControl);
            Canvas.SetLeft(itemsControl, 0);
            Canvas.SetTop(itemsControl, 0);
            Panel.SetZIndex(itemsControl, 999);

            var vm = this.DataContext as EditWindowViewModel;
            itemsControl.Width = screen.Width > 0 ? screen.Width : (vm?.DeviceWidth ?? 800);
            itemsControl.Height = screen.Height > 0 ? screen.Height : (vm?.DeviceHeight ?? 600);

            itemsControl.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CurrentScreen.Widgets"));

            _myItemsControl = itemsControl;

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
            if (TreeContextMenu.IsOpen)
            {
                TreeContextMenu.IsOpen = false;
            }

            var source = e.OriginalSource as DependencyObject;
            var clickedButton = FindVisualParent<Button>(source);

            if (clickedButton != null)
            {
                return;
            }

            _selectionManager.ClearAllSelection();

            if (_currentWidgetCreator == null) return;
            if (_viewModel?.CurrentScreen == null) return;

            // 在添加 Widget 前保存 Undo 快照
            _viewModel.PushUndoSnapshot();

            Point pos = e.GetPosition(DrawingCanvas);
            var widget = _currentWidgetCreator.Create(pos);

            _viewModel.CurrentScreen.Widgets.Add(widget);
            MarkProjectDirty();

            _currentWidgetCreator = null;
            DrawingCanvas.Cursor = Cursors.Arrow;
            AddButtonModeBtn.Content = "Button";
        }

        #endregion

        #region Widget 右键菜单（委托给 WidgetContextMenuHandler）

        /// <summary>
        /// 右键点击 Widget 时触发，在鼠标位置显示上下文菜单 Popup。
        /// 委托给 <see cref="WidgetContextMenuHandler.Show"/>。
        /// </summary>
        private void OnWidgetRightClick(Widget widget, Point screenPos)
            => _widgetContextMenuHandler.Show(widget, screenPos);

        /// <summary>
        /// 右键菜单「删除」按钮点击：从当前画面移除选中的 Widget。
        /// 委托给 <see cref="WidgetContextMenuHandler.OnDeleteWidgetClick"/>。
        /// </summary>
        private void DeleteWidget_Click(object sender, RoutedEventArgs e)
            => _widgetContextMenuHandler.OnDeleteWidgetClick(sender, e);

        #endregion

        #region 添加模式

        private void ToggleAddButtonMode(object sender, RoutedEventArgs e)
        {
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

        #region 项目保存 & 关闭

        private void SaveCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            SaveProject(currentProject, currentProject.ProjectFilePath);
        }

        private void CloseCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            if (!TryCloseProject(false))
                return;

            isProjectDirty = false;

            WelComeWindow welcome = new WelComeWindow();
            welcome.Show();

            skipClosingCheck = true;
            this.Close();
        }

        /// <summary>
        /// 检查未保存更改，返回 true 表示可以继续关闭，false 表示用户取消。
        /// </summary>
        private bool TryCloseProject(bool isAppClosing)
        {
            if (string.IsNullOrEmpty(currentProject.ProjectFilePath) || !isProjectDirty)
                return true;

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
                return true;
            }
            else
            {
                return false;
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

        #region 树形视图右键编辑菜单（委托给 TreeViewContextMenuHandler）

        /// <summary>
        /// 右键点击树节点：选中节点并显示上下文菜单。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnTreeViewItemRightClick"/>。
        /// </summary>
        private void TreeViewItem_RightClick(object sender, MouseButtonEventArgs e)
            => _treeContextMenuHandler.OnTreeViewItemRightClick(sender, e);

        /// <summary>
        /// 树节点右键菜单「删除画面」点击。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnDeleteScreenClick"/>。
        /// </summary>
        private void DeleteScreen_Click(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnDeleteScreenClick(sender, e);

        /// <summary>
        /// 右键菜单「重命名」按钮点击：进入编辑模式。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnRenameScreenClick"/>。
        /// </summary>
        private void RenameScreen_Click(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnRenameScreenClick(sender, e);

        /// <summary>
        /// EditNameTextBox 加载后自动获取焦点并全选文本。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnEditNameTextBoxLoaded"/>。
        /// </summary>
        private void EditNameTextBox_Loaded(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnEditNameTextBoxLoaded(sender, e);

        /// <summary>
        /// EditNameTextBox 按键处理：回车确认，Escape 取消。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnEditNameTextBoxKeyDown"/>。
        /// </summary>
        private void EditNameTextBox_KeyDown(object sender, KeyEventArgs e)
            => _treeContextMenuHandler.OnEditNameTextBoxKeyDown(sender, e);

        #endregion
    }
}
