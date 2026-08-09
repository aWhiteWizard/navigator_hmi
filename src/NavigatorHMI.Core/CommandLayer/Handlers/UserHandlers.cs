using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer
{
    /// <summary>W2 用户系统命令：create_user / update_user / delete_user / list_users（GUI/AI/CLI 三端统一）。</summary>
    public class CreateUserHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "create_user", Description = "创建用户账户",
            Parameters = new()
            {
                ["user_name"] = new() { Type = "string", Required = true, Description = "用户名" },
                ["password"] = new() { Type = "string", Required = true, Description = "密码（SHA256 哈希存储）" },
                ["group_name"] = new() { Type = "string", DefaultValue = "访客", Description = "所属组（管理员/操作员/访客）", KeepInCompact = true },   // A1：compact schema 可见——AI 创建用户时可指定组（否则只能默认访客）
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("user_name") || string.IsNullOrWhiteSpace(p["user_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: user_name");
            if (!p.ContainsKey("password") || string.IsNullOrWhiteSpace(p["password"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: password");
            return ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["user_name"]!.ToString()!.Trim();
            if (project.Users.Any(u => u.UserName == name))
                return CommandResult.Fail("DUPLICATE", $"用户 \"{name}\" 已存在");
            var group = p.GetValueOrDefault("group_name")?.ToString() ?? "访客";
            if (project.Groups.Count > 0 && !project.Groups.Any(g => g.Name == group))
                return CommandResult.Fail("NOT_FOUND", $"用户组 \"{group}\" 不存在，可用：{string.Join("/", project.Groups.Select(g => g.Name))}");   // A1：错误附可用组清单——AI 设错组名时模型可自纠
            project.Users.Add(new UserAccount
            {
                UserName = name,
                PasswordHash = Sha256(p["password"]!.ToString()!),
                GroupName = group,
            });
            return CommandResult.Ok(new { user_name = name, group_name = group });
        }

        internal static string Sha256(string s)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(bytes);
        }
    }

    /// <summary>update_user：改用户名/密码/组（密码留空=不改）。</summary>
    public class UpdateUserHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_user", Description = "更新用户（改名/改密/改组；密码留空=不改）",
            Parameters = new()
            {
                ["user_name"] = new() { Type = "string", Required = true, Description = "要修改的用户名" },
                ["new_user_name"] = new() { Type = "string", DefaultValue = "", Description = "新用户名（留空=不改）" },
                ["new_password"] = new() { Type = "string", DefaultValue = "", Description = "新密码（留空=不改）" },
                ["new_group_name"] = new() { Type = "string", DefaultValue = "", Description = "新所属组（留空=不改）" },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
            => p.ContainsKey("user_name") && !string.IsNullOrWhiteSpace(p["user_name"]?.ToString())
                ? ValidationResult.Ok : ValidationResult.Fail("缺少必填参数: user_name");

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["user_name"]!.ToString()!.Trim();
            var user = project.Users.FirstOrDefault(u => u.UserName == name);
            if (user == null) return CommandResult.Fail("NOT_FOUND", $"用户 \"{name}\" 不存在");
            // 先验证后修改（原子性：任一校验失败不改变任何状态）
            var newName = (p.GetValueOrDefault("new_user_name")?.ToString() ?? "").Trim();
            if (newName.Length > 0 && newName != name && project.Users.Any(u => u.UserName == newName))
                return CommandResult.Fail("DUPLICATE", $"用户名 \"{newName}\" 已被占用");
            var newGroup = (p.GetValueOrDefault("new_group_name")?.ToString() ?? "").Trim();
            if (newGroup.Length > 0 && project.Groups.Count > 0 && !project.Groups.Any(g => g.Name == newGroup))
                return CommandResult.Fail("NOT_FOUND", $"用户组 \"{newGroup}\" 不存在，可用：{string.Join("/", project.Groups.Select(g => g.Name))}");   // A1 一致性：错误附可用组清单——AI 设错组名时可自纠
            // 全部校验通过后统一落库
            if (newName.Length > 0 && newName != name) user.UserName = newName;
            var newPass = p.GetValueOrDefault("new_password")?.ToString() ?? "";
            if (newPass.Length > 0) user.PasswordHash = CreateUserHandler.Sha256(newPass);
            if (newGroup.Length > 0) user.GroupName = newGroup;
            return CommandResult.Ok(new { user_name = user.UserName, group_name = user.GroupName });
        }
    }

    /// <summary>delete_user：删除用户。</summary>
    public class DeleteUserHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_user", Description = "删除用户账户",
            Parameters = new() { ["user_name"] = new() { Type = "string", Required = true, Description = "用户名" } }
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
            => p.ContainsKey("user_name") && !string.IsNullOrWhiteSpace(p["user_name"]?.ToString())
                ? ValidationResult.Ok : ValidationResult.Fail("缺少必填参数: user_name");

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["user_name"]!.ToString()!.Trim();
            var user = project.Users.FirstOrDefault(u => u.UserName == name);
            if (user == null) return CommandResult.Fail("NOT_FOUND", $"用户 \"{name}\" 不存在");
            // 防删最后一个管理员（删前检查——BLOCKED 不得改变状态）
            if (user.GroupName == "管理员" && !project.Users.Any(u => u.GroupName == "管理员" && u.UserName != name))
                return CommandResult.Fail("BLOCKED", "不能删除最后一个管理员用户");
            project.Users.Remove(user);
            return CommandResult.Ok(new { user_name = name });
        }
    }

    /// <summary>list_users：列出用户（GUI/AI 查询用）。</summary>
    public class ListUsersHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "list_users", Description = "列出全部用户账户",
            Parameters = new()
        };

        public ValidationResult Validate(Dictionary<string, object?> p) => ValidationResult.Ok;

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
            => CommandResult.Ok(new { users = project.Users.Select(u => new { u.UserName, u.GroupName }).ToArray() });
    }

    // ═══ P2-1 用户组 CRUD（弹窗编辑/增删组/右键菜单用） ═══

    /// <summary>create_group：新建用户组（预设三组外的自定义组）。</summary>
    public class CreateGroupHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "create_group", Description = "新建用户组",
            Parameters = new()
            {
                ["group_name"] = new() { Type = "string", Required = true, Description = "组名" },
                ["permissions"] = new() { Type = "string", DefaultValue = "", Description = "权限列表（逗号分隔枚举名：ScreenEdit/AlarmAck/UserManage/SystemSettings；空=全禁）" },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("group_name") || string.IsNullOrWhiteSpace(p["group_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: group_name");
            var err = UserGroupHandlers.ParsePermissions(p.GetValueOrDefault("permissions")?.ToString());
            return err == null ? ValidationResult.Fail("INVALID_PARAM: 权限名非法（ScreenEdit/AlarmAck/UserManage/SystemSettings）") : ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["group_name"]!.ToString()!.Trim();
            if (project.Groups.Any(g => g.Name == name))
                return CommandResult.Fail("DUPLICATE", $"用户组 \"{name}\" 已存在");
            var perms = UserGroupHandlers.ParsePermissions(p.GetValueOrDefault("permissions")?.ToString()) ?? new();
            var g = new UserGroup { Name = name };
            g.Permissions.AddRange(perms);
            project.Groups.Add(g);
            return CommandResult.Ok(new { group_name = name, permissions = perms.Count });
        }
    }

    /// <summary>update_group：更新用户组（改名/改权限；permissions 非空才覆盖）。</summary>
    public class UpdateGroupHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_group", Description = "更新用户组（改名/改权限）",
            Parameters = new()
            {
                ["group_name"] = new() { Type = "string", Required = true, Description = "要修改的组名" },
                ["new_group_name"] = new() { Type = "string", DefaultValue = "", Description = "新组名（留空=不改）" },
                ["permissions"] = new() { Type = "string", DefaultValue = "", Description = "权限列表（逗号分隔；留空=不改）" },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("group_name") || string.IsNullOrWhiteSpace(p["group_name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: group_name");
            var err = UserGroupHandlers.ParsePermissions(p.GetValueOrDefault("permissions")?.ToString());
            return err == null ? ValidationResult.Fail("INVALID_PARAM: 权限名非法（ScreenEdit/AlarmAck/UserManage/SystemSettings）") : ValidationResult.Ok;
        }

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["group_name"]!.ToString()!.Trim();
            var g = project.Groups.FirstOrDefault(x => x.Name == name);
            if (g == null) return CommandResult.Fail("NOT_FOUND", $"用户组 \"{name}\" 不存在");
            var newName = p.GetValueOrDefault("new_group_name")?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(newName) && newName != name)
            {
                if (project.Groups.Any(x => x.Name == newName))
                    return CommandResult.Fail("DUPLICATE", $"用户组 \"{newName}\" 已存在");
                g.Name = newName;
            }
            if (p.ContainsKey("permissions"))   // 显式提供才覆盖（空串=清空；未提供=不改——与 update_user 约定一致）
            {
                var perms = UserGroupHandlers.ParsePermissions(p["permissions"]?.ToString()) ?? new();
                g.Permissions.Clear();
                g.Permissions.AddRange(perms);
            }
            return CommandResult.Ok(new { group_name = g.Name });
        }
    }

    /// <summary>delete_group：删除用户组（预设三组不可删；有用户引用拒绝）。</summary>
    public class DeleteGroupHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_group", Description = "删除用户组（预设三组不可删；有用户引用拒绝）",
            Parameters = new()
            {
                ["group_name"] = new() { Type = "string", Required = true, Description = "组名" },
            }
        };

        public ValidationResult Validate(Dictionary<string, object?> p)
            => !p.ContainsKey("group_name") || string.IsNullOrWhiteSpace(p["group_name"]?.ToString())
                ? ValidationResult.Fail("缺少必填参数: group_name")
                : ValidationResult.Ok;

        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var name = p["group_name"]!.ToString()!.Trim();
            if (name is "管理员" or "操作员" or "访客")
                return CommandResult.Fail("BLOCKED", $"预设组 \"{name}\" 不可删除");
            var g = project.Groups.FirstOrDefault(x => x.Name == name);
            if (g == null) return CommandResult.Fail("NOT_FOUND", $"用户组 \"{name}\" 不存在");
            if (project.Users.Any(u => u.GroupName == name))
                return CommandResult.Fail("BLOCKED", $"用户组 \"{name}\" 仍被用户引用，不能删除");
            project.Groups.Remove(g);
            return CommandResult.Ok(new { group_name = name });
        }
    }

    /// <summary>P2-1 用户组权限解析辅助（字符串→枚举列表；非法返回错误文本）。</summary>
    public static class UserGroupHandlers
    {
        /// <summary>解析逗号分隔权限名；空/空白返回空列表；含非法名返回 null（调用方报 INVALID_PARAM）。</summary>
        public static List<UserPermission>? ParsePermissions(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new();
            var result = new List<UserPermission>();
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out _)) return null;   // 拒绝纯数字串（防 "2" 解析为 AlarmAck 绕过非法名校验）
                if (Enum.TryParse<UserPermission>(part, true, out var perm) && Enum.IsDefined(perm))
                { if (!result.Contains(perm)) result.Add(perm); }
                else return null;
            }
            return result;
        }
    }
}
