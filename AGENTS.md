# NavigatorHMI 工作区指令（DeepSeek Harness / dsh 版）

> 本文件由 Reasonix 于 2026-08-13 为 dsh 适配编写，与能力仓库的 `reviewer/projects/navigator-hmi/PROJECT.md` 配合使用。
> 每个会话开始时：读 PROJECT.md「继续点」判断当前 PDCA 环节，再按下方约定行事。

## 项目背景
- NavigatorHMI 工业 HMI 系统：PC 组态软件（C# WPF）+ HMI 设备端（RK3562 / Qt）。
- **任务结构（2026-08-14 用户定）**：NavigatorHMI = 同一个任务的两个子任务——子任务 A：PC 组态软件（进行中，V1.0 + bug 收尾完成，不结束）；子任务 B：HMI panel（设备端，RK3562/Qt，新建工程）；B 完成后 A/B 联调。两子任务同任务同会话推进。
- 代码仓库：`D:\workspace\code\navigator_hmi`（分支 arch-scaffold，本地提交未 push）。
- **能力仓库（AI-Capability，2026-08-23 用户定：不叫 Pi、直接叫能力仓库）**：本地根 `D:\workspace\code\Future-Tech-Workshop-AI-Capability`（git remote `git@github.com:aWhiteWizard/Future-Tech-Workshop-AI-Capability.git`）。知识库 = 仓库内 `reviewer/references/`（7 层 + drafts/inbox）+ 工程库 `reviewer/projects/`；引用一律写仓库内相对路径（如 `reviewer/references/4_bugs/`），不写带 Pi 的完整路径。**统一布局（2026-08-23）**：`~/.pi/agent/skills` 与 `~/.dsh/skills` 均为 junction 指向能力仓库（Pi 侧指向仓库根，dsh 侧指向 `skills/dsh/`）；能力仓库 = 唯一真源，旧目录已备份至 `D:\ProgramData\Agent_backup\`。

## 用户协作约定（源自 Reasonix memory，2026-08-11 用户定，跨项目）
- **3 Agent 协作模型（2026-08-23 用户定，取代旧"学习类/硬件分工"）**：① **主 Agent（写作/实施）**——唯一能改代码/写文件，负责任务实施、方案与文档写作，经子 Agent 结果中转交付；② **看图员 Agent（读图）**——🔴 关键图（原理图/时序图/含代码截图）必读、🟡 辅助图时间允许则读、⚫ 装饰图跳过；委派视觉能力子 Agent 或 `pi-diagram-read.sh`（kimi-k3），读图后清理临时图片；③ **审核 Agent（审查）**——加载能力仓库 `reviewer/SKILL.md`（三层框架 + B2 冲突扫描 + 结构化标签），按文件类型加载对应层 reference，只出报告不改代码。子 Agent 只读只产出（审查/读图/检索三类）。
- **学习类项目记录方式**（保留）**：学习类项目（如 hardware-design-learning）一律由用户自己记录笔记；阶段性完成后用户告知，主 Agent 统一入库知识库（references/ 层）——学习类工程产出最终都要落知识库，不能只存会话/project。会话中已确认可沉淀的经验经用户确认后即时入库，不等整个项目学完。
- **D 盘优先（2026-08-14 用户定）**：备份/存档/临时/下载等非必要文件一律放 D 盘（如 `D:\ProgramData\Agent_backup\`），C 盘空间紧张不装大文件；仅在 C 盘放必须的系统/工具文件。**AI/skill 相关一律 D 盘，查询优先 D 盘**——**dsh skill 根 = 能力仓库 `skills/dsh/`**（`D:\workspace\code\Future-Tech-Workshop-AI-Capability\skills\dsh`，经 junction 挂 `~/.dsh/skills`），现有 7 个 skill：`wpf-hmi-development` / `embedded-linux-bsp` / `deepseek-agent-integration` / `kb-review-methodology` / `doc-retrieval` / `dotnet-protobuf-contract` / `dsh-client-plugin-development`（2026-08-14 注册验证通过；2026-08-23 迁入能力仓库统一管理）。

## 长会话与上下文压缩（2026-08-14 用户定，防压缩丢记忆）
- **背景**：本项目在 DeepSeek Harness 中长期使用**同一个会话**做 PDCA 循环（连贯长任务，不开新会话）。dsh 上下文窗口 1M token，用到 **80%（80 万）自动压缩**：保留最近 16 万 token 细节，更早历史变摘要——压缩只丢 AI 的临时记忆，不丢磁盘上的项目状态。
- **铁律一（重要结论及时落盘）**：每轮 Do 完成 → 更新 PROJECT.md（继续点/提交记录/测试数）；每个 bug 根因 → 知识库 4_bugs；经验教训 → 5_sessions。**对话里产生的关键状态必须在压缩前落到磁盘**，不依赖会话回忆。
- **铁律二（压缩后靠文件恢复记忆）**：上下文到 80% 时（右侧概览栏进度条可见）会自动压缩；压缩后 AI 对早期细节只有摘要。**若需恢复某细节 → 读 PROJECT.md / 知识库，不靠 AI 回忆**。新会话/压缩后第一步：读 PROJECT.md「继续点」重建状态。
- **判断是否开新会话**：连贯长任务（PDCA 循环）→ 沿用同一会话（缓存命中率高、单价低）；任务告一段落转新方向 → 开新会话（避免旧摘要稀释注意力），关键结论先落盘。

## PDCA 工作流（铁律）
1. 环节顺序：**Plan → Do → Check → Act →（下一循环）Plan → Do**；环节切换**只由用户明确发起**（用户说「开始执行/进入下一环节」才推进）。**Check 完成后下一环节是 Act（收尾落库），不是直接进下一轮 Do**——必须完整走完 Act（落库+提交）→ 下一循环 Plan 定稿，用户发起后才进入 Do（2026-08-14 用户强化：主 Agent 不得在 Check 完成后引导「进入下一轮 Do」，Check 收尾后应引导进入 Act）。
2. **Check 只记录不实施**（劳动纪律）：Check 阶段发现的问题只记录到 PROJECT.md「Check 环节记录续N」区，**任何修改（无论多小）必须等用户明确说进入下一轮 Do 才执行**——即使用户口头说「修吧」也不等于进入 Do。
3. Check 期间不得主动提议「进入下一轮 Do」或任何环节切换。
4. **Act 收尾必落库（2026-08-14 用户定）**：进入 Act 阶段（收尾）时，把本轮**全部经验教训和知识**写入知识库——bug 根因 → `references/4_bugs/` 对应子层；经验/流程/决策 → `references/5_sessions/` 会话归档（或对应层）；按知识库入库流程（草稿 → reviewer 审查 → 合并 → 更新 README 索引）。**落库是 Act 的固定动作，不是可选**。
5. **Act→Plan 知识库提交（2026-08-14 用户定）**：离开 Act、进入下一阶段 Plan 时，**把本轮落库的知识库更新提交到 Git**（`~/.pi/agent/skills/` 仓库，含 4_bugs/5_sessions 及 README 索引变更）——commit message 无 BOM；是否 push 按用户批准。**提交是 Act 转 Plan 的固定动作**，确保知识库变更落盘可追溯，不随会话丢失。
6. **开工前阶段校验（2026-08-14 用户强调，防"方向 ≠ Do"误判）**：任何实施动作（写文件/改代码/建目录/提交/push）之前，先核对：① PROJECT.md「继续点」的当前阶段；② 用户是否说了**明确的环节切换短语**（如「当前阶段结束，进入下一阶段」「开始执行」「进入 Do」）。**只有两者都满足才实施**；否则任何指示一律视为「方向 / Plan 定稿授权」，只记录、不实施。拿不准时停下来问，绝不猜。用户强调：**只有用户明确说「当前阶段结束，进入下一阶段」才是正确的环节切换**——「放 D 盘」「都加入」「你确定好就行」等均不等于进入 Do。

## 审查纪律
- 每步/每提交必审：改动完成后、提交前必须跑 reviewer 审查；修复后复审；审查通过才允许 commit。
- 最多 3 轮修改-审查循环；审查未通过不得提交。
- **审核时机（2026-08-14 用户定：小步快跑 vs 整体审）**：
  - 判断标准：真正决定效率的是**驳回率 × 修复成本**，不是审查次数——整体审被驳回时，大修+大复审的往返成本可能超过多次小审。
  - **整体审**（一批多次改动一次审）：根因已查清 + 改动集中（≤2-3 文件）+ 总量小（<150 行）+ 改动同文件/同根因可合并。
  - **小步快跑**（每任务完成即审）：根因不明 / 跨多模块 / 大改（>300 行）/ 高风险连锁——把驳回成本摊小。
  - **三保险（整体审前置）**：① 根因先行（Do 前查清，不猜测）；② 自测先行（编译 + 测试全绿再送审）；③ diff 边界明确（审查前给 reviewer 精确 diff 范围 + 根因说明）。
- **B2 冲突扫描**（审查草稿强制）：提取硬约束遍历 related 文件，发现冲突输出三级标签——`CONFLICT_HARD` 暂停交用户裁决 / `CONFLICT_SOFT` 自动取保守值并标注 / `CONFLICT_CONTEXT` 两边补场景限定不改值。
- **结构化改进标签**：审查输出 `[NEED_RULE]`/`[INDEX_WEAK]`/`[STALE]` → 投递 `references/inbox/` 待消费（知识库更新请求通道）。
- commit message 无 BOM；**push 时机（2026-08-14 用户定）**：但凡有东西需要 push，都在 **Act 阶段等用户决定**——若该轮之后**没有下一轮**，Act 结束即 push；若**还有下一轮**，则在**下一轮 Do 的第一个动作**把积压的 push 做掉（先 push 再开始 Do 任务）。

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
- Plan 定稿时输出《项目计划执行书》`<循环>-execution-plan.md`（摘要/根因/修改点/边界/验收 7 字段），Do 阶段以执行书为准；**Check 完成后直接删除执行书**（2026-08-14 用户定：执行书只给 Do 看，Plan 内容已落 PROJECT.md，无需存档）。
- **更新甘特图状态（2026-08-30 用户定）**：Plan 定稿时本循环纳入的任务在甘特图数据源（pc/fw 03_implementation.md 待做表 + gantt.md）同步更新状态（如待做 → 计划/Do 中），与 Act 转 Plan 时「本轮做完的任务同步更新到甘特图」衔接。
- **Do 环节任务清单（2026-08-14 用户期望）**：进入 Do 的第一步，用 todo 清单（todo_write）建立任务分解表——每项任务一行（如 Z-1/Z-2/Z-3、编译+测试、reviewer 审查、修复/复审、提交），随进度更新状态（pending/in_progress/completed），GUI 可视化进度；不得直接开工而不建清单。

## 任务简报系统（2026-08-23 用户定，dsh 客户端插件）
- **UI**：右侧栏「简报」tab（详情面板 tab 条：概览/简报），4 个大框——前情提要 / 劳动纪律 / 附件说明 / 流程说明；点「保存简报」生成 Markdown。
- **简报文件**：`D:\workspace\code\navigator_hmi\.agent\briefing.md`（agent 目录，固定路径，任何会话都能找到）。
- **按项目隔离（2026-08-23 用户定）**：简报按会话工作区（cwd）定位——每个项目有自己的 `.agent/briefing.md`；切换到某个项目/会话时，右侧栏简报自动读取对应项目的简报，没有就显示空（首次保存时创建）。
- **模板功能（2026-08-23 用户定）**：简报 tab 顶部「选择模板」按钮——模板库 = 能力仓库内 `reviewer/references/1_application/briefing-templates/`（知识库 1_application 层，.md 文件，同 4 段结构，跨项目共享，随能力仓库管理）。**模板可在简报内编辑**：「选择模板」列表每项有「编辑」按钮（进入模板编辑模式，4 框加载模板内容，保存覆盖该模板或另存为新名）；列表底部有「新建模板」入口（输入名称 → 编辑模式 → 保存即建）。
- **启用简报开关（2026-08-23 用户定）**：简报 tab 工具栏「启用简报」checkbox（默认勾选，状态存 `.agent/briefing-settings.json`，按项目隔离）。**勾选时**：输入框工具行出现「☎ 简报发送」按钮，点它把简报全文 + 分隔线 + 用户输入拼好填入输入栏（软件拼接，不靠 LLM 判断）并自动发送（单条）；**未勾选时**：按钮灰「☎ 发送」，行为等同普通发送。
- **软件拼接按钮（2026-08-23 用户定，取代 agent 行为拼接）**：「☎ 简报发送」按钮 = client 半注册到 `conversation.input.right` 槽位（原发送按钮左侧）。**外观随模式变化**：启用简报且会话空闲 → 高亮蓝底白字「☎ 简报发送」；未启用/插嘴 → 灰底「☎ 发送」。点击流程：读简报全文（`/briefing/load` 的 rawText，若简报不存在则直接提交原输入）→ `inputActions.setDraft(简报全文 + "\n\n---\n\n" + 输入)` **填入输入栏（可见）** → 延迟一拍 `inputActions.submit()` **自动发送**（只发拼接这条单条消息；延迟确保 setDraft 生效、避免同步时序发两条）；插嘴（会话 `running`）或未启用时直接提交原输入；普通回车/原发送按钮不拼接。
- 简报文件只含 4 个编辑区（前情提要/劳动纪律/附件说明/流程说明），无「会话追加记录」（2026-08-23 用户定：该记录无保留必要，host 半已移除 append 端点与保存保留逻辑）。
- 插件位置：`~/.dsh/profiles/web/node_modules/@navigatorhmi/dsh-client-ui-briefing/`（host 半 lib/index.js 提供 /briefing RPC：load/save/templates.list/templates.load/settings.get/settings.set；client 半 lib/client.js 渲染编辑器 + 「☎ 简报发送」按钮）；宿主为 overview 插件改造后的 details 面板（tab 条）。

## 当前继续点
- 见 PROJECT.md「继续点」段（NavigatorHMI 一个任务两子任务：PC 组态软件进行中 + HMI panel 子任务 B 新建工程；当前进入子任务 B 项目架构讨论）。
