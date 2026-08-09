using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>P1-10 打开工程自动预置三组测试。</summary>
public class DefaultGroupsTests
{
    [Fact]
    public void CommandService构造_空组自动预置三组()
    {
        var p = new HMIProject();   // 旧工程：无组
        _ = new CommandService(p);
        Assert.Equal(3, p.Groups.Count);
        Assert.Contains(p.Groups, g => g.Name == "管理员");
        Assert.Contains(p.Groups, g => g.Name == "操作员");
        Assert.Contains(p.Groups, g => g.Name == "访客");
    }

    [Fact]
    public void EnsureDefaultGroups_已有组不重复()
    {
        var p = new HMIProject();
        p.Groups.Add(new UserGroup { Name = "自定义组" });
        CommandService.EnsureDefaultGroups(p);
        Assert.Single(p.Groups);   // 已有组 → 不追加预置
        Assert.Equal("自定义组", p.Groups[0].Name);
    }
}
