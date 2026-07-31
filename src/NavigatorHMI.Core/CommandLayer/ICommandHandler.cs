using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer
{
    /// <summary>
    /// 参数校验结果。
    /// </summary>
    public class ValidationResult
    {
        /// <summary>校验是否通过</summary>
        public bool IsValid => string.IsNullOrEmpty(Error);

        /// <summary>校验失败时的错误描述。null 或空字符串表示通过。</summary>
        public string? Error { get; private set; }

        /// <summary>创建"校验通过"结果</summary>
        public static ValidationResult Ok => new();

        /// <summary>创建"校验失败"结果</summary>
        /// <param name="error">失败原因（人类可读）</param>
        public static ValidationResult Fail(string error) => new() { Error = error };
    }

    /// <summary>
    /// 命令处理器接口。每个 Command 对应一个 ICommandHandler 实现。
    /// </summary>
    /// <remarks>
    /// 调用流程：<see cref="Validate"/>（参数结构校验）→ <see cref="Execute"/>（业务执行）。
    /// Validate 只检查参数存在性和格式正确性，Execute 处理业务逻辑和规则校验。
    /// </remarks>
    public interface ICommandHandler
    {
        /// <summary>命令元数据（名称、参数定义），供 AI Agent 和 CLI 帮助发现使用</summary>
        CommandDefinition Definition { get; }

        /// <summary>
        /// 校验参数合法性（在 Execute 之前调用）。
        /// 仅检查参数结构（必填项存在、类型正确），不做业务规则校验（如名称唯一性）。
        /// </summary>
        /// <param name="parameters">调用方传入的参数字典</param>
        /// <returns>校验结果。IsValid=false 时 Error 包含失败原因。</returns>
        ValidationResult Validate(Dictionary<string, object?> parameters);

        /// <summary>
        /// 执行命令（调用前已通过 Validate 校验）。
        /// </summary>
        /// <param name="project">当前工程对象（由 CommandService 注入）</param>
        /// <param name="parameters">已通过 Validate 校验的参数字典</param>
        /// <returns>执行结果。Success=true 表示成功，false 时 ErrorCode/ErrorMessage 描述失败原因。</returns>
        CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters);
    }
}
