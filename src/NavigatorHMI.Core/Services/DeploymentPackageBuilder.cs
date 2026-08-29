using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程部署包构建器（K 循环 K-3b）——把 .navihmi 编译产物 + 工程引用资源打包为 **zip 容器**（manifest + 文件区）。
    /// 容器结构（compile-download §2.1，与 .fw 组件表同构）：
    ///   manifest.json：{ name, type("app"/"res"), target, size, sha256, version }[]
    ///   app/app.navihmi   —— 编译主包（type=app）
    ///   res/&lt;相对路径&gt;  —— 工程引用资源（图片/图片列表项；type=res；内容 SHA256 去重；target 保留工程内相对子目录路径防同名冲突）
    /// 资源收集：控件 ImagePath + 图片列表 Items（相对工程目录）；RTSP 流 URL 不入包；target 白名单=工程目录内（Path.GetRelativePath 防穿越）。
    /// 注：执行书目标含字体/本地视频收集，模型当前无对应字段（字体=字体族名非文件；Frame 视频源属 D 批）——三类可落地资源先行。
    /// </summary>
    public static class DeploymentPackageBuilder
    {
        /// <summary>manifest 条目类型：app（.navihmi 主包）。</summary>
        public const string TypeApp = "app";

        /// <summary>manifest 条目类型：res（资源文件）。与 .fw 组件表 app/boot/rootfs/uboot 为两套独立枚举（compile-download §2.1 审查澄清）。</summary>
        public const string TypeRes = "res";

        /// <summary>manifest 条目（序列化 JSON；type 枚举 app/res 独立定义——本类内 manifest 专属）。</summary>
        public class ManifestEntry
        {
            /// <summary>条目名称（app=主包 / 资源文件名）。</summary>
            public string Name { get; set; } = "";

            /// <summary>类型：app（.navihmi 主包）/ res（资源文件）。</summary>
            public string Type { get; set; } = TypeRes;

            /// <summary>设备端落盘相对路径（target 白名单：仅工程目录内收集的资源，防路径穿越；保留相对子目录防同名冲突）。</summary>
            public string Target { get; set; } = "";

            /// <summary>字节数。</summary>
            public long Size { get; set; }

            /// <summary>SHA256（内容哈希，去重键）。</summary>
            public string Sha256 { get; set; } = "";

            /// <summary>版本（主包=工程版本；资源=1）。</summary>
            public string Version { get; set; } = "1";
        }

        /// <summary>
        /// 收集工程目录 tiles/ 瓦片子目录（z/x/y.png，Web Mercator 结构）——世界地图离线瓦片随工程包下发。
        /// target 保留 "tiles/..." 相对前缀（与 FW ZIP 直启格式一致）；白名单同资源收集（工程目录内）。
        /// 去重键 = 工程内相对路径（瓦片路径 z/x/y.png 是语义标识：同内容不同路径的瓦片（空白/纯色块常见）
        /// 必须各自入包，FW 按路径加载——不能用内容 sha256 去重，否则同内容瓦片被吞 → FW 按路径加载缺失（M-3 ① 审查修正）。
        /// </summary>
        private static void CollectTiles(string projectDir, Dictionary<string, (string Abs, string Rel)> resources)
        {
            if (string.IsNullOrWhiteSpace(projectDir)) return;
            var tilesDir = Path.Combine(projectDir, "tiles");
            if (!Directory.Exists(tilesDir)) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(tilesDir, "*.png", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(projectDir, file);
                    if (rel.StartsWith("..") || Path.IsPathRooted(rel)) continue;   // 白名单（理论不会触发，防御）
                    resources.TryAdd(rel, (file, rel));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[DeploymentPackageBuilder] 瓦片收集失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建部署包 zip 容器并原子输出到 <paramref name="outputDir"/>。
        /// </summary>
        /// <param name="project">当前工程（资源路径相对工程目录解析）</param>
        /// <param name="navihmiPath">编译产物 .navihmi 绝对路径（已生成）</param>
        /// <param name="outputDir">输出目录（不存在自动创建）</param>
        /// <returns>部署包绝对路径（&lt;工程名&gt;.deploy.zip）</returns>
        public static string Build(HMIProject project, string navihmiPath, string outputDir)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrEmpty(navihmiPath) || !File.Exists(navihmiPath))
                throw new FileNotFoundException($"编译产物不存在: {navihmiPath}");
            if (string.IsNullOrEmpty(outputDir)) throw new ArgumentNullException(nameof(outputDir));

            // 1. 收集资源（内容哈希去重：同内容只打一份；记录工程内相对路径）
            var projectDir = Path.GetDirectoryName(project.ProjectFilePath) ?? ".";
            var resources = new Dictionary<string, (string Abs, string Rel)>();   // sha256 → (源文件绝对路径, 工程内相对路径)
            var tiles = new Dictionary<string, (string Abs, string Rel)>();       // sha256 → (瓦片绝对路径, 工程内相对路径)（M-3 ①：独立集合，target 保留根级 tiles/ 前缀与 FW ZIP 直启格式一致）

            void Collect(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                // RTSP 流 URL 不入包（网络流，2026-08-29 用户定）
                if (path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)) return;
                string abs;
                try { abs = Path.GetFullPath(Path.Combine(projectDir, path)); }
                catch { return; }
                // target 白名单：资源必须位于工程目录内（GetRelativePath 防 C:\proj 与 C:\proj2 前缀陷阱 + 盘符根目录）
                var rel = Path.GetRelativePath(projectDir, abs);
                if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return;
                if (!File.Exists(abs)) return;
                resources.TryAdd(Sha256OfFile(abs), (abs, rel));
            }

            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                {
                    if (w is ImageWidget img) Collect(img.ImagePath);
                    else if (w is FrameWidget fr) Collect(fr.ImagePath);
                }
            foreach (var list in project.Lists.Where(l => l.Type == ListType.Image))
                foreach (var item in list.Items)
                    Collect(item);

            // M-3 ①：收集工程目录瓦片（tiles/ 子目录 z/x/y.png——世界地图离线瓦片，随工程包下发；
            // 与 FW resolveProjectPackage ZIP 直启同格式（tiles/ 根级），HTTP 下载链路落盘后单文件加载也按此探测）
            CollectTiles(projectDir, tiles);

            // 2. manifest + zip 容器（内存构建 → 原子落盘）
            var manifest = new List<ManifestEntry>();
            byte[] zipBytes;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // app 主包
                    var appEntry = zip.CreateEntry("app/app.navihmi");
                    using (var es = appEntry.Open())
                    using (var fs = File.OpenRead(navihmiPath))
                        fs.CopyTo(es);
                    var appSha = Sha256OfFile(navihmiPath);
                    manifest.Add(new ManifestEntry
                    {
                        Name = "app",
                        Type = TypeApp,
                        Target = "app/app.navihmi",
                        Size = new FileInfo(navihmiPath).Length,
                        Sha256 = appSha,
                        Version = string.IsNullOrEmpty(project.Version) ? "1" : project.Version
                    });

                    // res 资源（target 保留工程内相对路径，正斜杠——防同名冲突 + 兼容 Qt QZipReader）
                    foreach (var kv in resources)
                    {
                        var target = "res/" + kv.Value.Rel.Replace('\\', '/');
                        var entry = zip.CreateEntry(target);
                        using (var es = entry.Open())
                        using (var fs = File.OpenRead(kv.Value.Abs))
                            fs.CopyTo(es);
                        manifest.Add(new ManifestEntry
                        {
                            Name = Path.GetFileName(kv.Value.Abs),
                            Type = TypeRes,
                            Target = target,
                            Size = new FileInfo(kv.Value.Abs).Length,
                            Sha256 = kv.Key
                        });
                    }

                    // M-3 ①：瓦片（target 保留根级 "tiles/..." 前缀——与 FW ZIP 直启格式一致；
                    // FW httreceiver 落盘按 target 到工程目录，单文件加载按同目录 tiles/ 探测）
                    long tilesBytes = 0;
                    foreach (var kv in tiles)
                    {
                        var target = kv.Key.Replace('\\', '/');   // 键=工程内相对路径（语义标识）
                        var entry = zip.CreateEntry(target);
                        using (var es = entry.Open())
                        using (var fs = File.OpenRead(kv.Value.Abs))
                            fs.CopyTo(es);
                        tilesBytes += new FileInfo(kv.Value.Abs).Length;
                        manifest.Add(new ManifestEntry
                        {
                            Name = Path.GetFileName(kv.Value.Abs),
                            Type = TypeRes,
                            Target = target,
                            Size = new FileInfo(kv.Value.Abs).Length,
                            Sha256 = Sha256OfFile(kv.Value.Abs)
                        });
                    }
                    // 大小防护（M-3 ① 审查 🟡）：瓦片总和超 64MB 上传上限（FW kMaxUploadBytes）→ Trace 警告
                    //（zip 整体内存构建 + 单次 POST——超大瓦片集会在 FW 端被拒收，此处尽早提示）
                    if (tilesBytes > 64L * 1024 * 1024)
                        System.Diagnostics.Trace.WriteLine($"[DeploymentPackageBuilder] 警告: 瓦片共 {tilesBytes / (1024 * 1024)}MB 超 64MB 上传上限，FW 将拒收——请缩小离线瓦片范围");

                    // manifest.json（UTF-8 无 BOM；CamelCase 策略——与 FW 端 httreceiver 读取的 type/target/sha256 小写契约对齐，
                    // L-A1 联调发现大小写不匹配：原 PascalCase "Type" 致 FW 找不到 app 条目）
                    var mEntry = zip.CreateEntry("manifest.json");
                    using (var es = mEntry.Open())
                    {
                        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        var bytes = Encoding.UTF8.GetBytes(json);
                        es.Write(bytes, 0, bytes.Length);
                    }
                }
                zipBytes = ms.ToArray();
            }

            // 3. 原子输出（临时文件 + Move/Replace；失败清理）
            Directory.CreateDirectory(outputDir);
            var outPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(project.ProjectFilePath) + ".deploy.zip");
            string tmp = outPath + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, zipBytes);
                if (File.Exists(outPath)) File.Replace(tmp, outPath, null);
                else File.Move(tmp, outPath);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                throw;
            }
            return outPath;
        }

        private static string Sha256OfFile(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
    }
}
