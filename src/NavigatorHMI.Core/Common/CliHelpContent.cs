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
  create-tag --name <name> --type FLOAT [--source <uri>]  创建变量（source 缺省 = 内部变量）
  update-tag --name <name> [--new-name <name>] [--type FLOAT] [--source <uri>] [--unit °C] [--scan-interval 100] [--deadband 0] [--description ...]  更新变量（--source "" 清空为内部变量）
  delete-tag --name <name>                           删除变量（被引用时拒绝）
  bind-tag --screen <name> --widget <name> --tag <name>  绑定变量到控件
  create-alarm --name <name> --tag <name> --type High --threshold 80  创建报警
  copy-widget --screen <name> --widget <name>   复制控件
  paste-widget --screen <name>                   粘贴控件
  set-default-font --font-size 14                设置默认字体
  align --screen <name> --widgets a,b,c --direction left  对齐控件
  array --screen <name> --widgets a,b,c --mode rect --start-x 0 --start-y 0 --cols 3 --rows 2 --spacing-x 120 --spacing-y 80  阵列排列
  compile                                       编译工程
  save                                          保存工程
  list-screens / ls                             列出所有画面
  cls / clear                                   清屏
  help / ?                                      显示帮助

设备命令:
  configure-device --name <name> --protocol ModbusTCP --connection '<json>'  配置设备
  update-device --name <name> [--new-name <name>] [--protocol <...>] [--connection '<json>']  更新设备
  delete-device --name <name>                                               删除设备
  connect --ip <addr>                           连接设备
  scan [--nic eth0]                            扫描设备
  deploy-project --ip <addr> [--file <path>]   下载工程到设备
  deploy-firmware --ip <addr> [--file <path>]  下载固件（OTA）

set-property 属性键 (--screen <画面> --widget <控件> --key <键> --value <值>):
  文本: text | content | title | onText | offText
  颜色: textColor | fillColor | strokeColor
  字体: fontFamily | fontSize | fontWeight | fontStyle | textDecoration
  数值: value | min | max | strokeThickness | x2 | y2
  其他: hAlign | imagePath | stretchMode | isOn | isChecked | isReadOnly | fillStyle";
    }
}
