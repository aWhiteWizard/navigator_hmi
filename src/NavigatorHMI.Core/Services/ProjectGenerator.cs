using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程编译器。验证工程数据完整性，并将 <see cref="HMIProject"/> 编译输出为设备端可解析的 .navihmi 文件。
    /// </summary>
    /// <remarks>
    /// 输出格式为 ProtoBuf 二进制，与 FW 端 protobuf-cpp 兼容。
    /// 校验包括：控件 ObjectName 去重、变量名去重、报警规则引用变量存在性。
    /// 有错误时不输出文件，返回错误列表供调用方展示。
    /// </remarks>
    public static class ProjectGenerator
    {
        /// <summary>
        /// 编译结果。包含错误列表和输出文件路径。
        /// </summary>
        public class CompileResult
        {
            /// <summary>是否有编译错误（为 true 时不输出文件）</summary>
            public bool HasErrors => Errors.Count > 0;

            /// <summary>错误信息列表（人类可读）</summary>
            public List<string> Errors { get; } = new();

            /// <summary>输出文件路径（仅在 HasErrors 为 false 时有值）</summary>
            public string? OutputPath { get; set; }
        }

        /// <summary>
        /// 验证工程完整性并编译输出 .navihmi 文件。
        /// </summary>
        /// <param name="project">当前工程对象</param>
        /// <returns>编译结果。<see cref="CompileResult.HasErrors"/> 为 true 时不输出文件。</returns>
        public static CompileResult Compile(HMIProject project)
        {
            var result = new CompileResult();

            // 1. 校验：同一画面中控件 ObjectName 不能重复
            foreach (var screen in project.Screens)
            {
                var grouped = screen.Widgets
                    .Where(w => !string.IsNullOrEmpty(w.ObjectName))
                    .GroupBy(w => w.ObjectName)
                    .Where(g => g.Count() > 1);

                foreach (var group in grouped)
                    result.Errors.Add(
                        $"画面 \"{screen.Name}\" 中 ObjectName \"{group.Key}\" 重复 ({group.Count()} 次)");
            }

            // 2. 校验：变量名不能重复
            var dupTags = project.Tags
                .GroupBy(t => t.Name)
                .Where(g => g.Count() > 1);
            foreach (var g in dupTags)
                result.Errors.Add($"变量 \"{g.Key}\" 重复定义 ({g.Count()} 次)");

            // 3. 校验：报警规则引用的变量必须存在
            var tagNames = project.Tags.Select(t => t.Name).ToHashSet();
            foreach (var alarm in project.Alarms)
                if (!tagNames.Contains(alarm.TagName))
                    result.Errors.Add($"报警 \"{alarm.Name}\" 引用的变量 \"{alarm.TagName}\" 不存在");

            if (result.HasErrors)
                return result;

            // 4. 输出 .navihmi (ProtoBuf 二进制)
            string? projectDir = Path.GetDirectoryName(project.ProjectFilePath);
            if (string.IsNullOrEmpty(projectDir))
                projectDir = ".";

            string outputDir = Path.Combine(projectDir, "output");
            Directory.CreateDirectory(outputDir);

            string outputPath = Path.Combine(outputDir,
                Path.GetFileNameWithoutExtension(project.ProjectFilePath) + ".navihmi");

            using var fs = File.Create(outputPath);
            Serializer.Serialize(fs, project);

            result.OutputPath = outputPath;
            return result;
        }
    }
}
