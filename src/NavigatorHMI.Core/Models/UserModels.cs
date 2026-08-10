using System.Collections.Generic;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>用户权限项（DESIGN-WINDOWS.md §用户节点：运行时权限判定）。</summary>
    public enum UserPermission
    {
        /// <summary>画面编辑（运行时切编辑模式等）</summary>
        ScreenEdit,
        /// <summary>报警确认/清除</summary>
        AlarmAck,
        /// <summary>用户管理（增删用户/改密码/改组）</summary>
        UserManage,
        /// <summary>系统设置（时间/网络/配置）</summary>
        SystemSettings
    }

    /// <summary>用户组（权限集合；预置：管理员/操作员/访客）。</summary>
    [ProtoContract]
    public class UserGroup
    {
        /// <summary>组名（工程内唯一）。</summary>
        [ProtoMember(1)]
        public string Name { get; set; } = "";

        /// <summary>组权限位。</summary>
        [ProtoMember(2, IsPacked = true)]
        public List<UserPermission> Permissions { get; set; } = new();

        /// <summary>权限中文展示（权限列显示用，逗号分隔；ProtoIgnore 不入工程文件）。</summary>
        [ProtoIgnore]
        public string PermissionDisplay => string.Join("、", Permissions.Select(p => p switch
        {
            UserPermission.ScreenEdit => "画面编辑",
            UserPermission.AlarmAck => "报警确认",
            UserPermission.UserManage => "用户管理",
            UserPermission.SystemSettings => "系统设置",
            _ => p.ToString(),
        }));
    }

    /// <summary>用户账户（运行时用户系统；SHA256 密码哈希）。</summary>
    [ProtoContract]
    public class UserAccount
    {
        /// <summary>用户名（登录名）。</summary>
        [ProtoMember(1)]
        public string UserName { get; set; } = "";

        /// <summary>密码 SHA256 哈希（Hex）。</summary>
        [ProtoMember(2)]
        public string PasswordHash { get; set; } = "";

        /// <summary>所属组名（UserGroup.Name）。</summary>
        [ProtoMember(3)]
        public string GroupName { get; set; } = "访客";

        /// <summary>是否已强制改密（初始管理员首次登录改密标记）。</summary>
        [ProtoMember(4)]
        public bool MustChangePassword { get; set; }
    }

    /// <summary>安全设置（密码策略——用户安全设置面板）。</summary>
    [ProtoContract]
    public class SecuritySettings
    {
        /// <summary>最小密码长度（默认 6）。</summary>
        [ProtoMember(1)]
        public int MinPasswordLength { get; set; } = 6;

        /// <summary>要求包含数字。</summary>
        [ProtoMember(2)]
        public bool RequireDigit { get; set; }

        /// <summary>要求包含字母。</summary>
        [ProtoMember(3)]
        public bool RequireLetter { get; set; }

        /// <summary>要求大小写混合。</summary>
        [ProtoMember(4)]
        public bool RequireUpperLower { get; set; }

        /// <summary>要求包含特殊字符。</summary>
        [ProtoMember(5)]
        public bool RequireSpecial { get; set; }

        /// <summary>密码有效期（天，0=不过期）。</summary>
        [ProtoMember(6)]
        public int PasswordMaxAgeDays { get; set; }

        /// <summary>连续失败锁定阈值（0=不锁定）。</summary>
        [ProtoMember(7)]
        public int FailedLoginLockout { get; set; }

        /// <summary>锁定时长（分钟，配合 FailedLoginLockout）。</summary>
        [ProtoMember(8)]
        public int LockMinutes { get; set; }
    }
}
