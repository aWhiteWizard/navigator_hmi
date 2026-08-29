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
                        if (!root.TryGetProperty("broker", out var br) || br.ValueKind != System.Text.Json.JsonValueKind.String)
                            return "MQTT 必须包含字符串 broker 字段";
                        break;
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
                ["protocol"] = new() { Type = "enum", Required = true, EnumValues = new[] { "ModbusRTU", "ModbusTCP", "MQTT" }, Description = "通信协议" },
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

            // 打包部署容器（manifest + app + res，zip）；失败转明确错误码（对齐 compile-download §2.3 IO 异常明确报错）
            string deployZip;
            try
            {
                var projectDir = Path.GetDirectoryName(project.ProjectFilePath) ?? ".";
                deployZip = DeploymentPackageBuilder.Build(project, compileResult.OutputPath!, Path.Combine(projectDir, "output"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return CommandResult.Fail("PACKAGE_FAILED", $"部署包构建失败: {ex.Message}");
            }

            // K-5：真实 HTTP 传输（POST /api/transfer——FW 接收端校验+原子替换+工程重载；失败一次直接报错不重试）
            var ip = p["device_ip"]!.ToString()!;
            var transfer = HttpDownloadClient.DeployAsync(ip, deployZip).GetAwaiter().GetResult();
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

    /// <summary>下载固件到设备并触发 OTA。</summary>
    public class DeployFirmwareHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "deploy_firmware", Description = "下载固件到设备", RequiresConnection = true,
            Parameters = new()
            {
                ["device_ip"] = new() { Type = "string", Required = true, Description = "目标设备 IP" },
                ["file_path"] = new() { Type = "string", Required = false, Description = "固件文件路径" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("device_ip") || string.IsNullOrWhiteSpace(p["device_ip"]?.ToString())) return ValidationResult.Fail("缺少必填参数: device_ip");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            // TODO: 真实固件部署需要文件传输 + OTA 触发。骨架模拟。
            return CommandResult.Ok(new { message = "固件部署成功（骨架模式）" });
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
                ["protocol"] = new() { Type = "enum", EnumValues = new[] { "ModbusRTU", "ModbusTCP", "MQTT" }, Description = "通信协议", KeepInCompact = true },
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
            if (newName != null) device.Name = newName;
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
            project.Devices.Remove(device);
            return CommandResult.Ok(new { device_name = name });
        }
    }
}
