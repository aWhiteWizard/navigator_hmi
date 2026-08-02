# navihmi — NavigatorHMI CLI

组态软件命令行接口。所有操作通过 `CommandService` 执行，与 GUI 走同一路径。

## 构建

```bash
dotnet build src/NaviHmiCLI/NaviHmiCLI.csproj -c Release
```

产物：`src/NaviHmiCLI/bin/Release/net8.0/navihmi.exe`

## 快速开始

```bash
# 创建工程
navihmi create-project --name "产线监控" --path "./"

# 打开工程，创建画面，放置控件
navihmi --project ./产线监控.hmiproj create-screen --name "温度页"
navihmi -p ./产线监控.hmiproj add-widget --screen "温度页" --type button --x 100 --y 50

# 编译输出
navihmi -p ./产线监控.hmiproj compile
```

## 全局选项

| 选项 | 说明 |
|------|------|
| `--project`, `-p <path>` | 工程文件路径（`.hmiproj`）。不指定时从当前目录自动查找。 |
| `--json` | 以 JSON 格式输出结果（供脚本解析） |
| `--help`, `-h` | 显示帮助 |

## 命令参考

### 工程管理

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `create-project` | `--name` | `--path`(.), `--width`(800), `--height`(480) |
| `open-project` | `--path` | — |
| `save-project` | — | `--path`(覆盖原文件) |
| `compile` | — | `--output` |

```bash
navihmi create-project --name "demo" --path "./projects"
navihmi open-project --path "./projects/demo.hmiproj"
navihmi -p ./demo.hmiproj compile
```

### 画面

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `create-screen` | `--name` | `--type`(custom), `--width`(800), `--height`(480) |
| `delete-screen` | `--name` | — |

```bash
navihmi -p ./demo.hmiproj create-screen --name "主控页" --type custom
navihmi -p ./demo.hmiproj delete-screen --name "主控页"
```

> Template（全局画面）和 WorldMap（世界地图）不可删除。

### 控件

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `add-widget` | `--screen`, `--type`, `--x`, `--y` | `--width`(100), `--height`(40) |
| `move-widget` | `--screen`, `--widget`, `--x`, `--y` | — |
| `resize-widget` | `--screen`, `--widget`, `--width`, `--height` | — |
| `delete-widget` | `--screen`, `--widget` | — |
| `set-property` | `--screen`, `--widget`, `--key`, `--value` | — |

```bash
# 放置一个按钮
navihmi -p ./demo.hmiproj add-widget --screen "温度页" --type button --x 100 --y 50

# 放置一个文本标签
navihmi -p ./demo.hmiproj add-widget --screen "温度页" --type text --x 300 --y 100

# 移动控件
navihmi -p ./demo.hmiproj move-widget --screen "温度页" --widget button_1 --x 200 --y 80

# 改按钮文字
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget button_1 --key "text" --value "启动电机"
```

> `--type` 可选值（15 种）：
> - 基础：`button`, `text`, `label`, `rectangle`, `line`, `circle`, `ellipse`, `frame`
> - 显示：`image`, `numeric`（数值显示）, `progressbar`（进度条）
> - 交互：`switch`（开关）, `checkbox`（复选框）, `textbox`（输入框）, `iofield`（IO 字段）
>
> `--widget` 的值是控件的 `ObjectName`（如 `button_1`、`text_2`）

### 控件颜色设置

每个控件支持的颜色属性取决于其类型（按控件已有的属性，无需额外配置）：

| 颜色属性 | 含义 | 适用控件 |
|---------|------|---------|
| `textColor` | 文字颜色 | Button / Text / Label / NumericDisplay / Switch / IOField / CheckBox / TextBox |
| `fillColor` | 填充/背景色 | 除 Line 外全部 14 种（Image 为背景色、ProgressBar 为填充色） |
| `strokeColor` | 描边/边框色 | Line / Circle / Ellipse |

颜色值支持两种格式：
- **十六进制**：`#RRGGBB`（如 `#FF0000`）、`#AARRGGBB`（带透明度，如 `#80FFA500`）、`#RGB` 缩写（如 `#F00`）
- **WPF 命名颜色**：`Red`, `Green`, `Blue`, `Yellow`, `Black`, `White`, `Gray`, `Orange`, `LightBlue`, `Transparent`（透明）等

```bash
# 有文字的控件：文字色 + 背景色
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget button_1 --key textColor --value "#FFFFFF"
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget button_1 --key fillColor --value "#FF0000"

# 有边框的控件：描边色 + 描边粗细 + 填充色
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget circle_1 --key strokeColor --value "#0000FF"
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget circle_1 --key strokeThickness --value 2
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget circle_1 --key fillColor --value "#FFA500"

# 纯背景控件
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget rectangle_1 --key fillColor --value "#00FF00"

# 半透明背景（#AARRGGBB）
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget text_1 --key fillColor --value "#80FFA500"
```

> ⚠️ PowerShell 中 `#` 是注释符，值必须加引号：`--value "#FF0000"`（GUI CLI 面板无此限制）。
> 颜色能力按控件类型划分：有文字的控件（Button/Text/Label 等）支持 `textColor`+`fillColor`；Rectangle/Frame/ProgressBar 等仅 `fillColor`；Line/Circle/Ellipse 支持 `strokeColor`。盒状控件无边框色属性（GUI 边框为系统样式）。

### set-property 属性键参考

`set-property` 支持以下属性键（不同控件类型可用键不同，未匹配的键返回 `UNKNOWN_PROPERTY` 错误）：

| 分类 | 属性键 | 含义 | 适用控件 |
|------|--------|------|---------|
| 文本 | `text` | 按钮/标签/复选框显示文字 | Button / Label / CheckBox |
| 文本 | `content` | 文本内容 | Text / TextBox / IOField |
| 文本 | `title` | 标题 | Frame |
| 文本 | `onText` / `offText` | 开关 ON/OFF 标签 | Switch |
| 颜色 | `textColor` | 文字颜色 | Button / Text / Label / NumericDisplay / Switch / IOField / CheckBox / TextBox |
| 颜色 | `fillColor` | 填充/背景色 | 除 Line 外全部 |
| 颜色 | `strokeColor` | 描边/边框色 | Line / Circle / Ellipse |
| 字体 | `fontFamily` | 字体族 | 有文字控件 |
| 字体 | `fontSize` | 字号 | 有文字控件 |
| 字体 | `fontWeight` | 字重（Normal/Bold） | 有文字控件 |
| 字体 | `fontStyle` | 字型（Normal/Italic） | 有文字控件 |
| 字体 | `textDecoration` | 下划线（None/Underline） | 有文字控件 |
| 数值 | `value` | 数值 | NumericDisplay / ProgressBar |
| 数值 | `min` / `max` | 范围 | ProgressBar |
| 数值 | `strokeThickness` | 描边粗细 | Line / Circle / Ellipse |
| 数值 | `x2` / `y2` | 线终点偏移 | Line |
| 其他 | `hAlign` | 水平对齐（Left/Center/Right） | Text / Label |
| 其他 | `imagePath` | 图片路径 | Image / Frame |
| 其他 | `stretchMode` | 拉伸模式（None/Fill/Uniform/UniformToFill） | Image |
| 其他 | `isOn` | 开关状态 | Switch |
| 其他 | `isChecked` | 勾选状态 | CheckBox |
| 其他 | `isReadOnly` | 只读 | IOField |
| 其他 | `fillStyle` | 填充样式（Solid/Diagonal/Grid） | ProgressBar |

```bash
# 改按钮文字 + 字号
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget button_1 --key text --value "启动电机"
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget button_1 --key fontSize --value 24

# 开关/复选框状态
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget switch_1 --key isOn --value true
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget checkbox_1 --key isChecked --value false

# 进度条范围与填充样式
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget progressbar_1 --key min --value 0
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget progressbar_1 --key max --value 100
navihmi -p ./demo.hmiproj set-property --screen "温度页" --widget progressbar_1 --key value --value 60
```

> 提示：GUI 内嵌 CLI 面板输入 `help` 也会显示同一份属性键分类，无需记忆。

### 层级（Z-Order）

| 命令 | 说明 |
|------|------|
| `bring-to-front` | 置于顶层 |
| `bring-forward` | 上移一层 |
| `send-backward` | 下移一层 |
| `send-to-back` | 置于底层 |

```bash
navihmi -p ./demo.hmiproj bring-to-front --screen "温度页" --widget button_1
```

### 布局（阵列 / 对齐）

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `align` | `--screen`, `--widgets`, `--direction` | — |
| `array` | `--screen`, `--widgets`, `--mode`(rect/circle) | `--start-x`(0), `--start-y`(0), `--cols`(3), `--rows`(2), `--spacing-x`(120), `--spacing-y`(80), `--center-x`(0), `--center-y`(0), `--radius`(150), `--start-angle`(0), `--end-angle`(360) |

`--direction`: `left`, `center_h`, `right`, `top`, `center_v`, `bottom`
`--widgets`: 逗号分隔的控件名列表

```bash
# 左对齐 button_1, button_2, button_3
navihmi -p ./demo.hmiproj align --screen "温度页" --widgets button_1,button_2,button_3 --direction left

# 3×2 矩形阵列（起始中心 100,100，列距 120 行距 80）
# 注意：--start-x/--start-y 是第一个控件中心点坐标
navihmi -p ./demo.hmiproj array --screen "温度页" --widgets button_1,button_2,button_3 \
  --mode rect --start-x 100 --start-y 100 --cols 3 --rows 2 --spacing-x 120 --spacing-y 80

# 圆形阵列（圆心 400,240，半径 150，整圈）
navihmi -p ./demo.hmiproj array --screen "温度页" --widgets button_1,button_2,button_3 \
  --mode circle --center-x 400 --center-y 240 --radius 150 --start-angle 0 --end-angle 360
```

> 与 GUI 阵列对话框共用同一计算逻辑（`LayoutMath`），整圈 0~360° 按 360°/数量 均匀分布首尾不重叠。
### 事件绑定

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `bind-event` | `--screen`, `--widget`, `--event`, `--action` | `--params` |

```bash
navihmi -p ./demo.hmiproj bind-event \
  --screen "温度页" --widget button_1 \
  --event onClick --action screen_switch \
  --params "screen_name=主控页"
```

`--event`: `onClick`, `onPress`, `onRelease`, `onValueChange`, `onScreenLoad`, `onScreenUnload`, `onTimer`
`--action`: `tag_write`, `screen_switch`, `set_property`, `run_command`, `show_popup`, `send_notification`
`--params`: `key1=val1,key2=val2` 格式

### 变量

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `create-tag` | `--name`, `--type`, `--source` | `--unit`, `--scan-interval`(100), `--deadband`(0), `--description` |
| `bind-tag` | `--screen`, `--widget`, `--tag` | — |

```bash
# 定义一个 Modbus 变量
navihmi -p ./demo.hmiproj create-tag \
  --name "Tank1_Temp" --type FLOAT \
  --source "modbus://1/40001" --unit "°C"

# 把控件绑定到变量
navihmi -p ./demo.hmiproj bind-tag --screen "温度页" --widget text_2 --tag "Tank1_Temp"
```

`--type`: `BOOL`, `INT16`, `UINT16`, `INT32`, `FLOAT`, `STRING`
`--source`: `modbus://{从站}/{寄存器}` 或 `mqtt://{主题}`

### 报警

| 命令 | 必填参数 | 可选参数 |
|------|---------|---------|
| `create-alarm` | `--name`, `--tag`, `--type`, `--threshold` | `--deadband`(0), `--delay`(0), `--severity`(Warning), `--message` |

```bash
navihmi -p ./demo.hmiproj create-alarm \
  --name "温度过高" --tag "Tank1_Temp" \
  --type High --threshold 80.0 \
  --severity Important --message "温度超过安全范围"
```

`--type`: `High`, `Low`, `RateChange`, `Deviation`
`--severity`: `Emergency`, `Important`, `Warning`, `Info`

### 设备

| 命令 | 说明 |
|------|------|
| `configure-device` | 配置 Modbus/MQTT 通信参数 |
| `connect` | 连接 HMI 设备（HTTP GET `/api/device/info`） |
| `scan` | 扫描局域网内可用设备 |
| `deploy-project` | 下载 `.navihmi` 到设备 |
| `deploy-firmware` | 下载 `.fw` 固件并触发 OTA |

```bash
navihmi -p ./demo.hmiproj configure-device \
  --name "主PLC" --protocol ModbusTCP \
  --connection '{"ip":"192.168.1.50","port":502,"slaveId":1}'

navihmi -p ./demo.hmiproj connect --ip 192.168.1.100
navihmi -p ./demo.hmiproj deploy-project --ip 192.168.1.100
```

> `deploy-project` 和 `deploy-firmware` 需要先 `connect` 成功。
> 设备命令当前为骨架模式：`connect`/`scan`/`deploy-*` 返回模拟成功（真实 HTTP/OTA 通信未实现），`configure-device` 已可用。

## 输出格式

### 人类可读（默认）

```
✓ create-screen — {"screen_name":"温度页"}
✓ add_widget — {"widget_name":"button_1"}
✗ [NOT_FOUND] 画面 "xxx" 不存在
```

### JSON（`--json`）

```json
{"success":true,"data":{"widget_name":"button_1"}}
{"success":false,"error":{"code":"NOT_FOUND","message":"画面 \"xxx\" 不存在"}}
```

## 自动保存

所有**修改命令**（`create-screen`、`add-widget`、`set-property`、`align`、`array` 等）执行成功后自动保存工程文件。保存失败时输出警告但不阻断。

只读命令（`open-project`、`compile`）不触发保存。

如需显式保存：`navihmi -p ./demo.hmiproj save-project`

## 典型工作流

```bash
# 1. 创建工程
navihmi create-project --name "Demo" --path "./" --width 1024 --height 600

# 2. 批量创建画面
navihmi -p ./Demo.hmiproj create-screen --name "主控页"
navihmi -p ./Demo.hmiproj create-screen --name "温度曲线"
navihmi -p ./Demo.hmiproj create-screen --name "报警总览"

# 3. 定义变量
navihmi -p ./Demo.hmiproj create-tag --name "Temp" --type FLOAT --source "modbus://1/40001" --unit "°C"
navihmi -p ./Demo.hmiproj create-tag --name "Speed" --type INT16 --source "modbus://1/40003" --unit "rpm"

# 4. 放置控件
navihmi -p ./Demo.hmiproj add-widget --screen "主控页" --type text --x 50 --y 50 --width 200
navihmi -p ./Demo.hmiproj bind-tag --screen "主控页" --widget text_1 --tag "Temp"
navihmi -p ./Demo.hmiproj add-widget --screen "主控页" --type button --x 300 --y 200 --width 120 --height 40
navihmi -p ./Demo.hmiproj set-property --screen "主控页" --widget button_1 --key "text" --value "启动"

# 5. 编译输出
navihmi -p ./Demo.hmiproj compile
# → output/Demo.navihmi
```

## Tab 补全

当前版本**不支持** Tab 补全。计划后续通过 PowerShell 脚本或 `dotnet-suggest` 集成。

## 注意事项

- 参数名使用 `--kebab-case`，内部映射到 Handler 的 `snake_case`
- 控件类型支持 15 种：`button` / `text` / `label` / `rectangle` / `line` / `circle` / `ellipse` / `frame` / `image` / `numeric` / `progressbar` / `switch` / `checkbox` / `textbox` / `iofield`
- `set-property` 的 `--key` 为控件属性名（如 `text`、`fillColor`、`fontSize`），`--value` 为对应值；不同控件类型支持的属性键不同（如 button 支持 `text`/`fontSize`/`textColor`/`fillColor`，text 支持 `content`/`hAlign` 等），**未匹配的属性键返回 `UNKNOWN_PROPERTY` 错误并终止命令**（属性键大小写敏感，如 `Text` ≠ `text`）
- 设备命令（`connect`/`scan`/`deploy-*`）为**骨架模式**：返回模拟成功结果（真实 HTTP/OTA 通信尚未实现，见代码 TODO）；`configure-device` 已可用（含协议校验）
- 工程文件名和画面名**禁止**包含 `..`、`/`、`\`（路径遍历防护）
- 所有**修改**命令执行后自动保存，无需手动 `save-project`（除非显式需要另存为）
