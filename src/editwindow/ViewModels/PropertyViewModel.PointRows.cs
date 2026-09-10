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

        // AB-6 复审 🟡-2（2026-09-11）：本文件的点行删除复用 PropertyViewModel.WorldMap.cs 的 RemoveRowDeferred
        // （跨文件共享的 CollectionChanged 重入守卫——作业点/范围点行与该守卫同文件，多边形/矩形点行为其第二消费方；
        //  改动该守卫须同时核对两处消费者）
#region 原 未分区块（72daf0e^ 第 600-1941 行；17 个成员块）
        /// <summary>P7：多边形顶点可编辑表格行（画布绝对坐标；行内编辑/末行补行/删除禁 &lt;3 点）。</summary>
        public ObservableCollection<PolygonPointRowVM> PolygonPointRows { get; } = new();

        /// <summary>V-6b：矩形 4 顶点表格行（画布绝对坐标；固定 4 行，无增删——仿 PolygonPointRows，行内编辑反推 X/Y/W/H）。</summary>
        public ObservableCollection<PolygonPointRowVM> RectanglePointRows { get; } = new();

        /// <summary>重建多边形顶点表格（选中多边形时调用）：按模型 Points 重建行（PointD 无 INPC，行直写模型 + 重赋值触发渲染刷新）。</summary>
        public void RefreshPolygonPointRows()
        {
            PolygonPointRows.CollectionChanged -= PolygonPointRows_CollectionChanged;
            PolygonPointRows.Clear();
            if (_selectedWidget is PolygonWidget pg)
                foreach (var pt in pg.Points)
                    PolygonPointRows.Add(new PolygonPointRowVM(pt, OnPolygonPointRowChanged, BeforeModify));
            PolygonPointRows.CollectionChanged += PolygonPointRows_CollectionChanged;
            ReindexPolygonPointRows();
        }

        /// <summary>DataGrid 新行 Add：未编辑空行不移除、不挂模型（双击 placeholder 时行尚未输入——C12-1 同源），
        /// 是否保留由 RowEditEnding 提交时判定（CommitNewPolygonPointRow）；仅代码路径 Add 的已编辑行在此挂入模型。</summary>
        private void PolygonPointRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;
            foreach (PolygonPointRowVM row in e.NewItems)
            {
                if (_selectedWidget is not PolygonWidget pg) continue;
                if (!row.Edited) continue;   // 未编辑 → 交提交判定
                if (!pg.Points.Contains(row.Model))
                {
                    BeforeModify?.Invoke();   // 修改前快照（新增可撤销）
                    pg.Points.Add(row.Model);
                    OnPolygonPointRowChanged();
                }
            }
            ReindexPolygonPointRows();
        }

        /// <summary>DataGrid 新行提交（RowEditEnding Commit，C12-1 同源）：已编辑（输入过坐标）→ 挂入模型；未编辑 → 移除空行。</summary>
        public void CommitNewPolygonPointRow(PolygonPointRowVM row)
        {
            if (row == null) return;
            if (!row.Edited)
            {
                RemoveRowDeferred(PolygonPointRows, row);
                return;
            }
            if (_selectedWidget is not PolygonWidget pg) return;
            if (!pg.Points.Contains(row.Model))
            {
                BeforeModify?.Invoke();   // 修改前快照（新增可撤销）
                pg.Points.Add(row.Model);
                OnPolygonPointRowChanged();
            }
            ReindexPolygonPointRows();
        }

        /// <summary>删除顶点行（按钮）：删除后不足 3 点拒绝（封闭图形至少 3 顶点）。</summary>
        public void DeletePolygonPointRow(PolygonPointRowVM row)
        {
            if (_selectedWidget is not PolygonWidget pg || pg.Points.Count <= 3 || row.Model == null) return;
            BeforeModify?.Invoke();
            pg.Points.Remove(row.Model);
            PolygonPointRows.Remove(row);
            OnPolygonPointRowChanged();
            ReindexPolygonPointRows();
        }

        /// <summary>顶点变化（行内编辑/增删）→ 包围盒重算（X/Y/W/H 派生态）+ 重赋值 Points 触发渲染刷新 + 标脏。</summary>
        private void OnPolygonPointRowChanged()
        {
            if (_selectedWidget is not PolygonWidget pg) return;
            if (pg.Points.Count > 0)
            {
                double minX = pg.Points.Min(p => p.X), minY = pg.Points.Min(p => p.Y);
                double maxX = pg.Points.Max(p => p.X), maxY = pg.Points.Max(p => p.Y);
                pg.X = minX; pg.Y = minY;
                pg.Width = Math.Max(maxX - minX, 1); pg.Height = Math.Max(maxY - minY, 1);
            }
            pg.Points = new List<PointD>(pg.Points);   // PointD 无 INPC → 重赋值触发渲染刷新
            OnPropertyChanged(nameof(IsPolygonWidget));
            DirtyRequested?.Invoke();   // 顶点是 POCO 内 List，不在脏订阅范围 → 显式标脏
        }

        /// <summary>顶点表格行号（增删后重排）。</summary>
        private void ReindexPolygonPointRows()
        {
            for (int i = 0; i < PolygonPointRows.Count; i++) PolygonPointRows[i].Index = i + 1;
        }

        /// <summary>W-4b/Z-2：重建矩形 2 对角线顶点表格——左上 + 右下（用户拍板：4 顶点多余，2 对角点即确定矩形），画布绝对坐标。
        /// Z-2：可选传被拖矩形——拖拽实时刷新路径传被拖对象，摆脱 `_selectedWidget` 门控（静默选中/跨画面下其会陈旧导致 Clear 不重建/显示错误矩形）；不传时回退 `_selectedWidget`（选中/编辑路径）。</summary>
        public void RefreshRectanglePointRows(RectangleWidget? rect = null)
        {
            RectanglePointRows.CollectionChanged -= RectanglePointRows_CollectionChanged;
            RectanglePointRows.Clear();
            var r = rect ?? (_selectedWidget as RectangleWidget);
            if (r != null)
            {
                RectanglePointRows.Add(new PolygonPointRowVM(new PointD(r.X, r.Y), OnRectanglePointRowChanged, BeforeModify));                       // 左上
                RectanglePointRows.Add(new PolygonPointRowVM(new PointD(r.X + r.Width, r.Y + r.Height), OnRectanglePointRowChanged, BeforeModify));   // 右下
            }
            RectanglePointRows.CollectionChanged += RectanglePointRows_CollectionChanged;
            ReindexRectanglePointRows();
        }

        /// <summary>V-6b：矩形表格固定 2 行、无增删（CanUserAddRows=False），CollectionChanged 仅对称退订/订阅，无需处理。</summary>
        private void RectanglePointRows_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { }

        /// <summary>W-4b：矩形顶点行变化（行内编辑）→ 从 2 对角点反推 X/Y/W/H（左上=min、右下=max，防翻转）。</summary>
        private void OnRectanglePointRowChanged()
        {
            if (_selectedWidget is not RectangleWidget r) return;
            if (RectanglePointRows.Count == 2 && RectanglePointRows.All(x => x.Model != null))
            {
                var tl = RectanglePointRows[0].Model!;   // 左上
                var br = RectanglePointRows[1].Model!;   // 右下
                double minX = Math.Min(tl.X, br.X), minY = Math.Min(tl.Y, br.Y);
                double maxX = Math.Max(tl.X, br.X), maxY = Math.Max(tl.Y, br.Y);
                r.X = minX; r.Y = minY;
                r.Width = Math.Max(maxX - minX, 1); r.Height = Math.Max(maxY - minY, 1);
            }
            OnPropertyChanged(nameof(IsRectangleWidget));
            DirtyRequested?.Invoke();
        }

        /// <summary>W-4b：矩形表格行号（固定 2 行）。</summary>
        private void ReindexRectanglePointRows()
        {
            for (int i = 0; i < RectanglePointRows.Count; i++) RectanglePointRows[i].Index = i + 1;
        }

        /// <summary>V-6b/AA-2：拖拽/缩放结束后刷新矩形表格——矩形行 Model 是独立 PointD 拷贝（非共享引用，与 Polygon 不同），
        /// 仅发通知值不变 → 必须重建（从矩形取 2 对角点，天然反映拖拽结果）。AA-2：结束兜底传被拖对象（rect 非空），与拖拽过程一致。</summary>
        public void RefreshRectanglePointRowsDisplay(RectangleWidget? rect = null) => RefreshRectanglePointRows(rect);

        /// <summary>C12-12：拖拽/缩放结束后刷新顶点表格显示值（PointD 无 INPC、行 VM 直读模型——外部改动模型后需显式通知；
        /// 原表格只在选中时 RefreshPolygonPointRows 重建，拖拽/缩放后显示陈旧）。</summary>
        public void RefreshPolygonPointRowsDisplay()
        {
            foreach (var row in PolygonPointRows) row.RefreshValues();
        }
        /// <summary>多边形填充色。</summary>
        public string PolygonFillColor { get => _polygonFillColor; set { if (_polygonFillColor != value) { _polygonFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is PolygonWidget pg) pg.FillColor = value; } } }
        private string _polygonStrokeColor = "#1E90FF";
        /// <summary>多边形描边色。</summary>
        public string PolygonStrokeColor { get => _polygonStrokeColor; set { if (_polygonStrokeColor != value) { _polygonStrokeColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is PolygonWidget pg) pg.StrokeColor = value; } } }
        private double _polygonStrokeThickness = 1.5;
        /// <summary>多边形描边粗细。</summary>
        public double PolygonStrokeThickness { get => _polygonStrokeThickness; set { if (_polygonStrokeThickness != value) { _polygonStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is PolygonWidget pg) pg.StrokeThickness = value; } } }
        #endregion

    }
}