using NavigatorHMI.Common;

namespace NavigatorHMI.CommandLayer.Handlers
{
    /// <summary>新建工程。创建 HMIProject + 默认全局画面 + 世界地图，保存到指定路径。</summary>
    public class CreateProjectHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "create_project", Description = "新建工程",
            Parameters = new()
            {
                ["name"] = new() { Type = "string", Required = true, Description = "工程名称" },
                ["path"] = new() { Type = "string", Required = true, Description = "工程保存路径" },
                ["width"] = new() { Type = "int", DefaultValue = 800, Description = "设备屏幕宽度", KeepInCompact = true },
                ["height"] = new() { Type = "int", DefaultValue = 480, Description = "设备屏幕高度", KeepInCompact = true },
            }
        };
        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("name") || string.IsNullOrWhiteSpace(parameters["name"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: name");
            if (!parameters.ContainsKey("path") || string.IsNullOrWhiteSpace(parameters["path"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: path");
            return ValidationResult.Ok;
        }
        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var name = parameters["name"]?.ToString() ?? "";
            var path = parameters["path"]?.ToString() ?? ".";
            var width = Convert.ToInt32(parameters.GetValueOrDefault("width", 800));
            var height = Convert.ToInt32(parameters.GetValueOrDefault("height", 480));

            // 修改传入的 project 引用（而非创建新对象），确保 CommandService 持有最新状态
            project.Name = name;
            project.Version = "1.0";
            project.CreateTime = DateTime.UtcNow;
            project.ProjectFilePath = Path.Combine(path, $"{name}.hmiproj");
            project.DeviceWidth = width;
            project.DeviceHeight = height;
            project.Screens.Clear();
            project.Screens.Add(new Screen { Name = "全局画面", Type = ScreenType.Template, Width = width, Height = height, IsGlobal = true, ShowInNav = false });
            project.Screens.Add(new Screen { Name = "世界地图", Type = ScreenType.WorldMap, Width = width, Height = height, ShowInNav = true });

            // W3b/P1-10：预置用户组（统一入口 CommandService.EnsureDefaultGroups）
            project.Groups.Clear();
            CommandService.EnsureDefaultGroups(project);

            ProjectManager.Save(project, project.ProjectFilePath);
            return CommandResult.Ok(new { project_path = project.ProjectFilePath, screens = 2 });
        }
    }

    /// <summary>打开已有工程。加载 .hmiproj 并返回工程信息。</summary>
    public class OpenProjectHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "open_project", Description = "打开已有工程",
            Parameters = new() { ["path"] = new() { Type = "string", Required = true, Description = "工程文件路径 (.hmiproj)" } }
        };
        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters)
        {
            if (!parameters.ContainsKey("path") || string.IsNullOrWhiteSpace(parameters["path"]?.ToString()))
                return ValidationResult.Fail("缺少必填参数: path");
            return ValidationResult.Ok;
        }
        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var loaded = ProjectFileService.Load(parameters["path"]!.ToString()!);
            return CommandResult.Ok(new { name = loaded.Name, screens = loaded.Screens.Count, version = loaded.Version, path = loaded.ProjectFilePath });
        }
    }

    /// <summary>保存当前工程到 .hmiproj 文件。</summary>
    public class SaveProjectHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "save_project", Description = "保存当前工程",
            Parameters = new() { ["path"] = new() { Type = "string", Required = false, Description = "保存路径（空=覆盖原文件）" } }
        };
        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters) => ValidationResult.Ok;
        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var sp = parameters.GetValueOrDefault("path")?.ToString();
            var target = string.IsNullOrEmpty(sp) ? project.ProjectFilePath : sp;
            ProjectManager.Save(project, target);
            return CommandResult.Ok(new { path = target });
        }
    }

    /// <summary>编译工程为 .navihmi 文件。</summary>
    public class CompileHandler : ICommandHandler
    {
        /// <inheritdoc/>
        public CommandDefinition Definition => new()
        {
            Name = "compile", Description = "编译工程为 .navihmi 文件",
            Parameters = new() { ["output_path"] = new() { Type = "string", Required = false, Description = "输出路径" } }
        };
        /// <inheritdoc/>
        public ValidationResult Validate(Dictionary<string, object?> parameters) => ValidationResult.Ok;
        /// <inheritdoc/>
        public CommandResult Execute(HMIProject project, Dictionary<string, object?> parameters)
        {
            var result = ProjectGenerator.Compile(project);
            if (result.HasErrors) return CommandResult.Fail("BUILD_FAILED", string.Join("; ", result.Errors));
            return CommandResult.Ok(new { output_path = result.OutputPath });
        }
    }
}
