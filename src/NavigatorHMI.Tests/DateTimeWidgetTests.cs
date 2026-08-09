using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>P2-2 DateTime 格式联动 + 失焦保留测试。</summary>
public class DateTimeWidgetTests
{
    [Fact]
    public void DisplayText_绑定变量按格式格式化()
    {
        var p = new HMIProject();
        p.Tags.Add(new Tag { Name = "T1", DataType = TagDataType.DATETIME, BaseValue = "2026-05-01 10:30:00" });
        TagResolver.CurrentProject = p;   // 静态单工程（GUI 加载时设置）
        try
        {
            var w = new DateTimeWidget { BoundTag = "T1", Format = "yyyy-MM-dd" };
            Assert.Equal("2026-05-01", w.DisplayText);   // P2-2：格式联动
        }
        finally { TagResolver.CurrentProject = null; }
    }

    [Fact]
    public void DisplayText_绑定变量解析失败原样返回()
    {
        var p = new HMIProject();
        p.Tags.Add(new Tag { Name = "T2", DataType = TagDataType.DATETIME, BaseValue = "异常值" });
        TagResolver.CurrentProject = p;
        try
        {
            var w = new DateTimeWidget { BoundTag = "T2", Format = "yyyy-MM-dd" };
            Assert.Equal("异常值", w.DisplayText);
        }
        finally { TagResolver.CurrentProject = null; }
    }

    [Fact]
    public void DisplayText_未绑定恒实时不受Text影响()
    {
        // D2：未绑定任何参数时直接按格式显示当前时间（不再保留可手改的 Text 残留——Text 字段保留仅序列化兼容，不参与显示）
        var w = new DateTimeWidget { Text = "2026-08-09 09:30:00" };
        var v = w.DisplayText;
        Assert.StartsWith(DateTime.Now.Year.ToString(), v);   // 1Hz 实时当前时间（旧编辑值不再显示）
    }

    [Fact]
    public void DisplayText_未绑定默认文本走实时时间()
    {
        var w = new DateTimeWidget();   // Text 默认 "2026-01-01 00:00:00"
        var v = w.DisplayText;
        Assert.StartsWith(DateTime.Now.Year.ToString(), v);   // 1Hz 实时当前时间
    }

    [Fact]
    public void DisplayText_非法格式回退默认不抛异常()
    {
        var w = new DateTimeWidget { Format = "gggg" };   // 非法格式
        var v = w.DisplayText;   // 不应抛 FormatException
        Assert.False(string.IsNullOrEmpty(v));
    }

    [Fact]
    public void Text_改动通知DisplayText()
    {
        var w = new DateTimeWidget { Text = "2026-01-01 00:00:00" };
        var notified = false;
        w.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DateTimeWidget.DisplayText)) notified = true; };
        w.Text = "2026-08-09 12:00:00";
        Assert.True(notified);
    }

    [Fact]
    public void DisplayText_绑定全零基准值按格式字面替换()
    {
        var p = new HMIProject();
        p.Tags.Add(new Tag { Name = "TZ", DataType = TagDataType.DATETIME, BaseValue = "0000:00:00 00:00:00" });   // 任务8：全 0 默认基准值
        TagResolver.CurrentProject = p;
        try
        {
            var w = new DateTimeWidget { BoundTag = "TZ", Format = "yyyy-MM-dd HH:mm:ss" };
            Assert.Equal("0000-00-00 00:00:00", w.DisplayText);   // 分隔符跟控件格式（冒号→横线）
            var w2 = new DateTimeWidget { BoundTag = "TZ", Format = "yyyy/MM/dd" };
            Assert.Equal("0000/00/00", w2.DisplayText);
        }
        finally { TagResolver.CurrentProject = null; }
    }

    [Fact]
    public void DisplayText_绑定全零基准值改格式立即重算()
    {
        var p = new HMIProject();
        p.Tags.Add(new Tag { Name = "TZ2", DataType = TagDataType.DATETIME, BaseValue = "0000:00:00 00:00:00" });
        TagResolver.CurrentProject = p;
        try
        {
            var w = new DateTimeWidget { BoundTag = "TZ2", Format = "yyyy-MM-dd" };
            Assert.Equal("0000-00-00", w.DisplayText);
            w.Format = "yyyy年MM月dd日";   // 改格式 → 全 0 按新格式字面替换
            Assert.Equal("0000年00月00日", w.DisplayText);
        }
        finally { TagResolver.CurrentProject = null; }
    }
}
