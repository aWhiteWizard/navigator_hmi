# FutureTechHMI_V1 (NavigatorHMI)

工业 HMI 系统：PC 组态软件（C# WPF）+ HMI 设备端（i.MX6ULL / Qt）

本项目实现与现有工业 HMI 相同功能，支持 Type-C 接口作串口检测读取使用的 HMI 设备。

## 仓库结构

| 目录 | 说明 |
|------|------|
| `src/NavigatorHMI.Core/` | 数据模型、Command Layer（32 个命令处理器） |
| `src/editwindow/` | PC 组态软件（WPF，画面编辑器/属性面板/CLI 控制台） |
| `src/NaviHmiCLI/` | 独立命令行工具（与 GUI 走同一 Command Layer） |

## 画布快捷键

所有快捷键在当前编辑画面生效（焦点在输入框/属性编辑时自动放行，不干扰输入）：

| 快捷键 | 功能 |
|--------|------|
| `Ctrl+A` | 全选当前画面所有控件 |
| `Ctrl+C` | 复制选中控件 |
| `Ctrl+X` | 剪切选中控件（可粘贴还原） |
| `Ctrl+V` | 粘贴控件 |
| `Delete` | 删除选中控件 |
| `←` `→` `↑` `↓` | 微移选中控件 1px |
| `Shift` + 方向键 | 微移选中控件 10px |
| `Ctrl+Z` | 撤销 |
| `Ctrl+Y` / `Ctrl+Shift+Z` | 重做 |
| `Ctrl+S` | 保存工程 |
| `ESC` | 退出添加控件模式 / 取消两点式绘制 |

> 微移细节：连续按方向键只记一次撤销点（不会吃光撤销历史）；无选中时方向键不拦截（TreeView/下拉框导航正常）。

## 画布页面标签

- 画布顶部标签栏显示**已打开的画面**（自定义/全局画面/世界地图），打开才显示，避免画面多时标签过长
- 点击标签切换画面（项目树 ✅ 高亮同步）；点标签 `✕` 关闭（画面不删除，树中可重新打开）

## 变量管理器

项目树「通信变量 → 变量」**双击**打开，以画布标签栏「变量」Tab 展示（与画面 Tab 并列切换），管理工程全部变量，变量是控件与数据采集的桥梁：

| 能力 | 说明 |
|------|------|
| 新建/编辑 | 名称（唯一）/数据类型/来源/单位/采集周期/死区/描述 |
| 搜索过滤 | 按名称/来源/描述模糊过滤 |
| 双击编辑 | 双击表格行或点「✏ 编辑」 |
| 删除保护 | 被控件绑定或报警引用的变量**拒绝删除**并列出引用位置 |
| 重命名级联 | 改名后自动同步所有引用它的控件/报警，引用不悬空 |

数据类型：BOOL / INT16 / UINT16 / INT32 / FLOAT / STRING；来源格式：`modbus://{从站}/{寄存器地址}` 或 `mqtt://{主题}`。
CLI 命令对应：`create-tag` / `update-tag` / `delete-tag` / `bind-tag`。

## CLI 使用指南

所有操作（GUI / CLI / AI Agent）走**同一 Command Layer**，命令格式统一。

### 方式一：GUI 内嵌 CLI 控制台（推荐日常使用）

主界面**底部**的「CLI 控制台」面板（黑色区域，`>` 提示符）：

1. 在 `>` 后的输入框直接输入命令，**回车执行**（支持粘贴多行命令批量执行）
2. 输出显示在上方区域（白色=成功，红色=错误，灰色=列表/帮助），自动滚动到底部
3. `↑`/`↓` 键切换命令历史，`Shift+Enter` 换行继续编辑，`help` 或 `?` 查看帮助，`cls` 清屏

**常用命令**（与独立 CLI 格式一致，`--key value`）：

```
cs --name 新画面                             创建画面（cs=create-screen 简写）
aw --screen 测试页 --type button --x 50 --y 50   放置控件（aw=add-widget 简写）
ct --name Temp --type FLOAT --source modbus://1/40001   创建变量（ct=create-tag 简写）

# 布局（对齐 / 阵列）
align --screen 测试页 --widgets button_1,button_2,button_3 --direction left
array --screen 测试页 --widgets button_1,button_2,button_3 --mode rect \
  --start-x 100 --start-y 100 --cols 3 --rows 2 --spacing-x 120 --spacing-y 80
array --screen 测试页 --widgets button_1,button_2,button_3 --mode circle \
  --center-x 400 --center-y 240 --radius 150 --start-angle 0 --end-angle 360

# 画面复制/粘贴
copy-screen --name 主控页
paste-screen                              # 生成 主控页_副本

ls                                       列出所有画面（ls=list-screens 简写）
b                                        编译工程（b=compile 简写）
save                                     保存工程
```

**简写对照**：`cs`=create-screen，`ds`=delete-screen，`aw`=add-widget，`ct`=create-tag，`ls`=list-screens，`b`=compile，`save`=save-project

> GUI 内 CLI 与独立 CLI 共享同一 Command Layer（32 个命令）：常用命令有显式路由，其余走通用路由（命令名 `-` 转 `_`，参数键经 MapCliKey 映射）。`bind-event` 的 `--params` 需字典参数，GUI 面板暂不支持（请用独立 CLI）。

### 方式二：独立 CLI 工具（脚本/CI 用）

编译产物：`src/NaviHmiCLI/bin/Release/net8.0/navihmi.exe`

```bash
# Windows
navihmi.exe --project ./demo.hmiproj align --screen 测试页 --widgets button_1,button_2 --direction left

# dotnet dll
dotnet src/NaviHmiCLI/bin/Release/net8.0/navihmi.dll create-project --name demo --path .
```

完整命令参考见 [`src/NaviHmiCLI/README.md`](src/NaviHmiCLI/README.md)（32 个命令：工程/画面/控件/层级/布局/剪贴板/事件/变量/报警/设备/默认字体）。

## 构建

```bash
# 全部项目
dotnet build navigator_hmi.sln

# CLI（Release）
dotnet build src/NaviHmiCLI/NaviHmiCLI.csproj -c Release
```

## AI 助手（语义 Agent）

GUI 右下角圆形 AI 按钮 → 侧边栏对话（Copilot 风格：模型切换 / 推理深度 / 上下文长度 / 新建会话 / 停止按钮）：

- **DeepSeek Chat**（默认，函数调用）：自然语言操作工程（建画面/放控件/建变量列表报警/绑定/阵列等）
- **DeepSeek Reasoner**（深度思考，无工具）：提问/分析
- **本地 Qwen**（GGUF，离线）：`models/qwen2.5-7b-instruct-q4_k_m.gguf`（对话 + `<tool_call>` 工具调用）
- API Key：`设置 → AI 助手设置…`（DPAPI 密文存储；优先于环境变量 `DEEPSEEK_API` / `NAVIGATOR_HMI_AI_KEY`）

AI 可执行全部组态命令（黑名单为空，机制保留）；执行后输出操作清单，画面改动可 Ctrl+Z 整体撤销。CLI 同链路：`navihmi --project xxx.hmiproj ai --mode cloud|local|rule "指令"`。

## CLI 命令

`navihmi --project 工程.hmiproj <命令>`：画面（create/delete/rename/copy-screen）、控件（add/delete/move/resize-widget、set-property、align、array）、变量（create/update/delete-tag、bind-tag）、列表（create/update/delete-list）、报警（create/update/delete-alarm）、设备（configure/update/delete-device）。交互式：直接 `navihmi` 进入 REPL。

## 测试

`src/NavigatorHMI.Tests/`（xunit）：RuleAgent 模板引擎 16 用例（画面/控件/变量/报警/保护/未识别）。运行：`dotnet test src/NavigatorHMI.Tests`。

## 当前状态（2026-08-08）

- PC 端核心功能就绪：画面编辑/多选批量/撤销重做/变量列表报警管理/三管理器多选批量删除/AI 语义 Agent/本地 Qwen
- FW 端：9 模块设计完成；目标平台迅为 RK3562（Qt 6.5.6），开发板到货后配环境 + 编码