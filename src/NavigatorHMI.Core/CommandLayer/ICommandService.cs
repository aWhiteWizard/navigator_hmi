namespace NavigatorHMI.CommandLayer
{
    /// <summary>
    /// Command Service 接口。所有操作入口（GUI ViewModel、CLI、AI Agent）统一通过此接口执行命令。
    /// </summary>
    public interface ICommandService
    {
        /// <summary>
        /// 执行指定命令。
        /// </summary>
        /// <param name="commandName">命令名（蛇形命名，如 "create_screen"）</param>
        /// <param name="parameters">参数字典（键=参数名，值=参数值）</param>
        /// <returns>执行结果。Success=true 表示成功。</returns>
        CommandResult Execute(string commandName, Dictionary<string, object?> parameters);

        /// <summary>
        /// 获取所有已注册命令的定义列表。
        /// 供 AI Agent 做 Function Calling 工具发现使用。
        /// </summary>
        /// <returns>命令定义列表</returns>
        List<CommandDefinition> GetAvailableCommands();

        /// <summary>
        /// 获取当前工程全部画面名称（供规则引擎做画面名歧义消解，如"中心"后缀 vs 画面名含"中心"）。
        /// </summary>
        /// <returns>画面名称列表</returns>
        List<string> GetScreenNames();

        /// <summary>E11 当前画面名（运行时：GUI 画面切换维护；current_screen 命令与 AI prompt 读取）。</summary>
        string? CurrentScreenName { get; set; }
    }
}
