using CommunityToolkit.Mvvm.Messaging;
using NavigatorHMI.Common;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.CommandLayer.Handlers;
using NavigatorHMI.ViewModels;
using NavigatorHMI.Views.Behaviors;
using NavigatorHMI.Views.Helpers;
using NavigatorHMI.Views.Helpers.Creators;
using ProtoBuf;
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
        private Action? _resizeDragCompletedCallback;   // C12-12：缩放结束回调（字段化供 EditWindow_Closed 清理——静态回调闭包泄漏防护）
        private Action? _resizeDragDeltaCallback;   // W-4c：缩放过程回调（字段化同上；节流刷新矩形端点表格）
        private DateTime _lastResizeDeltaRefresh = DateTime.MinValue;   // W-4c：拖拽过程刷新节流时间戳
        /// <summary>画布尺寸回调引用（Closing 时比较清理）。</summary>
        private Func<Size>? _getCanvasSizeCallback;

        // 画布缩放
        private double _zoomLevel = 1.0;
        /// <summary>P10：首次 Loaded 保存的 XAML 默认布局（内存流；「恢复默认布局」菜单从此恢复）。</summary>
        private MemoryStream? _defaultLayout;
        /// <summary>P10：布局序列化回调回填用——ContentId → 原始 Content（XAML 命名元素；Deserialize 重建 LayoutRoot 后重新挂载）。</summary>
        private readonly Dictionary<string, object> _layoutContents = new();
        private ScaleTransform _canvasScale = new(1, 1);


        // 框选
        private Point _marqueeStart;
        private bool _isMarquee;

        // 两点式绘制（Line/Circle/Rectangle）
        private Point _drawStartPoint;
        private bool _isDrawingPreview;
        /// <summary>多边形连续点击收集的顶点（世界地图批 3：Polygon 模式左键加点、右键闭合）。</summary>
        private readonly List<System.Windows.Point> _polygonPoints = new();
        /// <summary>作业范围加点编辑模式（P3/P4）：地图点击加经纬度点，ESC/右键退出。</summary>
        private bool _isEditingWorkRange;
        private bool _isAddingWorkPoint;   // C12-9：作业点地图选点模式（点击地图追加作业点）
        private System.Windows.Shapes.Path? _drawPreviewPath;
        // V-3a：marker 拖拽状态（地图上直接移动作业点/范围点——拖拽中只移视觉位置，MouseUp 提交模型）
        private System.Windows.Shapes.Ellipse? _dragMarker;
        private object? _dragModel;   // MapWorkPoint / WorkRangePoint
        private Point _markerDragStart;
        private bool _markerDragMoved;
        // V-3b：选点模式十字光标旁经纬度跟随标签
        private TextBlock? _cursorGeoLabel;

        // CLI 命令历史
        private readonly List<string> _cliHistory = new();
        private int _historyIndex;

        private void Canvas_PreviewRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 世界地图画面：地图交互优先——画布右键位置记录短路（画布右键菜单挂 Canvas_MouseDown，世界地图时 Canvas 已穿透不触发）
            if (_viewModel?.IsWorldMapActive == true) return;
            // 多边形绘制：右键闭合（≥3 点生成控件，退出添加模式）；不足 3 点退出绘制（P6：短路防弹画布菜单）
            if (_currentWidgetCreator is PolygonWidgetCreator)
            {
                if (_polygonPoints.Count >= 3)
                {
                    FinishPolygon(e.GetPosition(DrawingCanvas));
                }
                else
                {
                    _polygonPoints.Clear();
                    _isDrawingPreview = false;
                    HideDrawPreview();
                    ExitAddMode();
                }
                e.Handled = true;
                return;
            }
            // GetPosition 已返回逻辑坐标（LayoutTransform 逆变换），禁除缩放（双重除 bug）；_contextMenuPos 供粘贴定位
            _contextMenuPos = e.GetPosition(DrawingCanvas);
        }

        /// <summary>多边形闭合：以收集顶点生成 PolygonWidget（包围盒左上角为 X/Y），退出添加模式。</summary>
        private void FinishPolygon(Point ignoredEnd)
        {
            if (_viewModel?.CurrentScreen == null || _currentWidgetCreator is not PolygonWidgetCreator pgc) return;
            _viewModel.PushUndoSnapshot();
            var poly = pgc.Create(_polygonPoints, _viewModel.CurrentScreen);
            _viewModel.CurrentScreen.Widgets.Add(poly);
            MarkProjectDirty();
            _polygonPoints.Clear();
            _isDrawingPreview = false;
            ExitAddMode();
            _dragBehavior.SuppressDragUntilMouseUp();
        }

        // widget的专职类
        private readonly WidgetSelectionManager _selectionManager;
        private readonly WidgetDragBehavior _dragBehavior;
        private readonly DispatcherTimer _clockTimer;   // D6：画布 1Hz 时钟（未绑定 DateTime 控件实时刷新）

        // 树形视图和 Widget 的右键菜单处理器
        private readonly TreeViewContextMenuHandler _treeContextMenuHandler;
        private readonly WidgetContextMenuHandler _widgetContextMenuHandler;
        // 属性面板
        private readonly PropertyViewModel _propertyViewModel;
        /// <summary>属性面板 ViewModel（供 XAML 绑定）。</summary>
        public PropertyViewModel PropertyVM => _propertyViewModel;
        // 变量管理器面板
        private readonly VariableManagerViewModel _variableManagerVM;
        /// <summary>变量管理器 ViewModel（供 XAML 绑定）。</summary>
        public VariableManagerViewModel VariableManagerVM => _variableManagerVM;
        // 通讯配置面板
        private readonly CommunicationDeviceViewModel _commDeviceVM;
        /// <summary>通讯配置 ViewModel（供 XAML 绑定）。</summary>
        public CommunicationDeviceViewModel CommDeviceVM => _commDeviceVM;

        // 报警配置面板
        private readonly AlarmManagerViewModel _alarmVM;
        /// <summary>报警配置 ViewModel（供 XAML 绑定）。</summary>
        public AlarmManagerViewModel AlarmVM => _alarmVM;
        // 列表管理面板
        private readonly ListManagerViewModel _listManagerVM;
        /// <summary>列表管理 ViewModel（供 XAML 绑定）。</summary>
        public ListManagerViewModel ListManagerVM => _listManagerVM;
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

            // 4. 初始化右键菜单处理器（注入 CommandService 统一命令层 + Undo 快照回调）；树节点菜单用独立的 TreeScreenMenu（删除/重命名）
            _treeContextMenuHandler = new TreeViewContextMenuHandler(
                TreeScreenMenu,
                _viewModel.CommandService,
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
                () => _currentWidgetCreator != null,  // 添加/绘制模式标志
                () => _propertyViewModel.RefreshPolygonPointRowsDisplay());  // C12-12：拖拽结束 → 多边形顶点表格实时刷新

            // 6. 订阅事件
            WeakReferenceMessenger.Default.Register<ScreenAddedMessage>(this, OnScreenAdded);

            // 7. 在 Loaded 事件中初始化 UI
            this.Loaded += EditWindow_Loaded;
            this.Closed += EditWindow_Closed;
            _viewModel.AiMessages.CollectionChanged += AiMessages_CollectionChanged;   // AI 消息自动滚动到底

            // D6：画布 1Hz 时钟——未绑定 DateTime 控件显示实时当前时间（Tick 通知 DisplayText 刷新）
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) =>
            {
                var screen = _viewModel.CurrentScreen;
                if (screen == null) return;
                foreach (var w in screen.Widgets)
                    if (w is DateTimeWidget dt) dt.RefreshDisplay();
                if (_viewModel.IsWorldMapActive) UpdateDynamicGeoWidgets();   // 批 4：GPS 动态点位（绑变量 + 基准值 → 屏幕位置）
            };
            _clockTimer.Start();

            // 8. 订阅 ViewModel 事件
            _viewModel.CanvasReloadRequested += LoadCanvas;
            _viewModel.RefreshCanvasRequested += () => LoadCanvas(_viewModel.CurrentScreen);
            _viewModel.ProjectDirtyRequested += MarkProjectDirty;

            // 10. 初始化属性窗口（必须在 LoadCanvas 之前——LoadCanvas 注入画布尺寸到 PropertyViewModel）
            _propertyViewModel = new PropertyViewModel();
            // 注入工程引用（绑定变量下拉数据源）+ 命令服务（bind_tag 统一入口）
            _propertyViewModel.Project = _currentProject;
            _propertyViewModel.CommandService = _viewModel.CommandService;
            // 属性面板修改前 Push 撤销快照（属性修改可撤销；选中同步初始化不触发）
            _propertyViewModel.BeforeModify = () => _viewModel.PushUndoSnapshot();
            // 绑定失败 → 弹出已推但未生效的空撤销快照（防空快照污染撤销栈）
            _propertyViewModel.OnModifyFailed = () => _viewModel.PopUndoSnapshot();
            // 世界地图「全局叠加」勾选变化 → 局部刷新虚影层（不动主层与选中，LoadCanvas 含 ClearAllSelection 副作用不可全量重载）
            _propertyViewModel.OverlayChanged = () => RefreshGlobalGhost();
            // 勾选写 WorldMapConfig（POCO 不在脏订阅范围）→ 显式标脏（防关闭静默丢失）
            _propertyViewModel.DirtyRequested = MarkProjectDirty;
            // P4：作业点/作业范围点行编辑 → overlay 实时刷新（表格改动立即反映到地图）
            _propertyViewModel.WorldMapPointsChanged = () => UpdateAllGeoWidgets();
            // P5：锁定预览勾选变化 → 地图交互开关（ViewLocked 时点击/滚轮短路在事件处理器内实现）
            _propertyViewModel.WorldMapViewLockChanged = () => { ApplyWorldMapLock(); UpdateAllGeoWidgets(); };
            // C12-2：WorldMap 撤销/重做后刷新地图 overlay + 作业点/范围点表格（数据已由 UndoManager 写回模型）
            _viewModel.WorldMapUndoRequested = () =>
            {
                ApplyWorldMapLock();   // 撤销/重做可能改 ViewLocked → 同步 Navigator 锁
                UpdateAllGeoWidgets();
                _propertyViewModel.RefreshWorkPointRows();
                _propertyViewModel.RefreshWorkRangeRows();
            };
            // X-1c：变量基准值撤销/重做后刷新（变量表 + 地图点位置由变量驱动）
            _viewModel.TagUndoRequested = () => RefreshAfterTagBaseValueChange();
            _viewModel.TagRedoRequested = () => RefreshAfterTagBaseValueChange();
            // C12-2：作业点/范围点表格增删改/行编辑前快照 → WorldMap 独立撤销栈（原 BeforeModify 绑 Widgets 栈，快照不含 WorldMap）
            _propertyViewModel.WorldMapBeforeModify = () => _viewModel.PushWorldMapUndoSnapshot();
            _propertyViewModel.WorldMapZoomToBoxRequested = () =>
            {
                if (_viewModel?.IsWorldMapActive == true && !TryFitWorldMapViewport())
                    MessageBox.Show(this, "没有可定位的数据：作业点/作业范围点均为空", "视口自适应", MessageBoxButton.OK, MessageBoxImage.Information);   // C12-7：全空时提示（原静默无反应）
            };
            // 缩放手柄：拖拽开始 Push 撤销快照 + 画布尺寸提供器（缩放钳制）
            _resizeDragStartedCallback = () => _viewModel.PushUndoSnapshot();
            _getCanvasSizeCallback = () => new Size(_propertyViewModel.CanvasWidth, _propertyViewModel.CanvasHeight);
            SelectorHelper.ResizeDragStarted = _resizeDragStartedCallback;
            SelectorHelper.GetCanvasSize = _getCanvasSizeCallback;
            // C12-12：缩放结束 → 多边形顶点表格实时刷新（缩放平移顶点后表格不再陈旧）；V-6b：矩形端点表格同刷新
            _resizeDragCompletedCallback = () =>
            {
                _propertyViewModel.RefreshPolygonPointRowsDisplay();
                _propertyViewModel.RefreshRectanglePointRowsDisplay();
            };
            SelectorHelper.ResizeDragCompleted = _resizeDragCompletedCallback;
            // W-4c：缩放手柄拖拽过程 → 矩形端点表格实时同步（100ms 节流；DragCompleted 兜底最终值）
            _resizeDragDeltaCallback = () =>
            {
                var now = DateTime.UtcNow;
                if ((now - _lastResizeDeltaRefresh).TotalMilliseconds < 100) return;
                _lastResizeDeltaRefresh = now;
                _propertyViewModel.RefreshRectanglePointRowsDisplay();
            };
            SelectorHelper.ResizeDragDelta = _resizeDragDeltaCallback;
            // 选中变化（单选/多选/清空）→ 同步属性面板多选状态
            _selectionManager.SelectionChanged += SyncSelectionToPropertyPanel;

            // 11. 初始化变量管理器面板（新建/编辑/删除走 CommandService）
            _variableManagerVM = new VariableManagerViewModel(_currentProject, _viewModel.CommandService);
            _variableManagerVM.TagEditRequested += OnTagEditRequested;
            _variableManagerVM.TagDeleteRequested += OnTagDeleteRequested;

            // 12. 初始化通讯配置面板（新建/编辑/删除走 CommandService）
            _commDeviceVM = new CommunicationDeviceViewModel(_currentProject, _viewModel.CommandService);
            _commDeviceVM.DeviceEditRequested += OnDeviceEditRequested;

            // 12.6 初始化报警配置面板（新建/编辑/删除走 CommandService）
            _alarmVM = new AlarmManagerViewModel(_currentProject, _viewModel.CommandService);
            _alarmVM.AlarmEditRequested += OnAlarmEditRequested;
            _alarmVM.AlarmDeleteRequested += OnAlarmDeleteRequested;

            // 12.5 初始化列表管理面板（新建/删除/重命名/编辑项走 CommandService）
            _listManagerVM = new ListManagerViewModel(_currentProject, _viewModel.CommandService);
            _viewModel.ListManager = _listManagerVM;   // A12：列表撤销优先回调接入（工具栏撤销/Ctrl+Z/菜单统一）

            // 13. 注册设计态变量解析器（绑定控件渲染基准值）
            TagResolver.CurrentProject = _currentProject;
            // 14. 存量迁移：已绑定（BoundTag 或列表 ListRef）的控件清除旧设计态值
            //     （Image/Frame 仅绑 BoundTag 无 ListRef 时保留 ImagePath——显示仍读它，与 DisplayPath 驱动源一致）
            foreach (var screen in _currentProject.Screens)
                foreach (var w in screen.Widgets.Where(WidgetDesignValue.HasBinding))
                    WidgetDesignValue.Clear(w);

            LoadCanvas(_viewModel.CurrentScreen);

            _isProjectDirty = false;
            this.CheckBinding();


            // 全局点击监听：点击 Popup 外部时关闭菜单（三个菜单统一处理）
            this.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var clicked = e.OriginalSource as DependencyObject;
                CloseIfOutsideClick(TreeContextMenu, clicked);
                CloseIfOutsideClick(TreeScreenMenu, clicked);
                CloseIfOutsideClick(WidgetContextMenu, clicked);
                CloseIfOutsideClick(WorldMapContextMenu, clicked);
            };
            // 10. 初始化属性窗口
            _selectionManager.WidgetSelected += OnWidgetSelected;
        }

        #region 变量管理器
        /// <summary>变量新建/编辑：对话框收集 → CommandService 落库（GUI/CLI/AI 同一路径）。</summary>
        private void OnTagEditRequested(Tag? tag)
        {
            var dlg = new TagEditDialog(tag, _currentProject) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var result = dlg.Result;
            if (tag == null)
            {
                var r = _viewModel.CommandService.Execute("create_tag", new Dictionary<string, object?>
                {
                    ["name"] = result.Name,
                    ["data_type"] = result.DataType.ToString(),
                    ["source"] = result.Source,
                    ["unit"] = result.Unit,
                    ["scan_interval"] = result.ScanIntervalMs,
                    ["deadband"] = result.Deadband,
                    ["description"] = result.Description,
                    ["base_value"] = result.BaseValue,
                });
                if (!r.Success) MessageBox.Show(r.ErrorMessage ?? "创建变量失败", "变量", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // 只提交变化的字段（避免空操作）；重命名由 update_tag 级联同步控件/报警引用
                var p = new Dictionary<string, object?> { ["name"] = tag.Name };
                if (result.Name != tag.Name) p["new_name"] = result.Name;
                if (result.DataType != tag.DataType) p["data_type"] = result.DataType.ToString();
                if (result.Source != tag.Source) p["source"] = result.Source;
                if (result.Unit != tag.Unit) p["unit"] = result.Unit;
                if (result.ScanIntervalMs != tag.ScanIntervalMs) p["scan_interval"] = result.ScanIntervalMs;
                if (result.Deadband != tag.Deadband) p["deadband"] = result.Deadband;
                if (result.Description != tag.Description) p["description"] = result.Description;
                if (result.BaseValue != tag.BaseValue) p["base_value"] = result.BaseValue;
                if (p.Count > 1)
                {
                    var r = _viewModel.CommandService.Execute("update_tag", p);
                    if (!r.Success) MessageBox.Show(r.ErrorMessage ?? "更新变量失败", "变量", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>变量删除：确认 → CommandService（delete_tag 自带引用保护）。</summary>
        /// <summary>变量表格快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void TagGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { TagCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { TagCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.Key == Key.Delete) { TagDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { TagGrid.SelectAll(); e.Handled = true; }
        }

        /// <summary>报警表格快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void AlarmGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { AlarmCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { AlarmCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.Key == Key.Delete) { AlarmDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { AlarmGrid.SelectAll(); e.Handled = true; }
        }

        /// <summary>文本列表快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void TextListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { TextListDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { TextListBox.SelectAll(); e.Handled = true; }
        }

        /// <summary>图片列表快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void ImageListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { ImageListDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { ImageListBox.SelectAll(); e.Handled = true; }
        }
        /// <summary>设备表格快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void DeviceGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { DeviceCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { DeviceCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.Key == Key.Delete) { DeviceDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { DeviceGrid.SelectAll(); e.Handled = true; }
        }
        /// <summary>打开事件配置对话框（属性面板事件栏 [配置…] 按钮）。选中控件 → 控件级事件；选中世界地图画面 → 地图级 onClick 事件。</summary>
        private void OpenEventConfig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not EventType evt) return;
            if (PropertyVM.SelectedWidget is Widget w)
            {
                var dlg = new EventConfigDialog(_currentProject, w, evt, _viewModel.CommandService) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    MarkProjectDirty();   // WidgetEvent.Actions 就地修改不触发订阅 → 显式标脏防静默丢配置
                    PropertyVM.RefreshEventBar(w);   // 配置后刷新事件栏状态
                }
            }
            else if (PropertyVM.IsWorldMapScreen)
            {
                var dlg = new EventConfigDialog(_currentProject, null, evt, _viewModel.CommandService) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    MarkProjectDirty();
                    PropertyVM.RefreshWorldMapEventBar();   // 配置后刷新事件栏计数
                }
            }
        }
        /// <summary>表格行复制剪贴板（Ctrl+C 深拷贝选中行，Ctrl+V 粘贴新建行副本）。</summary>
        private readonly List<object> _copiedRows = new();

        /// <summary>生成不冲突的名字（原名已存在 → 原名_副本 → 原名_副本2 …）。</summary>
        private static string UniqueName(string name, IEnumerable<string> existing)
        {
            if (!existing.Contains(name, StringComparer.OrdinalIgnoreCase)) return name;
            for (int i = 1; ; i++)
            {
                var candidate = $"{name}_副本{i}";
                if (!existing.Contains(candidate, StringComparer.OrdinalIgnoreCase)) return candidate;
            }
        }

        /// <summary>变量表格复制粘贴：Ctrl+C 深拷贝选中变量行，Ctrl+V 新建变量副本（重名自动 _副本N）。</summary>
        private void TagCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (Tag t in TagGrid.SelectedItems)
                    _copiedRows.Add(ProtoBuf.Serializer.DeepClone(t));
                return;
            }
            var existing = _viewModel.CurrentProject.Tags.Select(t => t.Name).ToList();
            foreach (Tag t in _copiedRows.OfType<Tag>())
            {
                var name = UniqueName(t.Name, existing);
                existing.Add(name);
                var r = _viewModel.CommandService.Execute("create_tag", new Dictionary<string, object?>
                {
                    ["name"] = name, ["data_type"] = t.DataType.ToString(), ["source"] = t.Source,
                    ["unit"] = t.Unit, ["scan_interval"] = t.ScanIntervalMs, ["deadband"] = t.Deadband,
                    ["description"] = t.Description, ["base_value"] = t.BaseValue,
                });
                if (!r.Success) MessageBox.Show($"{name}: {r.ErrorMessage}", "复制变量", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>报警表格复制粘贴：Ctrl+C 深拷贝选中报警行，Ctrl+V 新建报警副本。</summary>
        private void AlarmCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (AlarmRule a in AlarmGrid.SelectedItems)
                    _copiedRows.Add(ProtoBuf.Serializer.DeepClone(a));
                return;
            }
            var existing = _viewModel.CurrentProject.Alarms.Select(a => a.Name).ToList();
            foreach (AlarmRule a in _copiedRows.OfType<AlarmRule>())
            {
                var name = UniqueName(a.Name, existing);
                existing.Add(name);
                var r = _viewModel.CommandService.Execute("create_alarm", new Dictionary<string, object?>
                {
                    ["name"] = name, ["tag_name"] = a.TagName, ["type"] = a.Type.ToString(),
                    ["threshold"] = a.Threshold, ["deadband"] = a.Deadband, ["delay_ms"] = a.DelayMs,
                    ["severity"] = a.Level.ToString(), ["message"] = a.Message,
                });
                if (!r.Success) MessageBox.Show($"{name}: {r.ErrorMessage}", "复制报警", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>设备表格复制粘贴：Ctrl+C 深拷贝选中设备行，Ctrl+V 新建设备副本。</summary>
        private void DeviceCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (DeviceConfig d in DeviceGrid.SelectedItems)
                    _copiedRows.Add(ProtoBuf.Serializer.DeepClone(d));
                return;
            }
            var existing = _viewModel.CurrentProject.Devices.Select(d => d.Name).ToList();
            foreach (DeviceConfig d in _copiedRows.OfType<DeviceConfig>())
            {
                var name = UniqueName(d.Name, existing);
                existing.Add(name);
                var r = _viewModel.CommandService.Execute("create_device", new Dictionary<string, object?>
                {
                    ["name"] = name, ["protocol"] = d.Protocol.ToString(), ["connection_info"] = d.ConnectionInfo,
                });
                if (!r.Success) MessageBox.Show($"{name}: {r.ErrorMessage}", "复制设备", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>文本列表项复制粘贴：Ctrl+C 存值，Ctrl+V 添加新项（复制值）。</summary>
        private void TextItemsCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (ListItemVM it in TextItemsGrid.SelectedItems) _copiedRows.Add(it.Value);
                return;
            }
            foreach (string v in _copiedRows.OfType<string>())
                _listManagerVM.PasteItem(ListType.Text, v);
        }

        /// <summary>图像列表项复制粘贴：Ctrl+C 存值，Ctrl+V 添加新项（复制值）。</summary>
        private void ImageItemsCopyPaste_Click(object sender, bool copy)
        {
            if (copy)
            {
                _copiedRows.Clear();
                foreach (ListItemVM it in ImageItemsGrid.SelectedItems) _copiedRows.Add(it.Value);
                return;
            }
            foreach (string v in _copiedRows.OfType<string>())
                _listManagerVM.PasteItem(ListType.Image, v);
        }

        /// <summary>P2-1 用户组：新建组弹窗。</summary>
        private void GroupAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new GroupEditDialog { Owner = this };
            if (dlg.ShowDialog() == true)
                _viewModel.AddGroupCommand(dlg.GroupName.Trim(), dlg.GetPermissions());
        }

        /// <summary>P2-1 用户组：双击行编辑弹窗（W-5a：预设组组名只读——防改名绕过删除保护，仅可改权限）。</summary>
        private void GroupsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItem is not UserGroup g) return;
            var dlg = new GroupEditDialog { Owner = this };
            dlg.Prefill(g.Name, g.Permissions);
            dlg.IsPreset = g.Name is "管理员" or "操作员" or "访客";
            if (dlg.ShowDialog() == true)
                _viewModel.UpdateGroupCommand(g.Name, dlg.GroupName.Trim(), dlg.GetPermissions());
        }

        /// <summary>A2：组表格 DELETE 键批量删除选中（一次确认；预设组命令层 BLOCKED 跳过并汇总）。PreviewKeyDown 隧道事件——KeyDown 冒泡会被 DataGridCell 内置删除命令拦截（原 KeyDown 失效根因）；仅无修饰键时触发（对齐窗口级 Delete 分支）。</summary>
        private void GroupsGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None) return;
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItems.Count == 0) return;
            var groups = dg.SelectedItems.OfType<UserGroup>().ToList();
            if (System.Windows.MessageBox.Show($"确定删除选中的 {groups.Count} 个用户组吗？（预设组不可删）", "用户组",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            {
                e.Handled = true;
                return;
            }
            _viewModel.DeleteGroupsBatch(groups.Select(g => g.Name).ToList());
            e.Handled = true;
        }

        /// <summary>P2-1 用户组：删除选中（按钮/右键；预设组命令层 BLOCKED）。</summary>
        private void GroupDelete_Click(object sender, RoutedEventArgs e)
        {
            var g = FindGroup(sender);
            if (g != null) _viewModel.DeleteGroupCommand(g.Name);
        }

        /// <summary>P2-1/任务12 用户组：复制选中组（多选 SelectedItems——右键行已在 PreviewMouseRightButtonDown 选中）。</summary>
        private void GroupCopy_Click(object sender, RoutedEventArgs e)
        {
            var grid = FindName("GroupsGrid") as System.Windows.Controls.DataGrid;
            if (grid == null) return;
            _viewModel.CopyGroupsCommand(grid.SelectedItems.OfType<UserGroup>().ToList());
        }

        /// <summary>P2-1/任务12 用户组：右键未选中行时先选中该行（右键复制语义正确）。</summary>
        private void GroupsGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg) return;
            var row = FindDataGridRow(e);
            if (row?.Item is UserGroup g && !dg.SelectedItems.Contains(g))
                dg.SelectedItem = g;
        }

        /// <summary>从鼠标事件源上溯到 DataGridRow（无则 null）。</summary>
        private static System.Windows.Controls.DataGridRow? FindDataGridRow(System.Windows.Input.MouseButtonEventArgs e)
        {
            var el = e.OriginalSource as System.Windows.DependencyObject;
            while (el != null && el is not System.Windows.Controls.DataGridRow)
                el = System.Windows.Media.VisualTreeHelper.GetParent(el);
            return el as System.Windows.Controls.DataGridRow;
        }

        /// <summary>P2-1/任务12 用户组：粘贴组副本（多副本）。</summary>
        private void GroupPaste_Click(object sender, RoutedEventArgs e) => _viewModel.PasteGroupsCommand();

        /// <summary>从 sender（按钮/菜单项）溯源到组表格并取选中组。</summary>
        private UserGroup? FindGroup(object sender)
        {
            var fe = sender as System.Windows.FrameworkElement;
            if (fe?.DataContext is UserGroup g) return g;   // 菜单项：DataContext=右键行组
            var grid = FindName("GroupsGrid") as System.Windows.Controls.DataGrid;
            return grid?.SelectedItem as UserGroup;
        }
        /// <summary>P2-12 用户：添加用户弹窗（用户名+密码+组下拉）。</summary>
        private void UserAdd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UserEditDialog(_viewModel.UserGroups) { Owner = this };
            if (dlg.ShowDialog() == true)
                _viewModel.AddUserCommand(dlg.UserName.Trim(), dlg.Password, dlg.SelectedGroup);
        }

        /// <summary>P2-12 用户：双击行编辑弹窗（用户名+密码+组）。</summary>
        private void UsersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItem is not UserAccount u) return;
            var dlg = new UserEditDialog(_viewModel.UserGroups) { Owner = this };
            dlg.Prefill(u.UserName, u.GroupName);
            if (dlg.ShowDialog() == true)
                _viewModel.UpdateUserCommand(u.UserName, dlg.UserName.Trim(), dlg.Password, dlg.SelectedGroup);
        }

        /// <summary>补 #2：用户表格 DELETE 键批量删除选中用户（一次确认；命令层最后管理员保护，失败汇总——对齐 A2 组表格语义）。PreviewKeyDown 隧道事件（KeyDown 冒泡会被 DataGridCell 内置删除命令拦截）。</summary>
        private void UsersGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Delete || Keyboard.Modifiers != ModifierKeys.None) return;
            if (sender is not System.Windows.Controls.DataGrid dg || dg.SelectedItems.Count == 0) return;
            var users = dg.SelectedItems.OfType<UserAccount>().ToList();
            if (System.Windows.MessageBox.Show($"确定删除选中的 {users.Count} 个用户吗？（最后管理员不可删）", "用户管理",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            {
                e.Handled = true;
                return;
            }
            _viewModel.DeleteUsersBatch(users.Select(u => u.UserName).ToList());
            e.Handled = true;
        }

        /// <summary>P2-12/任务12 用户：复制选中用户（多选 SelectedItems——右键行已在 PreviewMouseRightButtonDown 选中）。</summary>
        private void UserCopy_Click(object sender, RoutedEventArgs e)
        {
            var grid = FindName("UsersGrid") as System.Windows.Controls.DataGrid;
            if (grid == null) return;
            _viewModel.CopyUsersCommand(grid.SelectedItems.OfType<UserAccount>().ToList());
        }

        /// <summary>P2-12/任务12 用户：右键未选中行时先选中该行（右键复制语义正确）。</summary>
        private void UsersGrid_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.DataGrid dg) return;
            var row = FindDataGridRow(e);
            if (row?.Item is UserAccount u && !dg.SelectedItems.Contains(u))
                dg.SelectedItem = u;
        }

        /// <summary>P2-12/任务12 用户：粘贴用户副本（多副本）。</summary>
        private void UserPaste_Click(object sender, RoutedEventArgs e) => _viewModel.PasteUsersCommand();

        /// <summary>W3b 用户面板：删除选中用户（走 VM DeleteUserCommand——最后管理员保护）。</summary>
        private void UserDelete_Click(object sender, RoutedEventArgs e) => _viewModel.DeleteUserCommand();

        /// <summary>W3b 用户面板：保存安全设置（走 VM SaveSecurityCommand）。</summary>
        private void UserSaveSecurity_Click(object sender, RoutedEventArgs e) => _viewModel.SaveSecurityCommand();
        /// <summary>批量删除选中设备（删除按钮 + 右键；多选时全部删除）。</summary>
        private void DeviceDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = DeviceGrid.SelectedItems.Cast<DeviceConfig>().ToList();
            if (selected.Count == 0) return;
            var names = string.Join("、", selected.Select(d => $"\"{d.Name}\""));
            var confirm = MessageBox.Show($"确定删除设备 {names} 吗？", "删除设备",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var dev in selected)
            {
                var r = _viewModel.CommandService.Execute("delete_device", new Dictionary<string, object?> { ["name"] = dev.Name });
                if (!r.Success) failed.Add($"{dev.Name}: {r.ErrorMessage}");
            }
            if (failed.Count > 0)
                MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "设备", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        /// <summary>批量删除选中变量（删除按钮 + 右键菜单；多选时全部删除，被引用项拒绝并提示）。</summary>
        private void TagDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TagGrid.SelectedItems.Cast<Tag>().ToList();
            if (selected.Count == 0) return;
            var names = string.Join("、", selected.Select(t => $"\"{t.Name}\""));
            var confirm = MessageBox.Show($"确定删除变量 {names} 吗？", "删除变量",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var tag in selected)
            {
                var r = _viewModel.CommandService.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = tag.Name });
                if (!r.Success) failed.Add($"{tag.Name}: {r.ErrorMessage}");
            }
            if (failed.Count > 0)
                MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "变量", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnTagDeleteRequested(Tag tag)
        {
            var confirm = MessageBox.Show($"确定删除变量 \"{tag.Name}\" 吗？", "删除变量",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var r = _viewModel.CommandService.Execute("delete_tag", new Dictionary<string, object?> { ["name"] = tag.Name });
            if (!r.Success)
                MessageBox.Show(r.ErrorMessage ?? "删除失败", "变量", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>双击变量行 → 编辑。</summary>
        private void TagGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_variableManagerVM.SelectedTag != null)
                _variableManagerVM.EditTagCommand.Execute(null);
        }
        #endregion

        #region 通讯配置
        /// <summary>设备新建/编辑：对话框收集 → CommandService 落库（GUI/CLI/AI 同一路径）。</summary>
        private void OnDeviceEditRequested(DeviceConfig? device)
        {
            var dlg = new DeviceEditDialog(device, _currentProject) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var result = dlg.Result;
            if (device == null)
            {
                var r = _viewModel.CommandService.Execute("configure_device", new Dictionary<string, object?>
                {
                    ["name"] = result.Name,
                    ["protocol"] = result.Protocol.ToString(),
                    ["connection_info"] = result.ConnectionInfo,
                });
                if (!r.Success) MessageBox.Show(r.ErrorMessage ?? "新建设备失败", "通讯", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // 只提交变化的字段（避免空操作）
                var p = new Dictionary<string, object?> { ["name"] = device.Name };
                if (result.Name != device.Name) p["new_name"] = result.Name;
                if (result.Protocol != device.Protocol) p["protocol"] = result.Protocol.ToString();
                if (result.ConnectionInfo != device.ConnectionInfo) p["connection_info"] = result.ConnectionInfo;
                if (p.Count > 1)
                {
                    var r = _viewModel.CommandService.Execute("update_device", p);
                    if (!r.Success) MessageBox.Show(r.ErrorMessage ?? "更新设备失败", "通讯", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>双击设备行 → 编辑。</summary>
        private void DeviceGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_commDeviceVM.SelectedDevice != null)
                _commDeviceVM.EditDeviceCommand.Execute(null);
        }
        #endregion

        #region 报警配置
        /// <summary>报警新建/编辑：对话框收集 → CommandService 落库（GUI/CLI/AI 同一路径）。</summary>
        private void OnAlarmEditRequested(AlarmRule? alarm)
        {
            var dlg = new AlarmEditDialog(alarm, _currentProject) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            var result = dlg.Result;
            if (alarm == null)
            {
                var r = _viewModel.CommandService.Execute("create_alarm", new Dictionary<string, object?>
                {
                    ["name"] = result.Name,
                    ["tag_name"] = result.TagName,
                    ["type"] = result.Type.ToString(),
                    ["threshold"] = result.Threshold,
                    ["deadband"] = result.Deadband,
                    ["delay_ms"] = result.DelayMs,
                    ["severity"] = result.Level.ToString(),
                    ["message"] = result.Message,
                });
                if (!r.Success) MessageBox.Show(r.ErrorMessage ?? "创建报警失败", "报警", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // 只提交变化的字段（避免空操作）
                var p = new Dictionary<string, object?> { ["name"] = alarm.Name };
                if (result.Name != alarm.Name) p["new_name"] = result.Name;
                if (result.TagName != alarm.TagName) p["tag_name"] = result.TagName;
                if (result.Type != alarm.Type) p["type"] = result.Type.ToString();
                if (result.Threshold != alarm.Threshold) p["threshold"] = result.Threshold;
                if (result.Deadband != alarm.Deadband) p["deadband"] = result.Deadband;
                if (result.DelayMs != alarm.DelayMs) p["delay_ms"] = result.DelayMs;
                if (result.Level != alarm.Level) p["severity"] = result.Level.ToString();
                if (result.Message != alarm.Message) p["message"] = result.Message;
                if (p.Count > 1)
                {
                    var r = _viewModel.CommandService.Execute("update_alarm", p);
                    if (!r.Success) MessageBox.Show(r.ErrorMessage ?? "更新报警失败", "报警", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>报警删除：确认 → CommandService（delete_alarm 无引用限制，直接删除）。</summary>
        /// <summary>批量删除选中报警（删除按钮 + 右键；多选时全部删除）。</summary>
        private void AlarmDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = AlarmGrid.SelectedItems.Cast<AlarmRule>().ToList();
            if (selected.Count == 0) return;
            var names = string.Join("、", selected.Select(a => $"\"{a.Name}\""));
            var confirm = MessageBox.Show($"确定删除报警 {names} 吗？", "删除报警",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var failed = new List<string>();
            foreach (var alarm in selected)
            {
                var r = _viewModel.CommandService.Execute("delete_alarm", new Dictionary<string, object?> { ["name"] = alarm.Name });
                if (!r.Success) failed.Add($"{alarm.Name}: {r.ErrorMessage}");
            }
            if (failed.Count > 0)
                MessageBox.Show("部分删除失败：\n" + string.Join("\n", failed), "报警", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>批量删除选中文本列表项（删除按钮/Delete 键；多选时全部删除）。</summary>
        private void TextItemsDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TextItemsGrid.SelectedItems.Cast<ListItemVM>().ToList();
            if (selected.Count > 0) _listManagerVM.RemoveItems(ListType.Text, selected);
        }

        /// <summary>批量删除选中图片列表项（删除按钮/Delete 键；多选时全部删除）。</summary>
        private void ImageItemsDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ImageItemsGrid.SelectedItems.Cast<ListItemVM>().ToList();
            if (selected.Count > 0) _listManagerVM.RemoveItems(ListType.Image, selected);
        }

        /// <summary>文本列表项快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void TextItemsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { TextItemsCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { TextItemsCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;   // 单元格编辑态：Delete 交给文本编辑，防整行误删
            if (e.Key == Key.Delete) { TextItemsDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { TextItemsGrid.SelectAll(); e.Handled = true; }
        }

        /// <summary>图片列表项快捷键：Delete 删除选中、Ctrl+A 全选。</summary>
        private void ImageItemsGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { ImageItemsCopyPaste_Click(sender, true); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { ImageItemsCopyPaste_Click(sender, false); e.Handled = true; return; }
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;   // 单元格编辑态：Delete 交给文本编辑，防整行误删
            if (e.Key == Key.Delete) { ImageItemsDelete_Click(sender, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { ImageItemsGrid.SelectAll(); e.Handled = true; }
        }
        /// <summary>批量删除选中文本列表（删除按钮；多选时全部删除）。</summary>
        private void TextListDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = TextListBox.SelectedItems.Cast<ListDef>().ToList();
            if (selected.Count > 0) _listManagerVM.DeleteLists(selected);
        }

        /// <summary>批量删除选中图片列表（删除按钮；多选时全部删除）。</summary>
        private void ImageListDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ImageListBox.SelectedItems.Cast<ListDef>().ToList();
            if (selected.Count > 0) _listManagerVM.DeleteLists(selected);
        }
        private void OnAlarmDeleteRequested(AlarmRule alarm)
        {
            var confirm = MessageBox.Show($"确定删除报警 \"{alarm.Name}\" 吗？", "删除报警",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            var r = _viewModel.CommandService.Execute("delete_alarm", new Dictionary<string, object?> { ["name"] = alarm.Name });
            if (!r.Success)
                MessageBox.Show(r.ErrorMessage ?? "删除失败", "报警", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>双击报警行 → 编辑。</summary>
        private void AlarmGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_alarmVM.SelectedAlarm != null)
                _alarmVM.EditAlarmCommand.Execute(null);
        }
        #endregion

        #region 变量拖拽（变量行 → 标签页/树节点切画面 → 画布生成绑定 IO Field）
        /// <summary>拖拽起点（TagGrid_PreviewMouseMove 判定阈值用）。</summary>
        private Point _tagDragStart;
        /// <summary>框选起点（空白处按下时为 null；非 null = 框选进行中）。</summary>
        private Point? _tagMarqueeStart;
        /// <summary>悬停切换画面节流时间戳（DragOver 高频触发，200ms 内不重复切换）。</summary>
        private DateTime _lastDragSwitchTime = DateTime.MinValue;

        private void TagGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _tagDragStart = e.GetPosition(null);
            var src = e.OriginalSource as DependencyObject;
            // 多选状态下按住已选中行：拦截 DataGrid 单击单选重置（WPF 多选时单击已选中项会重置为单选），
            // 保持多选供拖拽——用户需求：框选后按住不放 = 拖动多个变量。
            // 排除 Ctrl/Shift 修饰键：修饰键点击交还 DataGrid 标准行为（Ctrl 取消多选 / Shift 区间选择）
            if (TagGrid.SelectedItems.Count > 1
             && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0
             && FindDataContext<Tag>(src) is Tag pressedTag
             && TagGrid.SelectedItems.Contains(pressedTag))
            {
                e.Handled = true;
            }
            // 列头（排序）/滚动条/行上按下 → 不进入框选（保持拖拽/排序/滚动正常）；仅数据区空白按下框选
            _tagMarqueeStart = (FindDataContext<Tag>(src) is Tag
                             || FindVisualParent<System.Windows.Controls.Primitives.DataGridColumnHeader>(src) != null
                             || FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(src) != null)
                ? null
                : e.GetPosition(TagGrid);
            if (_tagMarqueeStart != null)
            {
                RebuildRowRects();   // 框选启动时刷新行位置缓存（滚动后最新，框选高频不再强制布局）
                TagGrid.Focus();     // 激活 DataGrid（未激活时选中行模板触发器用浅灰≈背景，高亮不可见；激活后选中蓝）
                TagGrid.CaptureMouse();   // 拖出松开也能收到 Up，防 Marquee 残留
            }
        }

        /// <summary>按住变量行拖动超过阈值 → 开始拖拽（数据：当前选中集合 List&lt;Tag&gt;；多选一起拖）。</summary>
        private void TagGrid_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            // 框选模式：空白处拖动 → 画框 + 实时选中相交行
            if (_tagMarqueeStart is { } ms)
            {
                var cur = e.GetPosition(TagGrid);
                var rect = new Rect(ms, cur);
                rect = new Rect(Math.Min(ms.X, cur.X), Math.Min(ms.Y, cur.Y), Math.Abs(cur.X - ms.X), Math.Abs(cur.Y - ms.Y));
                TagGridMarquee.Visibility = Visibility.Visible;
                // 外包 Canvas：SetLeft/Top 权威定位（相对 Canvas 原点 = TagGrid 原点，与 GetPosition(TagGrid) 同源）
                Canvas.SetLeft(TagGridMarquee, rect.X);
                Canvas.SetTop(TagGridMarquee, rect.Y);
                TagGridMarquee.Width = rect.Width;
                TagGridMarquee.Height = rect.Height;
                SelectRowsInRect(rect);
                e.Handled = true;
                return;
            }
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _tagDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
             && Math.Abs(pos.Y - _tagDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (FindDataContext<Tag>(e.OriginalSource as DependencyObject) is not Tag tag) return;
            // 拖拽携带当前选中集合（含当前行；无多选时为单行）
            var tags = TagGrid.SelectedItems.Cast<Tag>().ToList();
            if (!tags.Contains(tag)) tags.Add(tag);
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject("NavigatorHMI.TagList", tags), DragDropEffects.Copy);
        }

        /// <summary>框选结束：隐藏覆盖层并清状态（保持选中结果）。</summary>
        private void TagGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            TagGridMarquee.Visibility = Visibility.Collapsed;
            _tagMarqueeStart = null;
            if (TagGrid.IsMouseCaptured) TagGrid.ReleaseMouseCapture();
        }

        /// <summary>框选行位置缓存（相对 TagGrid 坐标）：避免框选 MouseMove 高频 TransformToAncestor 强制布局（主线程忙 → 选中高亮延迟）。</summary>
        private List<Rect>? _tagRowRects;

        private void TagGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 守卫：新旧尺寸相同（初始布局连发/无变化）跳过重建
            if (e.NewSize != e.PreviousSize) RebuildRowRects();
        }

        /// <summary>重建行位置缓存（Items 数量或视口尺寸变化时；框选启动时确保最新）。</summary>
        private void RebuildRowRects()
        {
            _tagRowRects = new List<Rect>();
            for (int i = 0; i < TagGrid.Items.Count; i++)
            {
                if (TagGrid.ItemContainerGenerator.ContainerFromIndex(i) is not DataGridRow row)
                {
                    _tagRowRects.Add(Rect.Empty);   // 未生成容器（虚拟化）：跳过命中
                    continue;
                }
                var topLeft = row.TransformToAncestor(TagGrid).Transform(new Point(0, 0));
                _tagRowRects.Add(new Rect(topLeft.X, topLeft.Y, row.ActualWidth, row.ActualHeight));
            }
        }

        /// <summary>按矩形选中与其相交的变量行（用缓存行矩形，框选高频不强制布局；命中集差分更新避免每帧 SelectionChanged）。</summary>
        private void SelectRowsInRect(Rect rect)
        {
            if (_tagRowRects == null || _tagRowRects.Count != TagGrid.Items.Count)
                RebuildRowRects();
            // 计算命中集
            var hit = new List<object>();
            for (int i = 0; i < _tagRowRects!.Count && i < TagGrid.Items.Count; i++)
            {
                var rowRect = _tagRowRects[i];
                if (rowRect.IsEmpty || !rowRect.IntersectsWith(rect)) continue;
                if (TagGrid.Items[i] is Tag t) hit.Add(t);
            }
            // 差分更新：选中集合与命中集相同则跳过（避免每帧 Clear/Add 触发 SelectionChanged 与绑定刷新）
            if (TagGrid.SelectedItems.Count == hit.Count && hit.All(TagGrid.SelectedItems.Contains))
                return;
            TagGrid.SelectedItems.Clear();
            foreach (var t in hit) TagGrid.SelectedItems.Add(t);
        }

        /// <summary>沿视觉树上溯查找指定类型 DataContext（DataGridRow/TreeViewItem 通用）。</summary>
        private static T? FindDataContext<T>(DependencyObject? source) where T : class
        {
            while (source != null)
            {
                if (source is System.Windows.FrameworkElement fe && fe.DataContext is T t) return t;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }

        /// <summary>拖拽的是否为变量（统一判定；数据为选中集合 List&lt;Tag&gt;）。</summary>
        private static bool IsTagDrag(DragEventArgs e, out List<Tag> tags)
        {
            tags = e.Data.GetDataPresent("NavigatorHMI.TagList")
                && e.Data.GetData("NavigatorHMI.TagList") is List<Tag> list
                ? list : new List<Tag>();
            return tags.Count > 0;
        }

        /// <summary>标签页悬停/放下 → 自动切换画面（200ms 节流防 DragOver 高频来回切换）。</summary>
        private void ScreenTab_DragOver(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                e.Effects = DragDropEffects.Copy;
                if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Screen screen
                 && !ReferenceEquals(screen, _viewModel.CurrentScreen)
                 && (DateTime.Now - _lastDragSwitchTime).TotalMilliseconds >= 200)
                {
                    _lastDragSwitchTime = DateTime.Now;
                    _viewModel.ActivateScreen(screen);   // 悬停即切换（节流）
                }
                e.Handled = true;
            }
            else e.Effects = DragDropEffects.None;
        }

        private void ScreenTab_Drop(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Screen screen)
                    _viewModel.ActivateScreen(screen);
                e.Handled = true;
            }
        }

        /// <summary>树画面节点悬停/放下 → 自动切换画面（200ms 节流防 DragOver 高频来回切换）。</summary>
        private void ProjectTree_DragOver(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                e.Effects = DragDropEffects.Copy;
                if (FindDataContext<ScreenItemNode>(e.OriginalSource as DependencyObject) is { } node
                 && !ReferenceEquals(node.Screen, _viewModel.CurrentScreen)
                 && (DateTime.Now - _lastDragSwitchTime).TotalMilliseconds >= 200)
                {
                    _lastDragSwitchTime = DateTime.Now;
                    _viewModel.ActivateScreen(node.Screen);   // 悬停即切换（节流）
                }
                e.Handled = true;
            }
            else e.Effects = DragDropEffects.None;
        }

        private void ProjectTree_Drop(object sender, DragEventArgs e)
        {
            if (IsTagDrag(e, out _))
            {
                if (FindDataContext<ScreenItemNode>(e.OriginalSource as DependencyObject) is { } node)
                    _viewModel.ActivateScreen(node.Screen);
                e.Handled = true;
            }
        }

        /// <summary>画布悬停：接受变量拖拽（Copy 光标）。</summary>
        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            // 世界地图画面：地图交互优先——不接受拖放（防拖变量到地图误创建控件）
            if (_viewModel?.IsWorldMapActive == true) { e.Effects = DragDropEffects.None; return; }
            if (IsTagDrag(e, out _)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
            else e.Effects = DragDropEffects.None;
        }

        /// <summary>画布放下 → 生成绑定该变量的 IO Field（add_widget + bound_tag 一步落库）。</summary>
        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            if (_viewModel?.IsWorldMapActive == true) return;   // 世界地图画面：地图交互优先，不接受拖放
            if (!IsTagDrag(e, out var tags)) return;
            if (_viewModel.CurrentScreen == null) return;
            // GetPosition(DrawingCanvas) 经 LayoutTransform 逆变换已返回逻辑坐标，禁再次除缩放（双重除 bug）
            var pos = e.GetPosition(DrawingCanvas);
            // 坐标钳制到画布逻辑范围（拖到边框/滚动条区域不生成越界控件）
            pos.X = Math.Clamp(pos.X, 0, Math.Max(0, _viewModel.CurrentScreen.Width - 100));
            pos.Y = Math.Clamp(pos.Y, 0, Math.Max(0, _viewModel.CurrentScreen.Height - 30));
            _viewModel.PushUndoSnapshot();
            // 批量：每个变量生成一个绑定 IO Field（纵向错开 35px，底部钳制）
            // 抑制中间全量重建（N 个变量 = 1 次刷新而非 N 次），结束统一刷新一次
            int success = 0;
            _viewModel.SuppressRefresh = true;
            try
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    var y = Math.Min((int)pos.Y + i * 35, Math.Max(0, _viewModel.CurrentScreen.Height - 30));
                    var r = _viewModel.CommandService.Execute("add_widget", new Dictionary<string, object?>
                    {
                        ["screen_name"] = _viewModel.CurrentScreen.Name,
                        ["widget_type"] = "iofield",
                        ["x"] = (int)pos.X, ["y"] = y,
                        ["width"] = 100, ["height"] = 30,
                        ["bound_tag"] = tags[i].Name,
                    });
                    if (r.Success) success++;
                    else
                        MessageBox.Show($"变量 \"{tags[i].Name}\": {r.ErrorMessage}", "拖拽绑定", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally { _viewModel.SuppressRefresh = false; }
            if (success > 0)
                _viewModel.NotifyCanvasRefreshNeeded();   // 统一刷新一次
            else
                _viewModel.PopUndoSnapshot();   // 全部失败：弹出已推但未生效的空快照（与 OnModifyFailed 语义一致）
            e.Handled = true;
        }
        #endregion

        /// <summary>点击「通讯」Tab：切到通讯配置视图（与变量管理器/画面互斥切换）。</summary>
        private void CommTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateCommunication();
            e.Handled = true;
        }

        /// <summary>关闭「通讯」Tab（当前激活时切回当前画面）。</summary>
        private void CommTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseCommunicationTab();
            e.Handled = true;
        }

        /// <summary>点击「列表」Tab：切到列表管理视图（保持当前子页，与画面/变量/通讯互斥切换）。</summary>
        private void ListManagerTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateListManager(_viewModel.ListManagerListType);
            e.Handled = true;
        }

        /// <summary>关闭「列表」Tab（当前激活时切回当前画面）。</summary>
        private void ListManagerTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseListManagerTab();
            e.Handled = true;
        }


        /// <summary>点击「报警」Tab：切到报警配置视图（与画面/变量/通讯/列表互斥切换）。</summary>
        private void AlarmTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateAlarm();
            e.Handled = true;
        }

        /// <summary>关闭「报警」Tab（当前激活时切回当前画面）。</summary>
        private void AlarmTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseAlarmTab();
            e.Handled = true;
        }

        /// <summary>W3 点击「用户」Tab：切到用户面板（Activate 语义——Tab 已开时切换，不重复开）。</summary>
        private void UserTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateUser();
            e.Handled = true;
        }

        /// <summary>W3 关闭「用户」Tab（当前激活时切回当前画面）。</summary>
        private void UserTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseUserTab();
            e.Handled = true;
        }

        /// <summary>图片路径输入框回车确认：结束编辑并刷新行校验（路径不存在行标红）。</summary>
        private void ImagePathBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var box = sender as TextBox;
                if (box != null)
                {
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    var vm = box.DataContext as ListItemVM;
                    if (vm != null)
                    {
                        vm.NotifyPathRecheck();
                        DataGrid? grid = FindVisualParent<DataGrid>(box);
                        if (grid != null) grid.CommitEdit(DataGridEditingUnit.Row, true);
                    }
                }
                e.Handled = true;
            }
        }

        /// <summary>图片列表路径选择器：点选图片文件 → 转相对路径存值（与手动输入语义一致，自动刷新校验/预览）。</summary>
        private void ImageListBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ListItemVM item) return;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg;*.ico|所有文件|*.*",
            };
            if (dlg.ShowDialog() != true) return;
            // 工程已保存且图片在工程目录内 → 存相对路径（与手动输入一致）；否则存绝对路径
            var store = dlg.FileName;
            var projectDir = System.IO.Path.GetDirectoryName(_currentProject.ProjectFilePath);
            if (!string.IsNullOrEmpty(projectDir))
            {
                try
                {
                    var rel = System.IO.Path.GetRelativePath(projectDir, System.IO.Path.GetFullPath(dlg.FileName));
                    if (!rel.StartsWith("..")) store = rel;
                }
                catch (ArgumentException) { /* 路径跨盘等：保留绝对路径 */ }
            }
            store = store.Replace('\\', '/');   // 统一正斜杠（setter 层已下沉，此处幂等冗余）
            item.Value = store;
            item.NotifyPathRecheck();
        }


        /// <summary>沿视觉树上溯查找指定类型父元素（图片路径回车确认定位 DataGrid 用）。</summary>
        private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T t) return t;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
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

        /// <summary>点击目标在 Popup 外部时关闭菜单（Popup 内容有独立视觉树，点击菜单内按钮不会误关）。</summary>
        private void CloseIfOutsideClick(System.Windows.Controls.Primitives.Popup menu, DependencyObject? clicked)
        {
            if (menu.IsOpen && clicked != null && !IsDescendantOf(clicked, menu.Child))
                menu.IsOpen = false;
        }

        private void EditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureItemsControlInCanvas();

            // P10：首次 Loaded 保存 XAML 默认布局（内存流），「恢复默认布局」菜单从此恢复
            RegisterLayoutContents();   // 收集 ContentId → Content（反序列化回调回填用）
            try
            {
                _defaultLayout = new MemoryStream();
                new XmlLayoutSerializer(DockManager).Serialize(_defaultLayout);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[EditWindow] 保存默认布局失败: {ex.Message}");
                _defaultLayout = null;
            }

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            // 世界地图底图（Mapsui）：POC 用高德在线瓦片（国内可达；OSM 在本网络环境不通）。
            // 产品化：WorldMapConfig.TileSource=="amap" 高德在线 / =="offline" 离线 MBTiles（后续接配置）
            try
            {
                var map = new Mapsui.Map();
                var tileSource = new BruTile.Web.HttpTileSource(
                    new BruTile.Predefined.GlobalSphericalMercator(),
                    "https://webrd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&scale=1&style=8&x={x}&y={y}&z={z}",
                    new[] { "1", "2", "3", "4" },
                    "amap", null, null, null,
                    req => req.Headers.UserAgent.ParseAdd("NavigatorHMI/1.0 (Industrial HMI Config Tool)"));   // BruTile 6 requestModifier 为 Action（void）；UA 必须纯 ASCII（中文头值 HttpClient 拒绝——读图定位）
                map.Layers.Add(new Mapsui.Tiling.Layers.TileLayer(tileSource) { Name = "高德地图" });

                // 最小/最大缩放限制：高德 webrd 瓦片有效范围 z3~z19（低于 z3 无瓦片→空白，POC 实测问题②；ResZoom = Web Mercator 每像素米数）
                double ResZoom(int z) => 156543.03392804097 / Math.Pow(2, z);
                map.Navigator.OverrideZoomBounds = new Mapsui.MMinMax(ResZoom(19), ResZoom(3));

                // 初始视图（批 4）：进入世界地图时视口自适应优先（作业点/控件 Geo 包围盒）；无 Geo 数据回退中国 z6
                map.ViewportInitialized += (_, _) =>
                {
                    ApplyWorldMapLock();   // C12-10：初始化后按 ViewLocked 设置 Navigator 锁（打开工程即锁定）
                    if (!(_viewModel?.IsWorldMapActive == true && TryFitWorldMapViewport()))
                    {
                        var (mx, my) = Mapsui.Projections.SphericalMercator.FromLonLat(104.06, 30.67);
                        map.Navigator.CenterOnAndZoomTo(new Mapsui.MPoint(mx, my), ResZoom(6), 0, Mapsui.Animations.Easing.Linear);
                    }
                };

                WorldMapControl.Map = map;
                // 世界地图交互绘制（批 4）：绘制工具激活时地图点击 → 经纬度创建控件；MapControl 与画布同容器同尺寸同位置（坐标一致）
                WorldMapControl.PreviewMouseLeftButtonDown += WorldMap_PreviewMouseLeftButtonDown;
                WorldMapControl.PreviewMouseRightButtonDown += WorldMap_PreviewMouseRightButtonDown;   // 隧道先于 Mapsui 内部处理，防右键被吞
                WorldMapControl.MouseMove += WorldMap_MouseMove;
                WorldMapControl.PreviewMouseWheel += WorldMap_PreviewMouseWheel;   // P5：ViewLocked 禁缩放（滚轮）
                // 视口变化（平移/缩放）→ 重算含 Geo 控件屏幕位置（固定点位/多边形/线/圆跟随经纬度）
                map.Navigator.ViewportChanged += (_, _) =>
                {
                    if (_viewModel?.IsWorldMapActive == true) UpdateAllGeoWidgets();
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[WorldMap] Mapsui 初始化异常: {ex.Message}");
            }

            AiBallToggle.IsChecked = true;   // 任务11：🛰 开关状态置开（内嵌球 XAML 默认可见）

            System.Diagnostics.Debug.WriteLine($"✅ EditWindow 加载完成");
            System.Diagnostics.Debug.WriteLine($"   CurrentScreen: {_viewModel?.CurrentScreen?.Name}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {_viewModel?.CurrentScreen?.Widgets?.Count}");
        }

        #region 世界地图交互绘制与视口（批 4）

        /// <summary>屏幕坐标 → 经纬度（自实现换算：Mapsui 5.1 Viewport 无 ScreenToWorld API；无旋转场景）。</summary>
        private GeoPoint? ScreenToGeo(Point screenPos)
        {
            var map = WorldMapControl?.Map;
            if (map == null) return null;
            var vp = map.Navigator.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0 || !(vp.Resolution > 0)) return null;   // NaN 守卫：!(NaN>0)=true
            return MapViewportMath.ScreenToGeo(vp.CenterX, vp.CenterY, vp.Resolution, vp.Width, vp.Height, screenPos);
        }

        /// <summary>经纬度 → 画布屏幕坐标（与 ScreenToGeo 互逆；动态点位用）。</summary>
        private Point? GeoToScreen(GeoPoint geo)
        {
            var map = WorldMapControl?.Map;
            if (map == null) return null;
            var vp = map.Navigator.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0 || !(vp.Resolution > 0)) return null;   // NaN 守卫：!(NaN>0)=true
            return MapViewportMath.GeoToScreen(vp.CenterX, vp.CenterY, vp.Resolution, vp.Width, vp.Height, geo);
        }

        /// <summary>世界地图左键：ViewLocked → 短路禁平移（不模拟点击切换——需求变更 B：切换仅 FW 端执行）；
        /// 作业范围编辑模式：点击加点；Polygon 绘制：收集顶点。</summary>
        private void WorldMap_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.IsWorldMapActive != true) return;

            // P5/P6：锁定预览 → 短路禁平移（左键不落到 Mapsui 拖动）；点击切换画面事件由 FW 运行时执行，设计态不模拟。
            // V-3c：锁定仅禁平移缩放——加点/选点模式放行（允许放置新端点）；点击 marker（选中/拖拽查看）放行；其余保持短路
            // W-1c：点空白（非 marker/非加点选点）清除选中态
            if (_viewModel.CurrentProject?.WorldMap?.ViewLocked == true
                && !_isAddingWorkPoint && !_isEditingWorkRange && !IsWorkPointMarkerHit(e))
            {
                ClearGeoSelection();
                e.Handled = true;
                return;
            }

            if (_currentWidgetCreator is not PolygonWidgetCreator && !_isEditingWorkRange && !_isAddingWorkPoint)
            {
                if (!IsWorkPointMarkerHit(e)) ClearGeoSelection();   // W-1c：点 marker 不清除（选中/拖拽由其 handler 处理）
                return;
            }
            var pos = e.GetPosition(WorldMapControl);
            var geo = ScreenToGeo(pos);
            if (geo == null) return;

            if (_isAddingWorkPoint)
            {
                // C12-9：作业点地图选点（名称自动编号；连续加点，右键/ESC 退出）
                var wm = _viewModel.CurrentProject?.WorldMap;
                if (wm != null)
                {
                    // U-B4/C12-4：相邻重复点拦截（防误双击，与作业范围加点同规则）
                    var last = wm.WorkPoints.Count > 0 ? wm.WorkPoints[^1].FixedPoint : null;
                    if (last != null && Math.Abs(last.Longitude - geo.Longitude) < 1e-9 && Math.Abs(last.Latitude - geo.Latitude) < 1e-9)
                    {
                        e.Handled = true;
                        return;
                    }
                    _viewModel.PushWorldMapUndoSnapshot();   // 加点前快照（C12-2 机制）
                    wm.WorkPoints.Add(new MapWorkPoint { Name = NextWorkPointName(wm), FixedPoint = geo });
                    MarkProjectDirty();
                    UpdateAllGeoWidgets();
                    _propertyViewModel.RefreshWorkPointRows();   // C12-6 同款：表格实时刷新
                }
                e.Handled = true;
                return;
            }

            if (_isEditingWorkRange)
            {
                // P3/P4：作业范围加点（无名称；仅阻止相邻重复点——防误双击；非相邻重复允许（围栏首尾闭合场景））
                var wm = _viewModel.CurrentProject?.WorldMap;
                if (wm != null)
                {
                    var last = wm.WorkRangePoints.Count > 0 ? wm.WorkRangePoints[^1].FixedPoint : null;
                    if (last != null && Math.Abs(last.Longitude - geo.Longitude) < 1e-9 && Math.Abs(last.Latitude - geo.Latitude) < 1e-9)
                    {
                        e.Handled = true;
                        return;
                    }
                    _viewModel.PushWorldMapUndoSnapshot();   // C12-2：地图加点前快照（WorldMap 独立撤销栈）
                    wm.WorkRangePoints.Add(new WorkRangePoint { FixedPoint = geo });
                    MarkProjectDirty();
                    UpdateAllGeoWidgets();
                    _propertyViewModel.RefreshWorkRangeRows();   // C12-6：地图加点后表格实时刷新（原只挂切画面路径）
                }
                e.Handled = true;
                return;
            }

            if (_currentWidgetCreator is PolygonWidgetCreator)
            {
                _polygonPoints.Add(pos);   // 预览 + 闭合用屏幕坐标
                _isDrawingPreview = true;
                ShowDrawPreview();
            }
            e.Handled = true;   // 阻止地图平移
        }

        /// <summary>世界地图右键：作业范围编辑模式 → 退出；Polygon 绘制 → 闭合（≥3 点）/退出（不足）；其他 → 弹地图右键菜单（P3）。
        /// 锁定预览（ViewLocked）不短路右键——菜单照弹，配置不受锁（2026-08-11 需求变更 A）。</summary>
        private void WorldMap_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.IsWorldMapActive != true) return;

            if (_isAddingWorkPoint)
            {
                ExitWorkPointAddMode();
                e.Handled = true;
                return;
            }

            if (_isEditingWorkRange)
            {
                ExitWorkRangeEditMode();
                e.Handled = true;
                return;
            }

            if (_currentWidgetCreator is not PolygonWidgetCreator)
            {
                // 非绘制模式：地图右键菜单（属性/事件配置/设置作业范围/清除作业范围）
                var scrPos = e.GetPosition(this);
                WorldMapContextMenu.HorizontalOffset = scrPos.X + 5;
                WorldMapContextMenu.VerticalOffset = scrPos.Y + 5;
                WorldMapContextMenu.IsOpen = true;
                e.Handled = true;
                return;
            }

            if (_polygonPoints.Count < 3)
            {
                _polygonPoints.Clear();
                HideDrawPreview(); ExitAddMode();
                e.Handled = true;
                return;
            }
            if (_viewModel?.CurrentScreen == null) return;
            _viewModel.PushUndoSnapshot();
            double minX = _polygonPoints.Min(p => p.X), minY = _polygonPoints.Min(p => p.Y);
            double maxX = _polygonPoints.Max(p => p.X), maxY = _polygonPoints.Max(p => p.Y);
            var poly = new PolygonWidget
            {
                X = minX, Y = minY,
                Width = Math.Max(maxX - minX, 1), Height = Math.Max(maxY - minY, 1),
                ObjectName = $"polygon_{_viewModel.CurrentScreen.Widgets.Count + 1}",
            };
            foreach (var p in _polygonPoints) poly.Points.Add(new PointD(p.X, p.Y));   // P7：画布绝对坐标（不再 -minX/-minY）
            _viewModel.CurrentScreen.Widgets.Add(poly);
            MarkProjectDirty();
            _polygonPoints.Clear();
            _isDrawingPreview = false;
            ExitAddMode();
            _dragBehavior.SuppressDragUntilMouseUp();
            e.Handled = true;
        }

        /// <summary>P5：ViewLocked 时滚轮缩放拦截（禁缩放）。</summary>
        private void WorldMap_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewModel?.CurrentProject?.WorldMap?.ViewLocked == true)
                e.Handled = true;
        }

        /// <summary>C12-10：按 ViewLocked 设置 Mapsui Navigator 锁（官方 PanLock/ZoomLock 禁平移/缩放——事件短路为冗余双保险；
        /// 勾选切换/工程加载/撤销重做时调用）。</summary>
        private void ApplyWorldMapLock()
        {
            var map = WorldMapControl?.Map;
            if (map == null) return;
            bool locked = _viewModel?.CurrentProject?.WorldMap?.ViewLocked == true;
            map.Navigator.PanLock = locked;
            map.Navigator.ZoomLock = locked;
        }

        /// <summary>P5：属性面板「视口自适应」按钮 → ZoomToBox 框选（VM 回调）。</summary>
        private void WorldMapZoomToBox_Click(object sender, RoutedEventArgs e) => _propertyViewModel.WorldMapZoomToBox();

        /// <summary>C12-9：进入作业点地图选点模式：点击地图追加作业点（名称自动编号；ESC/右键退出）。</summary>
        private void StartEditWorkPoint_Click(object sender, RoutedEventArgs e)
        {
            WorldMapContextMenu.IsOpen = false;
            if (_viewModel?.CurrentProject?.WorldMap == null) return;
            ExitAddMode();   // V-3（审查 Nit4）：清 Polygon 绘制/工具箱状态，保证模式互斥（防两模式并存致橡皮筋预览冻结）
            _isEditingWorkRange = false;   // 互斥：退出作业范围加点模式
            _isAddingWorkPoint = true;
            WorldMapControl.Cursor = Cursors.Cross;
            System.Diagnostics.Trace.WriteLine("[WorldMap] 进入作业点选点模式：点击地图追加作业点，ESC/右键退出");
        }

        /// <summary>C12-9：退出作业点选点模式（右键/ESC；不清除已加点）。</summary>
        private void ExitWorkPointAddMode()
        {
            if (!_isAddingWorkPoint) return;
            _isAddingWorkPoint = false;
            WorldMapControl.Cursor = Cursors.Arrow;
            RemoveCursorGeoLabel();   // V-3b：退出选点模式隐藏经纬度跟随
            UpdateAllGeoWidgets();
        }

        /// <summary>C12-9：作业点默认名"作业点N"（N 从现有数量+1 起递增，避开重名）。</summary>
        private static string NextWorkPointName(WorldMapConfig wm)
        {
            var names = new HashSet<string>(wm.WorkPoints.Select(w => w.Name));
            int n = wm.WorkPoints.Count + 1;
            while (names.Contains($"作业点{n}")) n++;
            return $"作业点{n}";
        }

        /// <summary>进入作业范围加点编辑模式：地图点击加经纬度、ESC/右键退出（P3/P4）。</summary>
        private void StartEditWorkRange_Click(object sender, RoutedEventArgs e)
        {
            WorldMapContextMenu.IsOpen = false;
            if (_viewModel?.CurrentProject?.WorldMap == null) return;
            ExitAddMode();   // V-3（审查 Nit4）：清 Polygon 绘制/工具箱状态，保证模式互斥
            _isAddingWorkPoint = false;   // 互斥：退出作业点选点模式（C12-9）
            _isEditingWorkRange = true;
            WorldMapControl.Cursor = Cursors.Cross;
            // 地图禁拖动由 Preview 隧道事件 e.Handled=true 实现（WorldMap_PreviewMouseLeftButtonDown 加点分支先于 Mapsui 内部处理）
            System.Diagnostics.Trace.WriteLine("[WorldMap] 进入作业范围加点编辑模式：点击地图加经纬度，ESC/右键退出");
        }

        /// <summary>退出作业范围加点编辑模式（右键/ESC；不清除已加点）。</summary>
        private void ExitWorkRangeEditMode()
        {
            if (!_isEditingWorkRange) return;
            _isEditingWorkRange = false;
            WorldMapControl.Cursor = Cursors.Arrow;
            RemoveCursorGeoLabel();   // V-3b：退出选点模式隐藏经纬度跟随
            UpdateAllGeoWidgets();
        }

        /// <summary>地图右键菜单：属性 → 选中世界地图画面（属性面板显示作业点/范围配置）。</summary>
        private void WorldMapProperty_Click(object sender, RoutedEventArgs e)
        {
            WorldMapContextMenu.IsOpen = false;
            var wmScreen = _viewModel?.CurrentProject?.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap);
            if (wmScreen != null) _propertyViewModel.SelectedScreen = wmScreen;
        }

        /// <summary>地图右键菜单：事件配置 → 打开地图级点击切换事件对话框（P3/P5）。</summary>
        private void WorldMapEventConfig_Click(object sender, RoutedEventArgs e)
        {
            WorldMapContextMenu.IsOpen = false;
            if (_currentProject == null) return;
            var dlg = new EventConfigDialog(_currentProject, null, EventType.onClick, _viewModel.CommandService) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                MarkProjectDirty();
                if (PropertyVM.IsWorldMapScreen) PropertyVM.RefreshWorldMapEventBar();   // 属性面板事件栏计数同步
            }
        }

        /// <summary>地图右键菜单：清除作业范围（清空 WorkRangePoints + 刷新 overlay）。</summary>
        private void ClearWorkRange_Click(object sender, RoutedEventArgs e)
        {
            WorldMapContextMenu.IsOpen = false;
            var wm = _viewModel?.CurrentProject?.WorldMap;
            if (wm == null || wm.WorkRangePoints.Count == 0) return;
            _viewModel.PushWorldMapUndoSnapshot();   // C12-2：清除作业范围前快照（原 PushUndoSnapshot 只含 Widgets，对 WorldMap 无效）
            wm.WorkRangePoints.Clear();
            MarkProjectDirty();
            UpdateAllGeoWidgets();
            _propertyViewModel.RefreshWorkRangeRows();   // C12-8：清除后表格实时刷新（原只挂切画面路径）
        }

        /// <summary>世界地图多边形绘制：橡皮筋预览（屏幕坐标——MapControl 与画布同容器同尺寸，坐标一致）。
        /// V-3b：选点模式（作业点/作业范围加点）十字光标旁实时经纬度（DMS 跟随）。</summary>
        private void WorldMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel?.IsWorldMapActive != true) return;
            if (_isAddingWorkPoint || _isEditingWorkRange)
            {
                UpdateCursorGeoLabel(e.GetPosition(WorldMapControl));
                return;
            }
            RemoveCursorGeoLabel();
            if (_currentWidgetCreator is not PolygonWidgetCreator) return;
            if (!_isDrawingPreview || _drawPreviewPath == null || _polygonPoints.Count == 0) return;
            var end = e.GetPosition(WorldMapControl);
            var fig = new System.Windows.Media.PathFigure { StartPoint = _polygonPoints[0], IsClosed = false };
            var segPts = new List<Point>(_polygonPoints.Skip(1)) { end };
            fig.Segments.Add(new System.Windows.Media.PolyLineSegment(segPts, true));
            _drawPreviewPath.Data = new System.Windows.Media.PathGeometry(new[] { fig });
            Canvas.SetLeft(_drawPreviewPath, 0);
            Canvas.SetTop(_drawPreviewPath, 0);
        }

        /// <summary>V-3b：选点模式十字光标旁实时经纬度（DMS 跟随标签，光标右下 14px）。</summary>
        private void UpdateCursorGeoLabel(Point pos)
        {
            if (WorkPointsOverlay == null) return;
            var geo = ScreenToGeo(pos);
            if (geo == null) { RemoveCursorGeoLabel(); return; }
            if (_cursorGeoLabel == null)
            {
                _cursorGeoLabel = new TextBlock
                {
                    FontSize = 11,
                    Foreground = Brushes.Black,
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                    Padding = new Thickness(2, 1, 2, 1),
                    IsHitTestVisible = false,
                };
                Panel.SetZIndex(_cursorGeoLabel, 5);   // 附加属性：置顶于 polygon/marker 之上
                WorkPointsOverlay.Children.Add(_cursorGeoLabel);
            }
            _cursorGeoLabel.Text = geo.ToDmsString();
            Canvas.SetLeft(_cursorGeoLabel, pos.X + 14);
            Canvas.SetTop(_cursorGeoLabel, pos.Y + 14);
        }

        /// <summary>V-3b：移除十字光标经纬度跟随标签。</summary>
        private void RemoveCursorGeoLabel()
        {
            if (_cursorGeoLabel != null)
            {
                WorkPointsOverlay?.Children.Remove(_cursorGeoLabel);
                _cursorGeoLabel = null;
            }
        }

        /// <summary>V-3c：点击位置是否命中作业点/范围点 marker（锁定短路放行判定；marker 带 Tag="workpoint-marker"）。</summary>
        private static bool IsWorkPointMarkerHit(MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            while (src != null)
            {
                if (src is FrameworkElement fe && fe.Tag as string == "workpoint-marker") return true;
                src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            }
            return false;
        }

        /// <summary>视口自适应：作业点 + 控件 Geo 包围盒（最小 1km + 10% padding）→ ZoomToBox；无 Geo 数据返回 false（调用方回退初始视图）。</summary>
        private bool TryFitWorldMapViewport()
        {
            var map = WorldMapControl?.Map;
            var project = _viewModel?.CurrentProject;
            if (map == null || project == null) return false;
            var vp = map.Navigator.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0 || !(vp.Resolution > 0)) return false;   // viewport 未初始化（首帧 Collapsed/NaN）守卫：防 ZoomToBox 除 0

            var geos = new List<GeoPoint>();
            // 1) 作业点（绑 GPS 变量取基准值 / 固定值）——C12-7 复用 ResolveWorkPointGeo（与 overlay 渲染语义一致）
            foreach (var wp in project.WorldMap?.WorkPoints ?? new())
                if (ResolveWorkPointGeo(project, wp.BoundTag, wp.FixedPoint) is { } wg) geos.Add(wg);
            // 2) 作业范围点并入包围盒（围栏顶点也要被视口自适应覆盖）
            foreach (var rp in project.WorldMap?.WorkRangePoints ?? new())
                if (ResolveWorkPointGeo(project, rp.BoundTag, rp.FixedPoint) is { } rg) geos.Add(rg);

            if (geos.Count == 0)
            {
                var wc = project.WorldMap;
                if (wc != null && wc.LngMin < wc.LngMax && wc.LatMin < wc.LatMax)
                {
                    geos.Add(new GeoPoint(wc.LngMin, wc.LatMin));
                    geos.Add(new GeoPoint(wc.LngMax, wc.LatMax));
                }
            }
            if (geos.Count == 0) return false;

            double minLng = geos.Min(g => g.Longitude), maxLng = geos.Max(g => g.Longitude);
            double minLat = geos.Min(g => g.Latitude), maxLat = geos.Max(g => g.Latitude);
            var (x1, y1) = Mapsui.Projections.SphericalMercator.FromLonLat(minLng, minLat);
            var (x2, y2) = Mapsui.Projections.SphericalMercator.FromLonLat(maxLng, maxLat);
            // 最小跨度 1km（3857 米）+ 10% padding
            if (x2 - x1 < 1000) { double add = (1000 - (x2 - x1)) / 2; x1 -= add; x2 += add; }
            if (y2 - y1 < 1000) { double add = (1000 - (y2 - y1)) / 2; y1 -= add; y2 += add; }
            double padX = (x2 - x1) * 0.1, padY = (y2 - y1) * 0.1;
            map.Navigator.ZoomToBox(new Mapsui.MRect(x1 - padX, y1 - padY, x2 + padX, y2 + padY),
                Mapsui.MBoxFit.Fit, 0, Mapsui.Animations.Easing.Linear);
            return true;
        }

        /// <summary>overlay 刷新节流：视口拖动高频触发 ViewportChanged 时合并刷新（80ms 防抖），避免每帧全量重建 marker（审查优化）。</summary>
        private DispatcherTimer? _overlayThrottleTimer;
        private bool _overlayRefreshQueued;

        /// <summary>重算世界地图作业点/作业范围点 overlay（经纬度 → 屏幕坐标换算后定位 marker）。视口变化与 1Hz 动态点位共用。</summary>
        private void UpdateAllGeoWidgets()
        {
            if (_overlayRefreshQueued) return;   // 节流中：跳过本次，防抖后合并刷新
            _overlayRefreshQueued = true;
            _overlayThrottleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _overlayThrottleTimer.Stop();
            _overlayThrottleTimer.Tick -= OverlayThrottle_Tick;
            _overlayThrottleTimer.Tick += OverlayThrottle_Tick;
            _overlayThrottleTimer.Start();
        }

        private void OverlayThrottle_Tick(object? sender, EventArgs e)
        {
            _overlayThrottleTimer?.Stop();
            _overlayRefreshQueued = false;
            RefreshWorkPointOverlayNow();
        }

        /// <summary>实际刷新 overlay（节流 tick / 显式调用时执行）。</summary>
        private void RefreshWorkPointOverlayNow()
        {
            // V-3a：拖拽中跳过重建——重建会移除拖拽中的 marker（WPF 自动释放捕获 → 拖拽静默中止 + 状态悬空）；
            // 拖拽 MouseUp 提交后调 UpdateAllGeoWidgets 自然刷新
            if (_dragMarker != null) return;
            var project = _viewModel?.CurrentProject;
            if (project?.WorldMap == null || WorkPointsOverlay == null) return;
            if (_viewModel.IsWorldMapActive != true) { WorkPointsOverlay.Children.Clear(); return; }
            WorkPointsOverlay.Children.Clear();
            _cursorGeoLabel = null;   // V-3b：Clear 已移除跟随标签 → 引用置空（否则标签"永久消失"，MouseMove 时重建）
            var map = WorldMapControl?.Map;
            if (map == null) return;
            var vp = map.Navigator.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0 || !(vp.Resolution > 0)) return;   // NaN 守卫：视口未初始化（首帧 Collapsed/NaN）不渲染

            var wm = project.WorldMap;
            var selRow = _propertyViewModel.SelectedWorkPointRow;   // V-2a：表格选中行 → 图上选中态（Model 引用相等）
            foreach (var wp in wm.WorkPoints)
            {
                var geo = ResolveWorkPointGeo(project, wp.BoundTag, wp.FixedPoint);
                if (geo == null) continue;
                var scr = GeoToScreen(geo);
                if (scr == null) continue;
                AddWorkPointMarker(scr.Value, wp.Name, isRangePoint: false, geo, wp.BoundTag, workPoint: wp, isSelected: selRow?.Model == wp);   // C12-5：气泡含名称/经纬度/绑定变量
            }
            // C12-3：作业范围闭合区域（≥3 个有效点 → 边界连线 + 半透明填充；IsHitTestVisible=false 不挡地图交互）
            var rangePts = new List<(Point Scr, GeoPoint Geo, string? Bound, WorkRangePoint Model)>();
            foreach (var rp in wm.WorkRangePoints)
            {
                var geo = ResolveWorkPointGeo(project, rp.BoundTag, rp.FixedPoint);
                if (geo == null) continue;
                var scr = GeoToScreen(geo);
                if (scr == null) continue;
                rangePts.Add((scr.Value, geo, rp.BoundTag, rp));
            }
            if (rangePts.Count >= 3)
            {
                var poly = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection(rangePts.Select(r => r.Scr)),
                    Fill = new SolidColorBrush(Color.FromArgb(40, 30, 144, 255)),   // 半透明蓝填充
                    Stroke = new SolidColorBrush(Color.FromArgb(200, 30, 144, 255)),
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false,   // 区域不挡地图点击/拖拽
                };
                Canvas.SetLeft(poly, 0); Canvas.SetTop(poly, 0);
                WorkPointsOverlay.Children.Add(poly);
            }
            var selRange = _propertyViewModel.SelectedWorkRangeRow;   // V-2a：范围点选中态
            foreach (var rp in rangePts) AddWorkPointMarker(rp.Scr, null, isRangePoint: true, rp.Geo, rp.Bound, rangePoint: rp.Model, isSelected: selRange?.Model == rp.Model);   // C12-5：范围点气泡含经纬度/绑定变量
        }

        /// <summary>作业点经纬度解析：绑 GPS 变量优先（取基准值，1Hz 模拟动态），否则固定值。</summary>
        private static GeoPoint? ResolveWorkPointGeo(HMIProject project, string boundTag, GeoPoint? fixedPoint)
        {
            if (!string.IsNullOrEmpty(boundTag))
            {
                var t = project.Tags.FirstOrDefault(x => x.Name == boundTag);
                if (t?.DataType == TagDataType.GPS && GeoPoint.TryParse(t.BaseValue, out var g) && g != null) return g;
            }
            return fixedPoint;
        }

        /// <summary>添加单个 marker 到 overlay：作业点 = 红点 + 名称标签；作业范围点 = 蓝色小方块（无名称）。
        /// C12-5：悬停气泡（ToolTip）显示名称/经纬度/绑定变量——悬停有反馈后即可验证 C12-4 重复点拦截。
        /// V-2：isSelected → 点外围 18×18 虚线框（表格选中联动）；选中时显示经纬度（DMS，名字下方/范围点上方）；
        /// marker 点击 → 选中对应表格行（双向选择，V-2c）。</summary>
        private void AddWorkPointMarker(Point scr, string? name, bool isRangePoint, GeoPoint? geo, string? boundTag,
            MapWorkPoint? workPoint = null, WorkRangePoint? rangePoint = null, bool isSelected = false)
        {
            // V-2a：选中态 → 点外围 18×18 虚线框（包住红点/蓝点，颜色区分——作业点橙/范围点深蓝）
            if (isSelected)
            {
                var box = new System.Windows.Shapes.Rectangle
                {
                    Width = 18, Height = 18,
                    Stroke = new SolidColorBrush(isRangePoint ? Color.FromRgb(0, 90, 200) : Color.FromRgb(230, 120, 0)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    IsHitTestVisible = false,   // 框不参与命中（点击仍命中内部 marker）
                };
                Canvas.SetLeft(box, scr.X - 9); Canvas.SetTop(box, scr.Y - 9);
                WorkPointsOverlay.Children.Add(box);
            }

            var mark = new System.Windows.Shapes.Ellipse();
            if (isRangePoint)
            {
                mark.Width = 10; mark.Height = 10;
                mark.Fill = Brushes.DodgerBlue; mark.Stroke = Brushes.White; mark.StrokeThickness = 1.5;
                Canvas.SetLeft(mark, scr.X - 5); Canvas.SetTop(mark, scr.Y - 5);
            }
            else
            {
                mark.Width = 12; mark.Height = 12;
                mark.Fill = Brushes.Red; mark.Stroke = Brushes.White; mark.StrokeThickness = 1.5;
                Canvas.SetLeft(mark, scr.X - 6); Canvas.SetTop(mark, scr.Y - 6);
            }
            // C12-5：悬停气泡（名称/经纬度/绑定变量；移出即隐藏由 WPF ToolTip 原生行为保证）
            var tt = new StringBuilder();
            tt.Append(isRangePoint ? "范围点" : $"作业点: {name}");
            tt.AppendLine();
            if (geo != null) tt.AppendLine(geo.ToBaseValue());
            if (!string.IsNullOrEmpty(boundTag)) tt.Append($"绑定: {boundTag}");
            mark.ToolTip = tt.ToString().TrimEnd();
            ToolTipService.SetInitialShowDelay(mark, 0);   // V-3d：悬停气泡无延时（鼠标放上即显示，原默认约 1 秒）
            mark.Tag = "workpoint-marker";   // V-3c：锁定短路放行判定用（点击 marker 允许选中查看）
            WorkPointsOverlay.Children.Add(mark);

            // V-2c + V-3a：marker 点击 → 选中表格行（双向选择）+ 按住拖拽移动（锁定时禁拖——仅允许选中查看）
            if (workPoint != null || rangePoint != null)
            {
                mark.MouseLeftButtonDown += (_, e) =>
                {
                    if (_isAddingWorkPoint || _isEditingWorkRange) return;   // 加点模式优先（Preview 隧道短路已拦，双保险）
                    // W-1a：先进入拖拽准备（捕获鼠标）再选中表格行——选中行 setter 会触发 overlay 重建，
                    // 若先选行则当前 marker 被重建移除 → CaptureMouse 在已移除元素上失效 → 拖不动
                    // （范围点首次按下即拖失效根因；重建被 _dragMarker 守卫拦截，marker 保留）
                    // W-1b：锁定下也可拖拽移动点（用户澄清：禁拖拽 = 禁拖地图，点拖拽锁定下允许；锁定禁平移缩放由锁定短路实现）
                    _dragMarker = mark;
                    _dragModel = (object?)workPoint ?? rangePoint;
                    _markerDragStart = e.GetPosition(WorldMapControl);
                    _markerDragMoved = false;
                    mark.CaptureMouse();
                    if (workPoint != null)
                    {
                        var row = _propertyViewModel.WorkPointRows.FirstOrDefault(r => r.Model == workPoint);
                        if (row != null) _propertyViewModel.SelectedWorkPointRow = row;   // DataGrid 绑定联动 + SelectionChanged 自动滚动
                    }
                    else if (rangePoint != null)
                    {
                        var row = _propertyViewModel.WorkRangeRows.FirstOrDefault(r => r.Model == rangePoint);
                        if (row != null) _propertyViewModel.SelectedWorkRangeRow = row;
                    }
                    e.Handled = true;   // 阻止地图拖动
                };
                mark.MouseMove += (_, e) =>
                {
                    if (_dragMarker != mark || !mark.IsMouseCaptured || _dragModel == null) return;
                    var pos = e.GetPosition(WorldMapControl);
                    if (!_markerDragMoved && (pos - _markerDragStart).Length < 4) return;   // 4px 阈值防点击误拖
                    _markerDragMoved = true;
                    Canvas.SetLeft(mark, pos.X - mark.Width / 2);   // 拖拽中只移视觉位置（不重建 overlay 保流畅）
                    Canvas.SetTop(mark, pos.Y - mark.Height / 2);
                };
                mark.MouseLeftButtonUp += (_, e) =>
                {
                    if (_dragMarker != mark || _dragModel == null) return;
                    mark.ReleaseMouseCapture();
                    _dragMarker = null;
                    var dragModel = _dragModel;
                    _dragModel = null;   // 统一清理（与 _dragMarker 一起，防早退路径悬空）
                    if (!_markerDragMoved)
                    {
                        // W-1a：纯点击未拖动 = 选中——Down 中选中行触发的重建被 _dragMarker 守卫拦截，
                        // 此处清守卫后补一次全量刷新重建 overlay，选中框（V-2a）随之出现
                        UpdateAllGeoWidgets();
                        e.Handled = true;
                        return;
                    }
                    var wm = _viewModel?.CurrentProject?.WorldMap;
                    var geo = ScreenToGeo(e.GetPosition(WorldMapControl));
                    if (wm == null || geo == null) return;
                    // V-3a（审查 Nit5）：提交前经纬度边界校验（防拖出地图范围写入无效 FixedPoint）
                    if (geo.Latitude < -90 || geo.Latitude > 90 || geo.Longitude < -180 || geo.Longitude > 180) return;
                    if (dragModel is MapWorkPoint mwp && !string.IsNullOrEmpty(mwp.BoundTag))
                    {
                        // X-1c：绑变量点拖拽 → 不清绑不写固定值，更新绑定 GPS 变量基准值（位置由变量驱动）
                        // 顺序：先查 tag 有效 → PushTagSnapshot 快照旧值 → 再更新（快照必须早于修改）
                        if (FindBoundGpsTag(mwp.BoundTag) is { } tag)
                        {
                            _viewModel.PushTagSnapshot();   // 变量基准值快照（Tag 撤销栈，Ctrl+Z 还原变量值）
                            tag.BaseValue = $"({GeoPoint.FormatDms(geo.Longitude, true)}, {GeoPoint.FormatDms(geo.Latitude, false)})";
                            _propertyViewModel.RefreshWorkPointRows();
                        }
                    }
                    else if (dragModel is WorkRangePoint mrp && !string.IsNullOrEmpty(mrp.BoundTag))
                    {
                        if (FindBoundGpsTag(mrp.BoundTag) is { } tag)
                        {
                            _viewModel.PushTagSnapshot();
                            tag.BaseValue = $"({GeoPoint.FormatDms(geo.Longitude, true)}, {GeoPoint.FormatDms(geo.Latitude, false)})";
                            _propertyViewModel.RefreshWorkRangeRows();
                        }
                    }
                    else
                    {
                        _viewModel.PushWorldMapUndoSnapshot();   // 拖拽提交前快照（C12-2 机制，WorldMap 独立撤销栈）
                        if (dragModel is MapWorkPoint mwp2)
                        {
                            mwp2.FixedPoint = geo; mwp2.BoundTag = "";   // 无绑定：固定经纬度
                            _propertyViewModel.RefreshWorkPointRows();
                        }
                        else if (dragModel is WorkRangePoint mrp2)
                        {
                            mrp2.FixedPoint = geo; mrp2.BoundTag = "";
                            _propertyViewModel.RefreshWorkRangeRows();
                        }
                    }
                    MarkProjectDirty();
                    UpdateAllGeoWidgets();   // 全量刷新：视觉位置落定到新经纬度 + 表格同步
                    e.Handled = true;
                };
            }

            if (!isRangePoint && !string.IsNullOrEmpty(name))
            {
                var label = new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    Foreground = Brushes.Black,
                    Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                    Padding = new Thickness(2, 0, 2, 0),
                    IsHitTestVisible = false,   // C12-5：名称标签不参与命中（有背景会挡地图拖拽；气泡由 marker 承载）
                };
                Canvas.SetLeft(label, scr.X + 8); Canvas.SetTop(label, scr.Y - 20);
                WorkPointsOverlay.Children.Add(label);

                // V-2b：选中作业点 → 名字标签下方追加实际经纬度（DMS；范围点无名字 → 点上方直接显示）
                if (isSelected && geo != null)
                {
                    var geoLabel = new TextBlock
                    {
                        Text = geo.ToDmsString(),
                        FontSize = 10,
                        Foreground = Brushes.DarkSlateGray,
                        Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                        Padding = new Thickness(2, 0, 2, 0),
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(geoLabel, scr.X + 8); Canvas.SetTop(geoLabel, scr.Y - 6);   // 名字下方
                    WorkPointsOverlay.Children.Add(geoLabel);
                }
            }
            else if (isSelected && geo != null)
            {
                // V-2b：范围点选中 → 点上方显示经纬度（DMS）
                var geoLabel = new TextBlock
                {
                    Text = geo.ToDmsString(),
                    FontSize = 10,
                    Foreground = Brushes.DarkSlateGray,
                    Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                    Padding = new Thickness(2, 0, 2, 0),
                    IsHitTestVisible = false,
                };
                Canvas.SetLeft(geoLabel, scr.X + 8); Canvas.SetTop(geoLabel, scr.Y - 18);
                WorkPointsOverlay.Children.Add(geoLabel);
            }
        }

        /// <summary>GPS 动态点位（设计态模拟，1Hz）：绑 GPS 变量的作业点/范围点按变量基准值实时更新位置。</summary>
        private void UpdateDynamicGeoWidgets() => UpdateAllGeoWidgets();

        /// <summary>属性面板：删除选中作业点行（P4 表格）。</summary>
        private void DeleteWorkPointRow_Click(object sender, RoutedEventArgs e)
        {
            if (_propertyViewModel.SelectedWorkPointRow != null)
            {
                _propertyViewModel.DeleteWorkPointRow(_propertyViewModel.SelectedWorkPointRow);
                e.Handled = true;
            }
        }

        /// <summary>C12-1：作业点表格新行提交（RowEditEnding Commit）——空行判定从 CollectionChanged Add 移到提交路径：
        /// 双击 placeholder 进入编辑时 DataGrid 已同步 Add 空行（此时未输入），Add 时判定必然误杀；提交时才有用户输入。</summary>
        private void WorkPointGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not WorkPointRowVM row) return;
            if (e.Row.IsNewItem) _propertyViewModel.CommitNewWorkPointRow(row);
            // W-3a：名称重名提交 → 名称列旁立即弹气泡（IsDuplicateName 已由 Name setter → ValidateDuplicateNames 更新；悬停 ToolTip 保留）
            if (row.IsDuplicateName) ShowErrorBubble(FindDataGridCell(e.Row, 0), "作业点名称重复，请改名");
        }

        /// <summary>V-2c：表格选中变化 → 滚动到该行（marker 点击经 SelectedItem 绑定触发，用户点表格同样适用；选中态由 setter → overlay 刷新联动）。</summary>
        private void WorkPointGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid dg && dg.SelectedItem != null)
                dg.ScrollIntoView(dg.SelectedItem);
        }

        /// <summary>W-1c：清除作业点/范围点选中态（置 null → PropertyViewModel setter 联动 overlay 刷新消选中）。</summary>
        private void ClearGeoSelection()
        {
            if (_propertyViewModel.SelectedWorkPointRow != null || _propertyViewModel.SelectedWorkRangeRow != null)
            {
                _propertyViewModel.SelectedWorkPointRow = null;
                _propertyViewModel.SelectedWorkRangeRow = null;
            }
        }

        /// <summary>X-1c：查找绑定的 GPS 变量（变量不存在/非 GPS 返回 null——拖拽时不清绑不写固定值，仅更新有效变量基准值）。</summary>
        private Tag? FindBoundGpsTag(string boundTag)
            => _viewModel?.CurrentProject?.Tags.FirstOrDefault(t => t.Name == boundTag && t.DataType == TagDataType.GPS);

        /// <summary>X-1c：变量基准值变更（拖拽写回/撤销/重做）后刷新——地图点位置由变量驱动 + 作业点/范围点表格 + 变量管理器表格（Tag 无 INPC，强制 Refresh）。</summary>
        private void RefreshAfterTagBaseValueChange()
        {
            UpdateAllGeoWidgets();
            _propertyViewModel.RefreshWorkPointRows();
            _propertyViewModel.RefreshWorkRangeRows();
            _variableManagerVM?.Refresh();   // Tag.BaseValue 无 INPC → 变量管理器表格强制刷新
            MarkProjectDirty();
        }

        /// <summary>W-1c：作业点表格点击空白（未命中行，且非滚动条/列头 chrome）→ 取消选中态；点击行由 SelectedItem 绑定联动。</summary>
        private void WorkPointGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsDataGridBlankHit(e)) ClearGeoSelection();
        }

        /// <summary>W-1c：作业范围表格点击空白（未命中行，且非滚动条/列头 chrome）→ 取消选中态（同作业点）。</summary>
        private void WorkRangeGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsDataGridBlankHit(e)) ClearGeoSelection();
        }

        /// <summary>W-1c：表格点击是否落在"空白"：未命中 DataGridRow，且非滚动条/列头（拖滚动条浏览、点列头排序不清除选中）。</summary>
        private static bool IsDataGridBlankHit(System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FindDataGridRow(e) != null) return false;
            var src = e.OriginalSource as System.Windows.DependencyObject;
            return FindVisualParent<System.Windows.Controls.Primitives.DataGridColumnHeader>(src) == null
                && FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(src) == null;
        }

        /// <summary>W-3a：错误提示气泡（重名/非法输入立即弹出，非悬停；锚定出错单元格旁，3s 自动消、进入编辑即消；悬停 ToolTip 保留共存）。</summary>
        private System.Windows.Controls.Primitives.Popup? _errorBubble;
        private System.Windows.Threading.DispatcherTimer? _errorBubbleTimer;
        private const double ErrorBubbleDurationMs = 3000;

        private void ShowErrorBubble(System.Windows.UIElement? target, string message)
        {
            if (_errorBubble == null)
            {
                _errorBubble = new System.Windows.Controls.Primitives.Popup
                {
                    AllowsTransparency = true,
                    StaysOpen = false,
                    IsHitTestVisible = false,   // W-3a nit：提示气泡穿透（不拦截覆盖区鼠标，与 dms-hint 一致）
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                    Child = new System.Windows.Controls.Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(240, 255, 235, 59)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 200, 160, 0)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 3, 6, 3),
                        Child = new System.Windows.Controls.TextBlock { FontSize = 11, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, MaxWidth = 280 },
                    }
                };
                _errorBubbleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ErrorBubbleDurationMs) };
                _errorBubbleTimer.Tick += (_, _) => HideErrorBubble();
            }
            if (_errorBubble.Child is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.TextBlock tb) tb.Text = message;
            _errorBubble.PlacementTarget = target;
            _errorBubbleTimer?.Stop();
            if (target == null) { _errorBubble.IsOpen = false; return; }
            _errorBubble.IsOpen = true;
            _errorBubbleTimer?.Start();
        }

        private void HideErrorBubble()
        {
            _errorBubbleTimer?.Stop();
            if (_errorBubble != null) _errorBubble.IsOpen = false;
        }

        /// <summary>W-3a：定位 DataGridRow 中指定列索引的单元格（气泡锚定用；GetCellContent 的 Parent 链含 DataGridCell）。</summary>
        private static System.Windows.Controls.DataGridCell? FindDataGridCell(System.Windows.Controls.DataGridRow row, int columnIndex)
        {
            var dg = FindVisualParent<System.Windows.Controls.DataGrid>(row);
            if (dg == null || columnIndex < 0 || columnIndex >= dg.Columns.Count) return null;
            var content = dg.Columns[columnIndex].GetCellContent(row);
            return content == null ? null : FindVisualParent<System.Windows.Controls.DataGridCell>(content);
        }

        /// <summary>W-3a：经纬度提交后非法 → 错误列单元格旁弹气泡（带示例；列索引区分作业点/范围点表）。</summary>
        private void ShowCoordErrorBubble(DataGridRow row, bool isRangeTable)
        {
            if (row?.Item is not WorkPointRowVM wp) return;
            int col = wp.CoordErrorLng.Length > 0 ? (isRangeTable ? 0 : 1) : (isRangeTable ? 1 : 2);
            ShowErrorBubble(FindDataGridCell(row, col), wp.CoordError);
        }
        private void ShowCoordErrorBubbleRange(DataGridRow row)
        {
            if (row?.Item is not WorkRangeRowVM wr) return;
            int col = wr.CoordErrorLng.Length > 0 ? 0 : 1;
            ShowErrorBubble(FindDataGridCell(row, col), wr.CoordError);
        }

        /// <summary>V-5a：经纬度单元格进入编辑 → 显示 DMS 实时换算提示（订阅 TextChanged；续23 增补 2：仅输入状态显示）。
        /// dms-hint 提示块是 DataGrid 的兄弟节点（同 StackPanel），从 DataGrid 的 Parent 向下查找。</summary>
        private void WorkPointGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            HideErrorBubble();   // W-3a：进入编辑 → 气泡立即消失
            PrepareGeoCellEdit(((FrameworkElement)sender).Parent, e);
        }

        private void WorkPointGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            UnsubscribeGeoHint();
            HideGeoCellHint(FindDmsHint(((FrameworkElement)sender).Parent));
            // W-3a：列绑定 LostFocus 在 CellEditEnding 之后才 UpdateSource——Commit 时先强制提交编辑值，
            // 确保 HasCoordError 读到新值（按 Enter/Tab 首次即弹）；仅 Commit 分支执行（Esc 取消不写回）
            if (e.EditAction == DataGridEditAction.Commit)
            {
                (e.EditingElement as System.Windows.Controls.TextBox)?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                // W-3a：非法输入提交 → 立即弹气泡（行浅黄 + ToolTip 保留）
                if (e.Row != null && e.Row.Item is WorkPointRowVM r && r.HasCoordError)
                    ShowCoordErrorBubble(e.Row, isRangeTable: false);
            }
        }

        /// <summary>V-5a：范围点经纬度单元格 DMS 提示（同作业点）。</summary>
        private void WorkRangeGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            HideErrorBubble();   // W-3a：进入编辑 → 气泡立即消失
            PrepareGeoCellEdit(((FrameworkElement)sender).Parent, e);
        }

        private void WorkRangeGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            UnsubscribeGeoHint();
            HideGeoCellHint(FindDmsHint(((FrameworkElement)sender).Parent));
            // W-3a：同 WorkPointGrid——Commit 分支先强制提交编辑值再读 HasCoordError（Esc 取消不写回）
            if (e.EditAction == DataGridEditAction.Commit)
            {
                (e.EditingElement as System.Windows.Controls.TextBox)?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                // W-3a：非法输入提交 → 立即弹气泡
                if (e.Row != null && e.Row.Item is WorkRangeRowVM wr && wr.HasCoordError)
                    ShowCoordErrorBubbleRange(e.Row);
            }
        }

        /// <summary>V-5a：当前 DMS 提示的 TextBox 订阅（防重复订阅泄漏——同格重复编辑只保留最新 handler）。</summary>
        private TextBox? _geoHintTextBox;
        private TextChangedEventHandler? _geoHintHandler;

        private void UnsubscribeGeoHint()
        {
            if (_geoHintTextBox != null && _geoHintHandler != null)
                _geoHintTextBox.TextChanged -= _geoHintHandler;
            _geoHintTextBox = null;
            _geoHintHandler = null;
        }

        /// <summary>V-5a：从 StackPanel（DataGrid 父级）向下按 Tag="dms-hint" 找提示 TextBlock（提示在 DataTemplate 内，x:Name 不生成窗口字段）。</summary>
        private static TextBlock? FindDmsHint(DependencyObject? root)
        {
            if (root == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb && tb.Tag as string == "dms-hint") return tb;
                if (FindDmsHint(child) is { } found) return found;
            }
            return null;
        }

        /// <summary>V-5a：经纬度列进入编辑 → 显示提示 + 订阅 TextChanged 实时换算（"104.06 → E104°3'36""）。</summary>
        private void PrepareGeoCellEdit(DependencyObject? root, DataGridPreparingCellForEditEventArgs e)
        {
            var hint = FindDmsHint(root);
            if (hint == null) return;
            var isLng = (e.Column.Header as string)?.StartsWith("经度") == true;
            var isLat = (e.Column.Header as string)?.StartsWith("纬度") == true;
            if (!isLng && !isLat) { UnsubscribeGeoHint(); hint.Visibility = Visibility.Collapsed; return; }   // 非经纬度列：清残留订阅
            if (e.EditingElement is not TextBox tb) return;
            UnsubscribeGeoHint();   // 移除旧订阅（防泄漏）
            _geoHintTextBox = tb;
            _geoHintHandler = (_, _) => UpdateGeoHint(hint, isLng, tb.Text);
            tb.TextChanged += _geoHintHandler;
            UpdateGeoHint(hint, isLng, tb.Text);
            hint.Visibility = Visibility.Visible;
        }

        private static void HideGeoCellHint(TextBlock? hint)
        {
            if (hint != null) hint.Visibility = Visibility.Collapsed;
        }

        /// <summary>V-5a：DMS 换算提示文本（小数度 → 度分秒；非法输入给格式示例）。</summary>
        private static void UpdateGeoHint(TextBlock hint, bool isLng, string text)
        {
            if (double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                hint.Text = $"→ {GeoPoint.FormatDms(v, isLng)}";
            else
                hint.Text = isLng ? "输入小数经度，如 104.06（负=西经）" : "输入小数纬度，如 30.67（负=南纬）";
        }

        /// <summary>C12-1：作业范围表格新行提交（同 WorkPoint 表格）。</summary>
        private void WorkRangeGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || !e.Row.IsNewItem) return;
            if (e.Row.Item is WorkRangeRowVM row) _propertyViewModel.CommitNewWorkRangeRow(row);
        }

        /// <summary>V-2c：作业范围表格选中变化 → 滚动到该行（同 WorkPointGrid）。</summary>
        private void WorkRangeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid dg && dg.SelectedItem != null)
                dg.ScrollIntoView(dg.SelectedItem);
        }

        /// <summary>C12-1：多边形顶点表格新行提交（同 WorkPoint 表格；已编辑行挂模型、未编辑空行移除）。</summary>
        private void PolygonPointGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || !e.Row.IsNewItem) return;
            if (e.Row.Item is PolygonPointRowVM row) _propertyViewModel.CommitNewPolygonPointRow(row);
        }

        /// <summary>属性面板：删除选中作业范围点行（P4 表格）。</summary>
        private void DeleteWorkRangeRow_Click(object sender, RoutedEventArgs e)
        {
            if (_propertyViewModel.SelectedWorkRangeRow != null)
            {
                _propertyViewModel.DeleteWorkRangeRow(_propertyViewModel.SelectedWorkRangeRow);
                e.Handled = true;
            }
        }

        /// <summary>属性面板：删除多边形顶点行（P7 表格；删除后不足 3 点拒绝）。</summary>
        private void DeletePolygonPointRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PolygonPointRowVM row)
            {
                _propertyViewModel.DeletePolygonPointRow(row);
                e.Handled = true;
            }
        }

        #endregion

        /// <summary>任务11：工具栏悬浮窗开关（退回内嵌）——开 → 显示内嵌球；关 → 隐藏。</summary>
        private void AiBallToggle_Checked(object sender, RoutedEventArgs e)
            => AiFabBtn.Visibility = Visibility.Visible;

        /// <summary>任务11：工具栏悬浮窗开关——关 → 隐藏内嵌球。</summary>
        private void AiBallToggle_Unchecked(object sender, RoutedEventArgs e)
            => AiFabBtn.Visibility = Visibility.Collapsed;

        // ── 任务11 内嵌悬浮球拖动（退回 0cbcb1f 逻辑）+ Z1 锚定/钳制 ──
        private bool _aiFabDragging;
        private Point _aiFabMouseDownPos;
        private Thickness _aiFabDownMargin;

        /// <summary>Z1：把 AiFab 的绝对位置（HorizontalAlignment=Left/Top + Margin）钳制回可视区——拖动后窗口缩小/状态切换不漂出窗外。
        /// 基准：AiFab 位于 Grid.Row=3 单元格（与 DockManager 同格），Margin 相对单元格左上角——用 DockManager 作基准（窗口客户区基准会因上方 Auto 行高错位 ~60px）。</summary>
        private void ClampAiFabToView()
        {
            if (AiFabBtn == null || AiFabBtn.HorizontalAlignment != HorizontalAlignment.Left) return;   // 未拖动过（锚定右下角）不处理
            var w = Math.Max(0, DockManager.ActualWidth - AiFabBtn.ActualWidth);
            var h = Math.Max(0, DockManager.ActualHeight - AiFabBtn.ActualHeight);
            var m = AiFabBtn.Margin;
            AiFabBtn.Margin = new Thickness(Math.Min(Math.Max(0, m.Left), w), Math.Min(Math.Max(0, m.Top), h), 0, 0);
        }

        /// <summary>Z1：窗口尺寸变化时钳制 AiFab 位置（拖动后的绝对 Margin 不随窗口自适应，缩小时漂出窗外）。</summary>
        private void EditWindow_SizeChanged(object sender, SizeChangedEventArgs e) => ClampAiFabToView();

        /// <summary>AI 悬浮按钮：按下记录起点并捕获鼠标（不立即移动；拖动/点击在 Move/Up 判定）。基准 = DockManager（与 AiFab 同 Grid 单元格，Margin/GetPosition 同一坐标系）。</summary>
        private void AiFab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            _aiFabDragging = false;
            _aiFabMouseDownPos = e.GetPosition(DockManager);
            _aiFabDownMargin = btn.Margin;
            btn.CaptureMouse();
            e.Handled = true;
        }

        private void AiFab_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn || !btn.IsMouseCaptured) return;
            var pos = e.GetPosition(DockManager);
            var dx = pos.X - _aiFabMouseDownPos.X;
            var dy = pos.Y - _aiFabMouseDownPos.Y;
            if (!_aiFabDragging && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5))
            {
                _aiFabDragging = true;
                btn.HorizontalAlignment = HorizontalAlignment.Left;
                btn.VerticalAlignment = VerticalAlignment.Top;
                btn.Margin = new Thickness(_aiFabMouseDownPos.X - btn.ActualWidth / 2, _aiFabMouseDownPos.Y - btn.ActualHeight / 2, 0, 0);
                _aiFabDownMargin = btn.Margin;
            }
            if (_aiFabDragging)
            {
                // Z1：拖动实时钳制在单元格可视区内（不漂出窗口）
                var maxW = Math.Max(0, DockManager.ActualWidth - btn.ActualWidth);
                var maxH = Math.Max(0, DockManager.ActualHeight - btn.ActualHeight);
                btn.Margin = new Thickness(
                    Math.Min(Math.Max(0, _aiFabDownMargin.Left + dx), maxW),
                    Math.Min(Math.Max(0, _aiFabDownMargin.Top + dy), maxH), 0, 0);
            }
            e.Handled = true;
        }

        private void AiFab_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var pos = e.GetPosition(DockManager);   // 与 Down/Move 同基准（Grid.Row=3 单元格）——混用窗口基准会使 moved 恒含 ~58px 偏移导致单击失效
            var moved = Math.Abs(pos.X - _aiFabMouseDownPos.X) + Math.Abs(pos.Y - _aiFabMouseDownPos.Y);
            var wasDragging = _aiFabDragging;
            _aiFabDragging = false;
            btn.ReleaseMouseCapture();
            ClampAiFabToView();   // Z1：松手钳制一次（防边缘越界残留）
            if (!wasDragging && moved < 10 && _viewModel.ToggleAiPanelCommand.CanExecute(null))
                _viewModel.ToggleAiPanelCommand.Execute(null);
            e.Handled = true;
        }

        private void EditWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 关闭窗口前清理菜单悬停状态：关闭所有子菜单 + 移出焦点/高亮。
            // WPF MenuItem 悬停 400ms 定时器（SetTimerToOpenHierarchy）在窗口销毁期间触发会
            // FocusOrSelect → PushMenuMode(null) 崩溃（"关闭工程"即此路径）；先关菜单停定时器治本。
            CloseAllMenus();

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
        /// 关闭顶部菜单所有子菜单并失焦（防窗口销毁期 MenuItem 悬停定时器竞态崩溃）。
        /// 注：仅遍历顶层 MenuItem（当前 XAML 无嵌套子菜单；若未来加"最近打开"等嵌套子菜单需改递归）。
        /// </summary>
        private void CloseAllMenus()
        {
            if (MainMenu != null)
            {
                foreach (var obj in MainMenu.Items)
                {
                    if (obj is MenuItem mi)
                    {
                        mi.IsSubmenuOpen = false;   // 关闭子菜单触发 WPF 内部停悬停定时器
                    }
                }
            }
            // 画布右键菜单同路径：销毁前关闭，防打开状态销毁竞态
            if (TreeContextMenu.IsOpen) TreeContextMenu.IsOpen = false;
            if (WorldMapContextMenu.IsOpen) WorldMapContextMenu.IsOpen = false;
            _overlayThrottleTimer?.Stop();   // 审查复核：关窗停 overlay 节流 timer，防残留 tick 访问已销毁 MapControl
            Focus();   // 焦点移出菜单到窗口
        }

        /// <summary>
        /// 窗口关闭（所有路径必触发）：清理静态回调，防闭包持有已关闭窗口/项目（GC 无法回收）。
        /// </summary>
        private void EditWindow_Closed(object? sender, EventArgs e)
        {
            _clockTimer.Stop();   // D6：关闭停止画布时钟
            if (SelectorHelper.ResizeDragStarted == _resizeDragStartedCallback)
                SelectorHelper.ResizeDragStarted = null;
            if (SelectorHelper.ResizeDragDelta == _resizeDragDeltaCallback)
                SelectorHelper.ResizeDragDelta = null;   // W-4c：拖拽过程回调清理（防闭包持有已关闭窗口）
            if (SelectorHelper.ResizeDragCompleted == _resizeDragCompletedCallback)
                SelectorHelper.ResizeDragCompleted = null;   // C12-12：静态回调清理（防闭包持有已关闭窗口）
            if (SelectorHelper.GetCanvasSize == _getCanvasSizeCallback)
                SelectorHelper.GetCanvasSize = null;
            // 清理设计态变量解析器静态引用（防窗口关闭后工程驻留内存）
            if (TagResolver.CurrentProject == _currentProject)
                TagResolver.CurrentProject = null;
            _viewModel.AiMessages.CollectionChanged -= AiMessages_CollectionChanged;   // 退订自动滚动（防关闭后无效回调）
            _viewModel.Dispose();   // 释放 AI 后端（CloudLLMBackend 的 HttpClient/Authorization）
            try { WorldMapControl?.Dispose(); } catch { }   // 释放 Mapsui MapControl（HttpClient/瓦片缓存）
            // W-3a：错误气泡清理（停定时器防闭包持有窗口、关闭残留 Popup）
            _errorBubbleTimer?.Stop();
            if (_errorBubble != null) _errorBubble.IsOpen = false;
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
            // 按 Name 定位主层 ItemsControl（虚影层同样为 ItemsControl，不能靠 OfType 唯一假设）
            var existingItemsControl = DrawingCanvas.Children.OfType<ItemsControl>()
                .FirstOrDefault(c => c.Name == "MyItemsControl");
            if (existingItemsControl == null)
            {
                if (_viewModel?.CurrentScreen != null)
                {
                    LoadCanvas(_viewModel.CurrentScreen);
                }
            }
        }

        /// <summary>虚影层 ItemsControl 的 Name（局部刷新/定位用，与主层 "MyItemsControl" 区分）。</summary>
        private const string GlobalGhostName = "GlobalGhostItemsControl";

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
            ghostItems.Width = globalScreen.Width > 0 ? globalScreen.Width : (vm?.DeviceWidth ?? 800);
            ghostItems.Height = globalScreen.Height > 0 ? globalScreen.Height : (vm?.DeviceHeight ?? 600);
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
            itemsControl.Width = screen.Width > 0 ? screen.Width : (vm?.DeviceWidth ?? 800);
            itemsControl.Height = screen.Height > 0 ? screen.Height : (vm?.DeviceHeight ?? 600);

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

                // 菜单互斥：打开画布菜单前先关树菜单（两菜单独立 Popup，避免重叠）
                if (TreeScreenMenu.IsOpen) TreeScreenMenu.IsOpen = false;

                var mousePos = Mouse.GetPosition(this);
                TreeContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute;
                TreeContextMenu.HorizontalOffset = mousePos.X;
                TreeContextMenu.VerticalOffset = mousePos.Y;
                _contextMenuPos = e.GetPosition(DrawingCanvas);   // 逻辑坐标（LayoutTransform 逆变换），禁除缩放（双重除 bug）
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
                // 添加模式（单点/两点/多边形绘制）：双击不放控件——每次左键都是"放点/加点"，不弹画面属性（批 3a 遗留）
                if (_currentWidgetCreator != null) return;
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
                if (child is System.Windows.Controls.Primitives.ButtonBase) return true;   // P3-11：覆盖 Button/ToggleButton/RepeatButton（🛰 开关不被工具栏拖拽拦截）
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
            _listManagerVM.RecheckImagePaths();   // 工程目录刚生效 → 重算图片路径标红状态
        }

        #endregion

        #region 树形视图 & 消息

        private void OnScreenAdded(object recipient, ScreenAddedMessage message)
        {
            Dispatcher.Invoke(() =>
            {
                MarkProjectDirty();
                // 新增画面不自动加标签（打开才显示），ObservableCollection 通知冗余
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

        /// <summary>画布顶部页面标签点击：切换当前编辑画面（同时退出变量管理器视图）。</summary>
        private void ScreenTab_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Screen screen) return;
            _viewModel.ActivateScreen(screen);
            e.Handled = true;
        }

        /// <summary>画布顶部页面标签关闭：从打开集合移除（画面不删除）。</summary>
        private void ScreenTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Screen screen)
            {
                _viewModel.CloseScreenTab(screen);
                e.Handled = true;
            }
        }

        #region 变量管理器 Tab（画布标签栏）
        /// <summary>点击「变量管理器」Tab：切到变量管理器视图（与画面 Tab 互斥切换）。</summary>
        private void VarManagerTab_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.ActivateVariableManager();
            e.Handled = true;
        }

        /// <summary>关闭「变量管理器」Tab（当前激活时切回当前画面）。</summary>
        private void VarManagerTabClose_Click(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CloseVariableManagerTab();
            e.Handled = true;
        }
        #endregion


        /// <summary>
        /// 右键点击树节点：选中节点并显示上下文菜单。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnTreeViewItemRightClick"/>。
        /// </summary>
        private void TreeViewItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 粘贴按钮：剪贴板为空时禁用（避免静默失败）
            if (ScreenPasteBtn != null)
                ScreenPasteBtn.IsEnabled = ScreenClipboard.GetItems() != null;
            _treeContextMenuHandler.OnTreeViewItemRightClick(sender, e);
        }

        /// <summary>
        /// 项目树空白处右键：弹粘贴画面菜单（粘贴到自定义画面下）。
        /// 节点右键已在 ItemContainerStyle 的 MouseRightButtonDown 处理（Handled=true），此事件仅在空白处触发。
        /// </summary>
        private void ProjectTree_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 已由节点处理器处理（Handled=true）则不重复弹菜单
            if (e.Handled) return;
            // 粘贴按钮：剪贴板为空时禁用
            if (ScreenPasteBtn != null)
                ScreenPasteBtn.IsEnabled = ScreenClipboard.GetItems() != null;
            _treeContextMenuHandler.OnTreeViewItemRightClick(sender, e);
        }

        /// <summary>
        /// 树节点右键菜单「删除画面」点击。
        /// 委托给 <see cref="TreeViewContextMenuHandler.OnDeleteScreenClick"/>。
        /// </summary>
        private void DeleteScreen_Click(object sender, RoutedEventArgs e)
            => _treeContextMenuHandler.OnDeleteScreenClick(sender, e);

        /// <summary>树节点右键「复制画面」：走 CommandLayer copy_screen（含控件深拷贝）。画面级操作不推 Undo 快照。</summary>
        private void CopyScreen_Click(object sender, RoutedEventArgs e)
        {
            var node = _treeContextMenuHandler.GetRightClickedNode();
            if (node == null) return;
            var result = _viewModel.CommandService.Execute("copy_screen", new() { ["name"] = node.Screen.Name });
            if (!result.Success)
                System.Windows.MessageBox.Show($"复制失败: {result.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
            TreeScreenMenu.IsOpen = false;
            _treeContextMenuHandler.ClearRightClickedNode();
        }

        /// <summary>树节点右键「剪切画面」：复制 + 删除（可粘贴还原）。画面级操作不推 Undo 快照。</summary>
        private void CutScreen_Click(object sender, RoutedEventArgs e)
        {
            var node = _treeContextMenuHandler.GetRightClickedNode();
            if (node == null) return;
            // 复制成功后再删除（copy_screen 触发 CommandExecuted → 树重建，旧 node 引用失效，删除必须走 CommandLayer 保持一致）
            var copyResult = _viewModel.CommandService.Execute("copy_screen", new() { ["name"] = node.Screen.Name });
            if (copyResult.Success)
            {
                var deleteResult = _viewModel.CommandService.Execute("delete_screen", new() { ["name"] = node.Screen.Name });
                if (!deleteResult.Success)
                    System.Windows.MessageBox.Show($"剪切失败（画面未删除）: {deleteResult.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
                System.Windows.MessageBox.Show($"剪切失败: {copyResult.ErrorMessage}", "NavigatorHMI", MessageBoxButton.OK, MessageBoxImage.Warning);
            TreeScreenMenu.IsOpen = false;
            _treeContextMenuHandler.ClearRightClickedNode();
        }

        /// <summary>树节点右键「粘贴画面」：走 CommandLayer paste_screen（新画面名自动去重），完成后切换到新画面。</summary>
        private void PasteScreen_Click(object sender, RoutedEventArgs e)
        {
            var result = _viewModel.CommandService.Execute("paste_screen", new() { });
            if (result.Success)
            {
                // 从匿名结果取 screen_name（CommandResult.Data 为 { screen_name, widgets }）
                var newName = result.Data?.GetType().GetProperty("screen_name")?.GetValue(result.Data)?.ToString();
                if (!string.IsNullOrEmpty(newName))
                {
                    var screen = _viewModel.CurrentProject.Screens.FirstOrDefault(s => s.Name == newName);
                    if (screen != null) _viewModel.CurrentScreen = screen;
                }
            }
            TreeScreenMenu.IsOpen = false;
            _treeContextMenuHandler.ClearRightClickedNode();
        }

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
                // P7：多边形顶点为画布绝对坐标——粘贴到菜单点 = 平移全部顶点（保持形状相对关系）
                if (w is PolygonWidget poly && poly.Points.Count > 0)
                {
                    double minX = poly.Points.Min(p => p.X), minY = poly.Points.Min(p => p.Y);
                    poly.TranslatePoints(_contextMenuPos.X - minX, _contextMenuPos.Y - minY);
                }
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
                double dx = 20 / _zoomLevel, dy = 20 / _zoomLevel;
                // P7：多边形顶点为画布绝对坐标——整体平移与 X/Y 同步（保持形状相对关系）
                if (w is PolygonWidget poly && poly.Points.Count > 0) poly.TranslatePoints(dx, dy);
                w.X += dx; w.Y += dy;
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
            var selected = _viewModel.CurrentScreen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null || selected.Count == 0) return;   // 无选中不操作（不清空剪贴板）
            _clipboard.Clear();
            foreach (var w in selected) { using var ms = new MemoryStream(); Serializer.Serialize(ms, w); _clipboard.Add(ms.ToArray()); }
            // 同步写入 CLI 剪贴板（GUI 复制的内容可在 CLI 面板 paste-widget）
            NavigatorHMI.CommandLayer.Handlers.WidgetClipboard.SetItems(new List<byte[]>(_clipboard));
            WidgetPasteBtn.IsEnabled = _clipboard.Count > 0;
            UpdateCanvasMenuButtons();
        }
        private void DeleteSelectedWidgets()
        {
            if (_viewModel.CurrentScreen == null) return;
            var selected = _viewModel.CurrentScreen.Widgets.Where(w => w.IsSelected).ToList();
            if (selected.Count == 0) return;
            _viewModel.PushUndoSnapshot();   // 剪切/删除前快照（对齐 Delete 键行为）
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
            double cx, cy;
            if (isCircle)
            {
                // 圆形：默认圆心 = 选中群平均中心
                cx = Math.Min(Math.Max(sorted.Average(w => w.X + w.Width / 2), 50), (_viewModel.DeviceWidth - 50));
                cy = Math.Min(Math.Max(sorted.Average(w => w.Y + w.Height / 2), 50), (_viewModel.DeviceHeight - 50));
            }
            else
            {
                // W3：矩形阵列起点=第一个控件左上角——默认值取选中群包络左上角（贴合原选中位置，避免阵列整体向右下偏移）
                cx = Math.Max(0, sorted.Min(w => w.X));
                cy = Math.Max(0, sorted.Min(w => w.Y));
            }

            GridArrayDialog? dialog = null;
            dialog = new GridArrayDialog(isCircle, sorted.Count, cx, cy, () =>
            {
                // 仅更新辅助线预览（不移动控件）
                UpdateArrayGuide(isCircle, sorted.Count, dialog!.Cols, dialog.Rows,
                    dialog.StartX, dialog.StartY, dialog.SpacingX, dialog.SpacingY,
                    dialog.CenterX, dialog.CenterY, dialog.Radius, dialog.StartAngle, dialog.EndAngle);
            }) { Owner = this };

            // 初始默认值预览（画辅助线）
            dialog.InvokePreview();

            if (dialog.ShowDialog() == true)
            {
                // 确认：统一走 Command Layer 落位（GUI/CLI/AI 同一路径，含中心基准与钳制）
                _viewModel.PushUndoSnapshot();
                var param = new Dictionary<string, object?>
                {
                    ["screen_name"] = _viewModel.CurrentScreen?.Name ?? "",
                    ["widgets"] = string.Join(",", sorted.Select(w => w.ObjectName)),
                    ["mode"] = isCircle ? "circle" : "rect",
                };
                if (isCircle)
                {
                    param["center_x"] = dialog.CenterX;
                    param["center_y"] = dialog.CenterY;
                    param["radius"] = dialog.Radius;
                    param["start_angle"] = dialog.StartAngle;
                    param["end_angle"] = dialog.EndAngle;
                }
                else
                {
                    param["start_x"] = dialog.StartX;
                    param["start_y"] = dialog.StartY;
                    param["cols"] = dialog.Cols;
                    param["rows"] = dialog.Rows;
                    param["spacing_x"] = dialog.SpacingX;
                    param["spacing_y"] = dialog.SpacingY;
                }
                var result = _viewModel.CommandService.Execute("array_layout", param);
                if (result.Success)
                {
                    _viewModel.NotifyCanvasRefreshNeeded();
                    DrawingCanvas.InvalidateVisual();
                }
            }
            ClearArrayGuide();
        }

        /// <summary>绘制阵列辅助线预览（矩形网格 / 圆形弧线 + 落点中心标记），不移动控件。</summary>
        private void UpdateArrayGuide(bool isCircle, int count, int cols, int rows,
            double startX, double startY, double sx, double sy,
            double centerX, double centerY, double radius, double startAngle, double endAngle)
        {
            if (ArrayGuideShape == null || ArrayGuideOverlay == null) return;
            var posTuples = LayoutMath.CalcPositions(isCircle, count, cols, rows, startX, startY, sx, sy,
                centerX, centerY, radius, startAngle, endAngle);
            var positions = posTuples.Select(t => new Point(t.X, t.Y)).ToList();
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

            // 落点标记层（自绘覆盖层）：蓝色空心方块 + 序号数字，位置 = CalcPositions 返回的交点
            // 语义：矩形非中心起点模式下交点=起点（第一个控件左上角）；中心起点/圆形模式下交点=控件中心（W3 用户拍板）
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

        private static int ExtractIdNumber(string name)
        {
            var num = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(num, out var n) ? n : 0;
        }

        private void AlignLeft_Click(object sender, RoutedEventArgs e) => AlignWidgets("left");
        private void AlignCenterH_Click(object sender, RoutedEventArgs e) => AlignWidgets("center_h");
        private void AlignRight_Click(object sender, RoutedEventArgs e) => AlignWidgets("right");
        private void AlignTop_Click(object sender, RoutedEventArgs e) => AlignWidgets("top");
        private void AlignCenterV_Click(object sender, RoutedEventArgs e) => AlignWidgets("center_v");
        private void AlignBottom_Click(object sender, RoutedEventArgs e) => AlignWidgets("bottom");

        private void AlignWidgets(string direction)
        {
            var selected = _viewModel.CurrentScreen?.Widgets.Where(w => w.IsSelected).ToList();
            if (selected == null || selected.Count == 0) return;
            _viewModel.PushUndoSnapshot();
            // 统一走 Command Layer（GUI/CLI/AI 同一路径）
            var result = _viewModel.CommandService.Execute("align_widgets", new()
            {
                ["screen_name"] = _viewModel.CurrentScreen?.Name ?? "",
                ["widgets"] = string.Join(",", selected.Select(w => w.ObjectName)),
                ["direction"] = direction,
            });
            if (result.Success)
            {
                _viewModel.NotifyCanvasRefreshNeeded();
                DrawingCanvas.InvalidateVisual();
            }
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
            // P7/#22：未保存检查 + 显式回欢迎窗（欢迎窗已关时防 App 因所有窗口关闭而退出）
            if (!TryCloseProject(false))
                return;
            _isProjectDirty = false;

            WelComeWindow welcome = new WelComeWindow();
            welcome.Show();
            // P9：欢迎窗打开后自动触发「新建工程」对话框（Dispatcher 延迟到欢迎窗消息循环——复用 CreateNewProject_Click 流转，不易丢对象）
            welcome.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => welcome.AutoCreateNewProject()));

            _skipClosingCheck = true;
            Close();
        }

        /// <summary>P10：恢复默认布局（从首次 Loaded 保存的内存流 Deserialize——左栏项目窗口/控件面板上下排布；
        /// 反序列化重建 LayoutRoot 后经 LayoutSerializationCallback 按 ContentId 回填 Content，防文档/面板空白）。</summary>
        private void ResetLayout_Click(object sender, RoutedEventArgs e)
        {
            if (_defaultLayout == null) return;
            try
            {
                _defaultLayout.Position = 0;
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.LayoutSerializationCallback += (_, args) =>
                {
                    var id = args.Model switch
                    {
                        LayoutAnchorable a => a.ContentId,
                        LayoutDocument d => d.ContentId,
                        _ => null
                    };
                    if (id != null && _layoutContents.TryGetValue(id, out var content))
                        args.Content = content;
                };
                serializer.Deserialize(_defaultLayout);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[EditWindow] 恢复默认布局失败: {ex.Message}");
            }
        }

        /// <summary>P10：收集当前 LayoutRoot 中所有 Anchorable/Document 的 ContentId → Content（XAML 静态元素引用不变，反序列化回调复用）。</summary>
        private void RegisterLayoutContents()
        {
            if (DockManager.Layout == null) return;
            _layoutContents.Clear();
            foreach (var anchorable in DockManager.Layout.Descendents().OfType<LayoutAnchorable>())
                if (anchorable.Content != null && !string.IsNullOrEmpty(anchorable.ContentId))
                    _layoutContents[anchorable.ContentId] = anchorable.Content;
            foreach (var doc in DockManager.Layout.Descendents().OfType<LayoutDocument>())
                if (doc.Content != null && !string.IsNullOrEmpty(doc.ContentId))
                    _layoutContents[doc.ContentId] = doc.Content;
        }

        /// <summary>P7：文件菜单「打开」——OpenFileDialog 选 .hmiproj → 新 EditWindow 加载（对齐欢迎窗打开路径）。</summary>
        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (!TryCloseProject(false))
                return;

            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "工程文件|*.hmiproj", DefaultExt = ".hmiproj" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using var fs = new FileStream(dlg.FileName, FileMode.Open);
                var project = Serializer.Deserialize<HMIProject>(fs);
                project.ProjectFilePath = dlg.FileName;
                project.LastModifiedTime = DateTime.Now;
                RecentProjectManager.Instance.AddRecentProject(dlg.FileName);

                _isProjectDirty = false;
                _skipClosingCheck = true;
                var editWindow = new EditWindow(project);
                editWindow.Show();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开工程失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                _listManagerVM.RecheckImagePaths();   // 另存后工程目录可能变更 → 重算图片路径标红状态
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
        /// 点击 CLI 面板（分隔条/提示符/空白）自动聚焦输入框并显示光标；点击输出区不聚焦——
        /// 输出区保持可交互（鼠标拖选/Ctrl+A 全选/右键菜单复制），聚焦输入框会抢走输出区选区。
        /// 用 Dispatcher.BeginInvoke 延后到冒泡阶段完成后执行——WPF TextBox 在冒泡 OnMouseDown
        /// 会把焦点抢回自身（输出区只读 TextBox），延后聚焦保证最终焦点在输入框。
        /// 短路条件基于点击目标视觉链归属（输入框/输出区内部都跳过），不依赖当前焦点状态——
        /// 否则"输入框已聚焦 + 点击输出区"时焦点会被输出区抢走且不回（高频组合）。
        /// </summary>
        private void CliPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (CliInput == null) return;
            // 仅左键触发聚焦（右键/中键用于其他用途时不干扰）
            if (e.ChangedButton != MouseButton.Left) return;
            // 点击目标是否在输入框内部（TextBoxView/滚动条 Thumb 等）→ 不重复聚焦
            if (IsDescendantOfCliInput(e.OriginalSource as DependencyObject)) return;
            // 点击输出区/其滚动条 → 不聚焦输入框（保持输出文本可拖选/右键复制/Ctrl+A 全选）
            if (IsDescendantOfCliOutput(e.OriginalSource as DependencyObject)) return;
            // 延后到事件冒泡完成后：让输出区 TextBox 的默认 Focus 先执行，再强制聚焦输入框
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (CliInput == null) return;
                CliInput.Focus();
                CliInput.CaretIndex = CliInput.Text.Length;  // 光标移到末尾
            }));
        }

        /// <summary>沿视觉树向上查找点击目标是否属于 CliOutput 子树（输出区不参与聚焦，保留文本选择/复制能力）。</summary>
        private bool IsDescendantOfCliOutput(DependencyObject? node)
        {
            while (node != null)
            {
                // 滚动条 Thumb 视觉链止于 ScrollViewer（模板元素不经过内容 TextBox），须同时检查 Scroller 本身
                if (ReferenceEquals(node, CliOutput) || ReferenceEquals(node, CliOutputScroller)) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        /// <summary>沿视觉树向上查找点击目标是否属于 CliInput 子树。</summary>
        private bool IsDescendantOfCliInput(DependencyObject? node)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, CliInput)) return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }


        private void AiInput_KeyDown(object sender, KeyEventArgs e)
        {
            // Enter 发送，Shift+Enter 换行（与聊天工具一致）；IME 候选确认回车（ImeProcessedKey != None）放行给 TextBox 提交候选词，不发送
            if (e.Key == Key.Enter && e.ImeProcessedKey == Key.None && !(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
            {
                e.Handled = true;
                if (_viewModel.AiSendCommand.CanExecute(null))
                    _viewModel.AiSendCommand.Execute(null);
            }
        }

        /// <summary>A7：IME 候选词确认回车（ImeProcessedKey != None）不触发发送——隧道阶段拦截，中文输入法选词不误发送；普通 Enter 在 Preview 发送并 Handled（KeyDown 被屏蔽），IME 回车放行给 TextBox 提交候选词。</summary>
        private void AiInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.ImeProcessedKey == Key.None
                && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                if (_viewModel.AiSendCommand.CanExecute(null))
                    _viewModel.AiSendCommand.Execute(null);
            }
        }

        /// <summary>AI 消息新增时自动滚动到底部（长会话不丢失新消息视野）。</summary>
        private void AiMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || AiMessagesList == null) return;
            AiMessagesList.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (AiMessagesList.Items.Count > 0)
                    AiMessagesList.ScrollIntoView(AiMessagesList.Items[AiMessagesList.Items.Count - 1]);
            }));
        }

        /// <summary>菜单「设置 → AI 助手设置…」：打开 API Key 配置窗口（DPAPI 密文存储），保存后刷新 ViewModel。</summary>
        private void AiSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AiSettingsWindow { Owner = this };
            if (dlg.ShowDialog() == true)
                _viewModel.ReloadAiKeyFromStore();
        }

        /// <summary>AI 消息右键「复制」：拖选了文字则复制选中段，否则复制整条消息。</summary>
        private void AiMsgCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Parent is not ContextMenu cm) return;
            if (cm.PlacementTarget is System.Windows.Controls.TextBox tb)
            {
                try
                {
                    if (tb.SelectionLength > 0)
                        System.Windows.Clipboard.SetText(tb.SelectedText);
                    else if (tb.Tag is string full)
                        System.Windows.Clipboard.SetText(full);
                }
                catch (Exception) { /* 剪贴板被其他进程短暂锁定时静默，不阻断 UI */ }
            }
        }


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

            // Shift+Enter：插入换行继续编辑（多行命令输入）；普通 Enter：执行全部行
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                CliInput.SelectedText = "\n"; // 光标处断行，光标跟随
                e.Handled = true;
                return;
            }
            e.Handled = true;

            var input = CliInput.Text;
            CliInput.Clear();

            // 多行支持：按行拆分逐条执行（支持粘贴多行命令脚本）
            // WPF TextBox 行分隔符为 \r\n，显式处理 \r 避免残留（不依赖 Trim 兜底）
            var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(l => l.Trim())
                             .Where(l => l.Length > 0)
                             .ToList();
            if (lines.Count == 0) return;

            // 记入历史（整段，回显逐条）
            _cliHistory.Add(input.Trim());
            _historyIndex = _cliHistory.Count;

            foreach (var line in lines)
                ExecuteCliLine(line);
        }

        /// <summary>执行单条 CLI 命令（含回显与结果输出）。</summary>
        private void ExecuteCliLine(string input)
        {
            // 回显命令
            AppendCliOutput($"> {input}", "LimeGreen");

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
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["type"] = opts.GetValueOrDefault("type", "custom"), ["width"] = opts.GetValueOrDefault("width", ""), ["height"] = opts.GetValueOrDefault("height", "") }),
                "delete-screen" or "ds" => _viewModel.CommandService.Execute("delete_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "rename-screen" => _viewModel.CommandService.Execute("rename_screen", new() { ["name"] = opts.GetValueOrDefault("name", ""), ["new_name"] = opts.GetValueOrDefault("new-name", "") }),
                "copy-screen" => _viewModel.CommandService.Execute("copy_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "paste-screen" => _viewModel.CommandService.Execute("paste_screen", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "copy-widget" => _viewModel.CommandService.Execute("copy_widget", new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widget_name"] = opts.GetValueOrDefault("widget", "") }),
                "paste-widget" => _viewModel.CommandService.Execute("paste_widget", new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["x"] = opts.GetValueOrDefault("x", ""), ["y"] = opts.GetValueOrDefault("y", "") }),
                "set-default-font" => _viewModel.CommandService.Execute("set_default_font",
                    new() { ["font_family"] = opts.GetValueOrDefault("font-family", ""), ["font_size"] = opts.GetValueOrDefault("font-size", ""), ["font_weight"] = opts.GetValueOrDefault("font-weight", ""), ["font_style"] = opts.GetValueOrDefault("font-style", ""), ["text_decoration"] = opts.GetValueOrDefault("text-decoration", "") }),
                "add-widget" or "aw" => _viewModel.CommandService.Execute("add_widget",
                    new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widget_type"] = opts.GetValueOrDefault("type", "button"), ["x"] = opts.GetValueOrDefault("x", "100"), ["y"] = opts.GetValueOrDefault("y", "100"), ["width"] = opts.GetValueOrDefault("width", "100"), ["height"] = opts.GetValueOrDefault("height", "40"), ["center"] = opts.GetValueOrDefault("center", "false"), ["bound_tag"] = opts.GetValueOrDefault("bound-tag", ""), ["window_type"] = opts.GetValueOrDefault("window-type", "userview") }),
                "compile" or "b" => _viewModel.CommandService.Execute("compile", new()),
                "save" => _viewModel.CommandService.Execute("save_project", new()),
                "create-tag" or "ct" => _viewModel.CommandService.Execute("create_tag",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["data_type"] = opts.GetValueOrDefault("type", "FLOAT"), ["source"] = opts.GetValueOrDefault("source", ""), ["unit"] = opts.GetValueOrDefault("unit", ""), ["scan_interval"] = opts.GetValueOrDefault("scan-interval", "100"), ["deadband"] = opts.GetValueOrDefault("deadband", "0"), ["description"] = opts.GetValueOrDefault("description", ""), ["base_value"] = opts.GetValueOrDefault("base-value", "") }),
                "align" => _viewModel.CommandService.Execute("align_widgets",
                    new() { ["screen_name"] = opts.GetValueOrDefault("screen", ""), ["widgets"] = opts.GetValueOrDefault("widgets", ""), ["direction"] = opts.GetValueOrDefault("direction", "") }),
                "array" => ArrayLayoutFromGuiCli(opts),
                "list-screens" or "ls" => ListScreens(),
                "scan" => _viewModel.CommandService.Execute("scan_devices",
                    new() { ["nic"] = opts.GetValueOrDefault("nic", "") }),
                "connect" => _viewModel.CommandService.Execute("connect",
                    new() { ["ip"] = opts.GetValueOrDefault("ip", ""), ["model"] = opts.GetValueOrDefault("model", "NavigatorHMI") }),
                "configure-device" => _viewModel.CommandService.Execute("configure_device",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["protocol"] = opts.GetValueOrDefault("protocol", ""), ["connection_info"] = opts.GetValueOrDefault("connection", "") }),
                "deploy-project" => _viewModel.CommandService.Execute("deploy_project",
                    new() { ["device_ip"] = opts.GetValueOrDefault("ip", ""), ["file_path"] = opts.GetValueOrDefault("file", "") }),
                "deploy-firmware" => _viewModel.CommandService.Execute("deploy_firmware",
                    new() { ["device_ip"] = opts.GetValueOrDefault("ip", ""), ["file_path"] = opts.GetValueOrDefault("file", "") }),
                "create-alarm" => _viewModel.CommandService.Execute("create_alarm",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["tag_name"] = opts.GetValueOrDefault("tag", ""), ["type"] = opts.GetValueOrDefault("type", ""), ["threshold"] = opts.GetValueOrDefault("threshold", ""), ["deadband"] = opts.GetValueOrDefault("deadband", "0"), ["delay_ms"] = opts.GetValueOrDefault("delay", "0"), ["severity"] = opts.GetValueOrDefault("severity", "Warning"), ["message"] = opts.GetValueOrDefault("message", ""), ["trigger_mode"] = opts.GetValueOrDefault("trigger-mode", "Threshold"), ["category"] = opts.GetValueOrDefault("category", "User"), ["priority"] = opts.GetValueOrDefault("priority", "0"), ["ack_required"] = opts.GetValueOrDefault("ack-required", "true"), ["ack_group"] = opts.GetValueOrDefault("ack-group", ""), ["color_override"] = opts.GetValueOrDefault("color-override", "") }),
                "update-alarm" => UpdateAlarm(opts),
                "delete-alarm" => _viewModel.CommandService.Execute("delete_alarm", new() { ["name"] = opts.GetValueOrDefault("name", "") }),
                "create-list" => _viewModel.CommandService.Execute("create_list",
                    new() { ["name"] = opts.GetValueOrDefault("name", ""), ["type"] = opts.GetValueOrDefault("type", ""), ["items"] = opts.GetValueOrDefault("items", "") }),
                "update-tag" => UpdateTag(opts),
                // 续29/30：GUI CLI add-event 系参数映射（对齐独立 CLI AddEvent/RemoveEvent/UpdateEvent；
                // 原走 ExecuteDefaultCommand 无 event/action 映射 + params 未解析字典 → COMMAND_CRASH）
                "add-event" => AddEventFromGuiCli(opts),
                "remove-event" => RemoveEventFromGuiCli(opts),
                "update-event" => UpdateEventFromGuiCli(opts),
                // 续30 对齐审计：bind-event 原走 ExecuteDefaultCommand，params 未解析字典 → 强转崩溃；补专用分支对齐 CLI BindEvent
                "bind-event" => BindEventFromGuiCli(opts),
                _ => ExecuteDefaultCommand(command, opts)
            };
        }

        /// <summary>GUI CLI bind-event：为控件绑定事件-动作（对齐独立 CLI BindEvent：screen/widget/event/action 必填 + params 字典解析）。</summary>
        private CommandResult BindEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "widget", "event", "action") is { } err) return err;
            return _viewModel.CommandService.Execute("bind_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event"] = opts.GetValueOrDefault("event", ""),
                ["action"] = opts.GetValueOrDefault("action", ""),
                ["params"] = ParseCliParamsDict(opts),
            });
        }

        /// <summary>GUI CLI add-event：--screen→screen_name、--widget→widget_name（可选，缺省+世界地图=地图级事件）、--event→event_type、--action→action_type、--params "k=v,k=v" 解析为字典；缺必填参数报错（对齐独立 CLI RequireVal）。</summary>
        private CommandResult AddEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "event", "action") is { } err) return err;
            return _viewModel.CommandService.Execute("add_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event_type"] = opts.GetValueOrDefault("event", ""),
                ["action_type"] = opts.GetValueOrDefault("action", ""),
                ["params"] = ParseCliParamsDict(opts),
            });
        }

        /// <summary>GUI CLI remove-event：--action 指定仅移除该动作；否则移除整个事件（--event 必填、--action 可选，对齐独立 CLI RemoveEvent）。</summary>
        private CommandResult RemoveEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "event") is { } err) return err;
            return _viewModel.CommandService.Execute("remove_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event_type"] = opts.GetValueOrDefault("event", ""),
                ["action_type"] = opts.GetValueOrDefault("action", ""),
            });
        }

        /// <summary>GUI CLI update-event：更新事件下动作的参数（params 完整替换，对齐独立 CLI UpdateEvent）。</summary>
        private CommandResult UpdateEventFromGuiCli(Dictionary<string, string> opts)
        {
            if (MissingRequired(opts, "screen", "event", "action") is { } err) return err;
            return _viewModel.CommandService.Execute("update_event", new()
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widget_name"] = opts.GetValueOrDefault("widget", ""),
                ["event_type"] = opts.GetValueOrDefault("event", ""),
                ["action_type"] = opts.GetValueOrDefault("action", ""),
                ["params"] = ParseCliParamsDict(opts),
            });
        }

        /// <summary>GUI CLI 缺必填参数检查（对齐独立 CLI RequireVal 的缺参报错语义，逐个 key 报缺失）。</summary>
        private static CommandResult? MissingRequired(Dictionary<string, string> opts, params string[] keys)
        {
            foreach (var k in keys)
                if (string.IsNullOrWhiteSpace(opts.GetValueOrDefault(k, "")))
                    return CommandResult.Fail("MISSING_PARAM", $"缺少必填参数: --{k}");
            return null;
        }

        /// <summary>GUI CLI --params "k=v,k=v" 解析为 Dictionary（对齐独立 CLI AddEvent/UpdateEvent 的 raw 解析）。</summary>
        private static Dictionary<string, string> ParseCliParamsDict(Dictionary<string, string> opts)
        {
            var raw = opts.GetValueOrDefault("params", "");
            var dict = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(raw))
                foreach (var kv in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = kv.Split('=', 2);
                    if (parts.Length == 2) dict[parts[0].Trim()] = parts[1].Trim();
                }
            return dict;
        }

        /// <summary>GUI CLI array：OptIfProvided 语义（cols/rows 未提供不进字典 → 命令层自动计算，与独立 CLI Program.cs 对齐；防双入口漂移）。</summary>
        private CommandResult ArrayLayoutFromGuiCli(Dictionary<string, string> opts)
        {
            var p = new Dictionary<string, object?>
            {
                ["screen_name"] = opts.GetValueOrDefault("screen", ""),
                ["widgets"] = opts.GetValueOrDefault("widgets", ""),
                ["mode"] = opts.GetValueOrDefault("mode", "rect"),
                ["start_x"] = opts.GetValueOrDefault("start-x", "0"),
                ["start_y"] = opts.GetValueOrDefault("start-y", "0"),
                ["spacing_x"] = opts.GetValueOrDefault("spacing-x", "120"),
                ["spacing_y"] = opts.GetValueOrDefault("spacing-y", "80"),
                ["center_x"] = opts.GetValueOrDefault("center-x", "0"),
                ["center_y"] = opts.GetValueOrDefault("center-y", "0"),
                ["radius"] = opts.GetValueOrDefault("radius", "150"),
                ["start_angle"] = opts.GetValueOrDefault("start-angle", "0"),
                ["end_angle"] = opts.GetValueOrDefault("end-angle", "360"),
            };
            // OptIfProvided：显式传了才覆盖（未提供 → 命令层按控件数自动计算 cols/rows）
            if (opts.TryGetValue("cols", out var c) && c.Length > 0) p["cols"] = c;
            if (opts.TryGetValue("rows", out var r) && r.Length > 0) p["rows"] = r;
            return _viewModel.CommandService.Execute("array_layout", p);
        }

        /// <summary>GUI CLI update-alarm：OptIfProvided 语义（未提供的字段不进字典保留现值；message 显式提供才传，含空串 = 清空描述）。</summary>
        private CommandResult UpdateAlarm(Dictionary<string, string> opts)
        {
            var p = new Dictionary<string, object?> { ["name"] = opts.GetValueOrDefault("name", "") };
            if (opts.TryGetValue("new-name", out var nn) && nn.Length > 0) p["new_name"] = nn;
            if (opts.TryGetValue("tag", out var tag) && tag.Length > 0) p["tag_name"] = tag;
            if (opts.TryGetValue("type", out var type) && type.Length > 0) p["type"] = type;
            if (opts.TryGetValue("threshold", out var th) && th.Length > 0) p["threshold"] = th;
            if (opts.TryGetValue("deadband", out var db) && db.Length > 0) p["deadband"] = db;
            if (opts.TryGetValue("delay", out var dl) && dl.Length > 0) p["delay_ms"] = dl;
            if (opts.TryGetValue("severity", out var sv) && sv.Length > 0) p["severity"] = sv;
            if (opts.TryGetValue("message", out var msg)) p["message"] = msg;
            // W1 六参数（OptIfProvided 语义：未提供不进字典保留现值；与 CLI Program.cs:141 逐 key 对齐）
            if (opts.TryGetValue("trigger-mode", out var tm) && tm.Length > 0) p["trigger_mode"] = tm;
            if (opts.TryGetValue("category", out var cat) && cat.Length > 0) p["category"] = cat;
            if (opts.TryGetValue("priority", out var pri) && pri.Length > 0) p["priority"] = pri;
            if (opts.TryGetValue("ack-required", out var ar) && ar.Length > 0) p["ack_required"] = ar;
            if (opts.TryGetValue("ack-group", out var ag) && ag.Length > 0) p["ack_group"] = ag;
            if (opts.TryGetValue("color-override", out var co) && co.Length > 0) p["color_override"] = co;
            return _viewModel.CommandService.Execute("update_alarm", p);
        }

        /// <summary>GUI CLI update-tag：type→data_type 显式映射（防 MapCliKey 全局 type→widget_type 误转，cli-param-sanitize §6）；OptIfProvided 语义，未提供字段保留现值。</summary>
        private CommandResult UpdateTag(Dictionary<string, string> opts)
        {
            var p = new Dictionary<string, object?> { ["name"] = opts.GetValueOrDefault("name", "") };
            if (opts.TryGetValue("new-name", out var nn) && nn.Length > 0) p["new_name"] = nn;
            if (opts.TryGetValue("type", out var ty) && ty.Length > 0) p["data_type"] = ty;
            if (opts.TryGetValue("source", out var src)) p["source"] = src;
            if (opts.TryGetValue("unit", out var un)) p["unit"] = un;
            if (opts.TryGetValue("scan-interval", out var si) && si.Length > 0) p["scan_interval"] = si;
            if (opts.TryGetValue("deadband", out var db) && db.Length > 0) p["deadband"] = db;
            if (opts.TryGetValue("description", out var desc)) p["description"] = desc;
            if (opts.TryGetValue("base-value", out var bv)) p["base_value"] = bv;
            return _viewModel.CommandService.Execute("update_tag", p);
        }

        private static string MapCliKey(string key) => key switch
        {
            "screen" => "screen_name", "widget" => "widget_name", "type" => "widget_type",
            "tag" => "tag_name", "file" => "file_path",
            // 用户/组命令参数键（对齐 CLI OptMap/Require：user-name→user_name 等；缺映射时命令层拿不到必填参数）
            "user-name" => "user_name", "new-user-name" => "new_user_name",
            "new-password" => "new_password", "new-group-name" => "new_group_name",
            "group-name" => "group_name",
            // 控件命令参数键（对齐 CLI OptMap：add_widget 的 bound-tag/window-type）
            "bound-tag" => "bound_tag", "window-type" => "window_type",
            // 布局命令参数键（与 CLI OptMap 对齐）：kebab → snake
            "start-x" => "start_x", "start-y" => "start_y",
            "spacing-x" => "spacing_x", "spacing-y" => "spacing_y",
            "center-x" => "center_x", "center-y" => "center_y",
            "start-angle" => "start_angle", "end-angle" => "end_angle",
            "scan-interval" => "scan_interval",
            "new-name" => "new_name",
            "base-value" => "base_value",
            "connection" => "connection_info",
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

        /// <summary>参数安全净化（三级分类，逻辑统一在 <see cref="NavigatorHMI.Common.CliParamSanitizer"/>，与 CLI 端逐 key 一致）。</summary>
        private static void SanitizeCliParams(Dictionary<string, string> opts)
        {
            foreach (var kv in opts.ToList())
            {
                var (key, value) = (kv.Key, kv.Value);
                var err = NavigatorHMI.Common.CliParamSanitizer.Validate(key, value);
                if (err != null) throw new ArgumentException(err);
            }
        }

        private void AppendCliOutput(string text, string color)
        {
            Dispatcher.Invoke(() =>
            {
                CliOutput.AppendText(text + "\n");
                // 输出更新后自动滚动到新一行：延后到布局完成后（AppendText 只触发 InvalidateMeasure，
                // 立即 ScrollToEnd 时 ScrollableHeight 还是旧值，连续多行会停在旧底部）
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (CliOutputScroller != null)
                        CliOutputScroller.ScrollToEnd();
                    else
                        CliOutput.ScrollToEnd();
                }));
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

        private const string CliHelpText = NavigatorHMI.Common.CliHelpContent.Text;  // 与帮助对话框共用单一来源
        #endregion
    }
}
