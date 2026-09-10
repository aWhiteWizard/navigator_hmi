using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Serilog;

namespace NavigatorHMI.ViewModels
{
    public partial class PropertyViewModel : INotifyPropertyChanged
    {

        #region 原 未分区块（72daf0e^ 第 76-1135 行，×30 段）

        /// <summary>刷新事件栏（世界地图画面选中时调用）：地图级事件仅 onClick（点击切换画面，P2 属性面板入口）。
        /// 事件存 WorldMapConfig.Events；WorldMap 未初始化（未放过点）时先 ??= 初始化再读，防临时列表不落库。</summary>
        public void RefreshWorldMapEventBar()
        {
            EventRows.Clear();
            var project = Project;
            if (project == null) { EventBarVisible = false; return; }
            project.WorldMap ??= new WorldMapConfig();
            EventBarVisible = true;
            var existing = project.WorldMap.Events.FirstOrDefault(e => e.Type == EventType.onClick);
            EventRows.Add(new EventRowVM(EventType.onClick) { ActionCount = existing?.Actions.Count ?? 0 });
            OnPropertyChanged(nameof(EventRows));
        }

        private bool _worldMapShowGlobalOverlay;
        /// <summary>世界地图画面是否叠加显示全局画面控件（持久化到 WorldMapConfig.ShowGlobalOverlay，仅 WorldMap 画面可编辑）。</summary>
        public bool WorldMapShowGlobalOverlay
        {
            get => _worldMapShowGlobalOverlay;
            set
            {
                if (_worldMapShowGlobalOverlay != value)
                {
                    _worldMapShowGlobalOverlay = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel)
                    {
                        // 工程级配置（非画面 Widgets），不推撤销快照；先写模型再标脏（标脏不依赖刷新链路），最后刷新虚影层
                        // 懒创建 WorldMapConfig：新建/历史工程均未初始化 HMIProject.WorldMap（全项目无实例化点），
                        // 首次勾选时创建并持久化，否则勾选仅停留在 UI 临时状态，重开属性栏即丢失
                        if (Project != null)
                        {
                            Project.WorldMap ??= new WorldMapConfig();
                            Project.WorldMap.ShowGlobalOverlay = value;
                        }
                        DirtyRequested?.Invoke();   // 标脏回调约定不抛异常（MarkProjectDirty 仅改标题），保持 try 外保证必达
                        try { OverlayChanged?.Invoke(); }   // 虚影刷新失败不阻断工程标脏（防关闭静默丢失）
                        catch (Exception ex) { Log.Warning(ex, "虚影刷新异常（仅影响虚影层显示，标脏已发生）"); }
                    }
                }
            }
        }

        /// <summary>当前选中是否为世界地图画面（属性面板据此显示「全局叠加」勾选入口）。</summary>
        public bool IsWorldMapScreen => _selectedScreen?.Type == ScreenType.WorldMap;

        private bool _includeWorldMapOnDevice = true;

        /// <summary>
        /// P-3（2026-09-02）：设备端是否显示世界地图画面（世界地图画面属性面板 checkbox，默认勾选）。
        /// **只影响设备端**（编译产物过滤 WorldMap Screen），组态软件编辑态始终显示/可编辑（用户语义定稿）。
        /// 工程级配置（非画面 Widgets），不推撤销快照；写模型 + 标脏（WorldMapShowGlobalOverlay 同模式）。
        /// </summary>
        public bool IncludeWorldMapOnDevice
        {
            get => _includeWorldMapOnDevice;
            set
            {
                if (_includeWorldMapOnDevice != value)
                {
                    _includeWorldMapOnDevice = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel && Project != null)
                    {
                        Project.IncludeWorldMapOnDevice = value;   // 模型 setter 自带 MarkDirty
                        DirtyRequested?.Invoke();
                    }
                }
            }
        }

        // ── 世界地图作业点/作业范围点（P4：DataGrid 表格 + 行内编辑；两列互斥；末行输入自动补行）──

        /// <summary>作业点表格行（DataGrid 数据源；新增行提交时由 CollectionChanged 挂入模型）。</summary>
        public ObservableCollection<WorkPointRowVM> WorkPointRows { get; } = new();

        /// <summary>作业范围点表格行。</summary>
        public ObservableCollection<WorkRangeRowVM> WorkRangeRows { get; } = new();

        private WorkPointRowVM? _selectedWorkPointRow;
        /// <summary>作业点表格选中行（DataGrid SelectedItem 双向绑定）。V-2a：选中变化 → overlay 刷新（图上选中态联动）。</summary>
        public WorkPointRowVM? SelectedWorkPointRow { get => _selectedWorkPointRow; set { if (_selectedWorkPointRow == value) return; _selectedWorkPointRow = value; OnPropertyChanged(); WorldMapPointsChanged?.Invoke(); } }

        private WorkRangeRowVM? _selectedWorkRangeRow;
        /// <summary>作业范围表格选中行。V-2a：选中变化 → overlay 刷新（图上选中态联动）。</summary>
        public WorkRangeRowVM? SelectedWorkRangeRow { get => _selectedWorkRangeRow; set { if (_selectedWorkRangeRow == value) return; _selectedWorkRangeRow = value; OnPropertyChanged(); WorldMapPointsChanged?.Invoke(); } }

        /// <summary>作业点/范围点变更 → overlay 刷新回调（EditWindow 注入 → UpdateAllGeoWidgets）。</summary>
        public Action? WorldMapPointsChanged { get; set; }

        /// <summary>C12-2：WorldMap 数据修改前快照回调（EditWindow 注入 → PushWorldMapUndoSnapshot；表格增删改/行编辑专用——
        /// 原 BeforeModify 绑 Widgets 栈，快照不含 WorldMap 数据，撤销无效）。</summary>
        public Action? WorldMapBeforeModify { get; set; }

        /// <summary>从工程 WorldMapConfig 同步作业点/作业范围点表格（选中画面时装载）。</summary>
        private void RefreshWorldMapWorkPoints()
        {
            RefreshGpsTagNames();   // X-1a：表格重建即同步 GPS 绑定选项（不依赖属性面板 RefreshBindableTags 链路，世界地图场景下拉不空白）
            WorkPointRows.CollectionChanged -= WorkPointRows_CollectionChanged;
            WorkPointRows.Clear();
            if (Project?.WorldMap != null)
                foreach (var wp in Project.WorldMap.WorkPoints)
                    WorkPointRows.Add(new WorkPointRowVM(wp, OnWorkPointRowChanged, IsGpsTag, WorldMapBeforeModify) { GpsTagNames = GpsTagNames });   // W-2b：绑定变量下拉选项
            WorkPointRows.CollectionChanged += WorkPointRows_CollectionChanged;
            RefreshWorkMapRangePoints();
            ValidateDuplicateNames();   // V-4a：表格重载（切画面/撤销/重做）后重名校验
        }

        /// <summary>从工程 WorldMapConfig 同步作业点/作业范围点表格（选中画面装载；撤销/重做后由 EditWindow 显式调用）。</summary>
        public void RefreshWorkPointRows() => RefreshWorldMapWorkPoints();

        /// <summary>C12-6/8：地图加点/清除后显式同步作业范围点表格（原刷新只挂在选中画面/切画面路径 → 地图操作后表格不实时）。</summary>
        public void RefreshWorkRangeRows() => RefreshWorkMapRangePoints();

        /// <summary>绑定变量必须存在且为 GPS 类型（对齐批 4 添加校验）。</summary>
        private bool IsGpsTag(string name)
            => Project?.Tags.Any(t => t.Name == name && t.DataType == TagDataType.GPS) == true;

        /// <summary>行编辑/增删 → 标脏 + overlay 刷新。</summary>
        private void OnWorkPointRowChanged()
        {
            DirtyRequested?.Invoke();
            WorldMapPointsChanged?.Invoke();
            ValidateDuplicateNames();   // V-4a：名称重名校验（输入/增删后）
        }

        /// <summary>DataGrid 新行 Add（双击 placeholder 进入编辑时 DataGrid 同步 Add 空行，此刻用户尚未输入——C12-1）：
        /// 空行不移除、不挂模型，是否保留由 RowEditEnding 提交时判定（CommitNewWorkPointRow）；仅代码路径显式 Add 的非空行在此挂入模型。
        /// （2026-08-11 修复的 CollectionChanged 重入崩溃由 RemoveRowDeferred 延迟移除兜底。）</summary>
        private void WorkPointRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;
            foreach (WorkPointRowVM row in e.NewItems)
            {
                if (Project == null) continue;
                row.GpsTagNames = GpsTagNames;   // W-2b：DataGrid 自动新建的占位行也注入下拉选项
                // 双击 placeholder 触发 Add 时行必空（DataGrid 先 Add 后编辑）→ 移交提交判定，不在此移除
                if (string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.LngLat) && string.IsNullOrWhiteSpace(row.BoundTag))
                    continue;
                Project.WorldMap ??= new WorldMapConfig();
                if (!Project.WorldMap.WorkPoints.Contains(row.Model))
                {
                    WorldMapBeforeModify?.Invoke();   // C12-2：WorldMap 数据快照（原 BeforeModify 绑 Widgets 栈，快照不含 WorldMap）
                    Project.WorldMap.WorkPoints.Add(row.Model);
                    OnWorkPointRowChanged();
                }
            }
        }

        /// <summary>DataGrid 新行提交（RowEditEnding Commit，C12-1）：有输入 → 保留行并挂入模型（DataGrid 自动另起新空白行）；
        /// 无输入 → 移除该行（延迟移除防 CheckReentrancy）。空行判定从 CollectionChanged Add 移到此处——Add 时行必空，判定必然误杀。</summary>
        public void CommitNewWorkPointRow(WorkPointRowVM row)
        {
            if (row == null) return;
            bool empty = string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.LngLat) && string.IsNullOrWhiteSpace(row.BoundTag);
            if (empty)
            {
                RemoveRowDeferred(WorkPointRows, row);
                return;
            }
            if (Project == null) return;
            Project.WorldMap ??= new WorldMapConfig();
            if (!Project.WorldMap.WorkPoints.Contains(row.Model))
            {
                WorldMapBeforeModify?.Invoke();   // C12-2：WorldMap 数据快照
                Project.WorldMap.WorkPoints.Add(row.Model);
                OnWorkPointRowChanged();
            }
        }

        /// <summary>DataGrid 新行 Add（作业范围）：空行不移除（移交提交判定）；仅代码路径 Add 的非空行挂入模型。</summary>
        private void WorkRangeRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;
            foreach (WorkRangeRowVM row in e.NewItems)
            {
                if (Project == null) continue;
                row.GpsTagNames = GpsTagNames;   // W-2b：DataGrid 自动新建的占位行也注入下拉选项
                if (string.IsNullOrWhiteSpace(row.LngLat) && string.IsNullOrWhiteSpace(row.BoundTag))
                    continue;
                Project.WorldMap ??= new WorldMapConfig();
                if (!Project.WorldMap.WorkRangePoints.Contains(row.Model))
                {
                    WorldMapBeforeModify?.Invoke();   // C12-2：WorldMap 数据快照
                    Project.WorldMap.WorkRangePoints.Add(row.Model);
                    OnWorkPointRowChanged();
                }
            }
        }

        /// <summary>DataGrid 新行提交（作业范围，C12-1 同 WorkPoint）：有输入 → 挂入模型；无输入 → 移除。</summary>
        public void CommitNewWorkRangeRow(WorkRangeRowVM row)
        {
            if (row == null) return;
            bool empty = string.IsNullOrWhiteSpace(row.LngLat) && string.IsNullOrWhiteSpace(row.BoundTag);
            if (empty)
            {
                RemoveRowDeferred(WorkRangeRows, row);
                return;
            }
            if (Project == null) return;
            Project.WorldMap ??= new WorldMapConfig();
            if (!Project.WorldMap.WorkRangePoints.Contains(row.Model))
            {
                WorldMapBeforeModify?.Invoke();   // C12-2：WorldMap 数据快照
                Project.WorldMap.WorkRangePoints.Add(row.Model);
                OnWorkPointRowChanged();
            }
        }

        /// <summary>删除作业点行（按钮；修改前快照可撤销）。</summary>
        public void DeleteWorkPointRow(WorkPointRowVM row)
        {
            if (row?.Model == null || Project?.WorldMap == null) return;
            WorldMapBeforeModify?.Invoke();   // C12-2：WorldMap 数据快照（删除行可撤销）
            Project.WorldMap.WorkPoints.Remove(row.Model);
            WorkPointRows.Remove(row);
            OnWorkPointRowChanged();
        }

        /// <summary>删除作业范围点行（按钮；修改前快照可撤销）。</summary>
        public void DeleteWorkRangeRow(WorkRangeRowVM row)
        {
            if (row?.Model == null || Project?.WorldMap == null) return;
            WorldMapBeforeModify?.Invoke();   // C12-2：WorldMap 数据快照（删除行可撤销）
            Project.WorldMap.WorkRangePoints.Remove(row.Model);
            WorkRangeRows.Remove(row);
            OnWorkPointRowChanged();
        }

        /// <summary>全局叠加配置变化回调（EditWindow 注入 → 局部刷新虚影层，不动主层与选中）。</summary>
        public Action? OverlayChanged { get; set; }
        /// <summary>锁定预览：勾选后设计态地图禁平移缩放（左键/滚轮短路）；右键菜单仍可弹（配置不受锁）。
        /// 运行时点击切换画面由 FW 端执行，设计态不模拟（2026-08-11 需求变更 B）。</summary>
        public bool WorldMapViewLocked
        {
            get => _worldMapViewLocked;
            set
            {
                if (_worldMapViewLocked != value)
                {
                    _worldMapViewLocked = value;
                    if (!_syncingFromModel && Project != null)
                    {
                        Project.WorldMap ??= new WorldMapConfig();   // 懒创建：WorldMap 未初始化（未放过点）时勾选也要落库（Check 5-1 根因）
                        Project.WorldMap.ViewLocked = value;
                        DirtyRequested?.Invoke();
                        WorldMapViewLockChanged?.Invoke();   // EditWindow 注入 → 切换地图交互
                    }
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>锁定预览勾选变化回调（EditWindow 注入 → 地图禁交互/恢复）。</summary>
        public Action? WorldMapViewLockChanged { get; set; }

        /// <summary>视口自适应请求回调（EditWindow 注入 → TryFitWorldMapViewport）。</summary>
        public Action? WorldMapZoomToBoxRequested { get; set; }

        /// <summary>按钮：视口自适应（ZoomToBox 按作业点包围盒框选）。</summary>
        public void WorldMapZoomToBox() => WorldMapZoomToBoxRequested?.Invoke();
        #endregion

        #region 原 绑定变量（72daf0e^ 第 1471-1482 行，×2 段）

        /// <summary>W-2b：作业点/范围点绑定变量下拉选项 = 「无」+ 工程全部 GPS 变量（ObservableCollection 共享引用，行 VM 直接绑定；变量增删经 RefreshBindableTags 联动）。</summary>
        public ObservableCollection<string> GpsTagNames { get; } = new();

        private void RefreshGpsTagNames()
        {
            GpsTagNames.Clear();
            GpsTagNames.Add("无");
            if (Project == null) return;
            foreach (var t in Project.Tags)
                if (t.DataType == TagDataType.GPS) GpsTagNames.Add(t.Name);
        }
        #endregion

        // AB-6 复审 🟡-3（2026-09-11）：CollectionChanged 重入守卫 自 PropertyViewModel.cs 归位（内容零改动）
        private void ValidateDuplicateNames()
        {
            if (Project?.WorldMap == null) return;
            var dupNames = WorkPointRows
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var r in WorkPointRows) r.IsDuplicateName = dupNames.Contains(r.Name);
        }

        private static void RemoveRowDeferred<T>(ObservableCollection<T> rows, T row)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (rows.Contains(row)) rows.Remove(row);
                });
            }
            else
            {
                // 无 WPF Application（如单测环境）：集合修改不会伴随 DataGrid 重入，直接移除安全
                if (rows.Contains(row)) rows.Remove(row);
            }
        }

    }
}