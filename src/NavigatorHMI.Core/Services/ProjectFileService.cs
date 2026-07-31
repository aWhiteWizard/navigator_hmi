using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程文件持久化服务。
    /// 统一管理工程文件的保存（ProtoBuf 序列化 → .hmiproj）与加载（.hmiproj → ProtoBuf 反序列化）。
    /// </summary>
    public static class ProjectFileService
    {
        /// <summary>
        /// 将 <see cref="HMIProject"/> 序列化保存到 .hmiproj 源文件。
        /// 自动创建目标目录（如果不存在），并更新工程的 <see cref="HMIProject.ProjectFilePath"/> 和 <see cref="HMIProject.LastModifiedTime"/>。
        /// </summary>
        /// <param name="project">要保存的工程对象，不可为 null</param>
        /// <param name="filePath">目标文件完整路径（含 .hmiproj 扩展名）</param>
        /// <exception cref="ArgumentNullException"><paramref name="project"/> 或 <paramref name="filePath"/> 为 null/空时抛出</exception>
        /// <exception cref="IOException">目录创建或文件写入失败时抛出</exception>
        public static void Save(HMIProject project, string filePath)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using var fs = File.Create(filePath);
            Serializer.Serialize(fs, project);

            project.ProjectFilePath = filePath;
            project.LastModifiedTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 从 .hmiproj 文件反序列化加载工程。
        /// </summary>
        /// <param name="filePath">工程文件路径（含 .hmiproj 扩展名）</param>
        /// <returns>反序列化后的 <see cref="HMIProject"/> 对象，已设置 ProjectFilePath 和 LastModifiedTime</returns>
        /// <exception cref="FileNotFoundException">文件不存在</exception>
        /// <exception cref="InvalidDataException">文件损坏或 ProtoBuf 格式不兼容</exception>
        public static HMIProject Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"工程文件不存在: {filePath}");

            try
            {
                using var fs = File.OpenRead(filePath);
                var project = Serializer.Deserialize<HMIProject>(fs);
                project.ProjectFilePath = filePath;
                return project;
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                throw new InvalidDataException($"工程文件损坏或格式不兼容: {filePath}", ex);
            }
        }
    }
}
