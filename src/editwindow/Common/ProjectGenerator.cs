using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程生成器：验证错误并输出层级 XML 到 output 文件夹。
    /// </summary>
    public static class ProjectGenerator
    {
        /// <summary>
        /// 验证结果，包含错误列表。
        /// </summary>
        public class ValidationResult
        {
            /// <summary>是否有错误。</summary>
            public bool HasErrors => Errors.Count > 0;
            /// <summary>错误信息列表。</summary>
            public List<string> Errors { get; } = new List<string>();
        }

        /// <summary>
        /// 验证并生成 XML 输出文件。
        /// </summary>
        /// <param name="project">当前工程</param>
        /// <returns>验证结果，包含错误信息。调用方根据 HasErrors 决定是否继续。</returns>
        public static ValidationResult Generate(HMIProject project)
        {
            var result = new ValidationResult();

            // 1. 校验：检查所有 Widget 的 ObjectName 是否重名
            foreach (var screen in project.Screens)
            {
                var grouped = screen.Widgets
                    .Where(w => !string.IsNullOrEmpty(w.ObjectName))
                    .GroupBy(w => w.ObjectName)
                    .Where(g => g.Count() > 1);

                foreach (var group in grouped)
                {
                    result.Errors.Add(
                        $"画面 \"{screen.Name}\" 中 ObjectName \"{group.Key}\" 重复，共出现 {group.Count()} 次");
                }
            }

            if (result.HasErrors)
                return result;

            // 2. 没有错误，生成 XML
            string projectDir = Path.GetDirectoryName(project.ProjectFilePath);
            string outputDir = Path.Combine(projectDir, "output");
            Directory.CreateDirectory(outputDir);

            string xmlFileName = Path.GetFileNameWithoutExtension(project.ProjectFilePath) + ".xml";
            string xmlFilePath = Path.Combine(outputDir, xmlFileName);

            var doc = new XDocument(
                new XElement("Project",
                    new XAttribute("Name", project.Name ?? ""),
                    new XAttribute("Version", project.Version ?? ""),
                    new XAttribute("DeviceWidth", project.DeviceWidth),
                    new XAttribute("DeviceHeight", project.DeviceHeight),
                    new XAttribute("CreateTime", project.CreateTime.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XAttribute("LastModifiedTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),

                    // Screens 层级
                    new XElement("Screens",
                        project.Screens.Select(screen =>
                            new XElement("Screen",
                                new XAttribute("Name", screen.Name ?? ""),
                                new XAttribute("Width", screen.Width),
                                new XAttribute("Height", screen.Height),
                                new XAttribute("Type", screen.Type.ToString()),

                                // Widgets 层级
                                new XElement("Widgets",
                                    screen.Widgets.Select(widget =>
                                    {
                                        var elem = new XElement("Widget",
                                            new XAttribute("Type", widget.GetType().Name),
                                            new XAttribute("ObjectName", widget.ObjectName ?? ""),
                                            new XAttribute("X", widget.X),
                                            new XAttribute("Y", widget.Y),
                                            new XAttribute("Width", widget.Width),
                                            new XAttribute("Height", widget.Height)
                                        );

                                        // 各子类特有属性
                                        if (widget is ButtonWidget btn)
                                            elem.Add(new XAttribute("Text", btn.Text ?? ""));
                                        else if (widget is TextWidget txt)
                                            elem.Add(new XAttribute("Content", txt.Content ?? ""));
                                        else if (widget is RectangleWidget rect)
                                            elem.Add(new XAttribute("FillColor", rect.FillColor ?? ""));

                                        return elem;
                                    })
                                )
                            )
                        )
                    )
                )
            );

            doc.Save(xmlFilePath);
            return result;
        }
    }
}
