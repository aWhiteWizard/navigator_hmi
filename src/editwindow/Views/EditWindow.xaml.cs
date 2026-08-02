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
        private HMIProject _currentProject;
        private bool _isProjectDirty;
        private bool _skipClosingCheck = false;
        /// <summary>缩放手柄静态回调引用（Closing 时比较清理，防闭包泄漏）。</summary>
        private Action? _resizeDragStartedCallback;
        /// <summary>画布尺寸回调引用（Closing 时比较清理）。</summary>
        private Func<Size>? _getCanvasSizeCallback;

        // 画布缩放
        private double _zoomLevel = 1.0;
        private ScaleTransform _canvasScale = new(1, 1);


        // 框选
        private Point _marqueeStart;
        private bool _isMarquee;

        // 两点式绘制（Line/Circle/Rectangle）
        private Point _drawStartPoint;
        private bool _isDrawingPreview;
        private System.Windows.Shapes.Path? _drawPreviewPath;

        // CLI 命令历史
        private readonly List<string> _cliHistory = new();
        private int _historyIndex;

        private void Canvas_PreviewRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            _contextMenuPos = e.GetPosition(DrawingCanvas);
            _contextMenuPos.X /= _zoomLevel;
            _contextMenuPos.Y /= _zoomLevel;
        }

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
            _currentProject = project;
            this.Title = project.ProjectFilePath;

            // 画布缩放
            DrawingCanvas.LayoutTransform = _canvasScale;

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
                () => _viewModel.PushUndoSnapshot(),
                () => _currentWidgetCreator != null);  // 添加/绘制模式标志

            // 6. 订阅事件
            WeakReferenceMessenger.Default.Register<ScreenAddedMessage>(this, OnScreenAdded);

            // 7. 在 Loaded 事件中初始化 UI
            this.Loaded += EditWindow_Loaded;
            this.Closed += EditWindow_Closed;

            // 8. 订阅 ViewModel 事件
            _viewModel.CanvasReloadRequested += LoadCanvas;
            _viewModel.RefreshCanvasRequested += () => LoadCanvas(_viewModel.CurrentScreen);
            _viewModel.ProjectDirtyRequested += MarkProjectDirty;

            // 10. 初始化属性窗口（必须在 LoadCanvas 之前——LoadCanvas 注入画布尺寸到 PropertyViewModel）
            _propertyViewModel = new PropertyViewModel();
            // 属性面板修改前 Push 撤销快照（属性修改可撤销；选中同步初始化不触发）
            _propertyViewModel.BeforeModify = () => _viewModel.PushUndoSnapshot();
            // 缩放手柄：拖拽开始 Push 撤销快照 + 画布尺寸提供器（缩放钳制）
            _resizeDragStartedCallback = () => _viewModel.PushUndoSnapshot();
            _getCanvasSizeCallback = () => new Size(_propertyViewModel.CanvasWidth, _propertyViewModel.CanvasHeight);
            SelectorHelper.ResizeDragStarted = _resizeDragStartedCallback;
            SelectorHelper.GetCanvasSize = _getCanvasSizeCallback;
            // 选中变化（单选/多选/清空）→ 同步属性面板多选状态
            _selectionManager.SelectionChanged += SyncSelectionToPropertyPanel;

            LoadCanvas(_viewModel.CurrentScreen);

            _isProjectDirty = false;
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
            if (_skipClosingCheck)
            {
                _skipClosingCheck = false;
                return;
            }

            if (!TryCloseProject(true))
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// 窗口关闭（所有路径必触发）：清理静态回调，防闭包持有已关闭窗口/项目（GC 无法回收）。
        /// </summary>
        private void EditWindow_Closed(object? sender, EventArgs e)
        {
            if (SelectorHelper.ResizeDragStarted == _resizeDragStartedCallback)
                SelectorHelper.ResizeDragStarted = null;
            if (SelectorHelper.GetCanvasSize == _getCanvasSizeCallback)
                SelectorHelper.GetCanvasSize = null;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 标记工程已修改（脏标记），并更新窗口标题显示星号。
        /// 拖拽行为和添加控件操作均通过此回调触发。
        /// </summary>
        private void MarkProjectDirty()
        {
            if (!_isProjectDirty)
            {
                _isProjectDirty = true;
                this.Title = _currentProject.ProjectFilePath + "*";
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

        /// <summary>当前已订阅脏标记的 Screen（防止重复订阅/泄漏）。</summary>
        private Screen? _subscribedScreen;

        /// <summary>
        /// 订阅模型脏标记：任意 Widget/Screen 属性变化 → 标脏（覆盖属性面板/CLI/AI/拖拽所有入口）。
        /// 排除瞬态属性（IsSelected）。
        /// </summary>
        private void SubscribeModelDirty(Screen screen)
        {
            if (_subscribedScreen != null)
            {
                _subscribedScreen.PropertyChanged -= OnScreenPropertyChanged;
                _subscribedScreen.Widgets.CollectionChanged -= OnWidgetsCollectionChanged;
                foreach (var w in _subscribedScreen.Widgets)
                    w.PropertyChanged -= OnWidgetPropertyChanged;
            }
            _subscribedScreen = screen;
            if (screen == null) return;

            screen.PropertyChanged += OnScreenPropertyChanged;
            screen.Widgets.CollectionChanged += OnWidgetsCollectionChanged;
            foreach (var w in screen.Widgets)
                w.PropertyChanged += OnWidgetPropertyChanged;
        }

        private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 排除选中态（瞬态，不持久化，不标脏）
            if (e.PropertyName == nameof(Widget.IsSelected)) return;
            MarkProjectDirty();
        }

        private void OnWidgetsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            MarkProjectDirty();
            // 退订被移除的 Widget（防残留订阅；Remove/Replace/Reset 均含 OldItems）
            if (e.OldItems != null)
            {
                foreach (Widget w in e.OldItems)
                    w.PropertyChanged -= OnWidgetPropertyChanged;
            }
            // 新增 Widget 纳入订阅（简单方案：全量重订阅）
            if (_subscribedScreen != null)
                SubscribeModelDirty(_subscribedScreen);
        }

        private void OnScreenPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            MarkProjectDirty();
        }

        /// <summary>
        /// 属性面板数字输入框 Enter 键确认：移动焦点到下一元素（触发 LostFocus 应用绑定值）。
        /// </summary>
        private void PropNumberBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        /// <summary>菜单「默认字体」：打开工厂默认字体设置对话框。</summary>
        private void FontDefaults_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FontDefaultsDialog { Owner = this };
            dialog.ShowDialog();
        }

        /// <summary>
        /// 选中变化 → 同步属性面板多选状态（框选/单击/清空统一入口）。
        /// </summary>
        private void SyncSelectionToPropertyPanel()
        {
            var screen = _viewModel?.CurrentScreen;
            var selected = screen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null || selected.Count == 0)
            {
                _propertyViewModel.ClearMultiSelection();
                return;
            }
            _propertyViewModel.SelectWidgets(selected);
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
                // 画面切换：重订阅脏标记（新画面）
                SubscribeModelDirty(_viewModel?.CurrentScreen);
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
                null!,  // clickHandler 已不再绑定（双击检测移至 MouseLeftButtonDown），保留签名兼容
                _dragBehavior.OnPreviewMouseLeftButtonDown,
                _dragBehavior.OnMouseLeftButtonDown,
                _dragBehavior.OnMouseMove,
                _dragBehavior.OnMouseLeftButtonUp,
                _dragBehavior.OnPreviewMouseRightButtonDown,
                _dragBehavior.OnMouseRightButtonUp);

            DrawingCanvas.Children.Add(itemsControl);
            Canvas.SetLeft(itemsControl, 0);
            Canvas.SetTop(itemsControl, 0);
            Panel.SetZIndex(itemsControl, -1);

            var rootGrid = (System.Windows.Controls.Grid)this.Content;

            DrawingCanvas.Children.Remove(MarqueeRect);
            if (!DrawingCanvas.Children.Contains(MarqueeRect))
                DrawingCanvas.Children.Add(MarqueeRect);
            Panel.SetZIndex(MarqueeRect, 1001);

            // 重挂载两点式绘制预览（DrawPreviewShape 也被 Children.Clear 清掉了）
            DrawingCanvas.Children.Remove(DrawPreviewShape);
            if (!DrawingCanvas.Children.Contains(DrawPreviewShape))
                DrawingCanvas.Children.Add(DrawPreviewShape);
            Panel.SetZIndex(DrawPreviewShape, 1002);
            DrawPreviewShape.Visibility = Visibility.Collapsed;
            DrawPreviewShape.Data = null;

            // 重挂载阵列辅助线预览（ArrayGuideShape 同样被 Children.Clear 清掉）
            DrawingCanvas.Children.Remove(ArrayGuideShape);
            if (!DrawingCanvas.Children.Contains(ArrayGuideShape))
                DrawingCanvas.Children.Add(ArrayGuideShape);
            Panel.SetZIndex(ArrayGuideShape, 1003);
            ArrayGuideShape.Visibility = Visibility.Collapsed;
            ArrayGuideShape.Data = null;

            // 重挂载阵列落点标记层（自绘覆盖层，同样被 Children.Clear 清掉）
            DrawingCanvas.Children.Remove(ArrayGuideOverlay);
            if (!DrawingCanvas.Children.Contains(ArrayGuideOverlay))
                DrawingCanvas.Children.Add(ArrayGuideOverlay);
            Panel.SetZIndex(ArrayGuideOverlay, 1004);
            ArrayGuideOverlay.Visibility = Visibility.Collapsed;
            ArrayGuideOverlay.ClearMarks();

            var vm = this.DataContext as EditWindowViewModel;
            itemsControl.Width = screen.Width > 0 ? screen.Width : (vm?.DeviceWidth ?? 800);
            itemsControl.Height = screen.Height > 0 ? screen.Height : (vm?.DeviceHeight ?? 600);

            itemsControl.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CurrentScreen.Widgets"));

            _myItemsControl = itemsControl;

            _selectionManager.ClearAllSelection();
            SelectorHelper.ClearAllAdorners();

            // 重置两点式绘制状态（切换画面时防止跨画面残留）
            HideDrawPreview();

            // 订阅模型脏标记（任意控件属性变化 → 标题加 * 并在关闭时提醒保存）
            SubscribeModelDirty(screen);

            // 注入画布尺寸到属性面板（坐标/尺寸钳制用）
            _propertyViewModel.CanvasWidth = screen.Width > 0 ? screen.Width : (_viewModel?.DeviceWidth ?? screen.Width);
            _propertyViewModel.CanvasHeight = screen.Height > 0 ? screen.Height : (_viewModel?.DeviceHeight ?? screen.Height);

            System.Diagnostics.Debug.WriteLine($"✅ ItemsControl 已创建并添加到 Canvas");
            System.Diagnostics.Debug.WriteLine($"   尺寸: {itemsControl.Width}x{itemsControl.Height}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {screen.Widgets.Count}");
        }

        /// <summary>
        /// 画布点击事件：在点击位置创建 Widget（添加模式下），或清除选中状态。
        /// </summary>
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {            
            // 两点式绘制预览更新
            if (_isDrawingPreview)
            {
                UpdateDrawPreview(e.GetPosition(DrawingCanvas));
                return;
            }

            if (!_isMarquee) return;
            var pos = e.GetPosition(DrawingCanvas);
            var x = Math.Min(_marqueeStart.X, pos.X);
            var y = Math.Min(_marqueeStart.Y, pos.Y);
            var w = Math.Abs(pos.X - _marqueeStart.X);
            var h = Math.Abs(pos.Y - _marqueeStart.Y);
            Canvas.SetLeft(MarqueeRect, x);
            Canvas.SetTop(MarqueeRect, y);
            MarqueeRect.Width = w;
            MarqueeRect.Height = h;
            // 边框灰色细线(实线=左→右, 虚线=右→左)，填充蓝/绿
            bool leftToRight = pos.X >= _marqueeStart.X;
            MarqueeRect.Stroke = Brushes.Gray;
            MarqueeRect.StrokeThickness = 1;
            MarqueeRect.StrokeDashArray = leftToRight ? null : new DoubleCollection { 4, 4 };
            MarqueeRect.Fill = leftToRight
                ? new SolidColorBrush(Color.FromArgb(0x20, 0x33, 0x99, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x20, 0x33, 0xCC, 0x33));
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 容器层无条件清理泄漏候选状态（pending + 粘性标志）
            // 注意：不清 _isDragging——widget 冒泡 up 负责拖拽收尾（光标恢复 + 捕获释放），
            // capture 保证 up 必路由到捕获元素，此处兜底不失效。
            _dragBehavior.ClearLeakState();

            if (!_isMarquee) return;
            _isMarquee = false;
            MarqueeRect.Visibility = Visibility.Collapsed;

            var screen = _viewModel.CurrentScreen;
            if (screen == null) return;

            var rect = new Rect(Canvas.GetLeft(MarqueeRect), Canvas.GetTop(MarqueeRect), MarqueeRect.Width, MarqueeRect.Height);
            bool leftToRight = e.GetPosition(DrawingCanvas).X >= _marqueeStart.X;

            foreach (var widget in screen.Widgets)
            {
                var wRect = new Rect(widget.X, widget.Y, widget.Width, widget.Height);
                if (leftToRight)
                    widget.IsSelected = rect.Contains(wRect);      // 完全包含
                else
                    widget.IsSelected = rect.IntersectsWith(wRect); // 相交即可
            }

            // 框选收尾：同步属性面板（多选批量编辑 / 单选 / 清空）
            SyncSelectionToPropertyPanel();
        }

        /// <summary>显示两点式绘制预览（根据创建器类型生成对应形状的 Path）。</summary>
        private void ShowDrawPreview()
        {
            _drawPreviewPath = DrawPreviewShape;
            _drawPreviewPath.Visibility = Visibility.Visible;
            UpdateDrawPreview(_drawStartPoint);
        }

        /// <summary>更新预览形状：根据当前创建器类型绘制 Line/Rectangle/Ellipse。</summary>
        private void UpdateDrawPreview(Point end)
        {
            if (_drawPreviewPath == null || _currentWidgetCreator == null) return;

            double x = Math.Min(_drawStartPoint.X, end.X);
            double y = Math.Min(_drawStartPoint.Y, end.Y);
            double w = Math.Abs(end.X - _drawStartPoint.X);
            double h = Math.Abs(end.Y - _drawStartPoint.Y);

            switch (_currentWidgetCreator)
            {
                case LineWidgetCreator:
                    _drawPreviewPath.Data = new LineGeometry(_drawStartPoint, end);
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                case CircleWidgetCreator:
                {
                    double dx = end.X - _drawStartPoint.X;
                    double dy = end.Y - _drawStartPoint.Y;
                    double radius = Math.Max(10, Math.Sqrt(dx * dx + dy * dy));  // 与 Creator 一致：最小 10px
                    _drawPreviewPath.Data = new EllipseGeometry(new Point(_drawStartPoint.X, _drawStartPoint.Y), radius, radius);
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                }
                case EllipseWidgetCreator:
                {
                    // 自由椭圆预览（左上角→右下角）
                    double ex = Math.Min(_drawStartPoint.X, end.X);
                    double ey = Math.Min(_drawStartPoint.Y, end.Y);
                    double ew = Math.Max(Math.Abs(end.X - _drawStartPoint.X), 10);
                    double eh = Math.Max(Math.Abs(end.Y - _drawStartPoint.Y), 10);
                    // 椭圆预览（与落点形状一致，避免矩形预览跳变椭圆）
                    _drawPreviewPath.Data = new EllipseGeometry(new Rect(ex, ey, ew, eh));
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                }
                default: // RectangleWidgetCreator
                    _drawPreviewPath.Data = new RectangleGeometry(new Rect(x, y, Math.Max(10, w), Math.Max(10, h)));  // 与 Creator 一致：最小 10px
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
            }
        }

        /// <summary>隐藏两点式绘制预览并复位状态。</summary>
        private void HideDrawPreview()
        {
            _isDrawingPreview = false;
            if (DrawPreviewShape != null)
            {
                DrawPreviewShape.Visibility = Visibility.Collapsed;
                DrawPreviewShape.Data = null;
            }
            _drawPreviewPath = null;
        }

        /// <summary>
        /// 判断命中元素是否属于 Adorner 树（缩放手柄 Thumb 及其模板内部元素）。
        /// Adorner 树上的元素 DataContext 继承自窗口（EditWindowViewModel），
        /// 不是 Widget，会被 FindWidgetElement 误判为"空白点击"而清除选中——
        /// 必须在清除逻辑前拦截。
        /// </summary>
        private static bool IsAdornerHit(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is System.Windows.Documents.Adorner) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TreeContextMenu.IsOpen) { TreeContextMenu.IsOpen = false; }

            // [修复] 命中 Adorner 缩放手柄（Thumb 或其内部模板元素）时，视为点击选中装饰器：
            // 保持选中状态不清除，让 Thumb 正常接收 MouseDown 进入拖拽缩放
            if (IsAdornerHit(e.OriginalSource as DependencyObject)) return;

            // 两点式绘制：第二点击生成控件（优先响应，不受已有点击拦截影响）
            if (_currentWidgetCreator is ITwoPointCreator twoPoint && _isDrawingPreview)
            {
                if (_viewModel?.CurrentScreen == null) return;
                _viewModel.PushUndoSnapshot();
                Point end = e.GetPosition(DrawingCanvas);
                var widget = twoPoint.Create(_drawStartPoint, end, _viewModel.CurrentScreen);
                _viewModel.CurrentScreen.Widgets.Add(widget);
                MarkProjectDirty();
                HideDrawPreview();
                ExitAddMode();
                _dragBehavior.SuppressDragUntilMouseUp();  // 粘性抑制本次 down→up 周期的拖拽（冒泡阶段仍生效）
                e.Handled = true;  // 阻止事件继续冒泡到被点中的 Widget
                return;
            }

            // 两点式绘制：第一点击记录起点并显示预览（优先于 FindWidgetElement，允许从控件上开始画线）
            if (_currentWidgetCreator is ITwoPointCreator tpc && !_isDrawingPreview)
            {
                if (_viewModel?.CurrentScreen == null) return;
                _drawStartPoint = e.GetPosition(DrawingCanvas);
                _isDrawingPreview = true;
                ShowDrawPreview();
                e.Handled = true;
                return;
            }

            // 点击了 Widget → 交给已有逻辑
            if (FindWidgetElement(e.OriginalSource as DependencyObject) != null) return;

            _selectionManager.ClearAllSelection();
            HidePropertyWindow();

            // 非添加模式 → 开始框选
            if (_currentWidgetCreator == null)
            {
                _marqueeStart = e.GetPosition(DrawingCanvas);
                _isMarquee = true;
                Canvas.SetLeft(MarqueeRect, _marqueeStart.X);
                Canvas.SetTop(MarqueeRect, _marqueeStart.Y);
                MarqueeRect.Width = MarqueeRect.Height = 0;
                MarqueeRect.Visibility = Visibility.Visible;
                e.Handled = true;
                return;
            }

            // 添加 Widget 模式（单点式控件）
            if (_viewModel?.CurrentScreen == null) return;
            Point pos = e.GetPosition(DrawingCanvas);

            // 单点式：直接创建
            _viewModel.PushUndoSnapshot();
            var newWidget = _currentWidgetCreator.Create(pos, _viewModel.CurrentScreen);
            _viewModel.CurrentScreen.Widgets.Add(newWidget);
            MarkProjectDirty();
            ExitAddMode();
            e.Handled = true;
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
            else if (e.Key == Key.Escape)
            {
                // ESC 退出添加模式（含两点式绘制中途取消）
                if (_currentWidgetCreator != null)
                {
                    ExitAddMode();
                    e.Handled = true;
                }
            }
        }


        #endregion

        #region 添加模式

        private Button? _activeToolboxBtn;
        private string? _activeToolboxTag;
        private object? _activeToolboxOriginalContent;  // 保存按钮原始 Content（可能为图标/文本）

        private void ToggleAddWidgetMode(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tag) return;

            // 如果已经在添加模式且点击了同一个按钮 → 退出
            if (_currentWidgetCreator != null && _activeToolboxTag == tag)
            {
                ExitAddMode();
                return;
            }

            // 先退出之前的模式，恢复原按钮内容 + 清理绘制状态
            if (_activeToolboxBtn != null && _activeToolboxOriginalContent != null)
                _activeToolboxBtn.Content = _activeToolboxOriginalContent;
            HideDrawPreview();   // 清理两点式绘制残留状态

            // 进入新的添加模式
            _currentWidgetCreator = tag switch
            {
                "Button" => new ButtonWidgetCreator(),
                "Text" => new TextWidgetCreator(),
                "Rectangle" => new RectangleWidgetCreator(),
                "Label" => new LabelWidgetCreator(),
                "Image" => new ImageWidgetCreator(),
                "Numeric" => new NumericDisplayWidgetCreator(),
                "Switch" => new SwitchWidgetCreator(),
                "Line" => new LineWidgetCreator(),
                "Circle" => new CircleWidgetCreator(),
                "Ellipse" => new EllipseWidgetCreator(),
                "IOField" => new IOFieldWidgetCreator(),
                "CheckBox" => new CheckBoxWidgetCreator(),
                "TextBox" => new TextBoxWidgetCreator(),
                "Frame" => new FrameWidgetCreator(),
                "ProgressBar" => new ProgressBarWidgetCreator(),
                _ => null
            };

            if (_currentWidgetCreator != null)
            {
                _activeToolboxBtn = btn;
                _activeToolboxTag = tag;
                _activeToolboxOriginalContent = btn.Content;  // 保存原始内容
                DrawingCanvas.Cursor = Cursors.Cross;
                btn.Content = $"➕ {_activeToolboxOriginalContent}";
            }
        }

        private void ExitAddMode()
        {
            _currentWidgetCreator = null;
            DrawingCanvas.Cursor = Cursors.Arrow;
            HideDrawPreview();
            if (_activeToolboxBtn != null && _activeToolboxOriginalContent != null)
                _activeToolboxBtn.Content = _activeToolboxOriginalContent;
            _activeToolboxBtn = null;
            _activeToolboxTag = null;
            _activeToolboxOriginalContent = null;
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
            WidgetContextMenu.IsOpen = false;

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
            WidgetContextMenu.IsOpen = false;
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
            if (e.ChangedButton == MouseButton.Right)
            {
                // 点击了 Widget 则交给 Widget 自己的右键菜单
                if (FindWidgetElement(e.OriginalSource as DependencyObject) != null) return;

                var mousePos = Mouse.GetPosition(this);
                TreeContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
                TreeContextMenu.HorizontalOffset = mousePos.X;
                TreeContextMenu.VerticalOffset = mousePos.Y;
                _contextMenuPos = e.GetPosition(DrawingCanvas);
                _contextMenuPos.X /= _zoomLevel;
                _contextMenuPos.Y /= _zoomLevel;
                UpdateCanvasMenuButtons();
                TreeContextMenu.IsOpen = true;
                e.Handled = true;
                return;
            }

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
                if (FindWidgetElement(source) != null) return;

                // 双击画布空白处 → 显示画面属性
                if (_viewModel?.CurrentScreen != null)
                    ShowScreenProperty(_viewModel.CurrentScreen);
            }

        }


        #endregion

        #region 视觉树查找

        /// <summary>查找视觉树中第一个 DataContext 为 Widget 的 FrameworkElement。</summary>
        private FrameworkElement? FindWidgetElement(DependencyObject? child)
        {
            while (child != null)
            {
                if (child is FrameworkElement fe && fe.DataContext is Widget)
                    return fe;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        /// <summary>判断视觉树中是否存在 Button（用于工具栏拖拽检测）。</summary>
        private static bool IsDescendantOfButton(DependencyObject? child)
        {
            while (child != null)
            {
                if (child is System.Windows.Controls.Button) return true;
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }

        #endregion

        #region 项目保存 & 关闭

        private void SaveCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            SaveProject(_currentProject, _currentProject.ProjectFilePath);
        }

        private void CloseCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            if (!TryCloseProject(false))
                return;

            _isProjectDirty = false;

            WelComeWindow welcome = new WelComeWindow();
            welcome.Show();

            _skipClosingCheck = true;
            this.Close();
        }

        /// <summary>
        /// 检查未保存更改，返回 true 表示可以继续关闭，false 表示用户取消。
        /// </summary>
        private bool TryCloseProject(bool isAppClosing)
        {
            if (string.IsNullOrEmpty(_currentProject.ProjectFilePath) || !_isProjectDirty)
                return true;

            MessageBoxResult result = MessageBox.Show(
                "当前工程有未保存的修改，是否保存？",
                "提示",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveProject(_currentProject, _currentProject.ProjectFilePath);
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
            _isProjectDirty = false;
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
            // 点击按钮时不启动拖拽
            if (IsDescendantOfButton(e.OriginalSource as DependencyObject)) return;
            if (sender is ToolBar tb && e.LeftButton == MouseButtonState.Pressed)
            {
                _dragSource = tb;
                _dragStart = e.GetPosition(null);
                _isDragging = false;
                tb.CaptureMouse();
                e.Handled = true;
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
        // 缩放
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoomLevel * 1.25);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoomLevel / 1.25);
        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ApplyZoom(1.0);

        private void ApplyZoom(double level)
        {
            _zoomLevel = Math.Max(0.1, Math.Min(level, 5.0));
            _canvasScale.ScaleX = _zoomLevel;
            _canvasScale.ScaleY = _zoomLevel;
            ZoomLabel.Text = $"{_zoomLevel * 100:F0}%";
        }

        // 剪贴板
        private List<byte[]> _clipboard = new();
        private Point _contextMenuPos;
        private void CutWidget_Click(object sender, RoutedEventArgs e) { CopyWidgets(); DeleteSelectedWidgets(); WidgetContextMenu.IsOpen = false; }
        private void CopyWidget_Click(object sender, RoutedEventArgs e) { CopyWidgets(); WidgetContextMenu.IsOpen = false; }

        private void ShowCanvasProperty_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentScreen != null) _propertyViewModel.SelectedScreen = _viewModel.CurrentScreen;
            ShowAnchorable("property");
            TreeContextMenu.IsOpen = false;
        }

        private void PasteAtPosition(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentScreen == null || _clipboard.Count == 0) return;
            _viewModel.PushUndoSnapshot();
            foreach (var data in _clipboard)
            {
                using var ms = new MemoryStream(data);
                var w = Serializer.Deserialize<Widget>(ms);
                w.X = _contextMenuPos.X; w.Y = _contextMenuPos.Y;
                w.ObjectName = UniqueName(w.ObjectName);
                _viewModel.CurrentScreen.Widgets.Add(w);
            }
            _viewModel.NotifyCanvasRefreshNeeded();
            TreeContextMenu.IsOpen = false;
        }

        private void PasteWidget_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentScreen == null || _clipboard.Count == 0) return;
            _viewModel.PushUndoSnapshot();
            foreach (var data in _clipboard)
            {
                using var ms = new MemoryStream(data);
                var w = Serializer.Deserialize<Widget>(ms);
                w.X += 20 / _zoomLevel; w.Y += 20 / _zoomLevel;
                w.ObjectName = UniqueName(w.ObjectName);
                _viewModel.CurrentScreen.Widgets.Add(w);
            }
            _viewModel.NotifyCanvasRefreshNeeded();
            WidgetContextMenu.IsOpen = false;
        }
        private void UpdateCanvasMenuButtons()
        {
            var hasContent = _clipboard.Count > 0;
            CanvasPasteBtn.IsEnabled = hasContent;
        }

        private void CopyWidgets()
        {
            _clipboard.Clear();
            var selected = _viewModel.CurrentScreen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null) return;
            foreach (var w in selected) { using var ms = new MemoryStream(); Serializer.Serialize(ms, w); _clipboard.Add(ms.ToArray()); }
            WidgetPasteBtn.IsEnabled = _clipboard.Count > 0;
            UpdateCanvasMenuButtons();
        }
        private void DeleteSelectedWidgets()
        {
            if (_viewModel.CurrentScreen == null) return;
            var selected = _viewModel.CurrentScreen.Widgets.Where(w => w.IsSelected).ToList();
            foreach (var w in selected) _viewModel.CurrentScreen.Widgets.Remove(w);
            _viewModel.NotifyCanvasRefreshNeeded();
            MarkProjectDirty();
            SyncSelectionToPropertyPanel();   // 删除后同步多选集合
            WidgetContextMenu.IsOpen = false;
        }

        // 对齐
        private void RectArray_Click(object sender, RoutedEventArgs e) => DoArrayLayout(false);
        private void CircleArray_Click(object sender, RoutedEventArgs e) => DoArrayLayout(true);

        private void DoArrayLayout(bool isCircle)
        {
            var selected = _viewModel.CurrentScreen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null || selected.Count < 2) return;

            var sorted = selected.OrderBy(w => ExtractIdNumber(w.ObjectName)).ToList();
            double cx = Math.Min(Math.Max(sorted.Average(w => w.X + w.Width / 2), 50), (_viewModel.DeviceWidth - 50));
            double cy = Math.Min(Math.Max(sorted.Average(w => w.Y + w.Height / 2), 50), (_viewModel.DeviceHeight - 50));
            // 钳制边界单源：与 UpdateArrayGuide 共用同一 maxW/maxH
            double maxW = _viewModel.CurrentScreen?.Width ?? _viewModel.DeviceWidth;
            double maxH = _viewModel.CurrentScreen?.Height ?? _viewModel.DeviceHeight;

            GridArrayDialog? dialog = null;
            dialog = new GridArrayDialog(isCircle, sorted.Count, cx, cy, () =>
            {
                // 仅更新辅助线预览（不移动控件）
                UpdateArrayGuide(isCircle, sorted.Count, dialog!.Cols, dialog.Rows,
                    dialog.StartX, dialog.StartY, dialog.SpacingX, dialog.SpacingY,
                    dialog.CenterX, dialog.CenterY, dialog.Radius, dialog.StartAngle, dialog.EndAngle,
                    maxW, maxH);
            }) { Owner = this };

            // 初始默认值预览（画辅助线）
            dialog.InvokePreview();

            if (dialog.ShowDialog() == true)
            {
                // 确认：按辅助线位置真正落位
                _viewModel.PushUndoSnapshot();
                var positions = CalcPositions(isCircle, sorted.Count, dialog.Cols, dialog.Rows,
                    dialog.StartX, dialog.StartY, dialog.SpacingX, dialog.SpacingY,
                    dialog.CenterX, dialog.CenterY, dialog.Radius, dialog.StartAngle, dialog.EndAngle,
                    maxW, maxH);
                for (int i = 0; i < Math.Min(positions.Count, sorted.Count); i++)
                {
                    // 中心点基准：落位 = 中心点 - 控件自身宽高/2（控件中心对齐阵列点）
                    sorted[i].X = Math.Max(0, Math.Min(positions[i].X - sorted[i].Width / 2, maxW - sorted[i].Width));
                    sorted[i].Y = Math.Max(0, Math.Min(positions[i].Y - sorted[i].Height / 2, maxH - sorted[i].Height));
                }
                _viewModel.NotifyCanvasRefreshNeeded();
                DrawingCanvas.InvalidateVisual();
            }
            ClearArrayGuide();
        }

        /// <summary>绘制阵列辅助线预览（矩形网格 / 圆形弧线 + 落点中心标记），不移动控件。</summary>
        private void UpdateArrayGuide(bool isCircle, int count, int cols, int rows,
            double startX, double startY, double sx, double sy,
            double centerX, double centerY, double radius, double startAngle, double endAngle,
            double maxW, double maxH)
        {
            if (ArrayGuideShape == null || ArrayGuideOverlay == null) return;
            var positions = CalcPositions(isCircle, count, cols, rows, startX, startY, sx, sy,
                centerX, centerY, radius, startAngle, endAngle, maxW, maxH);
            if (positions.Count == 0) { ClearArrayGuide(); return; }

            var group = new System.Windows.Media.GeometryGroup();
            if (isCircle)
            {
                // 圆形阵列：圆弧轨迹 + 圆心十字
                double start = startAngle * Math.PI / 180, end = endAngle * Math.PI / 180;
                double total = (end > start ? end - start : 2 * Math.PI + end - start);
                if (total >= 2 * Math.PI - 1e-9)
                {
                    // 整圆退化修复：起点==终点时 ArcSegment 画不出 360° 弧，改用 EllipseGeometry
                    group.Children.Add(new System.Windows.Media.EllipseGeometry(
                        new Point(centerX, centerY), radius, radius));
                }
                else
                {
                    var arc = CreateArcGeometry(centerX, centerY, radius, start, end);
                    if (arc != System.Windows.Media.Geometry.Empty) group.Children.Add(arc);
                }
                var cross = new System.Windows.Media.GeometryGroup();
                cross.Children.Add(new System.Windows.Media.LineGeometry(
                    new Point(centerX - 6, centerY), new Point(centerX + 6, centerY)));
                cross.Children.Add(new System.Windows.Media.LineGeometry(
                    new Point(centerX, centerY - 6), new Point(centerX, centerY + 6)));
                group.Children.Add(cross);
            }
            else
            {
                // 矩形阵列：行/列网格线（经过中心点，圆点落在交点上）
                // 行数按实际所需 ceil(count/cols) 画，避免 count > rows*cols 时圆点悬空
                int gridRows = Math.Max(rows, (count + cols - 1) / cols);
                for (int c = 0; c < cols; c++)
                {
                    double x = startX + c * sx;
                    group.Children.Add(new System.Windows.Media.LineGeometry(
                        new Point(x, startY), new Point(x, startY + (gridRows - 1) * sy)));
                }
                for (int r = 0; r < gridRows; r++)
                {
                    double y = startY + r * sy;
                    group.Children.Add(new System.Windows.Media.LineGeometry(
                        new Point(startX, y), new Point(startX + (cols - 1) * sx, y)));
                }
            }
            ArrayGuideShape.Data = group;
            ArrayGuideShape.Visibility = Visibility.Visible;

            // 落点标记层（自绘覆盖层）：蓝色空心方块 + 序号数字，中心 = CalcPositions 返回的中心点（网格线交点）
            if (ArrayGuideOverlay != null)
            {
                ArrayGuideOverlay.SetMarks(positions);
                ArrayGuideOverlay.Visibility = Visibility.Visible;
            }
        }

        /// <summary>清除阵列辅助线。</summary>
        private void ClearArrayGuide()
        {
            if (ArrayGuideShape != null)
            {
                ArrayGuideShape.Data = null;
                ArrayGuideShape.Visibility = Visibility.Collapsed;
            }
            if (ArrayGuideOverlay != null)
            {
                ArrayGuideOverlay.ClearMarks();
                ArrayGuideOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private static System.Windows.Media.Geometry CreateArcGeometry(double cx, double cy, double r, double startAngle, double endAngle)
        {
            if (r <= 0) return System.Windows.Media.Geometry.Empty;
            double total = endAngle > startAngle ? endAngle - startAngle : 2 * Math.PI + endAngle - startAngle;
            bool largeArc = total > Math.PI;
            var startPoint = new Point(cx + r * Math.Cos(startAngle), cy + r * Math.Sin(startAngle));
            var endPoint = new Point(cx + r * Math.Cos(endAngle), cy + r * Math.Sin(endAngle));
            var fig = new System.Windows.Media.PathFigure { StartPoint = startPoint };
            fig.Segments.Add(new System.Windows.Media.ArcSegment(endPoint, new Size(r, r), 0, largeArc, System.Windows.Media.SweepDirection.Clockwise, true));
            return new System.Windows.Media.PathGeometry(new[] { fig });
        }

        private static List<Point> CalcPositions(bool isCircle, int count,
            int cols, int rows, double startX, double startY, double sx, double sy,
            double centerX, double centerY, double radius, double startAngle, double endAngle,
            double maxW, double maxH)
        {
            // 除零防御：列/行数至少为 1
            cols = Math.Max(1, cols); rows = Math.Max(1, rows);
            var result = new List<Point>();
            if (isCircle)
            {
                double start = startAngle * Math.PI / 180, end = endAngle * Math.PI / 180;
                double total = (end > start ? end - start : 2 * Math.PI + end - start);
                bool fullCircle = total >= 2 * Math.PI - 1e-9;
                for (int i = 0; i < count; i++)
                {
                    // 整圈（0~360°）时按 360°/count 均匀分布，首尾不重叠；
                    // 非整圈弧线时首尾各在两端（total/(count-1) 间隔）
                    double a = fullCircle
                        ? start + 2 * Math.PI * i / count
                        : start + total * i / Math.Max(count - 1, 1);
                    result.Add(new Point(
                        centerX + radius * Math.Cos(a),
                        centerY + radius * Math.Sin(a)));
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    int r = i / cols, c = i % cols;
                    result.Add(new Point(
                        startX + c * sx,
                        startY + r * sy));
                }
            }
            return result;
        }

        private static int ExtractIdNumber(string name)
        {
            var num = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(num, out var n) ? n : 0;
        }

        private void AlignLeft_Click(object sender, RoutedEventArgs e) => AlignWidgets((refs, w) => w.X = refs.MinX);
        private void AlignCenterH_Click(object sender, RoutedEventArgs e) => AlignWidgets((refs, w) => w.X = refs.MinX + (refs.MaxX - refs.MinX) / 2 - w.Width / 2);
        private void AlignRight_Click(object sender, RoutedEventArgs e) => AlignWidgets((refs, w) => w.X = refs.MaxX - w.Width);
        private void AlignTop_Click(object sender, RoutedEventArgs e) => AlignWidgets((refs, w) => w.Y = refs.MinY);
        private void AlignCenterV_Click(object sender, RoutedEventArgs e) => AlignWidgets((refs, w) => w.Y = refs.MinY + (refs.MaxY - refs.MinY) / 2 - w.Height / 2);
        private void AlignBottom_Click(object sender, RoutedEventArgs e) => AlignWidgets((refs, w) => w.Y = refs.MaxY - w.Height);

        private void AlignWidgets(Action<(double MinX, double MinY, double MaxX, double MaxY), Widget> align)
        {
            var selected = _viewModel.CurrentScreen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null || selected.Count == 0) return;
            var bounds = (MinX: selected.Min(w => w.X), MinY: selected.Min(w => w.Y),
                          MaxX: selected.Max(w => w.X + w.Width), MaxY: selected.Max(w => w.Y + w.Height));
            _viewModel.PushUndoSnapshot();
            foreach (var w in selected) align(bounds, w);
            _viewModel.NotifyCanvasRefreshNeeded();
            WidgetContextMenu.IsOpen = false;
        }

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
        private void ShowWidgetProperty_Click(object sender, RoutedEventArgs e)
        {
            var selected = _viewModel.CurrentScreen?.Widgets.FirstOrDefault(w => w.IsSelected);
            if (selected != null) _propertyViewModel.SelectedWidget = selected;
            ShowAnchorable("property");
            WidgetContextMenu.IsOpen = false;
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            _skipClosingCheck = true;
            _isProjectDirty = false;
            Close();
            // 关闭后由 App.xaml.cs 的 ShutdownMode/启动逻辑回到 WelcomeWindow
        }
        private void SaveAsProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "工程文件|*.hmiproj", DefaultExt = ".hmiproj" };
            if (dlg.ShowDialog() == true)
            {
                ProjectFileService.Save(_currentProject, dlg.FileName);
                _currentProject.ProjectFilePath = dlg.FileName;
                _viewModel.CommandService.ReplaceProject(_currentProject);
                _isProjectDirty = false;
                Title = $"NavigatorHMI - {dlg.FileName}";
            }
        }
        private void DeleteSelectedWidget_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentScreen == null) return;
            var selected = _viewModel.CurrentScreen.Widgets.Where(w => w.IsSelected).ToList();
            foreach (var w in selected) _viewModel.CurrentScreen.Widgets.Remove(w);
            _viewModel.NotifyCanvasRefreshNeeded();
            SyncSelectionToPropertyPanel();   // 删除后同步多选集合
            WidgetContextMenu.IsOpen = false;
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

        private string UniqueName(string baseName)
        {
            var names = new HashSet<string>(_viewModel.CurrentScreen?.Widgets.Select(w => w.ObjectName) ?? Enumerable.Empty<string>());
            if (!names.Contains(baseName + "_copy")) return baseName + "_copy";
            for (int i = 2; ; i++) { var n = $"{baseName}_copy{i}"; if (!names.Contains(n)) return n; }
        }
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
