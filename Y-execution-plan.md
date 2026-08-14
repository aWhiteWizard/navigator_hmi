# 循环 Y 执行书（2026-08-14）

> Plan 环节产物：用户裁决"欠账立即修完 + 开始执行循环 Y"后定稿。Do 阶段以本执行书为准；Do 完成后归档/删除。

## 摘要

循环 X Check 验收（续50~52）遗留 5 项待修 + 审查遗留 1 项（frontmatter 规范，待用户拍板格式，不阻塞代码 Do）。本轮 Do 修 5 项代码问题，全部集中在 WPF 图形端点编辑与变量表格刷新。

## 根因（Do 前定位）

| # | 问题 | 根因 |
|---|------|------|
| ① | 变量表格基准值不刷新 | 拖拽绑变量点直接写 `tag.BaseValue`（无 INPC），两个分支只刷作业点/范围点表格，漏 `_variableManagerVM?.Refresh()`（undo/redo 路径已有 `RefreshAfterTagBaseValueChange` 可复用） |
| ② | 矩形端点拖不动 | `ApplyRectCorners` 的 min/max 防翻转钳制有死区：向内拖角时被拖角仍在其余 3 角外接框内 → min/max 不变 → 矩形不动。其余 4 类无此钳制（直线直接 +=，圆/椭圆用 Abs 半径，多边形直接改点）——唯独矩形"拖不动" |
| ③ | 矩形拖动表格不实时 | 依赖②（DragDelta 不生效则 W-4c 节流无从触发）；② 修复后自动恢复 |
| ④ | 端点光标未按方位分派 | `AddHandle` 统一 `SizeNWSE`（圆心除外），未按角/边/圆心分派（对照 ResizeAdorner 8 方向） |
| ⑤ | 直线多余外圈虚蓝框 | `OnRender` 对所有 5 类统一画虚线框，Line 是 2 端点线性控件，框无意义 |

## 修改点

1. `src/editwindow/Views/EditWindow.xaml.cs`（①）：拖拽绑变量点两个分支（MapWorkPoint/WorkRangePoint）写回 BaseValue 后补 `_variableManagerVM?.Refresh()`。
2. `src/editwindow/Common/EndpointHandleAdorner.cs`（②③④⑤）：
   - Vertex 分支改**锚定对角算法**（仿 ResizeAdorner 四角：`newW = Max(10, w∓dx)`、`newH = Max(10, h∓dy)`、`newX = x + w - newW` 等），删除 `ApplyRectCorners`。
   - 新增 `CursorForKind`：Vertex 0/2→SizeNWSE、1/3→SizeNESW；QuadLeft/Right→SizeWE；QuadTop/Bottom→SizeNS；Center→SizeAll；Start/End/PolygonVertex→SizeAll。
   - `OnRender` 对 `LineWidget` 跳过虚线框。

## 边界

- ② 的 min 尺寸保持 10（与 RectangleWidgetCreator 一致）；不引入画布外钳制（与现状一致）。
- ① 只补变量管理器刷新，不动 Tag 模型（不加 INPC，避免 protobuf 模型改动风险；与 undo/redo 既有方案一致）。
- ④ 光标：角点对角斜向、边中点水平/垂直、圆心/线端点/多边形顶点 move。
- ⑤ 仅 Line 去框；Circle/Ellipse/Polygon/Rectangle 保留选中框。

## 验收

1. 编译 0 错误；`dotnet test` 全绿（286+）。
2. ① GUI：拖拽绑变量点 → 变量管理器表格基准值即时刷新（用户 Check）。
3. ② GUI：矩形 4 角端点均可拖（向内缩/向外扩都生效，min 10 防翻转）。
4. ③ GUI：拖动矩形端点 → 属性面板矩形端点表格实时同步（100ms 节流）。
5. ④ GUI：角点对角斜向光标、边中点水平/垂直光标、圆心 move。
6. ⑤ GUI：选中直线只有 2 端点手柄，无外圈虚蓝框。

## 批次纪律

编译 + 测试 + reviewer 审查 + 提交（message 无 BOM，不 push）。Y-1（①）与 Y-2（②③④⑤）一次 Do 完成、一次审查。
