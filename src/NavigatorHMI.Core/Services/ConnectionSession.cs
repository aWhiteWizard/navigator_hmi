namespace NavigatorHMI.Common
{
    /// <summary>
    /// 设备连接会话（K 循环 K-2，替代 CommandService.IsConnected bool）。
    /// 会话对象存在 = 已连接（<see cref="DeviceConnectionService.Disconnect"/> 置 null）；失败原因在 <see cref="DeviceTestResult"/>。
    /// CLI connect / GUI 设备管理面板 / 主窗口状态栏经 <see cref="DeviceConnectionService"/> 三方对等。
    /// </summary>
    public class ConnectionSession
    {
        /// <summary>构造连接会话。</summary>
        public ConnectionSession(string ip, string model, string? sizeInch, string? firmwareVersion, DateTime connectedAtUtc)
        {
            Ip = ip;
            Model = model;
            SizeInch = sizeInch;
            FirmwareVersion = firmwareVersion;
            ConnectedAtUtc = connectedAtUtc;
        }

        /// <summary>设备 IP 地址。</summary>
        public string Ip { get; }

        /// <summary>设备型号（device-profile 校验用，K-7 联动）。</summary>
        public string Model { get; }

        /// <summary>设备尺寸（7 寸/4 寸等，device-profile 校验用）。</summary>
        public string? SizeInch { get; }

        /// <summary>固件版本（GET /api/device/info 回显，K-4 面板在线版本回显）。</summary>
        public string? FirmwareVersion { get; }

        /// <summary>连接建立时间（UTC）。</summary>
        public DateTime ConnectedAtUtc { get; }
    }
}
