namespace NavigatorHMI.CommandLayer
{
    /// <summary>
    /// 参数定义。描述单个命令参数的类型、必填性、默认值、可选值范围。
    /// 供 CLI 帮助文本和 AI Agent Function Calling Schema 生成使用。
    /// </summary>
    public class ParameterDefinition
    {
        /// <summary>参数类型：string | int | double | bool | enum | dict</summary>
        public string Type { get; set; } = "string";

        /// <summary>参数描述（人类可读）</summary>
        public string Description { get; set; } = "";

        /// <summary>是否必填</summary>
        public bool Required { get; set; } = false;

        /// <summary>默认值（Type 为 int/double 时是数字，string 时是字符串）</summary>
        public object? DefaultValue { get; set; }

        /// <summary>当 Type 为 enum 时的可选值列表</summary>
        public string[]? EnumValues { get; set; }

        /// <summary>AI compact schema 也保留（非必填参数默认省略，功能关键的可选参数设此标志暴露给模型）</summary>
        public bool KeepInCompact { get; set; } = false;
    }

    /// <summary>
    /// 命令定义。描述一个 Command 的完整元数据。
    /// 通过 <see cref="ICommandService.GetAvailableCommands"/> 暴露给 AI Agent 做 Function Calling 发现。
    /// </summary>
    public class CommandDefinition
    {
        /// <summary>命令名（蛇形命名，如 "create_screen"）</summary>
        public string Name { get; set; } = "";

        /// <summary>命令描述（人类可读）</summary>
        public string Description { get; set; } = "";

        /// <summary>参数定义字典（键=参数名）</summary>
        public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();

        /// <summary>是否需要先连接设备才能执行此命令</summary>
        public bool RequiresConnection { get; set; } = false;
    }
}
