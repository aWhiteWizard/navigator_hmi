using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;

namespace NavigatorHMI.Tests;

/// <summary>P2-1 用户组 CRUD 命令测试。</summary>
public class GroupCommandsTests
{
    [Fact]
    public void CreateGroup_带权限成功()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        var r = svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["permissions"] = "ScreenEdit,AlarmAck" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        var g = p.Groups.First(x => x.Name == "工程师");
        Assert.Equal(2, g.Permissions.Count);
    }

    [Fact]
    public void CreateGroup_重名拒绝()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        Assert.True(svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师" }).Success);
        var r = svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师" });
        Assert.False(r.Success);
        Assert.Equal("DUPLICATE", r.ErrorCode);
    }

    [Fact]
    public void DeleteGroup_预设组拒绝()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);   // 构造预置三组
        var r = svc.Execute("delete_group", new Dictionary<string, object?> { ["group_name"] = "管理员" });
        Assert.False(r.Success);
        Assert.Equal("BLOCKED", r.ErrorCode);
        Assert.Equal(3, p.Groups.Count);   // 状态不变
    }

    [Fact]
    public void DeleteGroup_用户引用拒绝()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师" });
        svc.Execute("create_user", new Dictionary<string, object?> { ["user_name"] = "张三", ["password"] = "123", ["group_name"] = "工程师" });
        var r = svc.Execute("delete_group", new Dictionary<string, object?> { ["group_name"] = "工程师" });
        Assert.False(r.Success);
        Assert.Equal("BLOCKED", r.ErrorCode);
    }

    [Fact]
    public void UpdateGroup_改名改权限()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["permissions"] = "ScreenEdit" });
        var r = svc.Execute("update_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["new_group_name"] = "高级工程师", ["permissions"] = "AlarmAck,SystemSettings" });
        Assert.True(r.Success);
        var g = p.Groups.First(x => x.Name == "高级工程师");
        Assert.Equal(2, g.Permissions.Count);
        Assert.DoesNotContain(p.Groups, x => x.Name == "工程师");
    }

    [Fact]
    public void UpdateGroup_不带permissions权限不变()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["permissions"] = "ScreenEdit,AlarmAck" });
        // 只改名（不带 permissions）→ 权限保持
        var r = svc.Execute("update_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["new_group_name"] = "高级工程师" });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        var g = p.Groups.First(x => x.Name == "高级工程师");
        Assert.Equal(2, g.Permissions.Count);   // 权限未被清空
    }

    [Fact]
    public void UpdateGroup_空串permissions清空权限()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["permissions"] = "ScreenEdit,AlarmAck" });
        var r = svc.Execute("update_group", new Dictionary<string, object?> { ["group_name"] = "工程师", ["permissions"] = "" });
        Assert.True(r.Success);
        var g = p.Groups.First(x => x.Name == "工程师");
        Assert.Empty(g.Permissions);   // 显式空串=清空（GUI 取消全勾选）
    }

    [Fact]
    public void CreateGroup_非法权限拒绝()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        var r = svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "测试组", ["permissions"] = "BAD" });
        Assert.False(r.Success);
        Assert.Equal("INVALID_PARAM", r.ErrorCode);
        Assert.DoesNotContain(p.Groups, x => x.Name == "测试组");
    }

    [Fact]
    public void CreateGroup_数字串权限拒绝()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        var r = svc.Execute("create_group", new Dictionary<string, object?> { ["group_name"] = "测试组2", ["permissions"] = "2" });
        Assert.False(r.Success);
        Assert.Equal("INVALID_PARAM", r.ErrorCode);
    }

    [Fact]
    public void UpdateGroup_预设组改名拒绝_权限仍可改()
    {
        var p = new HMIProject();
        var svc = new CommandService(p);
        // 改名（同名新名不触发；改名为其他 → BLOCKED，防改名后变普通组绕过删除保护）
        var r = svc.Execute("update_group", new Dictionary<string, object?> { ["group_name"] = "管理员", ["new_group_name"] = "超级管理员" });
        Assert.False(r.Success);
        Assert.Equal("BLOCKED", r.ErrorCode);
        Assert.Contains(p.Groups, x => x.Name == "管理员");
        Assert.DoesNotContain(p.Groups, x => x.Name == "超级管理员");
        // 预设组权限仍可改（组名不变）
        var r2 = svc.Execute("update_group", new Dictionary<string, object?> { ["group_name"] = "管理员", ["permissions"] = "ScreenEdit,AlarmAck" });
        Assert.True(r2.Success, $"{r2.ErrorCode} {r2.ErrorMessage}");
        var g = p.Groups.First(x => x.Name == "管理员");
        Assert.Equal(2, g.Permissions.Count);
    }
}
