using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

public class GroupParseTests
{
    [Fact]
    public void Parse_单权限()
    {
        var r = UserGroupHandlers.ParsePermissions("ScreenEdit");
        Assert.NotNull(r);
        Assert.Single(r!);
        Assert.Equal(UserPermission.ScreenEdit, r![0]);
    }

    [Fact]
    public void Parse_多权限逗号()
    {
        var r = UserGroupHandlers.ParsePermissions("ScreenEdit,AlarmAck");
        Assert.NotNull(r);
        Assert.Equal(2, r!.Count);
        Assert.Contains(UserPermission.AlarmAck, r);
    }

    [Fact]
    public void Parse_空串返回空列表()
    {
        var r = UserGroupHandlers.ParsePermissions("");
        Assert.NotNull(r);
        Assert.Empty(r!);
    }

    [Fact]
    public void Parse_非法返回null()
    {
        Assert.Null(UserGroupHandlers.ParsePermissions("BAD"));
    }

    [Fact]
    public void Parse_混合非法返回null()
    {
        Assert.Null(UserGroupHandlers.ParsePermissions("ScreenEdit,BAD"));
    }
}
