using NavigatorHMI.Common;
using NavigatorHMI.Views;
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NavigatorHMI.Tests
{
    /// <summary>
    /// 2026-09-16：变量对话框「来源/地址」交互的运行期测试（STA）。
    /// 覆盖两条只在 UI 上体现、命令层/编译门禁抓不到的行为：
    /// ① 打开**存量坏来源**的变量（如 `4x:0001`）必须当场红字提示"设备端无法识别"（否则用户只会看到值恒 0）；
    /// ② 地址框支持三种写法（裸数字 / 4x 记法 / 40002 式）并实时显示将落库的规范串。
    /// </summary>
    public class TagEditDialogSourceTests
    {
        private static void RunOnSta(Action action)
        {
            Exception? threadEx = null;
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { threadEx = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (threadEx != null)
                throw new InvalidOperationException("STA 线程内失败：" + threadEx, threadEx);
        }

        private static HMIProject Project()
        {
            var p = new HMIProject { Name = "对话框测试" };
            p.Devices.Add(new DeviceConfig
            {
                Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":100}",
            });
            return p;
        }

        [Fact]
        public void 打开存量坏来源_红字提示不可识别()
        {
            RunOnSta(() =>
            {
                var p = Project();
                var tag = new Tag { Name = "TCP_0001", DataType = TagDataType.UINT16, Source = "4x:0001", DeviceName = "PT100" };
                p.Tags.Add(tag);
                var dlg = new TagEditDialog(tag, p);
                try
                {
                    Assert.Equal(Visibility.Visible, dlg.ErrorBox.Visibility);
                    Assert.Contains("无法识别", dlg.ErrorBox.Text);
                    Assert.Contains("4x:0001", dlg.ErrorBox.Text);
                }
                finally { dlg.Close(); }
            });
        }

        [Fact]
        public void 地址框换算提示_4x记法显示规范串()
        {
            RunOnSta(() =>
            {
                var p = Project();
                var dlg = new TagEditDialog(null, p);
                try
                {
                    var devItem = dlg.SourceBox.Items.Cast<ComboBoxItem>()
                        .First(i => i.Tag is DeviceConfig d && d.Name == "PT100");
                    dlg.SourceBox.SelectedItem = devItem;          // 触发 SourceBox_SelectionChanged → 地址框显示
                    Assert.Equal(Visibility.Visible, dlg.AddressPanel.Visibility);

                    dlg.AddressBox.Text = "4x:0002";              // 4x 记法 = 保持寄存器第 2 个 = PDU 1
                    Assert.Contains("modbus://100/1", dlg.AddressHint.Text);

                    dlg.AddressBox.Text = "40002";                // PLC 式同上
                    Assert.Contains("modbus://100/1", dlg.AddressHint.Text);

                    dlg.AddressBox.Text = "3x:0001";              // 未实现的区 → 红字
                    Assert.Contains("保持寄存器", dlg.AddressHint.Text);

                    dlg.AddressBox.Text = "4x:0001";              // 用户实录写法 → PDU 0（与"温度在 PDU 1"相邻，提示可见）
                    Assert.Contains("modbus://100/0", dlg.AddressHint.Text);
                }
                finally { dlg.Close(); }
            });
        }
        [Fact]
        public void 同从站多设备_反推不静默改写归属()
        {
            // 复现实录：联调工程1 的 `ModbusTCP设备` 与 `ModbusRTU设备` 同为 slaveId=100——
            // 早期"按从站取第一个设备"会让打开 RTU 变量时归属被静默改成 TCP 设备（设备端换传输，三层防线都看不见）。
            // ⚠️ 构造顺序必须"非归属设备在前"才有判别力（否则"取首个候选"的旧逻辑也会碰巧选中归属设备）。
            RunOnSta(() =>
            {
                var p = new HMIProject { Name = "对话框反推顺序" };
                p.Devices.Add(new DeviceConfig            // ← 先加：非归属（TCP）
                {
                    Name = "TCP-GW", Protocol = ProtocolType.ModbusTCP,
                    ConnectionInfo = "{\"ip\":\"192.168.1.12\",\"port\":502,\"slaveId\":100}",
                });
                p.Devices.Add(new DeviceConfig            // ← 后加：归属（RTU），与上行同从站
                {
                    Name = "PT100", Protocol = ProtocolType.ModbusRTU,
                    ConnectionInfo = "{\"port\":\"/dev/ttyUSB0\",\"baud\":9600,\"slaveId\":100}",
                });
                var tag = new Tag { Name = "RTU_0001", DataType = TagDataType.UINT16, Source = "modbus://100/1", DeviceName = "PT100" };
                p.Tags.Add(tag);
                var dlg = new TagEditDialog(tag, p);
                try
                {
                    // 按名字定位到归属设备 PT100（旧实现会选中列表首项 TCP-GW）
                    Assert.Equal("PT100", ((DeviceConfig?)((ComboBoxItem)dlg.SourceBox.SelectedItem).Tag)?.Name);
                }
                finally { dlg.Close(); }

                // 归属名不存在（设备已删/为空）且多候选 ⇒ 不猜，明示让用户重选
                var p2 = Project();
                p2.Devices.Add(new DeviceConfig
                {
                    Name = "TCP-GW", Protocol = ProtocolType.ModbusTCP,
                    ConnectionInfo = "{\"ip\":\"192.168.1.12\",\"port\":502,\"slaveId\":100}",
                });
                var tag2 = new Tag { Name = "孤儿", DataType = TagDataType.UINT16, Source = "modbus://100/1", DeviceName = "已删除的设备" };
                p2.Tags.Add(tag2);
                var dlg2 = new TagEditDialog(tag2, p2);
                try
                {
                    Assert.Equal(Visibility.Visible, dlg2.ErrorBox.Visibility);
                    Assert.Contains("多个设备", dlg2.ErrorBox.Text);
                }
                finally { dlg2.Close(); }
            });
        }
    }
}
