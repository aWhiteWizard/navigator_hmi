using System;
using System.Security.Cryptography;
using System.Text;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 设备凭据加密（Y-3a 2026-09-10 MQTT 连接凭据——DeviceEditDialog 保存 MQTT 设备时密码 DPAPI 加密入库）。
    /// 与 AiConfigStore 同款机制（Windows DPAPI CurrentUser + `dpapi:` 前缀；仅本机当前用户可解）——
    /// 工程文件内绝不明文存密码；CLI configure-device 收到的 password 若为明文由 DeviceEditDialog 层加密后入库
    /// （命令层校验只验格式，不负责加密——CLI 无 DPAPI 场景留空密码匿名连接，Y-3a 裁决记录）。
    /// 编译进 .navihmi 的再加密由 ProjectGenerator ToDto 阶段处理（本轮匿名联调 password 空，链路照落）。
    /// </summary>
    public static class CredentialStore
    {
        /// <summary>DPAPI 密文前缀（无前缀视为旧明文——读侧兼容，写侧恒加密）。</summary>
        public const string DpapiPrefix = "dpapi:";

        /// <summary>DPAPI 加密（空串返回空——空密码不制造密文）。</summary>
        public static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(bytes);
        }

        /// <summary>DPAPI 解密（无前缀视为旧明文原样返回；密文损坏/非本用户返回空——视为未配置）。</summary>
        public static string Decrypt(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal)) return stored;   // 旧明文兼容
            try
            {
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(stored[DpapiPrefix.Length..]), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";   // 密文损坏/非本用户：视为未配置
            }
        }

        /// <summary>判断是否为已加密凭据（dpapi: 前缀或非空非明文形态——GUI 回填用，加密包不回显）。</summary>
        public static bool IsEncrypted(string stored)
            => !string.IsNullOrEmpty(stored) && stored.StartsWith(DpapiPrefix, StringComparison.Ordinal);
    }
}
