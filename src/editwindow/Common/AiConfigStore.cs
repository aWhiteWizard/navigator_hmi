using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// AI 设置的用户级持久化（%APPDATA%\NavigatorHMI\ai-config.json）。
    /// API Key 用 Windows DPAPI（ProtectedData，CurrentUser 作用域）加密存储——仅本机当前用户可解密，非明文；
    /// 旧版明文 apiKey 兼容读取（无 dpapi: 前缀视为旧明文，下次保存自动转密文）。
    /// 模型/深度/上下文/本地模型路径非敏感，明文存储。
    /// </summary>
    public static class AiConfigStore
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NavigatorHMI", "ai-config.json");

        private const string DpapiPrefix = "dpapi:";

        /// <summary>AI 设置项。</summary>
        public sealed class AiConfig
        {
            public string ApiKey { get; set; } = "";
            public string LocalModelPath { get; set; } = "";
            public string Model { get; set; } = "";
            public string Depth { get; set; } = "";
            public string Context { get; set; } = "";

            /// <summary>命令黑名单（K-6 安全闸）：默认含高风险下载命令；设置窗可配增删。</summary>
            public List<string> Blacklist { get; set; } = new() { "deploy_project", "deploy_firmware" };
        }

        /// <summary>读取配置（损坏/缺失时返回全空默认值，不抛异常）。</summary>
        public static AiConfig Load()
        {
            var cfg = new AiConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("apiKey", out var k) && k.ValueKind == JsonValueKind.String)
                    cfg.ApiKey = Decrypt(k.GetString() ?? "");
                if (r.TryGetProperty("localModelPath", out var p) && p.ValueKind == JsonValueKind.String)
                    cfg.LocalModelPath = p.GetString() ?? "";
                if (r.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                    cfg.Model = m.GetString() ?? "";
                if (r.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.String)
                    cfg.Depth = d.GetString() ?? "";
                if (r.TryGetProperty("context", out var c) && c.ValueKind == JsonValueKind.String)
                    cfg.Context = c.GetString() ?? "";
                // K-6：黑名单（数组；区分「缺失/损坏（回退默认）」与「合法空数组（用户清空，照用）」——清空必须可持久化）
                if (r.TryGetProperty("blacklist", out var bl) && bl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    bool valid = true;
                    foreach (var item in bl.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) { valid = false; break; }
                        if (!string.IsNullOrWhiteSpace(item.GetString())) list.Add(item.GetString()!);
                    }
                    if (valid) cfg.Blacklist = list;   // 合法数组照用（空数组 = 用户清空黑名单）
                }
            }
            catch { /* 损坏/不可读时用默认值，不阻断 */ }
            return cfg;
        }

        /// <summary>保存配置（apiKey 经 DPAPI 加密）。返回是否成功（写入失败时调用方可提示用户）。</summary>
        public static bool Save(AiConfig cfg)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null) Directory.CreateDirectory(dir);
                var json = new
                {
                    apiKey = Encrypt(cfg.ApiKey),
                    localModelPath = cfg.LocalModelPath,
                    model = cfg.Model,
                    depth = cfg.Depth,
                    context = cfg.Context,
                    blacklist = cfg.Blacklist ?? new List<string>(),
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(json));
                return true;
            }
            catch { return false; }   // 写入失败（无权限/磁盘满等）
        }

        private static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(bytes);
        }

        private static string Decrypt(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(DpapiPrefix)) return stored;   // 旧版明文兼容（无前缀）
            try
            {
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored[DpapiPrefix.Length..]), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }   // 密文损坏/非本用户：视为未配置
        }
    }
}
