using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 固件包打包器（D 循环批 2 D1，2026-08-30）——把 app 二进制/rootfs.img/boot.img 打包为 .fw 固件包。
    /// 格式（v1.1-design §5.2 D1 为准；firmware-update.md §3.2 为 i.MX6ULL 历史参考待同步）：
    ///   Header（128B 定长）：
    ///     magic "NHFW" (4B) + version (16B, 定长右补空格) + timestamp (8B, Unix 秒 LE) +
    ///     component_count (4B, LE) + sha256 (64B, 整个 payload 的 SHA256 hex, 定长右补空格)
    ///   Component Table：每项定长 184B（name32 + type16 + target48 + size8 + sha64 + version16，
    ///     各字段右补空格；size LE）——审查 🟡：原注释误写 128B（128 是 Header），已修正为 184 防 pack_fw.py 误实现
    ///   Payload：各组件二进制按表顺序拼接（offset 由前项累积；表内 offset 字段废弃——依赖表序与 size）
    /// 注：PC 打包器供「编译产物即时打包」与测试用；正式发布走 Docker tools/pack_fw.py（格式同源，两端可互读——
    ///     字节级布局以本文件为单一事实源，pack_fw.py 照此实现）。
    /// 与 DeploymentPackageBuilder（zip 容器）为两套独立格式（.fw 固件 / .deploy.zip 工程）。
    /// </summary>
    public static class FwPackageBuilder
    {
        public const string Magic = "NHFW";
        public const int HeaderSize = 128;
        public const int ComponentEntrySize = 184;   // name32 + type16 + target48 + size8 + sha64 + version16

        /// <summary>固件组件类型（用户 2026-08-30 分组：app+rootfs+kernel 一组；U-Boot 不打包——开发工具另路）。</summary>
        public static class ComponentType
        {
            public const string App = "app";
            public const string Rootfs = "rootfs";
            public const string Kernel = "kernel";   // boot.img = 内核+dtb+resource（boot 分区内容）
        }

        /// <summary>合法组件类型集合（白名单——审查 🟡：防拼写漂移导致 pack_fw.py/otaupdater 无法识别）。</summary>
        private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal)
        {
            ComponentType.App, ComponentType.Rootfs, ComponentType.Kernel,
        };

        /// <summary>组件描述（打包输入）。</summary>
        public class Component
        {
            /// <summary>组件名（如 navigatorhmi-fw / rootfs.img / boot.img）。</summary>
            public required string Name { get; init; }

            /// <summary>组件类型（app/rootfs/kernel，白名单校验）。</summary>
            public required string Type { get; init; }

            /// <summary>设备端落盘目标路径（如 /usr/bin/navigatorhmi-fw / /dev 分区名）。</summary>
            public required string Target { get; init; }

            /// <summary>组件文件路径（本地源文件）。</summary>
            public required string FilePath { get; init; }
        }

        /// <summary>打包产物信息（测试/校验用）。</summary>
        public class BuildResult
        {
            public required string Path { get; init; }
            public required string Version { get; init; }
            public required int ComponentCount { get; init; }
            public required string PayloadSha256 { get; init; }
        }

        /// <summary>
        /// 打包 .fw（NHFW header + 组件表 + payload）。组件按传入顺序拼接（表序 = payload 序）。
        /// version 定长 16B 右补空格；sha256 定长 64B hex 右补空格（对齐两端互读）。
        /// </summary>
        public static BuildResult Build(string version, string outputDir, params Component[] components)
        {
            // 版本号规范 x.y.z 纯数字段（审查 🟡：与 CompareVersions 非数字段→0 口径统一——
            // 禁止 "1.2.3-rc1" 等后缀，防打包器放行但比较器误判旧版；字符白名单防路径穿越）
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("固件版本必填", nameof(version));
            if (version.Length > 16)
                throw new ArgumentException($"固件版本 \"{version}\" 超长（>16），.fw header version 定长字段不支持", nameof(version));
            if (!version.All(c => char.IsAsciiDigit(c) || c == '.'))
                throw new ArgumentException($"固件版本 \"{version}\" 非法（仅允许数字与点，格式 x.y.z）", nameof(version));
            var verSegs = version.Split('.');
            if (verSegs.Length > 3 || verSegs.Any(string.IsNullOrEmpty))
                throw new ArgumentException($"固件版本 \"{version}\" 非法（格式 x.y.z 三段，每段非空数字）", nameof(version));
            if (components == null || components.Length == 0)
                throw new ArgumentException("至少需要一个组件", nameof(components));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("输出目录必填", nameof(outputDir));

            // 校验组件（白名单/必填/文件存在）+ 构建 payload（表序拼接；流式读防大文件全量内存）
            var payload = new MemoryStream();
            var table = new List<(Component Comp, long Offset, long Size, string Sha)>();
            foreach (var c in components)
            {
                if (c == null)
                    throw new ArgumentException("组件不能为 null", nameof(components));
                if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Target))
                    throw new ArgumentException($"组件 {c.Name} 的 name/target 必填");
                if (!AllowedTypes.Contains(c.Type))
                    throw new ArgumentException($"组件 {c.Name} 类型 \"{c.Type}\" 非法（允许: {string.Join("/", AllowedTypes)}）");
                if (string.IsNullOrWhiteSpace(c.FilePath) || !File.Exists(c.FilePath))
                    throw new FileNotFoundException($"组件文件不存在: {c.FilePath}");
                var offset = payload.Length;
                using (var fs = File.OpenRead(c.FilePath))
                    fs.CopyTo(payload);
                var size = payload.Length - offset;
                table.Add((c, offset, size, Sha256HexFile(c.FilePath)));
            }

            var payloadBytes = payload.ToArray();
            var payloadSha = Sha256Hex(payloadBytes);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Header（128B）
            using var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes(Magic), 0, 4);
            ms.Write(FixedField(version, 16), 0, 16);
            Span<byte> tsBuf = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(tsBuf, timestamp);
            ms.Write(tsBuf);
            Span<byte> cntBuf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(cntBuf, table.Count);
            ms.Write(cntBuf);
            ms.Write(FixedField(payloadSha, 64), 0, 64);
            WritePadding(ms, HeaderSize - (int)ms.Length);

            // Component Table（每项 184B：name32 + type16 + target48 + size8 + sha64 + version16——恰好无填充）
            foreach (var (comp, offset, size, sha) in table)
            {
                ms.Write(FixedField(comp.Name, 32), 0, 32);
                ms.Write(FixedField(comp.Type, 16), 0, 16);
                ms.Write(FixedField(comp.Target, 48), 0, 48);
                Span<byte> szBuf = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(szBuf, size);
                ms.Write(szBuf);
                ms.Write(FixedField(sha, 64), 0, 64);
                ms.Write(FixedField(version, 16), 0, 16);
            }

            // Payload
            ms.Write(payloadBytes);

            // 原子输出（tmp + 替换；失败清理 tmp——审查 🟡，对齐 DeploymentPackageBuilder 原子写先例）
            var fwName = $"NavigatorHMI_v{version}.fw";
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, fwName);
            var tmp = outputPath + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, ms.ToArray());
                File.Move(tmp, outputPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }

            return new BuildResult { Path = outputPath, Version = version, ComponentCount = table.Count, PayloadSha256 = payloadSha };
        }

        /// <summary>定长字段：右补空格到固定长度；非 ASCII 字符抛出（审查 🟡：防静默 '?' 替换导致 pack_fw.py 读不一致）。</summary>
        internal static byte[] FixedField(string s, int len)
        {
            if (s.Length > len)
                throw new ArgumentException($"字段 \"{s}\" 超长（{s.Length} > {len}），.fw 定长格式不支持");
            var b = new byte[len];
            var bytes = Encoding.ASCII.GetBytes(s);
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] == (byte)'?')
                    throw new ArgumentException($"字段 \"{s}\" 含非 ASCII 字符，.fw 定长 ASCII 格式不支持");
            Array.Copy(bytes, b, bytes.Length);
            for (int i = bytes.Length; i < len; i++) b[i] = (byte)' ';
            return b;
        }

        private static void WritePadding(Stream s, int count)
        {
            if (count > 0) s.Write(new byte[count], 0, count);
        }

        /// <summary>SHA256 hex（小写，文件内容——组件表用）。</summary>
        internal static string Sha256HexFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }

        /// <summary>SHA256 hex（小写，字节数组——payload 整体用）。</summary>
        internal static string Sha256Hex(byte[] data)
        {
            var hash = SHA256.HashData(data);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
