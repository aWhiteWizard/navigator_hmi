using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
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
            if (!p.ContainsKey("name") || !p.ContainsKey("protocol") || !p.ContainsKey("connection_info"))
                return ValidationResult.Fail("缺少必填参数");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["name"]!.ToString()!;
            if (!Enum.TryParse<ProtocolType>(p["protocol"]!.ToString(), ignoreCase: true, out var pt))
                return CommandResult.Fail("INVALID_PARAM", $"未知协议: {p["protocol"]}");
            if (project.Devices.Any(d => d.Name == name))
                return CommandResult.Fail("DUPLICATE", $"设备 \"{name}\" 已存在");
            project.Devices.Add(new DeviceConfig { Name = name, Protocol = pt, ConnectionInfo = p["connection_info"]!.ToString()! });
            return CommandResult.Ok(new { device_name = name });
        }
    }

    /// <summary>连接 HMI 设备（HTTP GET /api/device/info 验证型号）。</summary>
    public class ConnectHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "connect", Description = "连接 HMI 设备", RequiresConnection = false,
            Parameters = new()
            {
                ["ip"] = new() { Type = "string", Required = true, Description = "设备 IP 地址" },
                ["model"] = new() { Type = "string", DefaultValue = "NavigatorHMI", Description = "设备型号" },
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
            // TODO: 真实 HTTP 连接需要 HttpClient。骨架阶段模拟成功。
            var ip = p["ip"]!.ToString()!;
            var model = p.GetValueOrDefault("model")?.ToString() ?? "NavigatorHMI";
            return CommandResult.Ok(new { ip, model, status = "connected", message = "连接成功（骨架模式）" });
        }
    }

    /// <summary>扫描网络中可用的 HMI 设备。</summary>
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
            // TODO: 真实网络扫描需要子网遍历。骨架返回空列表。
            return CommandResult.Ok(new { nic, devices = Array.Empty<object>(), message = "扫描完成（骨架模式，未发现设备）" });
        }
    }

    /// <summary>下载工程文件到设备。</summary>
    public class DeployProjectHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "deploy_project", Description = "下载工程文件到设备", RequiresConnection = true,
            Parameters = new()
            {
                ["device_ip"] = new() { Type = "string", Required = true, Description = "目标设备 IP" },
                ["file_path"] = new() { Type = "string", Required = false, Description = "工程文件路径（空=自动编译最新）" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("device_ip")) return ValidationResult.Fail("缺少必填参数: device_ip");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            // TODO: 真实部署需要编译 + HTTP POST。骨架模拟。
            return CommandResult.Ok(new { message = "部署成功（骨架模式）" });
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
            if (!p.ContainsKey("device_ip")) return ValidationResult.Fail("缺少必填参数: device_ip");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            // TODO: 真实固件部署需要文件传输 + OTA 触发。骨架模拟。
            return CommandResult.Ok(new { message = "固件部署成功（骨架模式）" });
        }
    }
}
