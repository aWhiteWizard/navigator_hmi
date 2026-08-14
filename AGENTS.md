# NavigatorHMI 工作区指令（DeepSeek Harness / dsh 版）

> 本文件由 Reasonix 于 2026-08-13 为 dsh 适配编写，与 `~/.pi/agent/skills/reviewer/projects/navigator-hmi/PROJECT.md` 配合使用。
> 每个会话开始时：读 PROJECT.md「继续点」判断当前 PDCA 环节，再按下方约定行事。

## 项目背景
- NavigatorHMI 工业 HMI 系统：PC 组态软件（C# WPF）+ HMI 设备端（RK3562 / Qt）。
- 代码仓库：`D:\workspace\code\navigator_hmi`（分支 arch-scaffold，本地提交未 push）。
- 知识库（只读引用，物理路径不变）：`~/.pi/agent/skills/reviewer/references/`（7 层）+ 工程库 `projects/navigator-hmi/PROJECT.md`。

## 用户协作约定（源自 Reasonix memory，2026-08-11 用户定，跨项目）
- **学习类项目记录方式**：学习类项目（如 hardware-design-learning）一律由用户自己记录笔记；阶段性完成后用户告知，主 Agent 统一入库知识库（references/ 层）——学习类工程产出最终都要落知识库，不能只存会话/project。会话中已确认可沉淀的经验经用户确认后即时入库，不等整个项目学完。
- **硬件协作分工**：硬件项目方案设计是用户与主 Agent **共同任务**（检索 3_hardware/4_bugs 提供支持）；方案定稿后硬件实现由用户主导；debug 由用户负责，每版问题/优缺点反馈 → 主 Agent 记录入库（问题→4_bugs 负样本、优缺点→3_hardware 设计经验）。与软件分工（主 Agent 实现、用户验证）相反。

## PDCA 工作流（铁律）
1. 环节顺序：**Plan → Do → Check → Act**；环节切换**只由用户明确发起**（用户说「开始执行/进入下一环节」才推进）。
2. **Check 只记录不实施**（劳动纪律）：Check 阶段发现的问题只记录到 PROJECT.md「Check 环节记录续N」区，**任何修改（无论多小）必须等用户明确说进入下一轮 Do 才执行**——即使用户口头说「修吧」也不等于进入 Do。
3. Check 期间不得主动提议「进入下一轮 Do」或任何环节切换。

## 审查纪律
- 每步/每提交必审：改动完成后、提交前必须跑 reviewer 审查；修复后复审；审查通过才允许 commit。
- 最多 3 轮修改-审查循环；审查未通过不得提交。
- **B2 冲突扫描**（审查草稿强制）：提取硬约束遍历 related 文件，发现冲突输出三级标签——`CONFLICT_HARD` 暂停交用户裁决 / `CONFLICT_SOFT` 自动取保守值并标注 / `CONFLICT_CONTEXT` 两边补场景限定不改值。
- **结构化改进标签**：审查输出 `[NEED_RULE]`/`[INDEX_WEAK]`/`[STALE]` → 投递 `references/inbox/` 待消费（知识库更新请求通道）。
- commit message 无 BOM；提交不 push（等用户批准）。

## 子 Agent 委派约定（主 Agent 唯一改代码）
- **铁则**：只有主 Agent（本会话）能改代码/写文件；子 Agent 只读只产出（审查/读图/检索三类），经主 Agent 中转交付。委派 prompt 一律声明「只读、禁止修改文件、一次性任务、禁止提问直接输出」。
- **委派手段（dsh 原生 subagent）**：优先用 subagent 工具委派（自带独立会话与工具面，比 Pi 的 `pi -p` 子进程更稳）；读图可用 read_image。
- **审查子 Agent**：加载 `~/.pi/agent/skills/reviewer/SKILL.md`（三层框架 + B2 冲突扫描 + 结构化标签），按文件类型加载对应层 reference，只出报告不改代码。
- **读图子 Agent**：按 🔴 关键图（原理图/时序图/含代码截图）必读 → 委派视觉能力子 Agent 或 `~/bin/pi-diagram-read.sh`（kimi-k3）；🟡 辅助图时间允许则读；⚫ 装饰图跳过；审查后清理临时图片。
- **检索子 Agent**：大文档提取用 doc-retriever 方法——先 grep 定位行号再 read 上下文，不全读大文件；输出按主题分节 + 来源 `文件:行号`；存疑单列不推断。
- **审查计数（替代 Pi B5 钩子）**：本工作区无扩展钩子，由主 Agent 自律——连续修改未审查达阈值（代码 3 次/文档 6 次）必须先送审再继续。
- **子 Agent 只读硬约束**：dsh 当前对外 subagent 接口未暴露 toolFilter，靠 prompt 声明只读；若需硬封（禁止 edit/write）需扩展 dsh 子代理调用层，暂不实施。

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
