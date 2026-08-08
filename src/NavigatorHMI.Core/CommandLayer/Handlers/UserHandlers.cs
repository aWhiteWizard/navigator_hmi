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
                ["group_name"] = new() { Type = "string", DefaultValue = "访客", Description = "所属组（管理员/操作员/访客）" },
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
                return CommandResult.Fail("NOT_FOUND", $"用户组 \"{group}\" 不存在");
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
                return CommandResult.Fail("NOT_FOUND", $"用户组 \"{newGroup}\" 不存在");
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
}
