using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Behaviors;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using ProtoBuf;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;   // P10：XmlLayoutSerializer（Dirkster.AvalonDock）

namespace NavigatorHMI.Views
{
    /// <summary>
    /// EditWindow 的交互逻辑。作为 WPF 窗口的 code-behind，
    /// 负责响应 UI 事件并将业务逻辑委托给拆分后的专职类。
    /// </summary>
    public partial class EditWindow : Window
    {

        #region 原 ItemsControl & 画布（第 3036-3635 行）

        /// <summary>当前画面是否应叠加全局虚影层：Custom 强制；WorldMap 按属性勾选；Template 自身不叠加；全局画面为空/不存在不叠加。</summary>
        private bool ShouldShowGlobalGhost(Screen screen)
        {
            if (screen == null || screen.Type == ScreenType.Template) return false;
            var vm = this.DataContext as EditWindowViewModel;
            var globalScreen = FindGlobalScreen(vm);
            if (globalScreen == null || ReferenceEquals(globalScreen, screen) || globalScreen.Widgets.Count == 0) return false;
            return screen.Type == ScreenType.Custom
                || (screen.Type == ScreenType.WorldMap && vm?.CurrentProject?.WorldMap?.ShowGlobalOverlay == true);
        }

        /// <summary>
        /// 定位全局画面：优先 Type == Template（与全项目识别方式一致，项目树/删除保护/CLI 均用 Type），
        /// IsGlobal 兜底兼容历史工程（ProtoBuf bool 默认 false，旧工程/CLI 创建的 Template 可能未置 IsGlobal）。
        /// </summary>
        private static Screen? FindGlobalScreen(EditWindowViewModel? vm)
            => vm?.CurrentProject?.Screens.FirstOrDefault(s => s.Type == ScreenType.Template || s.IsGlobal);

        /// <summary>
        /// 创建全局画面虚影层（叠加在当前画面上方）。半透明 + 整体不参与命中测试（鼠标穿透，
        /// 不可选中/编辑），与设备端运行时叠加显示行为一致。调用前应已删除旧虚影层。
        /// </summary>
        private void AddGlobalGhost(Screen screen)
        {
            var vm = this.DataContext as EditWindowViewModel;
            var globalScreen = FindGlobalScreen(vm);
            if (!ShouldShowGlobalGhost(screen)) return;

            // 跨画面选中残留防护：全局控件选中态不带到其它画面——否则虚影层元素（模板 TwoWay 绑
            // IsSelected）会挂上 ResizeAdorner，而 Adorner 渲染于窗口装饰层，不受 IsHitTestVisible
            // 约束，可被拖拽修改全局控件尺寸。切画面时显式清空全局控件选中态。
            foreach (var w in globalScreen!.Widgets) w.IsSelected = false;

            var ghostItems = WidgetItemsControlFactory.Create(
                globalScreen,
                null!,
                _dragBehavior.OnPreviewMouseLeftButtonDown,
                _dragBehavior.OnMouseLeftButtonDown,
                _dragBehavior.OnMouseMove,
                _dragBehavior.OnMouseLeftButtonUp,
                _dragBehavior.OnPreviewMouseRightButtonDown,
                _dragBehavior.OnMouseRightButtonUp);
            ghostItems.Name = GlobalGhostName;
            ghostItems.ItemsSource = globalScreen.Widgets;
            ghostItems.Width = globalScreen.Width > 0 ? globalScreen.Width : (vm?.DeviceWidth ?? DefaultDeviceWidth);
            ghostItems.Height = globalScreen.Height > 0 ? globalScreen.Height : (vm?.DeviceHeight ?? DefaultDeviceHeight);
            // 虚影：半透明 + 整体不参与命中测试（鼠标穿透，不可选中/编辑）
            ghostItems.Opacity = 0.35;
            ghostItems.IsHitTestVisible = false;
            ghostItems.Focusable = false;
            DrawingCanvas.Children.Add(ghostItems);
            Canvas.SetLeft(ghostItems, 0);
            Canvas.SetTop(ghostItems, 0);
            Panel.SetZIndex(ghostItems, 0);   // 当前画面控件(-1)之上、选中框/预览层(1001+)之下
        }

        /// <summary>局部刷新虚影层（世界地图「全局叠加」勾选变化时调用）：删旧重建，不动主层与选中。</summary>
        private void RefreshGlobalGhost()
        {
            var old = DrawingCanvas.Children.OfType<ItemsControl>()
                .FirstOrDefault(c => c.Name == GlobalGhostName);
            if (old != null) DrawingCanvas.Children.Remove(old);
            var screen = _viewModel?.CurrentScreen;
            if (screen != null) AddGlobalGhost(screen);
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
            // 排除瞬态属性（不持久化，不标脏）：
            //  IsSelected——选中态高亮；
            //  DisplayText——派生显示属性（1Hz 时钟 DateTimeWidget.RefreshDisplay 每秒通知刷新实时时间，
            //  未绑定控件同理），非持久化数据，变化不应标脏（M-1：标题栏脏标记误报根因①）。
            if (e.PropertyName == nameof(Widget.IsSelected)) return;
            if (e.PropertyName == nameof(Widget.DisplayText)) return;
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
            // 排除 IsCurrent（瞬态高亮属性，[ProtoIgnore] 不持久化，切画面即改——M-1：脏标记误报根因②）
            if (e.PropertyName == nameof(Screen.IsCurrent)) return;
            MarkProjectDirty();
        }

        /// <summary>
        /// 属性面板数字输入框 Enter 键确认：移动焦点到下一元素（触发 LostFocus 应用绑定值）。
        /// </summary>
        private void PropField_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                (sender as FrameworkElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        /// <summary>
        /// 数值输入框字符过滤：整体 TryParse 校验（防多小数点/逗号/非法字符）。
        /// 允许负数前缀态（"-"/"."/"-."）——逐字符输入时中间态，失焦时绑定兜底校验。
        /// 粘贴/IME 等非单字符输入走 PreviewTextInput 统一拦截。
        /// </summary>
        private void PropNumberBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (tb.IsReadOnly) { e.Handled = true; return; }
            var input = e.Text;
            var newText = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, input);
            // 负数前缀中间态放行（如 "-" / "." / "-."），失焦/回车时绑定校验兜底
            if (newText is "-" or "." or "-.") { return; }
            bool valid = double.TryParse(newText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)
                && double.IsFinite(d)
                && !input.Contains(',');   // 拒绝逗号小数点 locale 差异
            e.Handled = !valid;
        }

        /// <summary>菜单「默认字体」：打开工厂默认字体设置对话框。</summary>
        private void FontDefaults_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FontDefaultsDialog { Owner = this };
            dialog.ShowDialog();
        }

        /// <summary>菜单「帮助文档」/ F1：打开帮助对话框。</summary>
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HelpDialog { Owner = this };
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
            else if (e.PropertyName == nameof(EditWindowViewModel.IsWorldMapActive))
            {
                // 批 4：进入世界地图画面 → 视口自适应（作业点/控件 Geo 包围盒）；viewport 未就绪时由 ViewportInitialized 兜底
                if (_viewModel?.IsWorldMapActive == true)
                    TryFitWorldMapViewport();
            }
            else if (e.PropertyName is nameof(EditWindowViewModel.VariableManagerActive)
                     or nameof(EditWindowViewModel.CommunicationActive)
                     or nameof(EditWindowViewModel.ListManagerActive)
                     or nameof(EditWindowViewModel.AlarmActive)
                     or nameof(EditWindowViewModel.UserActive)
                     or nameof(EditWindowViewModel.DeviceActive))
            {
                // Q 循环 Check（2026-09-05 用户反馈）：切到非画布视图（变量管理器/通讯/列表/报警/用户/设备管理）
                // 画布隐藏但控件选中残留（属性面板仍显示画面控件、回画面仍选中）——任一非画布视图激活即清选中
                var vm = _viewModel;
                if (vm != null && (vm.VariableManagerActive || vm.CommunicationActive || vm.ListManagerActive
                                   || vm.AlarmActive || vm.UserActive || vm.DeviceActive))
                {
                    _selectionManager.ClearAllSelection();
                    System.Diagnostics.Debug.WriteLine("✅ 切到非画布视图，已清除控件选中");
                }
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

            // ── 全局画面虚影层（Custom 强制叠加；WorldMap 按属性勾选；Template 自身不叠加）──
            AddGlobalGhost(screen);

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
            itemsControl.Width = screen.Width > 0 ? screen.Width : (vm?.DeviceWidth ?? DefaultDeviceWidth);
            itemsControl.Height = screen.Height > 0 ? screen.Height : (vm?.DeviceHeight ?? DefaultDeviceHeight);

            itemsControl.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CurrentScreen.Widgets"));

            _myItemsControl = itemsControl;

            _selectionManager.ClearAllSelection();
            SelectorHelper.ClearAllAdorners();

            // 重置两点式绘制状态（切换画面时防止跨画面残留）
            HideDrawPreview();

            // P2：世界地图画面切换/重载后刷新作业点 overlay（视口未初始化时 ViewportChanged 不触发）
            if (_viewModel?.IsWorldMapActive == true) UpdateAllGeoWidgets();
            else if (_isEditingWorkRange) ExitWorkRangeEditMode();   // 切出世界地图 → 退出作业范围编辑模式
            else if (_isAddingWorkPoint) ExitWorkPointAddMode();   // C12-9：切出世界地图 → 退出作业点选点模式

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
            // 世界地图画面：画布编辑短路（框选/两点式预览不启动），事件到达 MapControl 平移
            if (_viewModel?.IsWorldMapActive == true) return;
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
            // 世界地图画面：地图交互优先——短路但保留泄漏状态兜底清理
            if (_viewModel?.IsWorldMapActive == true)
            {
                _dragBehavior.ClearLeakState();
                return;
            }
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
            if (_currentWidgetCreator is PolygonWidgetCreator && _polygonPoints.Count > 0)
            {
                // P12：多边形预览初始化为「末点 → 末点」零长度——_drawStartPoint 是两点式通用起点（残留旧值），
                // 直接传入会让首点先闪一段到 (0,0) 的线段；鼠标移动后再正常橡皮筋
                UpdateDrawPreview(_polygonPoints[^1]);
            }
            else
            {
                UpdateDrawPreview(_drawStartPoint);
            }
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
                case PolygonWidgetCreator:
                {
                    // 已收集顶点 + 当前鼠标橡皮筋线段（PathGeometry + PolyLineSegment）
                    var fig = new System.Windows.Media.PathFigure { StartPoint = _polygonPoints[0], IsClosed = false };
                    var segPts = new List<Point>(_polygonPoints.Skip(1)) { end };
                    fig.Segments.Add(new System.Windows.Media.PolyLineSegment(segPts, true));
                    _drawPreviewPath.Data = new System.Windows.Media.PathGeometry(new[] { fig });
                    Canvas.SetLeft(_drawPreviewPath, 0);
                    Canvas.SetTop(_drawPreviewPath, 0);
                    break;
                }
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
            // 世界地图画面：地图交互优先（平移/缩放）——画布框选/添加模式短路，事件自然到达 MapControl
            if (_viewModel?.IsWorldMapActive == true) return;

            // 画布交互：聚焦画布（方向键微移判定用；延后执行避免干扰本次点击的选中/拖拽逻辑）
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (DrawingCanvas != null) DrawingCanvas.Focus();
            }));

            if (TreeContextMenu.IsOpen) { TreeContextMenu.IsOpen = false; }

            // [修复] 命中 Adorner 缩放手柄（Thumb 或其内部模板元素）时，视为点击选中装饰器：
            // 保持选中状态不清除，让 Thumb 正常接收 MouseDown 进入拖拽缩放
            if (IsAdornerHit(e.OriginalSource as DependencyObject)) return;

            // 多边形连续点击：左键收集顶点（第一点击起绘，后续加点，右键闭合）
            if (_currentWidgetCreator is PolygonWidgetCreator)
            {
                if (_viewModel?.CurrentScreen == null) return;
                var polygonPos = e.GetPosition(DrawingCanvas);
                _polygonPoints.Add(polygonPos);
                _isDrawingPreview = true;
                ShowDrawPreview();
                e.Handled = true;
                return;
            }

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



        /// <summary>
        /// 右键点击 Widget 时触发，在鼠标位置显示上下文菜单 Popup。
        /// 委托给 <see cref="WidgetContextMenuHandler.Show"/>。
        /// </summary>
        private void OnWidgetRightClick(Widget widget, Point screenPos)
            => _widgetContextMenuHandler.Show(widget, screenPos);
        #endregion

        #region 原 Widget 右键菜单（委托给 WidgetContextMenuHandler）（第 3636-3848 行）

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
        /// <summary>键盘焦点是否在管理器表格/列表内（DataGrid/ListBox，或用户面板子页 TabControl）——是则 Ctrl+A/Delete 交给控件级 handler（表格多选/删除），窗口级画布快捷键不拦截。Z3 收窄：TabControl 仅用户面板子页命中（画布在 AvalonDock 文档区——DocumentPane 也是 TabControl，一刀切会让画布快捷键失效）。</summary>
        private bool IsFocusInManagerControl()
        {
            var f = System.Windows.Input.Keyboard.FocusedElement as System.Windows.DependencyObject;
            while (f != null)
            {
                if (f is System.Windows.Controls.DataGrid or System.Windows.Controls.ListBox) return true;
                // Z3：TabControl 仅用户面板子页 / 列表管理器命中（其余 TabControl 如画布 DocumentPane 不命中，画布快捷键才可用）
                if (f is System.Windows.Controls.TabControl tab && (ReferenceEquals(tab, UserPanelTabs) || ReferenceEquals(tab, ListManagerTabs))) return true;
                f = System.Windows.Media.VisualTreeHelper.GetParent(f);
            }
            return false;
        }

        private void EditWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 焦点在文本框/输入框内时不拦截（避免干扰 TreeView 重命名、属性输入、CLI 输入）
            if (Keyboard.FocusedElement is TextBox or PasswordBox) return;

            var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            var none = Keyboard.Modifiers == ModifierKeys.None;

            // ── 全选 ──（焦点在管理器表格/列表内时交给控件级 handler：表格 Ctrl+A 全选行）
            if (ctrl && !shift && e.Key == Key.A && !IsFocusInManagerControl())
            {
                SelectAllWidgets();
                e.Handled = true;
                return;
            }
            // ── 用户管理页 Ctrl+C/V：复制/粘贴选中用户或组（页面级路由，与 A3 同款；UserActive 时画布 Hidden 无冲突；
            //    补 #1：用户/组复制粘贴原仅右键菜单，无键盘快捷键；page==2（安全设置）不命中时放行不吞键）──
            if (ctrl && !shift && !alt && e.Key == Key.C && _viewModel.UserActive)
            {
                if (_viewModel.UserPanelPage == 0 && UsersGrid.SelectedItems.Count > 0)
                    _viewModel.CopyUsersCommand(UsersGrid.SelectedItems.OfType<UserAccount>().ToList());
                else if (_viewModel.UserPanelPage == 1 && GroupsGrid.SelectedItems.Count > 0)
                    _viewModel.CopyGroupsCommand(GroupsGrid.SelectedItems.OfType<UserGroup>().ToList());
                else return;   // page==2 或无选中：放行不吞键（避免未来该页加可复制控件时静默吞键）
                e.Handled = true;
                return;
            }
            if (ctrl && !shift && !alt && e.Key == Key.V && _viewModel.UserActive)
            {
                if (_viewModel.UserPanelPage == 0) _viewModel.PasteUsersCommand();
                else if (_viewModel.UserPanelPage == 1) _viewModel.PasteGroupsCommand();
                else return;   // page==2：放行不吞键
                e.Handled = true;
                return;
            }
            // ── 复制 ──（表格内放行默认复制/不拦截）
            if (ctrl && !shift && e.Key == Key.C && !IsFocusInManagerControl())
            {
                CopyWidgets();
                e.Handled = true;
                return;
            }
            // ── 剪切 ──（表格内放行）
            if (ctrl && !shift && e.Key == Key.X && !IsFocusInManagerControl())
            {
                if (_viewModel.CurrentScreen?.Widgets.Any(w => w.IsSelected) == true)
                {
                    CopyWidgets();
                    DeleteSelectedWidgets();
                }
                e.Handled = true;
                return;
            }
            // ── 粘贴（无右键菜单位置时用当前位置；表格内放行）──
            if (ctrl && !shift && e.Key == Key.V && !IsFocusInManagerControl())
            {
                if (_clipboard.Count > 0)
                    PasteWidgetsAtCurrent();
                e.Handled = true;
                return;
            }
            // ── 方向键微移选中控件（无 Ctrl/Shift：1px；Shift：10px）──
            // 仅当键盘焦点在画布内且画布有选中控件时才拦截（否则放行 TreeView/ComboBox 等键盘导航）
            if ((none || shift) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down
                && DrawingCanvas.IsKeyboardFocusWithin
                && _viewModel.CurrentScreen?.Widgets.Any(w => w.IsSelected) == true)
            {
                NudgeSelectedWidgets(e.Key, shift ? 10 : 1);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !shift
                && _viewModel.UserActive && _viewModel.UndoUser())
            { e.Handled = true; return; }   // A3：用户/组撤销——按「用户管理 Tab 页面激活」路由（不再要求焦点在管理器控件内：AI 操作后焦点在侧边栏也能撤）；栈空返回 false 不拦截，落到画面撤销/KeyBinding
            if (((e.Key == Key.Y && ctrl) || (e.Key == Key.Z && ctrl && shift))
                && _viewModel.UserActive && _viewModel.RedoUser())
            { e.Handled = true; return; }   // 用户/组重做
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !shift
                && !_viewModel.UserActive && IsFocusInManagerControl())
            { _listManagerVM.UndoList(); e.Handled = true; return; }   // 列表项撤销（焦点在列表管理器时拦截，空栈也不穿透到画面撤销；!UserActive 保证用户页激活时空栈统一落到画面 KeyBinding——行为与焦点位置无关）
            if (((e.Key == Key.Y && ctrl) || (e.Key == Key.Z && ctrl && shift))
                && !_viewModel.UserActive && IsFocusInManagerControl())
            { _listManagerVM.RedoList(); e.Handled = true; return; }   // 列表项重做
            if (e.Key == Key.Delete && none && !IsFocusInManagerControl())
            {
                _widgetContextMenuHandler.DeleteSelectedWidget();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // ESC 退出添加模式（含两点式绘制中途取消）；作业范围加点/作业点选点编辑模式优先退出
                if (_isEditingWorkRange)
                {
                    ExitWorkRangeEditMode();
                    e.Handled = true;
                }
                else if (_isAddingWorkPoint)
                {
                    ExitWorkPointAddMode();   // C12-9：ESC 退出作业点选点模式
                    e.Handled = true;
                }
                else if (_currentWidgetCreator != null)
                {
                    ExitAddMode();
                    e.Handled = true;
                }
            }
        }

        /// <summary>全选当前画面的所有控件（选中状态不持久化，不推 Undo 快照）。</summary>
        private void SelectAllWidgets()
        {
            var widgets = _viewModel.CurrentScreen?.Widgets;
            if (widgets == null || widgets.Count == 0) return;
            foreach (var w in widgets) w.IsSelected = true;
            _selectionManager.UpdateSelectionUI();
            SyncSelectionToPropertyPanel();
        }

        /// <summary>方向键微移选中控件（1px / Shift 10px）。同会话连续按键只推一次 Undo 快照（防 key repeat 吃光栈）。</summary>
        private int _nudgeSnapshotDepth = -1;
        private void NudgeSelectedWidgets(Key key, double step)
        {
            var selected = _viewModel.CurrentScreen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null || selected.Count == 0)
            {
                _nudgeSnapshotDepth = -1;
                return;
            }
            // 版本变化（任何 push/undo/redo 发生）→ 重新推快照并记录版本；同版本（纯微移）不重复推
            if (_nudgeSnapshotDepth != _viewModel.UndoVersion)
            {
                _viewModel.PushUndoSnapshot();
                _nudgeSnapshotDepth = _viewModel.UndoVersion;
            }
            double dx = 0, dy = 0;
            switch (key)
            {
                case Key.Left: dx = -step; break;
                case Key.Right: dx = step; break;
                case Key.Up: dy = -step; break;
                case Key.Down: dy = step; break;
            }
            foreach (var w in selected)
            {
                // P7：多边形顶点为画布绝对坐标——方向键微移同步平移顶点（防表格数据陈旧/重选跳回）
                if (w is PolygonWidget poly && (dx != 0 || dy != 0)) poly.TranslatePoints(dx, dy);
                w.X += dx; w.Y += dy;
            }
            // 不触发全量 LoadCanvas（会清空选中）：Widget.X/Y setter 的 PropertyChanged
            // 已驱动 Canvas.Left/Top 绑定自动更新位置，选中状态（TwoWay 绑定）保持不变
            _selectionManager.UpdateSelectionUI();   // 确保选中装饰器跟随新位置
            _propertyViewModel.RefreshPolygonPointRowsDisplay();   // C12-12：方向键微移平移顶点后表格实时刷新
        }

        /// <summary>粘贴到当前画布中心附近（快捷键 Ctrl+V；右键菜单粘贴用 PasteWidget_Click）。</summary>
        private void PasteWidgetsAtCurrent()
        {
            if (_viewModel.CurrentScreen == null || _clipboard.Count == 0) return;
            _viewModel.PushUndoSnapshot();
            foreach (var data in _clipboard)
            {
                using var ms = new MemoryStream(data);
                var w = Serializer.Deserialize<Widget>(ms);
                // 相对中心粘贴：后续可改为光标位置
                double dx = 20 / _zoomLevel, dy = 20 / _zoomLevel;
                // P7：多边形顶点为画布绝对坐标——整体平移与 X/Y 同步（保持形状相对关系）
                if (w is PolygonWidget poly && poly.Points.Count > 0) poly.TranslatePoints(dx, dy);
                w.X += dx; w.Y += dy;
                w.ObjectName = UniqueName(w.ObjectName);
                _viewModel.CurrentScreen.Widgets.Add(w);
            }
            _viewModel.NotifyCanvasRefreshNeeded();
        }




        private Button? _activeToolboxBtn;
        private string? _activeToolboxTag;
        private object? _activeToolboxOriginalContent;  // 保存按钮原始 Content（可能为图标/文本）
        #endregion

        #region 原 添加模式（第 3849-3944 行）

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
            _polygonPoints.Clear();   // 清理多边形已收集顶点（切换工具防旧点残留）
            _isDrawingPreview = false;

            // 进入新的添加模式
            _currentWidgetCreator = tag switch
            {
                "Button" => new ButtonWidgetCreator(),
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
                "TextList" => new TextListWidgetCreator(),
                "Frame" => new FrameWidgetCreator(),
                "ProgressBar" => new ProgressBarWidgetCreator(),
                "DateTime" => new DateTimeWidgetCreator(),
                "UserView" => new WindowWidgetCreator(WindowType.UserView),
                "AlarmView" => new WindowWidgetCreator(WindowType.AlarmView),
                "RobotList" => new WindowWidgetCreator(WindowType.RobotList),
                "Polygon" => new PolygonWidgetCreator(),
                "TrendView" => new TrendViewWidgetCreator(),   // P-4：趋势图（单点式）
                "HistoryView" => new HistoryViewWidgetCreator(),   // P-5：历史记录（单点式）
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
            _polygonPoints.Clear();
            _isDrawingPreview = false;
            DrawingCanvas.Cursor = Cursors.Arrow;
            HideDrawPreview();
            if (_activeToolboxBtn != null && _activeToolboxOriginalContent != null)
                _activeToolboxBtn.Content = _activeToolboxOriginalContent;
            _activeToolboxBtn = null;
            _activeToolboxTag = null;
            _activeToolboxOriginalContent = null;
        }



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
        #endregion

        #region 原 视觉树查找（第 4030-4049 行）

        /// <summary>判断视觉树中是否存在 Button（用于工具栏拖拽检测）。</summary>
        private static bool IsDescendantOfButton(DependencyObject? child)
        {
            while (child != null)
            {
                if (child is System.Windows.Controls.Primitives.ButtonBase) return true;   // P3-11：覆盖 Button/ToggleButton/RepeatButton（🛰 开关不被工具栏拖拽拦截）
                child = VisualTreeHelper.GetParent(child);
            }
            return false;
        }



        private void SaveCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            SaveProject(_currentProject, _currentProject.ProjectFilePath);
        }
        #endregion

    }
}
