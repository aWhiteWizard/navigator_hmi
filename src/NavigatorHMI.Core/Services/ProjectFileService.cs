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
        /// 将 <see cref="HMIProject"/> 序列化保存到 .hmiproj 源文件（**原子保存**：同目录临时文件 → File.Replace/File.Move，
        /// 失败删临时文件、原文件完好——2026-08-30 K 循环，供 deploy 前置自动编译检测「文件确实写完了」的可靠信号）。
        /// 自动创建目标目录（如果不存在），更新工程的 <see cref="HMIProject.ProjectFilePath"/> 和 <see cref="HMIProject.LastModifiedTime"/>，
        /// 保存成功后清脏（<see cref="HMIProject.ClearDirty"/>）。
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

            // 原子保存：临时文件序列化 → 替换目标；失败清理临时文件、原文件完好
            string tmpPath = filePath + ".tmp";
            try
            {
                using (var fs = File.Create(tmpPath))
                {
                    Serializer.Serialize(fs, project);
                }
                if (File.Exists(filePath))
                    File.Replace(tmpPath, filePath, null);
                else
                    File.Move(tmpPath, filePath);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* 清理失败不掩盖原异常 */ }
                throw;
            }

            project.ProjectFilePath = filePath;
            project.LastModifiedTime = DateTime.UtcNow;
            project.ClearDirty();
        }

        /// <summary>
        /// 从 .hmiproj 文件反序列化加载工程（同步，CLI/命令层用）。
        /// </summary>
        /// <param name="filePath">工程文件路径（含 .hmiproj 扩展名）</param>
        /// <returns>反序列化后的 <see cref="HMIProject"/> 对象，已设置 ProjectFilePath；
        /// LastModifiedTime 从文件还原（持久化字段，非本次写入——W-B 打开路径统一此语义）</returns>
        /// <exception cref="FileNotFoundException">文件不存在</exception>
        /// <exception cref="InvalidDataException">文件损坏或 ProtoBuf 格式不兼容</exception>
        public static HMIProject Load(string filePath) => LoadCore(filePath);

        /// <summary>
        /// 从 .hmiproj 文件**异步**加载工程（W-B 后台加载：Task.Run 反序列化 + 阶段进度回调，大工程不冻结 UI）。
        /// GUI 打开工程路径使用；异常语义与 <see cref="Load"/> 一致（await 时抛出）。
        /// </summary>
        /// <param name="filePath">工程文件路径（含 .hmiproj 扩展名）</param>
        /// <param name="progress">可选阶段进度回调（后台线程调用，GUI 侧用 Progress&lt;string&gt; 封送回 UI 线程）</param>
        public static Task<HMIProject> LoadAsync(string filePath, IProgress<string>? progress = null)
        {
            // 完整在后台执行（文件 IO + protobuf 反序列化 + 后处理）——脱离 UI 线程防冻结；
            // 不做 async/await 文件 API：protobuf-net 反序列化本身同步消耗 CPU，Task.Run 直接包住即可
            return Task.Run(() =>
            {
                progress?.Report("正在读取工程文件…");
                var project = LoadCore(filePath);
                progress?.Report("工程解析完成，正在打开编辑器…");
                return project;
            });
        }

        private static HMIProject LoadCore(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"工程文件不存在: {filePath}");

            try
            {
                using var fs = File.OpenRead(filePath);
                var project = Serializer.Deserialize<HMIProject>(fs);
                project.ProjectFilePath = filePath;
                project.ClearDirty();   // 反序列化 setter/集合事件会置脏——加载完成清脏（K 循环 IsDirty 单点化）
                return project;
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                throw new InvalidDataException($"工程文件损坏或格式不兼容: {filePath}", ex);
            }
        }
    }
}
