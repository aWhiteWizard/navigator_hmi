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

        #region 原 世界地图交互绘制与视口（72daf0e^ 批 4）（第 1865-2862 行）

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
        /// <summary>C12-9：退出作业点选点模式（右键/ESC；不清除已加点）。</summary>
        /// <summary>C12-9：作业点默认名"作业点N"（N 从现有数量+1 起递增，避开重名）。</summary>
        /// <summary>进入作业范围加点编辑模式：地图点击加经纬度、ESC/右键退出（P3/P4）。</summary>
        /// <summary>退出作业范围加点编辑模式（右键/ESC；不清除已加点）。</summary>
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
            // N-6（2026-08-30）：跳过 (0,0) 未配置坐标（与 FW computeBounds 一致，防把 0 度线拉进视口；合法 (0,0)=几内亚湾被跳过——中国场景无影响）
            foreach (var wp in project.WorldMap?.WorkPoints ?? new())
                if (ResolveWorkPointGeo(project, wp.BoundTag, wp.FixedPoint) is { } wg && !(wg.Longitude == 0 && wg.Latitude == 0))
                    geos.Add(wg);
            // 2) 作业范围点并入包围盒（围栏顶点也要被视口自适应覆盖）
            foreach (var rp in project.WorldMap?.WorkRangePoints ?? new())
                if (ResolveWorkPointGeo(project, rp.BoundTag, rp.FixedPoint) is { } rg && !(rg.Longitude == 0 && rg.Latitude == 0))
                    geos.Add(rg);

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
        /// <summary>作业点经纬度解析：绑 GPS 变量优先（取基准值，1Hz 模拟动态），否则固定值。</summary>
        /// <summary>添加单个 marker 到 overlay：作业点 = 红点 + 名称标签；作业范围点 = 蓝色小方块（无名称）。
        /// C12-5：悬停气泡（ToolTip）显示名称/经纬度/绑定变量——悬停有反馈后即可验证 C12-4 重复点拦截。
        /// V-2：isSelected → 点外围 18×18 虚线框（表格选中联动）；选中时显示经纬度（DMS，名字下方/范围点上方）；
        /// marker 点击 → 选中对应表格行（双向选择，V-2c）。</summary>
        /// <summary>GPS 动态点位（设计态模拟，1Hz）：绑 GPS 变量的作业点/范围点按变量基准值实时更新位置。</summary>
        private void UpdateDynamicGeoWidgets() => UpdateAllGeoWidgets();

        /// <summary>属性面板：删除选中作业点行（P4 表格）。</summary>
        /// <summary>C12-1：作业点表格新行提交（RowEditEnding Commit）——空行判定从 CollectionChanged Add 移到提交路径：
        /// 双击 placeholder 进入编辑时 DataGrid 已同步 Add 空行（此时未输入），Add 时判定必然误杀；提交时才有用户输入。
        /// X-3b：IsDuplicateName 已由 CellEditEnding 的名称列 UpdateSource 更新（e.EditingElement 时序确定；
        /// RowEditEnding 阶段编辑 TextBox 可能已被 DataGrid 移除，不可再定位/UpdateSource）。</summary>
        /// <summary>V-2c：表格选中变化 → 滚动到该行（marker 点击经 SelectedItem 绑定触发，用户点表格同样适用；选中态由 setter → overlay 刷新联动）。</summary>
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
        /// <summary>W-1c：作业范围表格点击空白（未命中行，且非滚动条/列头 chrome）→ 取消选中态（同作业点）。</summary>
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

        // 画布/虚影尺寸兜底（vm 无值时）：与 HMIProject 模型默认 _deviceWidth=800/_deviceHeight=480 对齐（2026-08-26 魔法数字整改：原 ??600 与规范 480 不一致）
        private const double DefaultDeviceWidth = 800;
        private const double DefaultDeviceHeight = 480;

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

        /// <summary>X-3 审查：输入是否为坐标合法前缀（全字符在坐标字符集内：数字/负号/点/度分秒符号/方向前缀/空白）——中间态不弹泡防闪烁。</summary>
        private static bool IsCoordPrefix(string s)
            => s.All(c => c is >= '0' and <= '9' or '.' or '-' or '°' or '\'' or '"' or ' ' or '′' or '″'
                or 'E' or 'e' or 'W' or 'w' or 'N' or 'n' or 'S' or 's' or '度' or '分' or '秒');

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
        /// X-3a：经纬度输入过程实时检测非法弹气泡；名称列实时检测重名弹气泡。
        /// dms-hint 提示块是 DataGrid 的兄弟节点（同 StackPanel），从 DataGrid 的 Parent 向下查找。</summary>
        /// <summary>V-5a：范围点经纬度单元格 DMS 提示（同作业点；范围点无名称列）。</summary>
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
        private void PrepareGeoCellEdit(DependencyObject? root, DataGridPreparingCellForEditEventArgs e, bool isRangeTable)
        {
            var hint = FindDmsHint(root);
            if (hint == null) return;
            var isLng = (e.Column.Header as string)?.StartsWith("经度") == true;
            var isLat = (e.Column.Header as string)?.StartsWith("纬度") == true;
            if (!isLng && !isLat) { UnsubscribeGeoHint(); hint.Visibility = Visibility.Collapsed; return; }   // 非经纬度列：清残留订阅
            if (e.EditingElement is not TextBox tb) return;
            UnsubscribeGeoHint();   // 移除旧订阅（防泄漏）
            _geoHintTextBox = tb;
            _geoHintHandler = (_, _) =>
            {
                UpdateGeoHint(hint, isLng, tb.Text);
                // X-3a：输入过程实时检测非法 → 立即弹气泡（锚定编辑格旁）；合法/空 → 消泡（不等提交，用户需求"错误发生时立即弹出"）
                // 中间态宽容（X-3 审查）："-"/"104." 等合法前缀（全字符在坐标字符集内）不弹泡防闪烁打扰
                var trimmed = tb.Text.Trim();
                if (GeoPoint.TryParseCoord(trimmed, isLng, out _)) HideErrorBubble();
                else if (trimmed.Length > 0 && !IsCoordPrefix(trimmed))
                {
                    int col = isRangeTable ? (isLng ? 0 : 1) : (isLng ? 1 : 2);
                    ShowErrorBubble(FindDataGridCell(e.Row, col),
                        isLng ? "经度格式：104.06（东经为正，负号=西经）；DMS 如 E104°3'29.88\""
                              : "纬度格式：30.67（北纬为正，负号=南纬）；DMS 如 N30°40'20.12\"");
                }
                else HideErrorBubble();   // 合法前缀中间态/空：不弹（保留已有气泡至 3s 或改对）
            };
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
        /// <summary>V-2c：作业范围表格选中变化 → 滚动到该行（同 WorkPointGrid）。</summary>
        /// <summary>C12-1：多边形顶点表格新行提交（同 WorkPoint 表格；已编辑行挂模型、未编辑空行移除）。</summary>
        private void PolygonPointGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || !e.Row.IsNewItem) return;
            if (e.Row.Item is PolygonPointRowVM row) _propertyViewModel.CommitNewPolygonPointRow(row);
        }

        /// <summary>属性面板：删除选中作业范围点行（P4 表格）。</summary>
        /// <summary>属性面板：删除多边形顶点行（P7 表格；删除后不足 3 点拒绝）。</summary>
        // Q-6（2026-09-04 用户 Check）：HistoryView 变量表格——添加/删除行
        private void AddHistoryTagRow_Click(object sender, RoutedEventArgs e) => _propertyViewModel.AddHistoryTagRow();
        private void RemoveHistoryTagRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PropertyViewModel.HistoryTagRowVM row)
            {
                _propertyViewModel.RemoveHistoryTagRow(row);
                e.Handled = true;
            }
        }


        /// <summary>任务11：工具栏悬浮窗开关（退回内嵌）——开 → 显示内嵌球；关 → 隐藏。</summary>
        private void AiBallToggle_Checked(object sender, RoutedEventArgs e)
            => AiFabBtn.Visibility = Visibility.Visible;
        #endregion

        // AB-6 复审 🟡-3（2026-09-11）：自 EditWindow.xaml.cs 归位（原行 754-761，内容零改动）
        private GeoPoint? ScreenToGeo(Point screenPos)
        {
            var map = WorldMapControl?.Map;
            if (map == null) return null;
            var vp = map.Navigator.Viewport;
            if (vp.Width <= 0 || vp.Height <= 0 || !(vp.Resolution > 0)) return null;   // NaN 守卫：!(NaN>0)=true
            return MapViewportMath.ScreenToGeo(vp.CenterX, vp.CenterY, vp.Resolution, vp.Width, vp.Height, screenPos);
        }

    }
}
