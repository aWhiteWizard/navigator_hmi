using NavigatorHMI.Common;
using NavigatorHMI.ViewModels;

namespace NavigatorHMI.Tests;

/// <summary>任务 9 用户/组撤销栈测试（GUI VM 方法——测试项目已引用 editwindow）。</summary>
public class UserUndoTests
{
    private static EditWindowViewModel CreateVm()
    {
        var p = new HMIProject { DeviceWidth = 800, DeviceHeight = 480 };
        p.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template, Width = 800, Height = 480 });
        p.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 800, Height = 480 });
        return new EditWindowViewModel(p);
    }

    [Fact]
    public void UndoUser_恢复用户与组快照()
    {
        var vm = CreateVm();
        vm.PushUserSnapshot();
        // 模拟用户/组操作（直接改 project——真实路径走命令层）
        var p = vm.CurrentProject;
        p.Users.Add(new UserAccount { UserName = "临时用户", PasswordHash = "X", GroupName = "访客" });
        p.Groups.Add(new UserGroup { Name = "临时组" });
        Assert.True(vm.UndoUser());
        Assert.Empty(p.Users);                 // 用户恢复（操作前空）
        Assert.Equal(3, p.Groups.Count);       // 组恢复（预置 3 组——临时组移除）
        Assert.DoesNotContain(p.Groups, g => g.Name == "临时组");
    }

    [Fact]
    public void RedoUser_重做恢复()
    {
        var vm = CreateVm();
        vm.PushUserSnapshot();
        var p = vm.CurrentProject;
        p.Users.Add(new UserAccount { UserName = "临时用户", PasswordHash = "X", GroupName = "访客" });
        vm.UndoUser();
        vm.RedoUser();
        Assert.Single(p.Users);
        Assert.Equal("临时用户", p.Users[0].UserName);
    }

    [Fact]
    public void 空栈撤销返回false()
    {
        var vm = CreateVm();
        Assert.False(vm.UndoUser());
        Assert.False(vm.RedoUser());
    }

    [Fact]
    public void PushSnapshot_清空重做栈()
    {
        var vm = CreateVm();
        vm.PushUserSnapshot();
        var p = vm.CurrentProject;
        p.Users.Add(new UserAccount { UserName = "A", PasswordHash = "X", GroupName = "访客" });
        vm.UndoUser();
        // 新操作（快照）→ 重做栈清空
        vm.PushUserSnapshot();
        Assert.False(vm.RedoUser());
    }

    [Fact]
    public void AI操作路径_命令层创建用户_撤销接线()
    {
        var vm = CreateVm();
        vm.PushUserSnapshot();   // AI 发送前快照（EditWindowViewModel.AiSendCommand 同接线）
        var r = vm.CommandService.Execute("create_user", new Dictionary<string, object?>
        {
            ["user_name"] = "AI用户",
            ["password"] = "pw123456",
            ["group_name"] = "访客",
        });
        Assert.True(r.Success, $"{r.ErrorCode} {r.ErrorMessage}");
        Assert.Contains(vm.CurrentProject.Users, u => u.UserName == "AI用户");
        // 撤销：恢复快照（命令层真实改动被回滚——接线覆盖）
        Assert.True(vm.UndoUser());
        Assert.DoesNotContain(vm.CurrentProject.Users, u => u.UserName == "AI用户");
    }
}
