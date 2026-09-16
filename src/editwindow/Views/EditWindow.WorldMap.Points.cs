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

        // AB-6 复审（2026-09-11）：世界地图作业点/范围点表格处理器与标记绘制 —— 自 EditWindow.WorldMap.cs 纯搬家（基线 72daf0e 第 210-1019 行，23 个成员；内容零改动）
        internal void StartEditWorkPoint_Click(object sender, RoutedEventArgs e)
        {
            WorldMapContextMenu.IsOpen = false;
            if (_viewModel?.CurrentProject?.WorldMap == null) return;
            ExitAddMode();   // V-3（审查 Nit4）：清 Polygon 绘制/工具箱状态，保证模式互斥（防两模式并存致橡皮筋预览冻结）
            _isEditingWorkRange = false;   // 互斥：退出作业范围加点模式
            _isAddingWorkPoint = true;
            WorldMapControl.Cursor = Cursors.Cross;
            System.Diagnostics.Trace.WriteLine("[WorldMap] 进入作业点选点模式：点击地图追加作业点，ESC/右键退出");
        }

        private void ExitWorkPointAddMode()
        {
            if (!_isAddingWorkPoint) return;
            _isAddingWorkPoint = false;
            WorldMapControl.Cursor = Cursors.Arrow;
            RemoveCursorGeoLabel();   // V-3b：退出选点模式隐藏经纬度跟随
            UpdateAllGeoWidgets();
        }

        private static string NextWorkPointName(WorldMapConfig wm)
        {
            var names = new HashSet<string>(wm.WorkPoints.Select(w => w.Name));
            int n = wm.WorkPoints.Count + 1;
            while (names.Contains($"作业点{n}")) n++;
            return $"作业点{n}";
        }

        internal void StartEditWorkRange_Click(object sender, RoutedEventArgs e)
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

        private void ExitWorkRangeEditMode()
        {
            if (!_isEditingWorkRange) return;
            _isEditingWorkRange = false;
            WorldMapControl.Cursor = Cursors.Arrow;
            RemoveCursorGeoLabel();   // V-3b：退出选点模式隐藏经纬度跟随
            UpdateAllGeoWidgets();
        }

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

        private static GeoPoint? ResolveWorkPointGeo(HMIProject project, string boundTag, GeoPoint? fixedPoint)
        {
            if (!string.IsNullOrEmpty(boundTag))
            {
                var t = project.Tags.FirstOrDefault(x => x.Name == boundTag);
                if (t?.DataType == TagDataType.GPS && GeoPoint.TryParse(t.BaseValue, out var g) && g != null) return g;
            }
            return fixedPoint;
        }

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
                            _variableManagerVM?.Refresh();   // Y-1a：拖拽写回 BaseValue 后刷新变量管理器表格（Tag 无 INPC，须强制刷新）
                        }
                    }
                    else if (dragModel is WorkRangePoint mrp && !string.IsNullOrEmpty(mrp.BoundTag))
                    {
                        if (FindBoundGpsTag(mrp.BoundTag) is { } tag)
                        {
                            _viewModel.PushTagSnapshot();
                            tag.BaseValue = $"({GeoPoint.FormatDms(geo.Longitude, true)}, {GeoPoint.FormatDms(geo.Latitude, false)})";
                            _propertyViewModel.RefreshWorkRangeRows();
                            _variableManagerVM?.Refresh();   // Y-1a：拖拽写回 BaseValue 后刷新变量管理器表格（Tag 无 INPC，须强制刷新）
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

        internal void DeleteWorkPointRow_Click(object sender, RoutedEventArgs e)
        {
            if (_propertyViewModel.SelectedWorkPointRow != null)
            {
                _propertyViewModel.DeleteWorkPointRow(_propertyViewModel.SelectedWorkPointRow);
                e.Handled = true;
            }
        }

        internal void WorkPointGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row.Item is not WorkPointRowVM row) return;
            // Y-6 reviewer 🟡3：新行含分隔符名（Name 拒写但坐标可能已落）→ 阻止挂入（防无名作业点——GUI 与命令层对称）
            if (row.HasNameError)
            {
                if (e.Row.IsNewItem)
                    row.Model.Name = "";   // 分隔符名未落库；清名防「坐标已写 + 名空」误挂——保持行待改
                ShowErrorBubble(FindDataGridCell(e.Row, 0), row.NameError);
                return;
            }
            if (e.Row.IsNewItem) _propertyViewModel.CommitNewWorkPointRow(row);
            // W-3a：名称重名提交 → 名称列旁立即弹气泡（IsDuplicateName 由 CellEditEnding UpdateSource 更新；悬停 ToolTip 保留）
            if (row.IsDuplicateName) ShowErrorBubble(FindDataGridCell(e.Row, 0), "作业点名称重复，请改名");
        }

        internal void WorkPointGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid dg && dg.SelectedItem != null)
                dg.ScrollIntoView(dg.SelectedItem);
        }

        internal void WorkPointGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsDataGridBlankHit(e)) ClearGeoSelection();
        }

        internal void WorkRangeGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsDataGridBlankHit(e)) ClearGeoSelection();
        }

        internal void WorkPointGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            HideErrorBubble();   // W-3a：进入编辑 → 气泡立即消失
            if ((e.Column.Header as string) == "名称" && e.EditingElement is TextBox tb && e.Row?.Item is WorkPointRowVM row)
            {
                // X-3a：名称列实时检测重名（Name setter 已实时更新 IsDuplicateName——实时弹泡不等提交）
                // X-3 审查：经纬度切名称列时隐藏 DMS hint（名称分支绕过 PrepareGeoCellEdit 的 hint 处理）
                if (FindDmsHint(((FrameworkElement)sender).Parent) is { } nameHint) nameHint.Visibility = Visibility.Collapsed;
                UnsubscribeGeoHint();
                _geoHintTextBox = tb;
                _geoHintHandler = (_, _) =>
                {
                    if (row.HasNameError) ShowErrorBubble(FindDataGridCell(e.Row, 0), row.NameError);   // Y-6：分隔符实时弹泡
                    else if (row.IsDuplicateName) ShowErrorBubble(FindDataGridCell(e.Row, 0), "作业点名称重复，请改名");
                    else HideErrorBubble();
                };
                tb.TextChanged += _geoHintHandler;
                return;
            }
            PrepareGeoCellEdit(((FrameworkElement)sender).Parent, e, isRangeTable: false);
        }

        internal void WorkPointGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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

        internal void WorkRangeGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            HideErrorBubble();   // W-3a：进入编辑 → 气泡立即消失
            PrepareGeoCellEdit(((FrameworkElement)sender).Parent, e, isRangeTable: true);
        }

        internal void WorkRangeGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
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

        internal void WorkRangeGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || !e.Row.IsNewItem) return;
            if (e.Row.Item is WorkRangeRowVM row) _propertyViewModel.CommitNewWorkRangeRow(row);
        }

        internal void WorkRangeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.DataGrid dg && dg.SelectedItem != null)
                dg.ScrollIntoView(dg.SelectedItem);
        }

        internal void DeleteWorkRangeRow_Click(object sender, RoutedEventArgs e)
        {
            if (_propertyViewModel.SelectedWorkRangeRow != null)
            {
                _propertyViewModel.DeleteWorkRangeRow(_propertyViewModel.SelectedWorkRangeRow);
                e.Handled = true;
            }
        }

        internal void DeletePolygonPointRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is PolygonPointRowVM row)
            {
                _propertyViewModel.DeletePolygonPointRow(row);
                e.Handled = true;
            }
        }

    }
}
