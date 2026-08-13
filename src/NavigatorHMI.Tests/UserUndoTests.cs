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

    [Fact]
    public void Tag快照_更新BaseValue后撤销重做还原()
    {
        // X-1c：拖拽绑变量点 → PushTagSnapshot → 更新 BaseValue → Undo/Redo 还原（Tag 基准值快照栈）
        var vm = CreateVm();
        var tag = new Tag { Name = "gps1", DataType = TagDataType.GPS, BaseValue = "(E104°3'29.88\", N30°40'19.92\")" };
        vm.CurrentProject.Tags.Add(tag);
        vm.PushTagSnapshot();
        tag.BaseValue = "(E105°0'0.00\", N31°0'0.00\")";   // 模拟拖拽写回
        Assert.True(vm.HasTagUndo);
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);   // 走 Tag 撤销路由
        Assert.Equal("(E104°3'29.88\", N30°40'19.92\")", tag.BaseValue);   // 还原拖前值
        Assert.True(vm.HasTagRedo);
        vm.RedoCommand.Execute(null);
        Assert.Equal("(E105°0'0.00\", N31°0'0.00\")", tag.BaseValue);   // 重做恢复新值
    }

    [Fact]
    public void 交错操作_撤销按LIFO取最近栈()
    {
        // X-1c should-fix：先拖绑点（Tag 栈）再拖非绑点（WorldMap 栈）→ Ctrl+Z 应撤 WorldMap（最近），而非固定 Tag 优先撤更早的
        var vm = CreateVm();
        // 世界地图画面（WorldMap 撤销前置）
        var wmScreen = vm.CurrentProject.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap);
        if (wmScreen == null) { wmScreen = new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 800, Height = 600 }; vm.CurrentProject.Screens.Add(wmScreen); }
        vm.CurrentScreen = wmScreen;
        // 第一步：Tag 快照（拖绑点）
        var tag = new Tag { Name = "gps1", DataType = TagDataType.GPS, BaseValue = "(E0°0'0.00\", N0°0'0.00\")" };
        vm.CurrentProject.Tags.Add(tag);
        vm.PushTagSnapshot();
        tag.BaseValue = "(E1°0'0.00\", N1°0'0.00\")";
        // 第二步：WorldMap 快照（拖非绑点）
        vm.CurrentProject.WorldMap ??= new WorldMapConfig();
        vm.PushWorldMapUndoSnapshot();
        vm.CurrentProject.WorldMap.WorkPoints.Add(new MapWorkPoint { Name = "点1", FixedPoint = new GeoPoint(2, 3) });
        // Ctrl+Z：应撤 WorldMap（最近一步），Tag 不动
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.CurrentProject.WorldMap.WorkPoints);   // WorldMap 还原
        Assert.Equal("(E1°0'0.00\", N1°0'0.00\")", tag.BaseValue);   // Tag 未动
        // 再 Ctrl+Z：撤 Tag
        vm.UndoCommand.Execute(null);
        Assert.Equal("(E0°0'0.00\", N0°0'0.00\")", tag.BaseValue);
    }

    [Fact]
    public void 交错操作_重做按撤销逆序()
    {
        // X-1c 复审：撤销 WM→Tag 后，重做应为 Tag→WM（后撤先重做，redo 用"最近撤销"版本路由）
        var vm = CreateVm();
        var wmScreen = vm.CurrentProject.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap);
        if (wmScreen == null) { wmScreen = new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 800, Height = 600 }; vm.CurrentProject.Screens.Add(wmScreen); }
        vm.CurrentScreen = wmScreen;
        var tag = new Tag { Name = "gps1", DataType = TagDataType.GPS, BaseValue = "(E0°0'0.00\", N0°0'0.00\")" };
        vm.CurrentProject.Tags.Add(tag);
        vm.PushTagSnapshot();
        tag.BaseValue = "(E1°0'0.00\", N1°0'0.00\")";
        vm.CurrentProject.WorldMap ??= new WorldMapConfig();
        vm.PushWorldMapUndoSnapshot();
        vm.CurrentProject.WorldMap.WorkPoints.Add(new MapWorkPoint { Name = "点1", FixedPoint = new GeoPoint(2, 3) });
        // 撤销两次：先 WM 后 Tag
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.CurrentProject.WorldMap.WorkPoints);
        Assert.Equal("(E0°0'0.00\", N0°0'0.00\")", tag.BaseValue);
        // 重做两次：应先 Tag（后撤）后 WM
        vm.RedoCommand.Execute(null);
        Assert.Equal("(E1°0'0.00\", N1°0'0.00\")", tag.BaseValue);   // 先重做 Tag
        Assert.Empty(vm.CurrentProject.WorldMap.WorkPoints);          // WM 未动
        vm.RedoCommand.Execute(null);
        Assert.Single(vm.CurrentProject.WorldMap.WorkPoints);         // 再重做 WM
    }

    [Fact]
    public void 交错操作_重做WM与Widgets逆序()
    {
        // X-1c 四审：widgets push 先、wm push 后（best 指向 wm）→ Undo wm → Undo widgets → Redo 应先 Widgets（后撤先重做）；
        // 若 WorldMap redo 分支缺版本条件（三审旧实现），Redo 1 会抢先重做 wm → 断言 Widgets 未恢复抓出 bug
        var vm = CreateVm();
        var wmScreen = vm.CurrentProject.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap);
        if (wmScreen == null) { wmScreen = new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = 800, Height = 600 }; vm.CurrentProject.Screens.Add(wmScreen); }
        vm.CurrentScreen = wmScreen;
        vm.CurrentProject.WorldMap ??= new WorldMapConfig();
        vm.PushUndoSnapshot();   // Widgets 栈（先）
        wmScreen.Widgets.Add(new ButtonWidget { ObjectName = "btn1", X = 10, Y = 10, Width = 50, Height = 30 });
        vm.PushWorldMapUndoSnapshot();   // WM 栈（后，best 指向 wm）
        vm.CurrentProject.WorldMap.WorkPoints.Add(new MapWorkPoint { Name = "点1", FixedPoint = new GeoPoint(2, 3) });
        // 撤销两次：先 WM（最新 push）后 Widgets
        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.CurrentProject.WorldMap.WorkPoints);   // 先撤 WM
        Assert.Single(wmScreen.Widgets);                        // Widgets 未动
        vm.UndoCommand.Execute(null);
        Assert.Empty(wmScreen.Widgets);
        // 重做两次：后撤先重做——最后撤销的是 Widgets → 先重做 Widgets，再 WM
        vm.RedoCommand.Execute(null);
        Assert.Single(wmScreen.Widgets);                        // 先重做 Widgets
        Assert.Empty(vm.CurrentProject.WorldMap.WorkPoints);    // WM 未动
        vm.RedoCommand.Execute(null);
        Assert.Single(vm.CurrentProject.WorldMap.WorkPoints);
    }
}
