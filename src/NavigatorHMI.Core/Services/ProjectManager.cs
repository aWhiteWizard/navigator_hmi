namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程保存/加载统一入口（K 循环 K-1c：6 处 Save 调用点收敛——CLI/命令层/GUI 同一条保存链路）。
    /// 内部委托 <see cref="ProjectFileService"/>（原子保存 + 清脏）；自动编译检测信号（deploy 前置自动编译）K-3 挂接此处。
    /// 注：命名空间与 Core 存量一致（块式）；纯委托门面，异常透传（ArgumentNullException/IOException）。
    /// </summary>
    public static class ProjectManager
    {
        /// <summary>
        /// 保存工程到指定路径（原子保存：临时文件 + File.Replace；失败清理、原文件完好；成功后清脏）。
        /// </summary>
        /// <param name="project">要保存的工程对象，不可为 null</param>
        /// <param name="filePath">目标文件完整路径（含 .hmiproj 扩展名）</param>
        /// <exception cref="ArgumentNullException">project 或 filePath 为 null/空</exception>
        /// <exception cref="IOException">目录创建或文件写入失败</exception>
        public static void Save(HMIProject project, string filePath)
            => ProjectFileService.Save(project, filePath);
    }
}
