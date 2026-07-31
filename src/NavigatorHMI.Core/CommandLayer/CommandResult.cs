namespace NavigatorHMI.CommandLayer
{
    /// <summary>
    /// Command Layer 统一返回格式。
    /// 所有 Command Handler 的 Execute 方法必须返回此类型，调用方（GUI/CLI/AI Agent）统一解析。
    /// </summary>
    public class CommandResult
    {
        /// <summary>操作是否成功</summary>
        public bool Success { get; private set; }

        /// <summary>成功时返回的数据（可以是任意对象）</summary>
        public object? Data { get; private set; }

        /// <summary>失败时的错误码（如 "DUPLICATE", "NOT_FOUND", "INVALID_PARAM"）</summary>
        public string? ErrorCode { get; private set; }

        /// <summary>失败时的错误描述（人类可读）</summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>创建成功结果</summary>
        /// <param name="data">可选的返回数据</param>
        public static CommandResult Ok(object? data = null)
            => new() { Success = true, Data = data };

        /// <summary>创建失败结果</summary>
        /// <param name="code">错误码（大写蛇形命名，如 "NOT_FOUND"）</param>
        /// <param name="message">人类可读错误描述</param>
        public static CommandResult Fail(string code, string message)
            => new() { Success = false, ErrorCode = code, ErrorMessage = message };
    }
}
