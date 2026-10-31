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
#region 原 私有字段（72daf0e^ 第 37-167 行；31 个成员块）
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
        private bool _skipClosingCheck = false;
        /// <summary>缩放手柄静态回调引用（Closing 时比较清理，防闭包泄漏）。</summary>
        private Action? _resizeDragStartedCallback;
        private Action<Widget>? _resizeDragCompletedCallback;   // C12-12/AA-2：缩放结束回调（字段化同上；携带被拖 widget 按类型兜底刷表）
        private Action<Widget>? _resizeDragDeltaCallback;   // W-4c/Z-2：缩放过程回调（字段化同上；携带被拖 widget 按类型节流刷新端点表格）
        private DateTime _lastResizeDeltaRefresh = DateTime.MinValue;   // W-4c：拖拽过程刷新节流时间戳
        private DateTime _lastDragMoveRefresh = DateTime.MinValue;   // AA-1：整体拖动过程刷新节流时间戳
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

#region 原 未分区块（72daf0e^ 第 168-176 行；1 个成员块）


        /// <summary>K-4: 连接会话变化 → 状态栏（SessionChanged 可能后台线程——Dispatcher 封送）。</summary>
        private void OnConnectionSessionChanged(ConnectionSession? session)
        {
            Dispatcher.BeginInvoke(new Action(UpdateConnectionStatusBar));
        }
        #endregion

#region 原 构造函数 & 初始化（72daf0e^ 第 177-3027 行；32 个成员块）

        /// <summary>K-4: 刷新状态栏文本（未连接 / 已连接 IP·型号·固件）。</summary>
        private void UpdateConnectionStatusBar()
        {
            var s = DeviceConnectionService.Session;
            ConnectionStatusBar.Text = s == null
                ? "未连接"
                : $"已连接 {s.Ip} · {s.Model} · 固件 {s.FirmwareVersion ?? "?"}";
        }

        public EditWindow(HMIProject project)
        {
            InitializeComponent();

            // 1. 先创建 ViewModel 并设置 DataContext
            _viewModel = new EditWindowViewModel(project);
            this.DataContext = _viewModel;
            // 🔴C1（reviewer Z-2）：MQTT 连接切换/密码应用成功 → 清 PasswordBox 明文（LoadConnectionEdits 只清 VM 字段，
            // 控件文本无绑定会跨连接残留——旧字符+新字符整体加密写错连接；Clear 触发 PasswordChanged 推空串幂等无害）
            _viewModel.MqttSettingsVM.ConnectionSwitched += ClearMqttPasswordBox;
            _viewModel.MqttSettingsVM.PasswordApplied += ClearMqttPasswordBox;

            // 2. 保存项目引用
            _currentProject = project;
            this.Title = project.ProjectFilePath;

            // K-4: 主窗口状态栏（连接状态随 ConnectionSession 实时联动——GUI 与 CLI connect 对等）
            DeviceConnectionService.SessionChanged += OnConnectionSessionChanged;
            UpdateConnectionStatusBar();

            // L-B4 输出窗口：设备命令（connect/disconnect/blink/vnc/deploy）结果日志聚合 → 输出窗口自动弹出
            _viewModel.CommandService.CommandExecuted += OnOutputCommandExecuted;

            // L-B1：设备面板内部操作（连接测试/搜索——不走命令层）日志 → 输出窗口
            _viewModel.DevicePanelVM.OutputRequested += OnDevicePanelOutput;

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
                () =>
                {
                    // C12-12/AA-1：拖拽结束 → 端点表格刷新（多边形 + 矩形，AA-1 补矩形表——整体移动后坐标同步）
                    _lastDragMoveRefresh = DateTime.MinValue;   // AA-1：重置 move 节流时间戳（对齐 Z-2 首帧模式，防跨拖拽残留吞首帧）
                    _propertyViewModel.RefreshPolygonPointRowsDisplay();
                    _propertyViewModel.RefreshRectanglePointRowsDisplay();
                },
                (Widget w) =>
                {
                    // AA-1：整体拖动过程实时刷端点表格（100ms 节流，按被拖类型刷表——修"矩形整体拖坐标不更新/多边形松开才变"）
                    var now = DateTime.UtcNow;
                    if ((now - _lastDragMoveRefresh).TotalMilliseconds < 100) return;
                    _lastDragMoveRefresh = now;
                    if (w is RectangleWidget rect) _propertyViewModel.RefreshRectanglePointRowsDisplay(rect);
                    else if (w is PolygonWidget) _propertyViewModel.RefreshPolygonPointRowsDisplay();
                });

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
            _resizeDragStartedCallback = () =>
            {
                _lastResizeDeltaRefresh = DateTime.MinValue;   // Z-2：拖拽开始重置节流时间戳，保证本次拖拽首帧即刷新（首帧不被跨拖拽残留时间戳吞掉）
                _viewModel.PushUndoSnapshot();
            };
            _getCanvasSizeCallback = () => new Size(_propertyViewModel.CanvasWidth, _propertyViewModel.CanvasHeight);
            SelectorHelper.ResizeDragStarted = _resizeDragStartedCallback;
            SelectorHelper.GetCanvasSize = _getCanvasSizeCallback;
            // C12-12/AA-2：缩放结束 → 端点表格兜底刷新（携带被拖 widget 按类型刷表——跨画面陈旧选中下松开后表格不空）
            _resizeDragCompletedCallback = (Widget w) =>
            {
                if (w is RectangleWidget rect) _propertyViewModel.RefreshRectanglePointRowsDisplay(rect);
                else if (w is PolygonWidget) _propertyViewModel.RefreshPolygonPointRowsDisplay();
            };
            SelectorHelper.ResizeDragCompleted = _resizeDragCompletedCallback;
            // W-4c/Z-2：缩放手柄拖拽过程 → 端点表格实时同步（100ms 节流；DragCompleted 兜底最终值）
            // Z-2/Z-3：按被拖 widget 类型刷对应表格（多边形实时同步 + 矩形跨画面修复）——携带被拖 widget，
            // 摆脱 _selectedWidget 门控（静默选中/跨画面下 _selectedWidget 会陈旧导致表格 Clear 不重建）
            _resizeDragDeltaCallback = (Widget w) =>
            {
                var now = DateTime.UtcNow;
                if ((now - _lastResizeDeltaRefresh).TotalMilliseconds < 100) return;
                _lastResizeDeltaRefresh = now;
                // Z-2/Z-3：按被拖 widget 类型刷对应表格——矩形传被拖对象（门控下沉到数据源，彻底摆脱 _selectedWidget 陈旧）
                if (w is RectangleWidget rect) _propertyViewModel.RefreshRectanglePointRowsDisplay(rect);
                else if (w is PolygonWidget) _propertyViewModel.RefreshPolygonPointRowsDisplay();
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

            // W-B 壳先行：首画面渲染从构造延后到 Loaded 后 UI 空闲执行（EditWindow_Loaded 尾部 Background 分派）——
            // 大工程首画面（ItemsControl 实例化全部控件）不阻塞窗口弹出（先见壳后见数据）

            _currentProject?.ClearDirty();   // K-1d：打开/切换工程后清脏（加载清脏在 ProjectFileService.Load）
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
                    ["device"] = result.DeviceName,   // AB-4：所属设备（proto tag.device_name——设备端据此选传输/连接）
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
                if (result.DeviceName != tag.DeviceName) p["device"] = result.DeviceName;   // AB-4：所属设备变更
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

        /// <summary>设备新建/编辑：对话框收集 → CommandService 落库（GUI/CLI/AI 同一路径）。</summary>
        /// <summary>报警新建/编辑：对话框收集 → CommandService 落库（GUI/CLI/AI 同一路径）。</summary>
        /// <summary>拖拽起点（TagGrid_PreviewMouseMove 判定阈值用）。</summary>
        private Point _tagDragStart;

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


        /// <summary>
        /// 上溯一层父节点——**兼容 ContentElement**（AH 2026-10-30 用户实测崩溃修复）。
        ///
        /// 背景：`VisualTreeHelper.GetParent` 只接受 Visual/Visual3D；命中的若是内联元素
        /// （`Run`/`Bold` 等 `ContentElement`，例如变量表分组组头的 <c>&lt;Run Text="组: "/&gt;</c>），
        /// 会抛 <see cref="InvalidOperationException"/>「System.Windows.Documents.Run 不是 Visual 或 Visual3D」。
        /// 这类元素走逻辑树：先用 <see cref="LogicalTreeHelper"/>，再退到 <see cref="ContentOperations"/>。
        /// </summary>
        internal static DependencyObject? GetParentSafe(DependencyObject? node)
        {
            if (node == null) return null;
            if (node is System.Windows.Media.Visual || node is System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(node);
            var logical = LogicalTreeHelper.GetParent(node);
            if (logical != null) return logical;
            if (node is ContentElement ce) return ContentOperations.GetParent(ce);
            return null;
        }

        /// <summary>沿树上溯查找指定类型父元素（图片路径回车确认定位 DataGrid 用）。</summary>
        private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T t) return t;
                source = GetParentSafe(source);   // AH：ContentElement（Run 等）安全上溯
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


        /// <summary>屏幕坐标 → 经纬度（自实现换算：Mapsui 5.1 Viewport 无 ScreenToWorld API；无旋转场景）。</summary>
        /// <summary>任务11：工具栏悬浮窗开关——关 → 隐藏内嵌球。</summary>
        // ── 任务11 内嵌悬浮球拖动（退回 0cbcb1f 逻辑）+ Z1 锚定/钳制 ──
        private bool _aiFabDragging;
        private Point _aiFabMouseDownPos;
        private Thickness _aiFabDownMargin;

        /// <summary>Z1：把 AiFab 的绝对位置（HorizontalAlignment=Left/Top + Margin）钳制回可视区——拖动后窗口缩小/状态切换不漂出窗外。
        /// 基准：AiFab 位于 Grid.Row=3 单元格（与 DockManager 同格），Margin 相对单元格左上角——用 DockManager 作基准（窗口客户区基准会因上方 Auto 行高错位 ~60px）。</summary>
        /// <summary>Z1：窗口尺寸变化时钳制 AiFab 位置（拖动后的绝对 Margin 不随窗口自适应，缩小时漂出窗外）。</summary>
        /// <summary>AI 悬浮按钮：按下记录起点并捕获鼠标（不立即移动；拖动/点击在 Move/Up 判定）。基准 = DockManager（与 AiFab 同 Grid 单元格，Margin/GetPosition 同一坐标系）。</summary>
        /// <summary>
        /// 关闭顶部菜单所有子菜单并失焦（防窗口销毁期 MenuItem 悬停定时器竞态崩溃）。
        /// 注：仅遍历顶层 MenuItem（当前 XAML 无嵌套子菜单；若未来加"最近打开"等嵌套子菜单需改递归）。
        /// </summary>
        /// <summary>
        /// 窗口关闭（所有路径必触发）：清理静态回调，防闭包持有已关闭窗口/项目（GC 无法回收）。
        /// </summary>


        /// <summary>
        /// 标记工程已修改（脏标记），并更新窗口标题显示星号。
        /// 拖拽行为和添加控件操作均通过此回调触发。
        /// </summary>
        private void MarkProjectDirty()
        {
            // Q-2（2026-09-04 Check 审查修复）：**无条件刷标题**（MarkDirty 幂等）——原 `if (IsDirty != true)` 分支
            // 使「模型先置脏、UI 脏回调后到」的路径标题星号不更新：B1 属性 setter 先写模型（HMIProject setter 自 MarkDirty）
            // 再 DirtyRequested → 跳变分支短路（B1 不标脏实测根因）；命令层根集合/根标量命令首脏同源。无条件执行消除顺序耦合
            //（Save 路径由 SaveProject 清标题；标题 DP 同值重复赋值等值短路无副作用——可接受每属性改都触发）
            if (_currentProject == null) return;
            _currentProject.MarkDirty();
            this.Title = _currentProject.ProjectFilePath + "*";
        }
        #endregion

#region 原 辅助方法（72daf0e^ 第 3028-3035 行；1 个成员块）




        /// <summary>虚影层 ItemsControl 的 Name（局部刷新/定位用，与主层 "MyItemsControl" 区分）。</summary>
        private const string GlobalGhostName = "GlobalGhostItemsControl";
        #endregion

#region 原 项目保存 & 关闭（72daf0e^ 第 4050-4116 行；4 个成员块）

        /// <summary>
        /// 检查未保存更改，返回 true 表示可以继续关闭，false 表示用户取消。
        /// </summary>
        /// <summary>
        /// 保存工程到文件。
        /// </summary>
        private void SaveProject(HMIProject project, string filePath)
        {
            ProjectManager.Save(project, filePath);   // K-1c：统一保存入口（原子保存 + 清脏）
            this.Title = project.ProjectFilePath;
            _listManagerVM.RecheckImagePaths();   // 工程目录刚生效 → 重算图片路径标红状态
        }



        #endregion

#region 原 未分区块（72daf0e^ 第 4366-4930 行；3 个成员块）

        private ToolBar? _dragSource;
        private Point _dragStart;
        private bool _isDragging;

        // 缩放（2026-08-26 魔法数字整改命名）
        private const double ZoomStepFactor = 1.25;   // 每步缩放倍率
        private const double MinZoomLevel = 0.1;      // 最小缩放（10%）
        private const double MaxZoomLevel = 5.0;      // 最大缩放（500%）
        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoomLevel * ZoomStepFactor);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(_zoomLevel / ZoomStepFactor);
        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ApplyZoom(1.0);

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
        #endregion

    }
}
