namespace NavigatorHMI.Common
{
    /// <summary>CLI 帮助文本（共享常量：CLI 控制台 help 与帮助对话框共用，单一来源）。</summary>
    public static class CliHelpContent
    {
        public const string Text = @"GUI CLI 帮助:
  create-screen --name <name> [--type custom]  创建画面
  delete-screen --name <name>                    删除画面
  rename-screen --name <name> --new-name <name>  重命名画面
  copy-screen --name <name>                      复制画面
  paste-screen                                   粘贴画面
  add-widget --screen <name> --type button --x 0 --y 0  添加控件
  create-tag --name <name> --type FLOAT [--source <uri>] [--base-value 25.5]  创建变量（source 缺省 = 内部变量；base-value = 设计态基准值）
  update-tag --name <name> [--new-name <name>] [--type FLOAT] [--source <uri>] [--unit °C] [--scan-interval 100] [--deadband 0] [--description ...] [--base-value ...]  更新变量（--source """" 清空为内部变量；--unit """" / --description """" 清空）
  delete-tag --name <name>                           删除变量（被引用时拒绝）
  bind-tag --screen <name> --widget <name> --tag <name>  绑定变量到控件
  create-list --name <name> --type Text --items 'a|b|c'  创建列表（Text/Image；items 用 | 分隔，图片列表为图片路径）
  update-list --name <name> [--new-name <name>] [--items 'a|b|c']  更新列表（重命名级联同步控件引用）
  delete-list --name <name>                           删除列表（被控件引用时拒绝）
  create-alarm --name <name> --tag <name> --type High --threshold 80 [--trigger-mode Threshold] [--category User] [--priority 0] [--ack-required true] [--ack-group <组>] [--color-override #RRGGBB]  创建报警
  update-alarm --name <name> [--new-name <name>] [--tag <name>] [--type <...>] [--threshold <n>] [--deadband <n>] [--delay <ms>] [--severity <s>] [--trigger-mode <m>] [--category <c>] [--priority <n>] [--ack-required <b>] [--ack-group <g>] [--color-override <c>]  更新报警
  delete-alarm --name <name>                           删除报警
  copy-widget --screen <name> --widget <name>   复制控件
  paste-widget --screen <name>                   粘贴控件
  set-default-font --font-size 14                设置默认字体
  align --screen <name> --widgets a,b,c --direction left  对齐控件
  array --screen <name> --widgets a,b,c --mode rect --start-x 0 --start-y 0 [--cols N] [--rows N] --spacing-x 120 --spacing-y 80  阵列排列（--cols/--rows 省略时按控件数自动计算：4 个 → 2×2）
  compile                                       编译工程
  save                                          保存工程
  list-screens / ls                             列出所有画面
  cls / clear                                   清屏
  help / ?                                      显示帮助

设备命令:
  configure-device --name <name> --protocol ModbusTCP --connection '<json>'  配置设备
  update-device --name <name> [--new-name <name>] [--protocol <...>] [--connection '<json>']  更新设备
  delete-device --name <name>                                               删除设备
  connect --ip <addr> [--model NavigatorHMI-7] [--size-inch 7寸]   连接设备
  disconnect                                                     断开设备连接
  scan [--nic eth0]                            扫描设备
  deploy-project --ip <addr> [--file <path>]   编译+打包+传输工程（deploy 前置编译门禁）
  deploy-firmware --ip <addr> [--file <path>]  下载固件（OTA，D 批）
  blink-device --ip <addr> --enable <on|off>   设备闪烁（定位）
  vnc --ip <addr> --enable <on|off>            VNC 运行时启停

set-property 属性键 (--screen <画面> --widget <控件> --key <键> --value <值>):
  文本: text | title | onText | offText
  颜色: textColor | fillColor | strokeColor
  字体: fontFamily | fontSize | fontWeight | fontStyle | textDecoration
  数值: value | min | max | strokeThickness | x2 | y2
  列表: listRef | defaultIndex
  其他: hAlign | imagePath | stretchMode | isOn | isChecked | isReadOnly | fillStyle | content

控件类型 (add-widget --type): button text rectangle label image numeric switch line circle ellipse iofield checkbox textlist frame progressbar";
    }
}
