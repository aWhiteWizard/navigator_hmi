# 循环 Z 执行书（2026-08-14）

> Plan 环节产物：用户确认"Act OK + 无新需求"后定稿（循环 Y 遗留 4 项中 frontmatter 统一 date 已在 Act 实施完成，剩余 3 项代码）。Do 阶段以本执行书为准；Check 完成后直接删除。

## 摘要

循环 Y Check 验收（续53/54）遗留 3 项代码任务：① 矩形端点手柄改 2 对角点（用户需求）；② 跨画面后矩形表格不同步 + 直接场景首帧不刷（BUG）；③ 多边形拖动表格实时同步（用户需求，与 ② 同根因同修复）。

## 根因（Do 前定位）

| # | 问题 | 根因 |
|---|------|------|
| ① | 矩形画布 4 角手柄（与表格 2 对角点不一致） | W-4b 只把表格改为 2 对角点（左上+右下），`BuildHandles` 仍生成 4 个 Vertex 手柄——画布/表格显示错位；用户要求画布也只需左上+右下 |
| ② | 跨画面后矩形表格不再实时同步 + 直接场景首帧不刷 | W-4c 拖拽节流 `RefreshRectanglePointRowsDisplay()` 内部以 **`_selectedWidget is RectangleWidget` 门控**（PropertyViewModel.RefreshRectanglePointRows）：`_selectedWidget` 更新链路有陈旧窗口（SelectedWidget setter 短路 `_selectedWidget != value`、静默选中不触发 WidgetSelected、HandleWidgetClick 对已选中控件不动作）→ 跨画面/静默选中后节流触发时门控不过 → 表格被 Clear 不重建；首帧不刷：节流时间戳跨拖拽会话残留，新拖拽首帧可能被 100ms 节流吞掉 |
| ③ | 多边形拖动表格不实时（松开才变） | DragDelta 节流只刷矩形表（`RefreshRectanglePointRowsDisplay`），多边形表只在 DragCompleted 刷新（C12-12）——与矩形体验不一致 |

## 修改点

1. **① 矩形 2 对角手柄**（`src/editwindow/Common/EndpointHandleAdorner.cs`）：
   - `BuildHandles` 的 `case RectangleWidget:` 从 `for i in 0..3` 改为只 `AddHandle(HandleKind.Vertex, 0)`（左上）+ `AddHandle(HandleKind.Vertex, 2)`（右下）。
   - 现有 Vertex 锚定对角逻辑（case 0 锚右下 / case 2 锚左上）与 `CursorForKind`（0/2→SizeNWSE）天然兼容，无需改；`ArrangeOverride` 的 Vertex 映射已含 0/2。
2. **② ③ 拖拽节流重构**（`SelectorHelper.cs` / `EndpointHandleAdorner.cs` / `EditWindow.xaml.cs`）：
   - `SelectorHelper.ResizeDragDelta` 类型 `Action?` → **`Action<Widget>?`**（携带被拖拽 widget）。
   - `EndpointHandleAdorner.Thumb_DragDelta` 末尾 `SelectorHelper.ResizeDragDelta?.Invoke()` → `?.Invoke(_widget)`。
   - `EditWindow` 回调改签名：按 widget 类型刷对应表格——`PolygonWidget → RefreshPolygonPointRowsDisplay()`、`RectangleWidget → RefreshRectanglePointRowsDisplay()`（摆脱 `_selectedWidget` 门控）；100ms 节流保留。
   - **首帧必刷**：`DragStarted`（ResizeDragStarted 回调或新增）重置 `_lastResizeDeltaRefresh = DateTime.MinValue`，保证每次拖拽首帧即刷新。

## 边界

- ② 只改节流刷新链路，不动 `_selectedWidget` 选择逻辑本身（避免影响属性面板既有行为）。
- ① 只改手柄生成，不改模型/表格/锚定算法。
- ③ 多边形实时刷新用 `RefreshPolygonPointRowsDisplay()`（行 VM 共享 PointD 引用，RefreshValues 即可实时），无需重建。
- 节流 100ms 保持；首帧重置时间戳不影响节流语义。

## 验收

1. 编译 0 错误；`dotnet test` 全绿（286+）。
2. ① GUI：选中矩形只显示**左上+右下** 2 个端点手柄，拖拽 2 角正常改形（向内缩/向外扩，min 10 防翻转），光标对角斜向。
3. ② GUI：画面一拖多边形 → 回画面二拖矩形，**表格实时同步**；直接到画面二拖矩形**首帧即刷**。
4. ③ GUI：拖动多边形 → 多边形端点表格**实时同步**（与矩形体验一致）。

## 批次纪律

编译 + 测试 + reviewer 审查 + 提交（message 无 BOM，不 push）；一次 Do 完成 3 项、一次审查；Check 完成后删除本执行书。
