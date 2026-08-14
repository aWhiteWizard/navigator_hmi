# NavigatorHMI 工作区指令（DeepSeek Harness / dsh 版）

> 本文件由 Reasonix 于 2026-08-13 为 dsh 适配编写，与 `~/.pi/agent/skills/reviewer/projects/navigator-hmi/PROJECT.md` 配合使用。
> 每个会话开始时：读 PROJECT.md「继续点」判断当前 PDCA 环节，再按下方约定行事。

## 项目背景
- NavigatorHMI 工业 HMI 系统：PC 组态软件（C# WPF）+ HMI 设备端（RK3562 / Qt）。
- 代码仓库：`D:\workspace\code\navigator_hmi`（分支 arch-scaffold，本地提交未 push）。
- 知识库（只读引用，物理路径不变）：`~/.pi/agent/skills/reviewer/references/`（7 层）+ 工程库 `projects/navigator-hmi/PROJECT.md`。

## PDCA 工作流（铁律）
1. 环节顺序：**Plan → Do → Check → Act**；环节切换**只由用户明确发起**（用户说「开始执行/进入下一环节」才推进）。
2. **Check 只记录不实施**（劳动纪律）：Check 阶段发现的问题只记录到 PROJECT.md「Check 环节记录续N」区，**任何修改（无论多小）必须等用户明确说进入下一轮 Do 才执行**——即使用户口头说「修吧」也不等于进入 Do。
3. Check 期间不得主动提议「进入下一轮 Do」或任何环节切换。

## 审查纪律
- 每步/每提交必审：改动完成后、提交前必须跑 reviewer 审查；修复后复审；审查通过才允许 commit。
- commit message 无 BOM；提交不 push（等用户批准）。

## 功能自验证
- 做完功能必须实际验证：命令层用 `navihmi` CLI 建临时工程实测 + 查看工程文件内容；GUI 侧交互留给用户 Check。
- CLI ↔ GUI 控制台对等：Reasonix/dsh 自测 CLI，用户测 GUI 控制台，两边对上即通过。

## 知识库检索（索引优先）
- 先读各层 `README.md` 索引表定位，命中后精读文件；禁止全文 grep references/。
- 入库流程：草稿 → reviewer 审查 → 合并对应层 → 更新 README 索引。

## Plan 环节产物
- Plan 定稿时输出《项目计划执行书》`<循环>-execution-plan.md`（摘要/根因/修改点/边界/验收 7 字段），Do 阶段以执行书为准；Do 完成后归档/删除。

## 当前继续点
- 见 PROJECT.md「继续点」段（循环 X Do 完成，Check 待用户验收）。
