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

        #region 原 工具栏拖拽（72daf0e^ 第 4372-4435 行）

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

        #region 原 工具栏按钮操作（72daf0e^ 第 4446-4902 行）

        private void ApplyZoom(double level)
        {
            _zoomLevel = Math.Max(MinZoomLevel, Math.Min(level, MaxZoomLevel));
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
            if (anchorable != null)
            {
                if (!anchorable.IsVisible) anchorable.Show();
                // L-B1 用户定：日志自动切出输出窗口——激活该 Tab（同 Pane 多 Tab 时切到前台）
                anchorable.IsActive = true;
            }
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
            _currentProject?.ClearDirty();   // K-1d：新建工程清脏（原窗口即将关闭）

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
                project.LastModifiedTime = DateTime.UtcNow;
                RecentProjectManager.Instance.AddRecentProject(dlg.FileName);

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
                ProjectManager.Save(_currentProject, dlg.FileName);   // K-1c：统一保存入口（原子保存 + 内部已置路径并清脏）
                _currentProject.ProjectFilePath = dlg.FileName;       // 防御性同步内存路径（等值短路 no-op）
                _currentProject.ClearDirty();                         // 防御性收尾（Save 已清脏）
                _viewModel.CommandService.ReplaceProject(_currentProject);
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

    }
}
