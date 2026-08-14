# 循环 AA 执行书（2026-08-14）

> Plan 环节产物：用户发起"进入下一轮 plan"，预期本轮结束后无下一轮（bug 收尾里程碑）。Do 阶段以本执行书为准；Check 完成后直接删除。

## 摘要

修复剩余 2 项 bug（用户判断本轮结束后无下一轮）：① **整体拖动（move）端点表格不刷新**——矩形坐标拖前拖后不更新、多边形松开才变（4_bugs 新条目 `wpf-widget-move-no-point-table-refresh.md`，待 Do 验证）；② **DragCompleted 兜底闭环**——跨画面陈旧选中下松开后表格兜底仍走 `_selectedWidget`（审查建议 a）。修复后回写 4_bugs 新条目为实测根因。**计划顺序：AA-1 → AA-2 → Skill 任务（最后做，2026-08-14 用户定）**——Skill 任务 = 知识库提炼 6 个 dsh skill（见 PROJECT.md「Skill 任务 Plan」段），排在 bug 修复之后最后执行。

## 根因（Do 前定位）

| # | 问题 | 根因 |
|---|------|------|
| ① | 整体拖动（move）端点表格不刷新 | `WidgetDragBehavior.OnMouseMove` 直接改 `w.X/w.Y`（+ 多边形 `TranslatePoints`），端点表格刷新只在**拖拽结束**（`OnMouseLeftButtonUp → _onDragEndCallback`，且仅刷多边形表）；**矩形端点表在整体拖动路径无刷新挂点**（RefreshRectanglePointRowsDisplay 只挂在 EndpointHandleAdorner 的 DragDelta/DragCompleted） |
| ② | 松开后表格兜底可能仍陈旧 | `_resizeDragCompletedCallback` 无参调用 `RefreshRectanglePointRowsDisplay()`/`RefreshPolygonPointRowsDisplay()`——跨画面陈旧选中（`_selectedWidget` 非被拖对象）时矩形表会被 Clear 不重建 |

## 修改点

1. **① 整体拖动实时刷新**：
   - `WidgetDragBehavior` 新增拖动过程回调 `Action<Widget>? _onDragMoveCallback`（携带被拖 widget，OnMouseMove 每帧调用；注入方节流）。
   - `EditWindow` 注入：100ms 节流（复用 `_resizeDragDeltaCallback` 模式）→ 按类型刷表（`RectangleWidget → RefreshRectanglePointRowsDisplay(rect)` / `PolygonWidget → RefreshPolygonPointRowsDisplay()`）；拖拽结束回调顺带刷矩形表（现状只刷多边形）。
   - 携带的 widget 取 `_draggingWidgets` 当前项（单选中唯一；多选时属性面板为批量模式，端点表格不显示）。
2. **② DragCompleted 闭环**：`SelectorHelper.ResizeDragCompleted` 改 `Action<Widget>`（与 ResizeDragDelta 同模式），`EndpointHandleAdorner` DragCompleted 携带 `_widget`，EditWindow 兜底按类型刷表（传被拖对象，不再依赖 `_selectedWidget`）。

## 边界

- ① 只给 WidgetDragBehavior 增 move 过程刷新，不改拖拽位移/多选逻辑本身。
- ② 只改 ResizeDragCompleted 签名与调用链（4 处：定义/EndpointHandleAdorner/EditWindow 赋值/清理），与 ResizeDragDelta 同型同构。
- 节流 100ms 保持；不做画布外钳制等额外行为。

## 验收

1. 编译 0 错误；`dotnet test` 全绿（286+）。
2. ① GUI：**整体拖动矩形** → 端点表格坐标**实时刷新**；**整体拖动多边形** → 端点表格**实时刷新**（不再松开才变）；单点拖动回归不变。
3. ② GUI：跨画面场景（先拖多边形再回画面二拖矩形）松开后表格仍正确（兜底不空）。
4. 4_bugs `wpf-widget-move-no-point-table-refresh.md` 从"待 Do 验证"回写为实测根因（Act 阶段）。

## 批次纪律

编译 + 测试 + reviewer 审查 + 提交（message 无 BOM）；**Do 第一个动作：push 循环 Z 积压 4 笔**（push 新规：有下一轮 → 下一轮 Do 第一个动作 push）；一次 Do 完成 2 项、一次审查；Check 后删除执行书。用户预期：本轮结束后无下一轮（Act 结束即 push 本轮提交）。
