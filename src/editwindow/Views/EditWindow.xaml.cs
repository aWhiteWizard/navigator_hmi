using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Behaviors;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AvalonDock.Layout;

namespace NavigatorHMI.Views
{
    /// <summary>
    /// EditWindow 的交互逻辑。作为 WPF 窗口的 code-behind，
    /// 负责响应 UI 事件并将业务逻辑委托给拆分后的专职类。
    /// </summary>
    public partial class EditWindow : Window
    {
        #region 私有字段
        // 类内部
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private double DpiScaleX => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        private double DpiScaleY => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        private ItemsControl _myItemsControl;
        private EditWindowViewModel _viewModel;
        /// <summary>当前激活的 Widget 创建策略，null 表示不在添加模式</summary>
        private IWidgetCreator? _currentWidgetCreator = null;
        private HMIProject currentProject;
        private bool isProjectDirty;
        private bool skipClosingCheck = false;

        // CLI 命令历史
        private readonly List<string> _cliHistory = new();
        private int _historyIndex;

        // widget的专职类
        private readonly WidgetSelectionManager _selectionManager;
        private readonly WidgetDragBehavior _dragBehavior;

        // 树形视图和 Widget 的右键菜单处理器
        private readonly TreeViewContextMenuHandler _treeContextMenuHandler;
        private readonly WidgetContextMenuHandler _widgetContextMenuHandler;
        // 属性面板
        private readonly PropertyViewModel _propertyViewModel;
        /// <summary>属性面板 ViewModel（供 XAML 绑定）。</summary>
        public PropertyViewModel PropertyVM => _propertyViewModel;
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
            // 10. 初始化属性窗口
            _propertyViewModel = new PropertyViewModel();
            _selectionManager.WidgetSelected += OnWidgetSelected;
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

            // 非添加模式下，双击画布空白处显示画面属性
            // 非添加模式下，单击画布空白处关闭属性窗口
            if (_currentWidgetCreator == null)
            {
                HidePropertyWindow();
                return;
            }

            if (_viewModel?.CurrentScreen == null) return;

            // 在添加 Widget 前保存 Undo 快照
            _viewModel.PushUndoSnapshot();

            Point pos = e.GetPosition(DrawingCanvas);
            var widget = _currentWidgetCreator.Create(pos, _viewModel.CurrentScreen);

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

        /// <summary>
        /// 窗口级预览键盘按下事件：
        /// - Delete 键：删除当前选中的 Widget
        /// - ESC 键：关闭属性窗口
        /// </summary>
        private void EditWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                // 如果焦点在文本框内，不处理（避免干扰 TreeView 重命名编辑操作）
                if (Keyboard.FocusedElement is TextBox) return;

                _widgetContextMenuHandler.DeleteSelectedWidget();
                e.Handled = true;
            }
        }


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

        #region 属性窗口

        /// <summary>
        /// 选中状态变化时，在鼠标位置旁边弹出属性窗口或隐藏。
        /// </summary>
        private void OnWidgetSelected(Widget? widget)
        {
             // 设置当前编辑的画面引用，供 ObjectName 重复检测使用
             _propertyViewModel.CurrentScreen = _viewModel.CurrentScreen;
            _propertyViewModel.SelectedWidget = widget;

            // 双击控件时自动显示属性窗口
            if (widget != null) ShowAnchorable("property");

            if (widget != null)
            {
                // 属性面板已停靠，无需额外操作
            }
            else
            {
                HidePropertyWindow();
            }
        }

        /// <summary>选中画面时更新属性面板。</summary>
        private void ShowScreenProperty(Screen screen)
        {
            _propertyViewModel.SelectedScreen = screen;
            ShowAnchorable("property");
        }

        /// <summary>清空属性面板选中。</summary>
        private void HidePropertyWindow()
        {
            _propertyViewModel.SelectedScreen = null;
        }

        private DateTime _lastClickTime;
        private Point _lastClickPosition;

        /// <summary>
        /// 画布鼠标按下事件：检测单击和双击。
        /// </summary>
        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            var now = DateTime.Now;
            var pos = e.GetPosition(DrawingCanvas);

            // 检测双击：两次点击间隔 < 500ms 且距离 < 10px
            bool isDoubleClick = (now - _lastClickTime).TotalMilliseconds < 500
                              && Math.Abs(pos.X - _lastClickPosition.X) < 10
                              && Math.Abs(pos.Y - _lastClickPosition.Y) < 10;

            _lastClickTime = now;
            _lastClickPosition = pos;

            if (isDoubleClick)
            {
                var source = e.OriginalSource as DependencyObject;
                if (FindVisualParent<Button>(source) != null) return;

                // 双击画布空白处 → 显示画面属性
                if (_viewModel?.CurrentScreen != null)
                    ShowScreenProperty(_viewModel.CurrentScreen);
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

        #region 生成xml文件
        /// <summary>
        /// 生成项目（F5 / 菜单点击）：先校验错误，无错误则输出 XML。
        /// </summary>
        private void BuildProject_Click(object sender, RoutedEventArgs e)
        {
            var result = _viewModel.CommandService.Execute("compile", new());

            if (!result.Success)
            {
                MessageBox.Show($"[{result.ErrorCode}] {result.ErrorMessage}", "编译失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            MessageBox.Show($"编译成功！\n输出: {result.Data}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        #endregion

        #region 工具栏拖拽
        private ToolBar? _dragSource;
        private Point _dragStart;
        private bool _isDragging;

        private void Toolbar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ToolBar tb && e.LeftButton == MouseButtonState.Pressed)
            {
                _dragSource = tb;
                _dragStart = e.GetPosition(null);
                _isDragging = false;
                tb.CaptureMouse();
            }
        }

        private void ToolbarHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragSource == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(null);
            if (!_isDragging && (Math.Abs(pos.X - _dragStart.X) < 5 && Math.Abs(pos.Y - _dragStart.Y) < 5)) return;

            _isDragging = true;
            _dragSource.Opacity = 0.5;

            // 根据鼠标 X 坐标找到最近的目标 ToolBar
            var mouseX = e.GetPosition(ToolbarPanel).X;
            var srcIdx = ToolbarPanel.Children.IndexOf(_dragSource);
            int targetIdx = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < ToolbarPanel.Children.Count; i++)
            {
                if (ToolbarPanel.Children[i] is not ToolBar t || !(t.Name?.StartsWith("Tb") ?? false)) continue;
                var elemX = t.TranslatePoint(new Point(0, 0), ToolbarPanel).X + t.ActualWidth / 2;
                var dist = Math.Abs(mouseX - elemX);
                if (dist < bestDist) { bestDist = dist; targetIdx = i; }
            }
            if (targetIdx >= 0 && targetIdx != srcIdx && srcIdx >= 0)
            {
                ToolbarPanel.Children.RemoveAt(srcIdx);
                ToolbarPanel.Children.Insert(targetIdx > srcIdx ? targetIdx - 1 : targetIdx, _dragSource);
            }
        }

        private void ToolbarHost_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragSource != null)
            {
                _dragSource.Opacity = 1.0;
                _dragSource.ReleaseMouseCapture();
                _dragSource = null;
            }
            _isDragging = false;
        }

        private void ToolbarHost_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_dragSource != null)
            {
                _dragSource.Opacity = 1.0;
                _dragSource.ReleaseMouseCapture();
                _dragSource = null;
            }
            _isDragging = false;
        }
        #endregion

        #region 工具栏按钮操作
        private void ToggleAnchorable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not string contentId) return;
            var anchorable = DockManager.Layout.Descendents()
                .OfType<AvalonDock.Layout.LayoutAnchorable>()
                .FirstOrDefault(a => a.ContentId == contentId);
            if (anchorable == null) return;
            if (mi.IsChecked)
                anchorable.Show();
            else
                anchorable.Hide();
            // 一次性注册同步（幂等，重复注册无害）
            anchorable.IsVisibleChanged -= SyncMenuCheck;
            anchorable.IsVisibleChanged += SyncMenuCheck;
            void SyncMenuCheck(object? s, EventArgs _) => mi.IsChecked = ((AvalonDock.Layout.LayoutAnchorable)s!).IsVisible;
        }
        private void ShowAnchorable(string contentId)
        {
            var anchorable = DockManager.Layout.Descendents()
                .OfType<LayoutAnchorable>()
                .FirstOrDefault(a => a.ContentId == contentId);
            if (anchorable != null && !anchorable.IsVisible)
                anchorable.Show();
        }
        private void ToggleToolbarBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string name)
            {
                var block = FindName(name) as FrameworkElement;
                if (block != null) block.Visibility = mi.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            skipClosingCheck = true;
            isProjectDirty = false;
            Close();
            // 关闭后由 App.xaml.cs 的 ShutdownMode/启动逻辑回到 WelcomeWindow
        }
        private void SaveAsProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "工程文件|*.hmiproj", DefaultExt = ".hmiproj" };
            if (dlg.ShowDialog() == true)
            {
                ProjectFileService.Save(currentProject, dlg.FileName);
                currentProject.ProjectFilePath = dlg.FileName;
                _viewModel.CommandService.ReplaceProject(currentProject);
                isProjectDirty = false;
                Title = $"NavigatorHMI - {dlg.FileName}";
            }
        }
        private void DeleteSelectedWidget_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentScreen == null) return;
            var selected = _viewModel.CurrentScreen.Widgets.Where(w => w.IsSelected).ToList();
            foreach (var w in selected) _viewModel.CurrentScreen.Widgets.Remove(w);
            _viewModel.NotifyCanvasRefreshNeeded();
        }
        private void BringToFront_Click(object sender, RoutedEventArgs e)
            => _viewModel.CommandService.Execute("bring_to_front", new() { ["screen_name"] = _viewModel.CurrentScreen?.Name ?? "", ["widget_name"] = GetFirstSelectedWidgetName() });
        private void BringForward_Click(object sender, RoutedEventArgs e)
            => _viewModel.CommandService.Execute("bring_forward", new() { ["screen_name"] = _viewModel.CurrentScreen?.Name ?? "", ["widget_name"] = GetFirstSelectedWidgetName() });
        private void SendBackward_Click(object sender, RoutedEventArgs e)
            => _viewModel.CommandService.Execute("send_backward", new() { ["screen_name"] = _viewModel.CurrentScreen?.Name ?? "", ["widget_name"] = GetFirstSelectedWidgetName() });
        private void SendToBack_Click(object sender, RoutedEventArgs e)
            => _viewModel.CommandService.Execute("send_to_back", new() { ["screen_name"] = _viewModel.CurrentScreen?.Name ?? "", ["widget_name"] = GetFirstSelectedWidgetName() });
        private string GetFirstSelectedWidgetName()
            => _viewModel.CurrentScreen?.Widgets.FirstOrDefault(w => w.IsSelected)?.ObjectName ?? "";
        #endregion

        #region CLI 控制台
        /// <summary>
        /// CLI 输入框回车事件：执行命令并显示结果。
        /// </summary>
        private void CliInput_KeyDown(object sender, KeyEventArgs e)
        {
            // 命令历史：↑/↓ 切换
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                if (_historyIndex > 0)
                {
                    _historyIndex--;
                    CliInput.Text = _cliHistory[_historyIndex];
                    CliInput.CaretIndex = CliInput.Text.Length;
                }
                return;
            }
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                if (_historyIndex < _cliHistory.Count - 1)
                {
                    _historyIndex++;
                    CliInput.Text = _cliHistory[_historyIndex];
                }
                else
                {
                    _historyIndex = _cliHistory.Count;
                    CliInput.Text = "";
                }
                CliInput.CaretIndex = CliInput.Text.Length;
                return;
            }

            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var input = CliInput.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            // 记入历史
            _cliHistory.Add(input);
            _historyIndex = _cliHistory.Count;

            // 回显命令
            AppendCliOutput($"> {input}", "LimeGreen");
            CliInput.Clear();

            // 特殊命令
            if (input is "cls" or "clear") { CliOutput.Clear(); return; }
            if (input is "help" or "?") { AppendCliOutput(CliHelpText, "Gray"); return; }

            try
            {
                // 解析命令：navihmi 格式 → key=value 参数
                var parts = ParseCliLine(input);
                if (parts.Length == 0) return;

                var command = parts[0];
                var opts = new Dictionary<string, string>();
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("--") && i + 1 < parts.Length && !parts[i + 1].StartsWith("--"))
                        opts[parts[i][2..]] = parts[++i];
                    else if (parts[i].StartsWith("--"))
                        opts[parts[i][2..]] = "true";
                }

                // 净化所有参数（防止路径遍历注入）
                SanitizeCliParams(opts);

                // 路由到对应 Handler
                var result = ExecuteGuiCommand(command, opts);
                if (result.Success)
                {
                    if (result.Data is List<string> lines)
                        foreach (var l in lines) AppendCliOutput(l, "Gray");
                    else
                        AppendCliOutput($"✓ {command}" + (result.Data != null ? $" — {result.Data}" : ""), "White");
                }
                else
                    AppendCliOutput($"✗ [{result.ErrorCode}] {result.ErrorMessage}", "Red");
            }
            catch (Exception ex)
            {
                AppendCliOutput($"✗ 错误: {ex.Message}", "Red");
            }
        }

        private CommandResult ExecuteGuiCommand(string command, Dictionary<string, string> opts)
        {

            return command switch
            {
                "create-screen" or "cs" => _viewModel.CommandService.Execute("create_screen",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["type"] = opts.GetValueOrDefault("type", "custom"), ["width"] = opts.GetValueOrDefault("width", "800"), ["height"] = opts.GetValueOrDefault("height", "480") }),
                "delete-screen" or "ds" => _viewModel.CommandService.Execute("delete_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "add-widget" or "aw" => _viewModel.CommandService.Execute("add_widget",
                    new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widget_type"] = opts.GetValueOrDefault("type", "button"), ["x"] = opts.GetValueOrDefault("x", "0"), ["y"] = opts.GetValueOrDefault("y", "0"), ["width"] = opts.GetValueOrDefault("width", "100"), ["height"] = opts.GetValueOrDefault("height", "40") }),
                "compile" or "b" => _viewModel.CommandService.Execute("compile", new()),
                "save" => _viewModel.CommandService.Execute("save_project", new()),
                "create-tag" or "ct" => _viewModel.CommandService.Execute("create_tag",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["data_type"] = opts.GetValueOrDefault("type", "FLOAT"), ["source"] = opts.GetValueOrDefault("source", ""), ["unit"] = opts.GetValueOrDefault("unit", ""), ["scan_interval"] = opts.GetValueOrDefault("scan-interval", "100"), ["deadband"] = opts.GetValueOrDefault("deadband", "0"), ["description"] = opts.GetValueOrDefault("description", "") }),
                "list-screens" or "ls" => ListScreens(),
                _ => ExecuteDefaultCommand(command, opts)
            };
        }

        private static string MapCliKey(string key) => key switch
        {
            "screen" => "screen_name", "widget" => "widget_name", "type" => "widget_type",
            "tag" => "tag_name", "ip" => "device_ip", "file" => "file_path",
            _ => key
        };

        private CommandResult ExecuteDefaultCommand(string command, Dictionary<string, string> opts)
        {
            var mapped = new Dictionary<string, object?>();
            foreach (var kv in opts) mapped[MapCliKey(kv.Key)] = kv.Value;
            return _viewModel.CommandService.Execute(command.Replace("-", "_"), mapped);
        }

        private CommandResult ListScreens()
        {
            var names = _viewModel.CurrentProject.Screens.Select(s => $"  {(s.Type == ScreenType.Custom ? "📄" : s.Type == ScreenType.Template ? "📌" : "🌍")} {s.Name}").ToList();
            return CommandResult.Ok(names);
        }

        /// <summary>参数安全净化（等效于 CLI 端 SanitizeParam 三级分类）。</summary>
        private static void SanitizeCliParams(Dictionary<string, string> opts)
        {
            foreach (var kv in opts.ToList())
            {
                var (key, value) = (kv.Key, kv.Value);
                bool hasUpDir = value.Contains("..");
                bool hasSep = value.Contains('/') || value.Contains('\\');
                bool isPathParam = key is "path" or "project" or "file" or "output" or "connection" or "source";
                bool isNameParam = key is "name" or "screen" or "widget" or "tag" or "key" or "value" or "event" or "action" or "nic" or "protocol" or "severity";
                bool isFreeText = key is "description" or "message" or "params" or "model";

                if (isPathParam)
                {
                    if (hasUpDir) throw new ArgumentException($"参数 --{key} 包含 '..' : {value}");
                    if (Path.IsPathRooted(value) && key is not "connection") throw new ArgumentException($"参数 --{key} 不允许绝对路径: {value}");
                }
                else if (isNameParam && (hasUpDir || hasSep))
                {
                    throw new ArgumentException($"参数 --{key} 包含非法字符: {value}");
                }
                else if (isFreeText && hasUpDir)
                {
                    throw new ArgumentException($"参数 --{key} 包含 '..' : {value}");
                }
            }
        }

        private void AppendCliOutput(string text, string color)
        {
            Dispatcher.Invoke(() =>
            {
                CliOutput.AppendText(text + "\n");
                CliOutput.ScrollToEnd();
            });
        }

        private static string[] ParseCliLine(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (char.IsWhiteSpace(line[i])) { i++; continue; }
                if (line[i] == '"')
                {
                    int end = line.IndexOf('"', i + 1);
                    if (end < 0) { result.Add(line[(i + 1)..]); break; }
                    result.Add(line[(i + 1)..end]);
                    i = end + 1;
                }
                else
                {
                    int end = i;
                    while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
                    result.Add(line[i..end]);
                    i = end;
                }
            }
            return result.ToArray();
        }

        private const string CliHelpText = @"GUI CLI 帮助:
  create-screen --name <name> [--type custom]  创建画面
  delete-screen --name <name>                    删除画面
  add-widget --screen <name> --type button --x 0 --y 0  添加控件
  create-tag --name <name> --type FLOAT --source <uri>  创建变量
  compile                                       编译工程
  save                                          保存工程
  list-screens / ls                             列出所有画面
  cls / clear                                   清屏
  help / ?                                      显示帮助";
        #endregion
    }
}
