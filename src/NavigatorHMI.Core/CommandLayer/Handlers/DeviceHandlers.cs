using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>校验 connection_info JSON 语法 + 协议必填字段（ModbusTCP 校验 ip 格式）。成功返回 null，失败返回错误消息。</summary>
    internal static class DeviceConnectionInfoValidator
    {
        public static string? Validate(ProtocolType protocol, string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                // 顶层必须是 JSON 对象：TryGetProperty 在非对象根（数组/标量/null）上抛 InvalidOperationException 而非返回 false
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return "connection_info 顶层必须是 JSON 对象";
                switch (protocol)
                {
                    case ProtocolType.ModbusRTU:
                        if (!root.TryGetProperty("port", out var port) || port.ValueKind != System.Text.Json.JsonValueKind.String)
                            return "ModbusRTU 必须包含字符串 port 字段（串口路径）";
                        if (!root.TryGetProperty("baud", out var baud) || baud.ValueKind != System.Text.Json.JsonValueKind.Number
                         || baud.GetInt32() <= 0)
                            return "ModbusRTU 必须包含正整数 baud 字段";
                        if (!root.TryGetProperty("slaveId", out var sid) || sid.ValueKind != System.Text.Json.JsonValueKind.Number
                         || sid.GetInt32() is < 1 or > 247)
                            return "ModbusRTU slaveId 必须是 1-247 的整数";
                        break;
                    case ProtocolType.ModbusTCP:
                        if (!root.TryGetProperty("ip", out var ip) || ip.ValueKind != System.Text.Json.JsonValueKind.String
                         || !System.Net.IPAddress.TryParse(ip.GetString(), out var ipAddr)
                         || ipAddr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                            return "ModbusTCP 必须包含合法 IPv4 地址（如 192.168.1.10）";
                        if (!root.TryGetProperty("port", out var tport) || tport.ValueKind != System.Text.Json.JsonValueKind.Number
                         || tport.GetInt32() is < 1 or > 65535)
                            return "ModbusTCP port 必须是 1-65535 的整数";
                        if (!root.TryGetProperty("slaveId", out var tsid) || tsid.ValueKind != System.Text.Json.JsonValueKind.Number
                         || tsid.GetInt32() is < 1 or > 247)
                            return "ModbusTCP slaveId 必须是 1-247 的整数";
                        break;
                    case ProtocolType.MQTT:
                        // Z 循环（2026-09-11）：MQTT 移出通讯页——设备协议不再承载 MQTT 连接（连接参数内联
                        // MqttConnection.Config，专用 MQTT 根多连接管理器）；ProtocolType.MQTT 保留 deprecated
                        // （旧工程反序列化兼容），configure/update 直达一律拒绝防绕过
                        return "MQTT 已移出通讯配置页——请在项目树「MQTT」根 →「连接管理」页点「＋ 新建连接」配置（连接参数内联，2026-09-11）";
                }
                return null;
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.InvalidOperationException or System.FormatException)
            {
                // FormatException：GetInt32 遇超 Int32 范围/小数（如 99999999999、9600.5）——统一归为参数非法，勿逃逸成 COMMAND_CRASH
                return "connection_info 不是合法 JSON: " + ex.Message;
            }
        }
    }

    /// <summary>配置设备通信参数。</summary>
    public class ConfigureDeviceHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "configure_device", Description = "配置设备通信参数",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "设备名称" },
                ["protocol"] = new() { Type = "enum", Required = true, EnumValues = new[] { "ModbusRTU", "ModbusTCP" }, Description = "通信协议（Z 循环：MQTT 移出通讯页——专用 MQTT 根配置）" },
                ["connection_info"] = new() { Type = "string", Required = true, Description = "连接信息 (JSON)" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())
             || !p.ContainsKey("protocol") || string.IsNullOrWhiteSpace(p["protocol"]?.ToString())
             || !p.ContainsKey("connection_info") || string.IsNullOrWhiteSpace(p["connection_info"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name/protocol/connection_info");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            if (!Enum.TryParse<ProtocolType>(p["protocol"]!.ToString(), ignoreCase: true, out var pt)
             || !Enum.IsDefined(pt))
                return CommandResult.Fail("INVALID_PARAM", $"未知协议: {p["protocol"]}");
            // 参数校验前置（JSON 语法/协议字段先于查重：错误消息更精准）
            var ci = p["connection_info"]!.ToString()!;
            var ciError = DeviceConnectionInfoValidator.Validate(pt, ci);
            if (ciError != null) return CommandResult.Fail("INVALID_PARAM", ciError);
            if (project.Devices.Any(d => d.Name == name))
                return CommandResult.Fail("DUPLICATE", $"设备 \"{name}\" 已存在");
            project.Devices.Add(new DeviceConfig { Name = name, Protocol = pt, ConnectionInfo = ci });
            return CommandResult.Ok(new { device_name = name });
        }
    }

    /// <summary>连接 HMI 设备（HTTP GET /api/device/info 验证型号/尺寸，经 DeviceConnectionService 服务单点建立会话）。</summary>
    public class ConnectHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "connect", Description = "连接 HMI 设备", RequiresConnection = false,
            Parameters = new()
            {
                ["ip"] = new() { Type = "string", Required = true, Description = "设备 IP 地址" },
                ["model"] = new() { Type = "string", DefaultValue = "NavigatorHMI-7", Description = "设备型号（device-profile 已知型号）" },
                ["size_inch"] = new() { Type = "string", Description = "设备尺寸（7寸/4寸，profile 校验）", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("ip") || string.IsNullOrWhiteSpace(p["ip"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: ip");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var ip = p["ip"]!.ToString()!;
            var model = p.GetValueOrDefault("model")?.ToString() ?? "NavigatorHMI";
            var sizeInch = p.GetValueOrDefault("size_inch")?.ToString();
            // K-2：真实连接经 DeviceConnectionService（HTTP GET /api/device/info；K-8b 端点就绪前 UseStub 可模拟）
            var result = DeviceConnectionService.TestConnection(ip, model, sizeInch);
            if (!result.Success)
                return CommandResult.Fail(
                    result.Status == DeviceTestStatus.DeviceMismatch ? "DEVICE_MISMATCH" : "CONNECTION_FAILED",
                    result.Message);
            var s = result.Session!;
            return CommandResult.Ok(new { ip = s.Ip, model = s.Model, size_inch = s.SizeInch, firmware_version = s.FirmwareVersion, status = "connected" });
        }
    }

    /// <summary>断开设备连接（幂等；CLI 对等 disconnect 命令，K-2）。</summary>
    public class DisconnectHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "disconnect", Description = "断开设备连接", RequiresConnection = false,
            Parameters = new()
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => ValidationResult.Ok;
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            DeviceConnectionService.Disconnect();
            return CommandResult.Ok(new { status = "disconnected" });
        }
    }

    /// <summary>扫描网络中可用的 HMI 设备（2026-08-30 L 循环实现：设计文档方案 B HTTP 网段扫描——/24 子网遍历 + GET /api/device/info）。</summary>
    public class ScanDevicesHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "scan_devices", Description = "扫描网络中可用的 HMI 设备", RequiresConnection = false,
            Parameters = new() { ["nic"] = new() { Type = "string", DefaultValue = "eth0", Description = "网卡名称" } }
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => ValidationResult.Ok;
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var nic = p.GetValueOrDefault("nic")?.ToString() ?? "eth0";
            var devices = DeviceScanner.Scan(nic);
            return CommandResult.Ok(new { nic, devices, count = devices.Count, message = $"扫描完成，发现 {devices.Count} 台设备" });
        }
    }

    /// <summary>下载工程文件到设备。K-3c：deploy 前置自动编译门禁——编译成功才允许传输；打包 zip 部署容器（K-4/K-8 接传输）。</summary>
    public class DeployProjectHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "deploy_project", Description = "下载工程文件到设备", RequiresConnection = true,
            Parameters = new()
            {
                ["device_ip"] = new() { Type = "string", Required = true, Description = "目标设备 IP" },
                ["file_path"] = new() { Type = "string", Required = false, Description = "工程文件路径（兼容保留，当前恒自动编译当前工程）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("device_ip") || string.IsNullOrWhiteSpace(p["device_ip"]?.ToString())) return ValidationResult.Fail("缺少必填参数: device_ip");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            // K-3c：deploy 前置自动编译门禁——编译失败拒绝传输（compile-download §1.3 强耦合声明）
            var compileResult = ProjectGenerator.Compile(project);
            if (compileResult.HasErrors)
                return CommandResult.Fail("COMPILE_FAILED",
                    $"编译失败，拒绝传输: {string.Join("; ", compileResult.Errors.Take(5))}");

            var projectDir = Path.GetDirectoryName(project.ProjectFilePath) ?? ".";

            // N-1：锁定视角底图生成（PC 拼单张 PNG 随包下发；缓存 = 组态软件 map-cache/ + 工程 tiles/ 已有瓦片；
            // 无缓存瓦片且联网不可用 → 拒绝部署（用户 2026-08-30 定：不用模拟底图）
            // 审查 🟡：map-cache/ 无条件创建并加入缓存——首次部署联网下载的瓦片写入该目录，后续无网可复用
            try
            {
                var caches = new List<string>();
                string mapCache = Path.Combine(AppContext.BaseDirectory, "map-cache");
                Directory.CreateDirectory(mapCache);   // 无条件创建（下载瓦片恒写此目录，供后续无网复用）
                caches.Add(mapCache);
                string projTiles = Path.Combine(projectDir, "tiles");
                if (Directory.Exists(projTiles)) caches.Add(projTiles);
                var shotErr = WorldMapScreenshotGenerator.Generate(project, projectDir, caches, allowNetwork: true);
                if (shotErr != null)
                    return CommandResult.Fail("MAPSHOT_FAILED", $"世界地图底图生成失败：{shotErr}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return CommandResult.Fail("MAPSHOT_FAILED", $"世界地图底图生成异常: {ex.Message}");
            }

            // 打包部署容器（manifest + app + res，zip）；失败转明确错误码（对齐 compile-download §2.3 IO 异常明确报错）
            // P-6（2026-09-04 审查修正）：InvalidOperationException = 64MB 视频护栏受控业务异常（DeploymentPackageBuilder
            // 抛）——必须落 PACKAGE_FAILED 而非 COMMAND_CRASH（错误码字典：受控业务失败归明确错误码）
            string deployZip;
            try
            {
                deployZip = DeploymentPackageBuilder.Build(project, compileResult.OutputPath!, Path.Combine(projectDir, "output"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return CommandResult.Fail("PACKAGE_FAILED", $"部署包构建失败: {ex.Message}");
            }

            // K-5：真实 HTTP 传输（POST /api/transfer——FW 接收端校验+原子替换+工程重载；失败一次直接报错不重试）
            // D-B4/T-1a：可选进度参数——progressEx（Func<DeployProgressInfo,bool>，GUI 新回调——含当前文件/字节）
            // 或 progress（Func<int,string,bool> 旧）——GUI 传轮询回调（设备端真实进度），CLI 不传。
            // T-1a（2026-09-05）：DeployProjectAsync = 磁盘预检（cap=空闲×2/3）+ >64MB 分块（新 FW）+ ≤64MB 整包兼容
            var ip = p["device_ip"]!.ToString()!;
            var progressEx = p.TryGetValue("progressEx", out var progEx) ? progEx as Func<HttpDownloadClient.DeployProgressInfo, bool> : null;
            var progressCb = p.TryGetValue("progress", out var prog) ? prog as Func<int, string, bool> : null;
            var transfer = progressEx != null
                ? HttpDownloadClient.DeployProjectAsync(ip, deployZip, progressEx).GetAwaiter().GetResult()
                : progressCb != null
                    ? HttpDownloadClient.DeployProjectAsync(ip, deployZip, info => progressCb(info.Percent, info.Stage)).GetAwaiter().GetResult()
                    : HttpDownloadClient.DeployProjectAsync(ip, deployZip).GetAwaiter().GetResult();
            if (!transfer.Success)
                return CommandResult.Fail(transfer.Code, $"部署传输失败: {transfer.Message}");

            return CommandResult.Ok(new { package = deployZip, message = transfer.Message });
        }
    }

    /// <summary>设备闪烁指令（blink_device <ip> on|off——K-9 FW 覆盖层闪烁 ~1s；多设备定位）。</summary>
    public class BlinkDeviceHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "blink_device", Description = "设备闪烁（定位）", RequiresConnection = true,
            Parameters = new()
            {
                ["ip"] = new() { Type = "string", Required = true, Description = "目标设备 IP" },
                ["enable"] = new() { Type = "enum", Required = true, EnumValues = new[] { "on", "off" }, Description = "on=开始闪烁 / off=停止" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("ip") || string.IsNullOrWhiteSpace(p["ip"]?.ToString())) return ValidationResult.Fail("缺少必填参数: ip");
            if (!p.ContainsKey("enable") || (p["enable"]?.ToString() is not ("on" or "off"))) return ValidationResult.Fail("enable 必须为 on 或 off");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var ip = p["ip"]!.ToString()!;
            var enable = p["enable"]!.ToString() == "on";
            var result = HttpDownloadClient.BlinkAsync(ip, enable).GetAwaiter().GetResult();
            if (!result.Success) return CommandResult.Fail(result.Code, $"闪烁指令失败: {result.Message}");
            return CommandResult.Ok(new { blink = enable });
        }
    }

    /// <summary>VNC 运行时启停（vnc <ip> on|off——K-9 FW 运行时启停 5900，不重启工程）。</summary>
    public class VncHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "vnc", Description = "VNC 运行时启停", RequiresConnection = true,
            Parameters = new()
            {
                ["ip"] = new() { Type = "string", Required = true, Description = "目标设备 IP" },
                ["enable"] = new() { Type = "enum", Required = true, EnumValues = new[] { "on", "off" }, Description = "on=启用 / off=停用" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("ip") || string.IsNullOrWhiteSpace(p["ip"]?.ToString())) return ValidationResult.Fail("缺少必填参数: ip");
            if (!p.ContainsKey("enable") || (p["enable"]?.ToString() is not ("on" or "off"))) return ValidationResult.Fail("enable 必须为 on 或 off");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var ip = p["ip"]!.ToString()!;
            var enable = p["enable"]!.ToString() == "on";
            var result = HttpDownloadClient.VncAsync(ip, enable).GetAwaiter().GetResult();
            if (!result.Success) return CommandResult.Fail(result.Code, $"VNC 指令失败: {result.Message}");
            return CommandResult.Ok(new { vnc = enable });
        }
    }

    /// <summary>下载固件到设备并触发 OTA（D 循环批 2 D1 落地：.fw 版本前置检查 + 上传 /api/transfer）。
    /// 链路：file_path 定位 .fw（未指定→默认目录扫描）→ 版本检查（GET /api/device/info version vs .fw header version，
    /// 一致跳过/旧→提示）→ POST /api/transfer（FW 区分 .navihmi/.fw 走 OTA 安装）→ 进度回调（可选）。</summary>
    public class DeployFirmwareHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "deploy_firmware", Description = "下载固件到设备", RequiresConnection = true,
            Parameters = new()
            {
                ["device_ip"] = new() { Type = "string", Required = true, Description = "目标设备 IP" },
                ["file_path"] = new() { Type = "string", Required = false, Description = "固件文件路径（.fw；未指定→默认目录最新）" },
                ["progress"] = new() { Type = "object", Required = false, Description = "进度回调（GUI 内部用）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("device_ip") || string.IsNullOrWhiteSpace(p["device_ip"]?.ToString())) return ValidationResult.Fail("缺少必填参数: device_ip");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var ip = p["device_ip"]!.ToString()!;

            // 1. 定位 .fw 文件（未指定 → 默认目录最新，兼容新旧命名 NavigatorHMI_v*.fw / NavigatorHMI_7inch_v*.fw）
            var fwPath = p.TryGetValue("file_path", out var fp) && fp is string s && !string.IsNullOrWhiteSpace(s)
                ? s : FindLatestFw(AppContext.BaseDirectory);
            if (fwPath == null || !File.Exists(fwPath))
                return CommandResult.Fail("FILE_NOT_FOUND", "固件文件不存在（请提供 file_path 或确认默认目录有 .fw 产物）");
            if (!fwPath.EndsWith(".fw", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Fail("INVALID_PARAM", $"固件文件必须是 .fw 格式: {fwPath}");

            // 2. 读 .fw header（magic 4B + version 16B @4 + timestamp 8B LE @20——FwPackageBuilder 单一事实源；
            //    <28B 损坏——审查 🟡 防短文件误报魔数非法）
            //    2026-09-04 调试 OTA：timestamp = 打包时刻（Unix 秒），调试包同版 v1.1.0 覆盖判断依据
            string fwVersion;
            long fwTs = 0;
            try
            {
                var fileLen = new FileInfo(fwPath).Length;
                if (fileLen < FwPackageBuilder.HeaderSize)
                    return CommandResult.Fail("INVALID_PARAM", $".fw 文件损坏（过短 {fileLen}B，至少需完整 header {FwPackageBuilder.HeaderSize}B——与 FW 端 fail-fast 对齐）: {fwPath}");
                using var fs = File.OpenRead(fwPath);
                var magic = new byte[4];
                if (fs.Read(magic, 0, 4) != 4)
                    return CommandResult.Fail("INVALID_PARAM", $".fw 文件读取失败（magic 读不完整）: {fwPath}");
                if (System.Text.Encoding.ASCII.GetString(magic) != FwPackageBuilder.Magic)
                    return CommandResult.Fail("INVALID_PARAM", $".fw 魔数非法（非 NHFW 包）: {fwPath}");
                var vbuf = new byte[16];
                if (fs.Read(vbuf, 0, 16) != 16)
                    return CommandResult.Fail("INVALID_PARAM", $".fw 文件损坏（version 段读不完整）: {fwPath}");
                fwVersion = System.Text.Encoding.ASCII.GetString(vbuf).Trim();
                var tsBuf = new byte[8];
                if (fs.Read(tsBuf, 0, 8) != 8)
                    return CommandResult.Fail("INVALID_PARAM", $".fw 文件损坏（timestamp 段读不完整）: {fwPath}");
                fwTs = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(tsBuf);   // LE（与 FwPackageBuilder WriteInt64LittleEndian 一致，消除平台字节序假设）
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return CommandResult.Fail("INVALID_PARAM", $"固件文件读取失败: {ex.Message}");
            }

            // 3. 版本检查前置（GET /api/device/info version = 固件版本，术语口径 FW httreceiver）
            // B-4 修复（2026-08-30）：同/旧判定统一走语义比较——设备端 version 返回 "vX.Y.Z"（带 v 前缀），
            // 若用字符串精确相等判同版本，"v1.1.1" vs "1.1.1" 永不相等 → VERSION_SAME 永不触发 → 同版本重复升级。
            // 2026-09-04 调试 OTA：调试包（文件名 NavigatorHMI_v1.1.0_<inch>_<14位打包时刻>.fw——pack_fw --name-ts）
            // 版本恒 v1.1.0，语义比较无意义——改按**打包时刻先后**判断（用户 2026-09-04 裁决「按打包时间」）：
            // 包 header timestamp > 设备当前 firmware_ts → 放行覆盖（后打的包覆盖前一个）；否则拒。
            // 旧固件无 firmware_ts（设备侧报 "0"）→ 任意非 0 时刻调试包放行（首次装载场景）。
            // 非调试包（标准/旧命名）保持语义比较不变（正式升级防呆）。
            // 判定边界（IsDebugPackName，文件名启发式）：官方工具链 pack_fw.py --name-ts 输出恒定 14 位数字尾段；
            // ⚠️ 假阳性路径：正式包被人为追加 14 位数字尾会误入调试分支（语义闸门被旁路）——工具链不产此名，风险接受；
            // ⚠️ 假阴性路径：非 14 位数字尾的调试中间命名走语义分支（同版会被 VERSION_SAME 拒并提示——见消息）
            bool debugPack = IsDebugPackName(fwPath);
            try
            {
                var (ok, devVer, devTs, err) = HttpDownloadClient.GetDeviceInfoAsync(ip).GetAwaiter().GetResult();
                if (!ok)
                    return CommandResult.Fail("VERSION_CHECK_FAILED", $"固件版本查询失败: {err}");
                if (!string.IsNullOrEmpty(devVer))
                {
                    if (debugPack)
                    {
                        var devTsNum = long.TryParse(devTs, out var dts) ? dts : 0L;
                        if (fwTs <= devTsNum)
                            return CommandResult.Fail("VERSION_SAME",
                                $"调试固件 {fwVersion}（打包时刻 {FormatPackTs(fwTs)}）不新于设备当前固件打包时刻 {FormatPackTs(devTsNum)}——已是最新或时刻倒挂，已拒绝");
                        // fwTs > devTsNum → 放行（调试包按打包时刻先后覆盖）
                    }
                    else
                    {
                        var cmp = CompareVersions(fwVersion, devVer);
                        if (cmp == 0)
                            return CommandResult.Fail("VERSION_SAME",
                                $"设备已是固件 {fwVersion}，无需升级（若为调试固件请确认文件名带 14 位打包时刻尾——pack_fw.py --name-ts，否则同版按语义防呆拒绝）");
                        if (cmp < 0)
                            return CommandResult.Fail("VERSION_OLDER", $"固件 {fwVersion} 旧于设备当前 {devVer}，已拒绝（如需降级请人工确认）");
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return CommandResult.Fail("UNREACHABLE", $"无法连接设备 {ip}: {ex.Message}");
            }

            // 4. 上传（POST /api/transfer——FW 魔数区分 .navihmi/.fw 走 OTA 安装；进度回调可选）
            try
            {
                var progressCb = p.TryGetValue("progress", out var prog) ? prog as Func<int, string, bool> : null;
                var transfer = progressCb != null
                    ? HttpDownloadClient.DeployAsync(ip, fwPath, progressCb).GetAwaiter().GetResult()
                    : HttpDownloadClient.DeployAsync(ip, fwPath).GetAwaiter().GetResult();
                if (!transfer.Success)
                    return CommandResult.Fail(transfer.Code, $"固件传输失败: {transfer.Message}");
                return CommandResult.Ok(new { package = fwPath, version = fwVersion, message = transfer.Message });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                return CommandResult.Fail("UNREACHABLE", $"固件传输失败: {ex.Message}");
            }
        }

        /// <summary>默认目录查找最新 .fw（新标准 NavigatorHMI_&lt;尺寸&gt;inch_v&lt;版本&gt;.fw；兼容旧 NavigatorHMI_v&lt;版本&gt;.fw——
        /// 按**语义版本**降序取最新，审查 🔴 修复：原字典序使 v1.10.0 &lt; v1.9.0 误取旧版；同版本冲突回退文件名序保证确定性）。
        /// 2026-09-04 调试 OTA：同语义版本且含 14 位打包时刻尾段（多个 v1.1.0 调试包并存）→ 按时刻**降序**取最新打的包
        /// （否则文件名序取最早包，前置检查时刻倒挂误拒——「每次编完都 OTA」默认扫描路径需选最新）。</summary>
        internal static string? FindLatestFw(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            // 匹配两种命名：NavigatorHMI_v1.2.3.fw（旧）与 NavigatorHMI_7inch_v1.2.3.fw（新）——共同特征 = "NavigatorHMI_" 开头 + 含 "_v"
            return Directory.EnumerateFiles(dir, "NavigatorHMI_*.fw")
                .Where(f => Path.GetFileName(f).Contains("_v", StringComparison.Ordinal))
                .OrderByDescending(f => ParseFwVersion(f))
                .ThenByDescending(f => DebugPackTimestamp(f))   // 2026-09-04：同版本调试包按打包时刻降序（非调试包尾段 0）
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)   // 全同确定性
                .FirstOrDefault();
        }

        /// <summary>2026-09-04 调试包判定：文件名尾段为 14 位纯数字打包时刻（pack_fw.py --name-ts 输出
        /// NavigatorHMI_v1.1.0_&lt;inch&gt;_&lt;YYYYMMDDHHMMSS&gt;.fw 特征；标准/旧命名尾段非 14 位数字）。
        /// 启发式边界：假阳性=正式包被追加 14 位数字尾（工具链不产）；假阴性=非 14 位调试中间命名（走语义分支）。</summary>
        internal static bool IsDebugPackName(string fwPath)
        {
            var fn = Path.GetFileNameWithoutExtension(fwPath);
            var lastSeg = fn.LastIndexOf('_');
            return lastSeg > 0 && fn.Length - lastSeg - 1 == 14
                && fn[(lastSeg + 1)..].All(char.IsAsciiDigit);
        }

        /// <summary>调试包文件名尾段打包时刻（14 位 YYYYMMDDHHMMSS → Unix 秒）；非调试命名/解析失败 → 0（tie-break 沉底）。</summary>
        private static long DebugPackTimestamp(string fwPath)
        {
            if (!IsDebugPackName(fwPath)) return 0;
            var fn = Path.GetFileNameWithoutExtension(fwPath);
            var tsSeg = fn[(fn.LastIndexOf('_') + 1)..];   // YYYYMMDDHHMMSS（本地时刻）
            if (!DateTime.TryParseExact(tsSeg, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return 0;
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }

        /// <summary>Unix 秒 → 本地时刻文本（YYYY-MM-dd HH:mm:ss；消息可读——与调试包文件名 14 位时刻同为本地基准）。</summary>
        private static string FormatPackTs(long unixSec)
        {
            if (unixSec <= 0) return "0（旧固件无记录）";
            return DateTimeOffset.FromUnixTimeSeconds(unixSec).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>从 .fw 文件名提取语义版本元组（NavigatorHMI_v1.2.3.fw 或 NavigatorHMI_7inch_v1.2.3.fw → [1,2,3]；
        /// 从 "_v" 后截取——两种命名均含 "_v"；解析失败 → [0,0,0] 沉底）。</summary>
        internal static (int, int, int) ParseFwVersion(string fwPath)
        {
            var name = Path.GetFileNameWithoutExtension(fwPath);   // NavigatorHMI_v1.2.3
            var idx = name.IndexOf("_v", StringComparison.Ordinal);
            if (idx < 0) return (0, 0, 0);
            var ver = name[(idx + 2)..];
            return CompareVersionsTuple(ver);
        }

        /// <summary>语义版本 → 三段数字元组（缺段/非数字段按 0）。</summary>
        internal static (int, int, int) CompareVersionsTuple(string v)
        {
            var segs = v.Split('.');
            int Get(int i) => i < segs.Length && int.TryParse(segs[i], out var n) ? n : 0;
            return (Get(0), Get(1), Get(2));
        }

        /// <summary>语义版本比较（x.y.z 三段数字；不同长度按缺失段 0 处理）。返回负数=a&lt;b。
        /// B-4 修复（2026-08-30）：归一 "v" 前缀——设备端 /api/device/info version 返回 "vX.Y.Z"
        /// （FW DeviceInfo::appVersion = "v"+常量），.fw header version 为 "X.Y.Z"——不归一则设备版本
        /// 首段 "v1" 解析为 0 → 设备永远判 0 → VERSION_SAME/VERSION_OLDER 永不触发（同版本仍升级）。</summary>
        internal static int CompareVersions(string a, string b)
        {
            int[] Parse(string s) => s.Split('.').Select(seg =>
                int.TryParse(seg.TrimStart('v'), out var n) ? n : 0).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
            var pa = Parse(a);
            var pb = Parse(b);
            for (int i = 0; i < 3; i++)
                if (pa[i] != pb[i]) return pa[i] < pb[i] ? -1 : 1;
            return 0;
        }
    }

    /// <summary>更新设备通信配置。仅更新提供的字段；重命名做唯一性校验。</summary>
    public class UpdateDeviceHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_device", Description = "更新设备通信配置",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "原设备名" },
                ["new_name"] = new() { Type = "string", Description = "新设备名（重命名）", KeepInCompact = true },
                ["protocol"] = new() { Type = "enum", EnumValues = new[] { "ModbusRTU", "ModbusTCP" }, Description = "通信协议（Z 循环：MQTT 移出通讯页）", KeepInCompact = true },
                ["connection_info"] = new() { Type = "string", Description = "连接信息 (JSON)", KeepInCompact = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var device = project.Devices.FirstOrDefault(d => d.Name == name);
            if (device == null) return CommandResult.Fail("NOT_FOUND", $"设备 \"{name}\" 不存在");

            // 先全量校验后统一应用（防部分更新：任一步失败不改模型——CommandService 原子性纪律）
            string? newName = null;
            if (p.TryGetValue("new_name", out var nn) && nn != null && !string.IsNullOrWhiteSpace(nn.ToString()) && nn.ToString() != name)
            {
                newName = nn.ToString()!;
                if (project.Devices.Any(d => d.Name == newName))
                    return CommandResult.Fail("DUPLICATE", $"设备 \"{newName}\" 已存在");
            }
            ProtocolType? newProto = null;
            if (p.TryGetValue("protocol", out var pt) && pt != null && !string.IsNullOrWhiteSpace(pt.ToString()))
            {
                if (!Enum.TryParse<ProtocolType>(pt.ToString(), ignoreCase: true, out var proto)
                 || !Enum.IsDefined(proto))
                    return CommandResult.Fail("INVALID_PARAM", $"未知协议: {pt}");
                if (proto == ProtocolType.MQTT)   // Z 循环：MQTT 移出通讯页（Enum.IsDefined 仍真——显式拦防绕过）
                    return CommandResult.Fail("INVALID_PARAM", "MQTT 已移出通讯配置页——请在项目树「MQTT」根配置连接（Z 循环 2026-09-11）");
                newProto = proto;
            }
            if (p.TryGetValue("connection_info", out var ci) && ci != null && !string.IsNullOrWhiteSpace(ci.ToString()))
            {
                // 校验用目标协议（本次更新后）：newProto 优先，未提供用设备现值
                var targetProto = newProto ?? device.Protocol;
                var ciError = DeviceConnectionInfoValidator.Validate(targetProto, ci.ToString()!);
                if (ciError != null) return CommandResult.Fail("INVALID_PARAM", ciError);
            }
            // 全部校验通过，统一应用
            if (newName != null)
            {
                // Z 循环：DeviceName deprecated（MQTT 连接参数内联）——设备改名不再级联 MQTT（原 Y Check review 🟡 级联已随语义废弃移除）
                device.Name = newName;
            }
            if (newProto != null) device.Protocol = newProto.Value;
            if (p.TryGetValue("connection_info", out var ci2) && ci2 != null && !string.IsNullOrWhiteSpace(ci2.ToString()))
                device.ConnectionInfo = ci2.ToString()!;

            return CommandResult.Ok(new { device_name = device.Name });
        }
    }

    /// <summary>删除设备通信配置。</summary>
    public class DeleteDeviceHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_device", Description = "删除设备通信配置",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "设备名" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            var device = project.Devices.FirstOrDefault(d => d.Name == name);
            if (device == null) return CommandResult.Fail("NOT_FOUND", $"设备 \"{name}\" 不存在");
            // Z 循环：DeviceName deprecated（MQTT 连接参数内联 MqttConnection）——删除设备无 MQTT IN_USE 守卫
            // （原 Y Check review 🟡 F1 死锁清理：旧工程 MQTT 设备随 Z-4 迁移移除，无残留引用场景）
            project.Devices.Remove(device);
            return CommandResult.Ok(new { device_name = name });
        }
    }

    /// <summary>列出全部设备（Y-3a 2026-09-10 新增——对齐 GUI 通讯表格，v1.1 设备清单无 CLI 查询口缺口补齐）。
    /// 输出：name/protocol/连接摘要（MQTT 掩码凭据与敏感字段——绝不明文回显；password 显示为 [已加密]）。</summary>
    public class ListDevicesHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "list_devices", Description = "列出全部设备（含连接摘要；MQTT 凭据掩码）",
            Parameters = new()
        };
        public ValidationResult Validate(Dictionary<string, object?> p) => ValidationResult.Ok;
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var list = project.Devices.Select(d => new
            {
                name = d.Name,
                protocol = d.Protocol.ToString(),
                summary = Summarize(d),
            }).ToList();
            return CommandResult.Ok(new { count = list.Count, devices = list });
        }

        /// <summary>连接摘要（JSON 解析失败/未知协议回退原始串；MQTT 掩码 password/enableTls 之外的敏感键）。</summary>
        private static string Summarize(DeviceConfig d)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(d.ConnectionInfo) ? "{}" : d.ConnectionInfo);
                var root = doc.RootElement;
                if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return d.ConnectionInfo;
                switch (d.Protocol)
                {
                    case ProtocolType.ModbusRTU:
                        return $"port={GetStr(root, "port")} baud={GetNum(root, "baud")} slaveId={GetNum(root, "slaveId")}";
                    case ProtocolType.ModbusTCP:
                        return $"ip={GetStr(root, "ip")} port={GetNum(root, "port")} slaveId={GetNum(root, "slaveId")}";
                    case ProtocolType.MQTT:
                        // Y-3a：掩码凭据——password 只显示状态不显示内容（防 CLI 日志/回显泄露；ASCII 标记防 unicode 转义歧义）
                        var pwd = root.TryGetProperty("password", out var pw) && pw.ValueKind == System.Text.Json.JsonValueKind.String
                                  && !string.IsNullOrEmpty(pw.GetString()) ? "[encrypted]" : "";
                        return $"broker={GetStr(root, "broker")} port={GetNum(root, "port", 1883)} version={GetNum(root, "version", 0)}"
                               + (string.IsNullOrEmpty(pwd) ? "" : $" password={pwd}");
                    default:
                        return d.ConnectionInfo;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return d.ConnectionInfo;   // 非法 JSON：回退原始串（不静默吞——调用方已在上游校验过，此处兜底）
            }
        }

        private static string GetStr(System.Text.Json.JsonElement root, string key)
            => root.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";

        private static string GetNum(System.Text.Json.JsonElement root, string key, int fallback = 0)
            => root.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
               ? v.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)
               : fallback.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
