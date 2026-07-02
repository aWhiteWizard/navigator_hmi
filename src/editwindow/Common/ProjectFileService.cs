using System;
using System.IO;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程文件持久化服务。
    /// 提供统一的工程保存逻辑，消除 <see cref="Views.EditWindow"/> 和
    /// <see cref="ViewModels.DeviceConfigViewModel"/> 中的重复代码。
    /// </summary>
    public static class ProjectFileService
    {
        /// <summary>
        /// 将 <see cref="HMIProject"/> 序列化保存到指定文件路径。
        /// 自动创建目标目录（如果不存在），并更新工程的
        /// <see cref="HMIProject.ProjectFilePath"/> 和 <see cref="HMIProject.LastModifiedTime"/>。
        /// </summary>
        /// <param name="project">要保存的工程对象</param>
        /// <param name="filePath">目标文件完整路径（含 .hmiproj 扩展名）</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="project"/> 或 <paramref name="filePath"/> 为 null 时抛出。
        /// </exception>
        /// <exception cref="IOException">目录创建或文件写入失败时抛出。</exception>
        public static void Save(HMIProject project, string filePath)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            // 确保目标目录存在
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 序列化写入文件
            using (var fs = File.Create(filePath))
            {
                Serializer.Serialize(fs, project);
            }

            // 更新工程元数据
            project.ProjectFilePath = filePath;
            project.LastModifiedTime = DateTime.Now;
        }
    }
}
