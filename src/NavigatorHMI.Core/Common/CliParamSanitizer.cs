namespace NavigatorHMI.Common
{
    /// <summary>
    /// CLI 与 GUI 共用的参数安全净化（三级分类）。
    /// - 路径类（允许 / \）：只拦 .. 和绝对路径（connection/project 豁免绝对路径——connection 承载 JSON 设备地址、project 为工程文件任意目录场景）
    /// - 标识符类：拦 .. / \
    /// - 自由文本类（描述/消息等）：只拦 ..
    /// 由 NaviHmiCLI.Program.SanitizeParam 与 EditWindow.SanitizeCliParams 双端调用，
    /// 消除双实现白名单漂移（新增自由文本 key 只需改此处）。
    /// </summary>
    public static class CliParamSanitizer
    {
        /// <summary>校验单个参数；合法返回 null，非法返回错误消息（含 --key 与原始值）。</summary>
        public static string? Validate(string key, string value)
        {
            bool hasUpDir = value.Contains("..");
            bool hasSeparator = value.Contains('/') || value.Contains('\\');
            bool isPathParam = key is "path" or "project" or "file" or "output" or "connection" or "source";
            bool isNameParam = key is "name" or "screen" or "widget" or "widgets" or "tag" or "key" or "new-name"
                or "user-name" or "new-user-name" or "new-group-name"
                or "event" or "action" or "nic" or "protocol" or "severity" or "direction" or "mode" or "ip" or "device_ip";
            // value 语义由 --key 决定（颜色/文本/路径/数值），静态分类无法覆盖：
            // 归自由文本类仅拦 '..'（imagePath 值含 / 或 \ 是合法的相对/绝对路径）
            // items：| 分隔的预设值集合（图片列表含路径），归自由文本仅拦 '..' 防路径遍历穿透工程
            bool isFreeText = key is "description" or "message" or "params" or "model" or "value" or "font-family" or "base-value" or "items";

            if (isPathParam)
            {
                if (hasUpDir) return $"参数 --{key} 包含非法字符 '..' : {value}";
                // connection 接受 JSON 格式，允许绝对路径（如 /dev/ttyUSB0 应包在 JSON 内）
                // project 放开绝对路径（工程文件在任意目录是正常使用场景；'..' 已单独拦截防目录逃逸）
                if (Path.IsPathRooted(value) && key is not ("connection" or "project"))
                    return $"参数 --{key} 不允许绝对路径: {value}";
            }
            else if (isNameParam && (hasUpDir || hasSeparator))
            {
                return $"参数 --{key} 包含非法字符: {value}";
            }
            else if (isFreeText && hasUpDir)
            {
                return $"参数 --{key} 包含非法字符 '..' : {value}";
            }
            return null;
        }
    }
}
