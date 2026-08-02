# FutureTechHMI_V1 (NavigatorHMI)

工业 HMI 系统：PC 组态软件（C# WPF）+ HMI 设备端（i.MX6ULL / Qt）

本项目实现与现有工业 HMI 相同功能，支持 Type-C 接口作串口检测读取使用的 HMI 设备。

## 仓库结构

| 目录 | 说明 |
|------|------|
| `src/NavigatorHMI.Core/` | 数据模型、Command Layer（26 个命令处理器） |
| `src/editwindow/` | PC 组态软件（WPF，画面编辑器/属性面板/CLI 控制台） |
| `src/NaviHmiCLI/` | 独立命令行工具（与 GUI 走同一 Command Layer） |

## CLI 使用指南

所有操作（GUI / CLI / AI Agent）走**同一 Command Layer**，命令格式统一。

### 方式一：GUI 内嵌 CLI 控制台（推荐日常使用）

主界面**底部**的「CLI 控制台」面板（黑色区域，`>` 提示符）：

1. 在 `>` 后的输入框直接输入命令，**回车执行**
2. 输出显示在上方区域（白色=成功，红色=错误，灰色=列表/帮助）
3. `↑`/`↓` 键切换命令历史，`help` 或 `?` 查看帮助，`cls` 清屏

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

ls                                       列出所有画面（ls=list-screens 简写）
b                                        编译工程（b=compile 简写）
save                                     保存工程
```

**简写对照**：`cs`=create-screen，`ds`=delete-screen，`aw`=add-widget，`ct`=create-tag，`ls`=list-screens，`b`=compile，`save`=save-project

> GUI 内 CLI 与独立 CLI 共享同一 Command Layer：常用命令（create-screen/add-widget/create-tag/align/array/scan/connect/create-alarm/compile/save 等）有显式路由，其余命令走通用路由（命令名 `-` 转 `_`，参数键经 MapCliKey 映射）。两个入口的 26 个命令均可执行；`bind-event` 的 `--params` 需字典参数，GUI 面板暂不支持（请用独立 CLI）。

### 方式二：独立 CLI 工具（脚本/CI 用）

编译产物：`src/NaviHmiCLI/bin/Release/net8.0/navihmi.exe`

```bash
# Windows
navihmi.exe --project ./demo.hmiproj align --screen 测试页 --widgets button_1,button_2 --direction left

# dotnet dll
dotnet src/NaviHmiCLI/bin/Release/net8.0/navihmi.dll create-project --name demo --path .
```

完整命令参考见 [`src/NaviHmiCLI/README.md`](src/NaviHmiCLI/README.md)（26 个命令：工程/画面/控件/层级/布局/事件/变量/报警/设备）。

## 构建

```bash
# 全部项目
dotnet build navigator_hmi.sln

# CLI（Release）
dotnet build src/NaviHmiCLI/NaviHmiCLI.csproj -c Release
```
