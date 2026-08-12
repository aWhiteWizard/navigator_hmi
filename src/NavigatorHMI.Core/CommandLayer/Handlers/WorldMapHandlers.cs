using System;
using System.Collections.Generic;
using System.Linq;
using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>
    /// 世界地图配置命令（P9）：作业点/作业范围点/地图配置的 AI/CLI 统一入口（GUI 表格与地图加点之外的能力通道）。
    /// </summary>
    public static class WorldMapCommandCommon
    {
        /// <summary>定位世界地图配置；画面不存在或非世界地图 → 错误。</summary>
        internal static (WorldMapConfig?, CommandResult?) FindWorldMap(HMIProject project, Dictionary<string, object?> p)
        {
            var screenName = p["screen_name"]!.ToString()!;
            var screen = project.Screens.FirstOrDefault(s => s.Name == screenName);
            if (screen == null) return (null, CommandResult.Fail("NOT_FOUND", $"画面 \"{screenName}\" 不存在"));
            if (screen.Type != ScreenType.WorldMap) return (null, CommandResult.Fail("INVALID_PARAM", $"画面 \"{screenName}\" 不是世界地图（world map 命令仅作用于世界地图画面）"));
            project.WorldMap ??= new WorldMapConfig();
            return (project.WorldMap, null);
        }

        /// <summary>解析作业点坐标来源：lng_lat 固定值或 bound_tag GPS 变量（二选一，互斥校验）。</summary>
        internal static (GeoPoint? fixedPoint, string boundTag, CommandResult? err) ResolveGeo(Dictionary<string, object?> p)
        {
            var hasLngLat = p.TryGetValue("lng_lat", out var ll) && !string.IsNullOrWhiteSpace(ll?.ToString());
            var hasBound = p.TryGetValue("bound_tag", out var bt) && !string.IsNullOrWhiteSpace(bt?.ToString());
            if (hasLngLat == hasBound) return (null, "", CommandResult.Fail("INVALID_PARAM", "lng_lat 与 bound_tag 必须且只能提供一个（二选一）"));
            if (hasLngLat)
            {
                if (!GeoPoint.TryParse(ll!.ToString(), out var g) || g == null)
                    return (null, "", CommandResult.Fail("INVALID_PARAM", $"lng_lat \"{ll}\" 不是合法经纬度（支持 DMS 如 (E104°3'30\", N30°40'20\") 或小数度）"));
                return (g, "", null);
            }
            return (null, bt!.ToString()!, null);
        }

        /// <summary>校验 bound_tag 存在且为 GPS 类型（对齐 GUI 添加校验）。</summary>
        internal static CommandResult? ValidateBoundTag(HMIProject project, string boundTag)
        {
            if (string.IsNullOrEmpty(boundTag)) return null;
            var tag = project.Tags.FirstOrDefault(t => t.Name == boundTag);
            if (tag?.DataType != TagDataType.GPS) return CommandResult.Fail("INVALID_PARAM", $"绑定变量 \"{boundTag}\" 不存在或不是 GPS 类型");
            return null;
        }
    }

    /// <summary>add_work_point：添加作业点（名称 + 固定经纬度 或 绑定 GPS 变量，二选一）。</summary>
    public class AddWorkPointHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "add_work_point",
            Description = "向世界地图添加作业点（name + lng_lat 固定经纬度 或 bound_tag 绑定 GPS 变量，二选一）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["name"] = new() { Type = "string", Required = true, Description = "作业点名称（如 1号泵站）" },
                ["lng_lat"] = new() { Type = "string", Required = false, KeepInCompact = true, Description = "固定经纬度（DMS 或小数度）；与 bound_tag 二选一" },   // C12-14：AI compact schema 暴露该参数（原省略 → AI 无法指定经纬度）
                ["bound_tag"] = new() { Type = "string", Required = false, KeepInCompact = true, Description = "绑定 GPS 变量名（动态移动）；与 lng_lat 二选一" },   // C12-14：AI compact schema 暴露该参数（原省略 → AI 无法指定绑定变量）
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            if (!p.ContainsKey("name") || string.IsNullOrWhiteSpace(p["name"]?.ToString())) return ValidationResult.Fail("缺少必填参数: name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (wm, err) = WorldMapCommandCommon.FindWorldMap(project, p);
            if (err != null) return err;
            var (fixedPoint, boundTag, geoErr) = WorldMapCommandCommon.ResolveGeo(p);
            if (geoErr != null) return geoErr;
            var tagErr = WorldMapCommandCommon.ValidateBoundTag(project, boundTag);
            if (tagErr != null) return tagErr;
            wm!.WorkPoints.Add(new MapWorkPoint { Name = p["name"]!.ToString()!, FixedPoint = fixedPoint, BoundTag = boundTag });
            return CommandResult.Ok(new Dictionary<string, object?> { ["count"] = wm.WorkPoints.Count });
        }
    }

    /// <summary>delete_work_point：按名称删除作业点（同名删除首个匹配）。</summary>
    public class DeleteWorkPointHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "delete_work_point",
            Description = "按名称删除世界地图作业点（同名删除首个匹配）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["name"] = new() { Type = "string", Required = true },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name") || !p.ContainsKey("name")) return ValidationResult.Fail("缺少必填参数: screen_name/name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (wm, err) = WorldMapCommandCommon.FindWorldMap(project, p);
            if (err != null) return err;
            var name = p["name"]!.ToString()!;
            var wp = wm!.WorkPoints.FirstOrDefault(x => x.Name == name);
            if (wp == null) return CommandResult.Fail("NOT_FOUND", $"作业点 \"{name}\" 不存在");
            wm.WorkPoints.Remove(wp);
            return CommandResult.Ok(new Dictionary<string, object?> { ["count"] = wm.WorkPoints.Count });
        }
    }

    /// <summary>add_work_range_point：添加作业范围点（围栏顶点；固定经纬度 或 绑定 GPS 变量，二选一；无名称）。</summary>
    public class AddWorkRangePointHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "add_work_range_point",
            Description = "向世界地图添加作业范围点（围栏顶点，无名称；lng_lat 固定经纬度 或 bound_tag 绑定 GPS 变量，二选一）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["lng_lat"] = new() { Type = "string", Required = false, KeepInCompact = true, Description = "固定经纬度（DMS 或小数度）；与 bound_tag 二选一" },   // C12-14：AI compact schema 暴露（与 add_work_point 同源缺陷一并修复）
                ["bound_tag"] = new() { Type = "string", Required = false, KeepInCompact = true, Description = "绑定 GPS 变量名；与 lng_lat 二选一" },   // C12-14：AI compact schema 暴露
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (wm, err) = WorldMapCommandCommon.FindWorldMap(project, p);
            if (err != null) return err;
            var (fixedPoint, boundTag, geoErr) = WorldMapCommandCommon.ResolveGeo(p);
            if (geoErr != null) return geoErr;
            var tagErr = WorldMapCommandCommon.ValidateBoundTag(project, boundTag);
            if (tagErr != null) return tagErr;
            wm!.WorkRangePoints.Add(new WorkRangePoint { FixedPoint = fixedPoint, BoundTag = boundTag });
            return CommandResult.Ok(new Dictionary<string, object?> { ["count"] = wm.WorkRangePoints.Count });
        }
    }

    /// <summary>clear_work_range：清除作业范围（清空全部作业范围点）。</summary>
    public class ClearWorkRangeHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "clear_work_range",
            Description = "清除世界地图作业范围（清空全部作业范围点）",
            Parameters = new() { ["screen_name"] = new() { Type = "string", Required = true } }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (wm, err) = WorldMapCommandCommon.FindWorldMap(project, p);
            if (err != null) return err;
            wm!.WorkRangePoints.Clear();
            return CommandResult.Ok();
        }
    }

    /// <summary>update_world_map：更新世界地图配置（瓦片源/缩放级别/全局叠加/锁定预览）。</summary>
    public class UpdateWorldMapHandler : ICommandHandler
    {
        public CommandDefinition Definition => new()
        {
            Name = "update_world_map",
            Description = "更新世界地图配置（tile_source 瓦片源 / zoom_level 缩放 / show_global_overlay 全局叠加 / view_locked 锁定预览）",
            Parameters = new()
            {
                ["screen_name"] = new() { Type = "string", Required = true },
                ["tile_source"] = new() { Type = "string", Required = false, Description = "瓦片来源：offline（离线）| amap（高德）| 自定义源" },
                ["zoom_level"] = new() { Type = "int", Required = false, Description = "默认缩放级别（值越大越详细）" },
                ["show_global_overlay"] = new() { Type = "bool", Required = false, Description = "是否叠加全局画面" },
                ["view_locked"] = new() { Type = "bool", Required = false, Description = "锁定预览：运行时禁平移缩放/点击切换" },
            }
        };
        public ValidationResult Validate(Dictionary<string, object?> p)
        {
            if (!p.ContainsKey("screen_name")) return ValidationResult.Fail("缺少必填参数: screen_name");
            return ValidationResult.Ok;
        }
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> p)
        {
            var (wm, err) = WorldMapCommandCommon.FindWorldMap(project, p);
            if (err != null) return err;
            if (p.TryGetValue("tile_source", out var ts) && !string.IsNullOrWhiteSpace(ts?.ToString())) wm!.TileSource = ts.ToString()!;
            if (p.TryGetValue("zoom_level", out var zl) && !string.IsNullOrWhiteSpace(zl?.ToString()))
            {
                if (!int.TryParse(zl.ToString(), out var z)) return CommandResult.Fail("INVALID_PARAM", $"zoom_level \"{zl}\" 必须是整数");
                wm!.ZoomLevel = z;
            }
            if (p.TryGetValue("show_global_overlay", out var sgo) && !string.IsNullOrWhiteSpace(sgo?.ToString()))
            {
                if (!bool.TryParse(sgo.ToString(), out var s)) return CommandResult.Fail("INVALID_PARAM", $"show_global_overlay \"{sgo}\" 必须是 true/false");
                wm!.ShowGlobalOverlay = s;
            }
            if (p.TryGetValue("view_locked", out var vl) && !string.IsNullOrWhiteSpace(vl?.ToString()))
            {
                if (!bool.TryParse(vl.ToString(), out var v)) return CommandResult.Fail("INVALID_PARAM", $"view_locked \"{vl}\" 必须是 true/false");
                wm!.ViewLocked = v;
            }
            return CommandResult.Ok();
        }
    }
}
