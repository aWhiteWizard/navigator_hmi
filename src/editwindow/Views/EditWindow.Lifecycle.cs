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

        // AB-6 复审（2026-09-11）：窗口生命周期/菜单清理/AI 悬浮球/关闭路径 —— 自 EditWindow.xaml.cs 纯搬家（基线 72daf0e 第 662-980 行，12 个成员；内容零改动）
        private void EditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // W-B 壳先行：首渲染统一由本方法尾部 Dispatcher.Background 分派执行（先见壳后见数据）——
            // 此处不再同步 EnsureItemsControlInCanvas()（否则构造删渲染后此处变同步首渲染，
            // 抵消壳先行 + 与尾部 Background 分派构成每次打开双全量渲染——reviewer E1）

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

            // W-B 壳先行：窗口已显示（壳），首画面渲染延后到 UI 空闲（Background 优先级低于 Input）——
            // 大工程首画面构建不阻塞打开；异常不崩溃窗口（Trace 记录，可后续切换画面重载）
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    LoadCanvas(_viewModel.CurrentScreen);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[EditWindow] 首画面渲染失败（W-B 后台分派）: {ex.Message}");
                }
            }));

            System.Diagnostics.Debug.WriteLine($"✅ EditWindow 加载完成");
            System.Diagnostics.Debug.WriteLine($"   CurrentScreen: {_viewModel?.CurrentScreen?.Name}");
            System.Diagnostics.Debug.WriteLine($"   Widgets 数量: {_viewModel?.CurrentScreen?.Widgets?.Count}");
        }

        private void AiBallToggle_Unchecked(object sender, RoutedEventArgs e)
            => AiFabBtn.Visibility = Visibility.Collapsed;

        private void ClampAiFabToView()
        {
            if (AiFabBtn == null || AiFabBtn.HorizontalAlignment != HorizontalAlignment.Left) return;   // 未拖动过（锚定右下角）不处理
            var w = Math.Max(0, DockManager.ActualWidth - AiFabBtn.ActualWidth);
            var h = Math.Max(0, DockManager.ActualHeight - AiFabBtn.ActualHeight);
            var m = AiFabBtn.Margin;
            AiFabBtn.Margin = new Thickness(Math.Min(Math.Max(0, m.Left), w), Math.Min(Math.Max(0, m.Top), h), 0, 0);
        }

        private void EditWindow_SizeChanged(object sender, SizeChangedEventArgs e) => ClampAiFabToView();

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

        private void EditWindow_Closed(object? sender, EventArgs e)
        {
            DeviceConnectionService.SessionChanged -= OnConnectionSessionChanged;   // K-4：状态栏退订（防闭包持有已关闭窗口）
            _viewModel.CommandService.CommandExecuted -= OnOutputCommandExecuted;   // L-B4：输出窗口日志订阅退订
            _viewModel.DevicePanelVM.OutputRequested -= OnDevicePanelOutput;   // L-B1：设备面板输出订阅退订
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
            try { WorldMapControl?.Dispose(); } catch (Exception ex) { Log.Warning(ex, "WorldMapControl 释放异常（Mapsui MapControl，关闭路径不阻塞）"); }   // 释放 Mapsui MapControl（HttpClient/瓦片缓存）
            // W-3a：错误气泡清理（停定时器防闭包持有窗口、关闭残留 Popup）
            _errorBubbleTimer?.Stop();
            if (_errorBubble != null) _errorBubble.IsOpen = false;
            _videoRowFrameTimer?.Stop();   // U 三修（2026-09-06 reviewer 🟡Y1）：视频预览锁帧定时器关闭必停（仓内 timer 清理纪律）
            _videoRowMedia?.Close();       // 释放预览 MediaElement 播放资源
        }

        private void CloseCurrentProject_Click(object sender, RoutedEventArgs e)
        {
            if (!TryCloseProject(false))
                return;

            _currentProject?.ClearDirty();   // K-1d：关闭当前工程清脏

            WelComeWindow welcome = new WelComeWindow();
            welcome.Show();

            _skipClosingCheck = true;
            this.Close();
        }

        private bool TryCloseProject(bool isAppClosing)
        {
            // K-1d：读模型级 IsDirty（唯一真源）
            if (string.IsNullOrEmpty(_currentProject.ProjectFilePath) || _currentProject.IsDirty != true)
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

    }
}
