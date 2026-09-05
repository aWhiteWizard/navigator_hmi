using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程部署包构建器（K 循环 K-3b）——把 .navihmi 编译产物 + 工程引用资源打包为 **zip 容器**（manifest + 文件区）。
    /// 容器结构（compile-download §2.1，与 .fw 组件表同构）：
    ///   manifest.json：{ name, type("app"/"res"), target, size, sha256, version }[]
    ///   app/app.navihmi   —— 编译主包（type=app）
    ///   res/&lt;相对路径&gt;  —— 工程引用资源（图片/图片列表项；type=res；去重键=工程内相对路径（N-7，路径是语义标识，FW 按 imagePath 加载——同内容不同路径各自入包，禁止内容哈希去重）；target 保留工程内相对子目录路径防同名冲突）
    /// 资源收集：控件 ImagePath + 图片列表 Items——**无论路径**（O-A1 用户语义：PC 路径只是找文件线索，
    /// 任意路径图片都收集文件本身入包 res/；目录内保留相对子目录，目录外 basename 唯一化）；
    /// 打包时改写 .navihmi 图片引用为包内相对 res 路径（FW resolveResPath 拼 <root>/res/ 加载，设备不关心 PC 路径）；
    /// RTSP 流 URL 不入包；瓦片收集保持工程目录内白名单。
    /// P-6（2026-09-04）：Frame 视频模式本地视频收集——入包 media/ 前缀（FW resolveVideoPath 拼 <工程目录>/media/ 加载，
    /// .navihmi videoSource 改写为包内 rel）；单文件 >64MB 抛异常报错（用户裁决）；RTSP/http(s) 网络流不入包不改写（设备端直连）；
    /// 目录外护栏同图片（视频扩展名白名单）。
    /// 注：执行书目标含字体收集，模型当前无对应字段（字体=字体族名非文件）——可落地资源已全部实现。
    /// </summary>
    public static class DeploymentPackageBuilder
    {
        /// <summary>manifest 条目类型：app（.navihmi 主包）。</summary>
        public const string TypeApp = "app";

        /// <summary>manifest 条目类型：res（资源文件）。与 .fw 组件表（app/rootfs/kernel，2026-08-30 用户分组定稿）为两套独立枚举（compile-download §2.1 审查澄清）。</summary>
        public const string TypeRes = "res";

        /// <summary>部署包上传上限（字节）——与 FW 端 httreceiver kMaxUploadBytes（64MB 单次 POST）对齐；
        /// 视频单文件超限抛异常报错（用户裁决）、视频/瓦片总和超限 Trace 预警（整包仍会被 FW 拒收，V1.1 大包流式扩展项）。
        /// internal（Q-4 2026-09-04）：ProjectGenerator 编译校验复用——64MB 上限单一来源。</summary>
        internal const long MaxUploadBytes = 64L * 1024 * 1024;

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

            /// <summary>SHA256（内容哈希，校验用；N-7 去重键已改工程内相对路径——哈希仅作完整性校验，不再承担去重）。</summary>
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
        /// <exception cref="ArgumentNullException">project/navihmiPath/outputDir 为空</exception>
        /// <exception cref="FileNotFoundException">编译产物不存在</exception>
        /// <exception cref="InvalidOperationException">Frame 视频源单文件超过 64MB 上传上限（用户裁决报错）</exception>
        public static string Build(HMIProject project, string navihmiPath, string outputDir)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrEmpty(navihmiPath) || !File.Exists(navihmiPath))
                throw new FileNotFoundException($"编译产物不存在: {navihmiPath}");
            if (string.IsNullOrEmpty(outputDir)) throw new ArgumentNullException(nameof(outputDir));

            // 1. 收集资源（去重键 = 包内相对路径：路径是语义标识，FW 按 imagePath 加载——同内容不同路径的图片必须各自入包；
            //    不能用内容 sha256 去重，否则同内容图片被吞 → FW 按路径加载缺失（N-7，对齐瓦片 M-3 ① rel 键先例））
            //    O-A1（2026-08-30 用户语义）：图片引用**无论路径**（绝对/相对/工程目录外）都收集文件本身入包——
            //    PC 路径只是"找文件"的线索；目录内保留相对子目录，目录外按文件名唯一化（防重名冲突）；
            //    同时维护「模型路径 → 包内相对 res 路径」映射，打包时改写 .navihmi 图片引用为包内路径
            //    （FW resolveResPath 拼 <root>/res/ 前缀加载，设备端不关心 PC 路径）。
            var projectDir = Path.GetDirectoryName(project.ProjectFilePath) ?? ".";
            var resources = new Dictionary<string, (string Abs, string Rel)>();   // 包内相对路径(res 下) → (源文件绝对路径, 包内相对路径)
            var tiles = new Dictionary<string, (string Abs, string Rel)>();       // 工程内相对路径 → (瓦片绝对路径, 工程内相对路径)（M-3 ①：独立集合，target 保留根级 tiles/ 前缀与 FW ZIP 直启格式一致）
            var imagePathMap = new Dictionary<string, string>();                  // 模型图片引用原值 → 包内相对 res 路径（.navihmi 改写用，O-A1）
            var usedPackNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // 已占用包内名（目录外 basename 唯一化）

            // 目录外收集护栏（审查 🟡，2026-08-30 信任边界决策：图片引用是用户显式内容（GUI 对话框/命令层），
            // 用户语义授权「任意路径收集文件本身」；为防「打包任意系统文件」外传原语，目录外仅接受常见图片扩展名；
            // 工程目录内引用不受限（工程内资源用户自行管理）；AI 通道（update_widget imagePath 无净化）属既有
            // 缺口——见 4_bugs cli-param-sanitize 豁免场景限定，随护栏记录信任边界）
            // P-6：Frame 视频源收集沿用同一信任边界（用户显式内容 + 视频扩展名白名单 videoExts），
            // 目录外同样仅白名单扩展名可入包；AI 通道若后续开放 videoSource 需同步净化
            var imageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg", ".ico"
            };
            // P-6（2026-09-04）：Frame 视频源收集扩展名白名单（目录外护栏同图片——防「打包任意系统文件」外传原语；
            // FW ffmpeg 后端解码能力 = buildroot ffmpeg all decoders，主流容器均支持）
            // ⚠️ 依赖前提：videoExts 与 imageExts 必须互斥（共用 absPackMap/usedPackNames 的跨型同名安全依赖此前提——
            // 若未来扩展名重叠，同 abs 文件会跨型复用包名/占用名导致静默错包；新增扩展名时须检查两侧）
            var videoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".ts", ".m4v", ".webm", ".mpg", ".mpeg", ".3gp"
            };

            // 解析单条文件引用（图片/视频通用）→ (源绝对路径, 是否工程目录内)；不可收集（空/网络流 URL/../逃逸/目录外非白名单扩展名/文件不存在）返回 null：
            //   绝对路径（无 ..，用户文件对话框正常场景，O-A1）→ 目录外也收集；相对路径 ../ 逃逸 → 拦截（防目录穿越打包任意文件）
            //   rtsp/http/https 网络流 URL → 显式拦截（不入包；视频场景设备端直连原值，图片场景本就无此语义）
            (string Abs, bool Inside)? ResolveFile(string path, HashSet<string> exts)
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                if (path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return null;   // 网络流 URL 不入包（与 skippedVideos 统计排除对称）
                string abs;
                try
                {
                    abs = Path.IsPathRooted(path)
                        ? Path.GetFullPath(path)
                        : Path.GetFullPath(Path.Combine(projectDir, path));
                }
                catch { return null; }
                if (!File.Exists(abs)) return null;
                var rel = Path.GetRelativePath(projectDir, abs);
                bool inside = !rel.StartsWith("..") && !Path.IsPathRooted(rel);
                if (!inside)
                {
                    if (!Path.IsPathRooted(path)) return null;                      // 相对路径解析越界（../ 逃逸）→ 拦截
                    if (!exts.Contains(Path.GetExtension(abs))) return null;   // 目录外护栏：仅白名单扩展名
                }
                return (abs, inside);
            }

            // 包内名唯一化（目录外 basename 冲突加序号；目录内规范名不参与改名）
            string? UniquePack(string pack)
            {
                if (!usedPackNames.Contains(pack)) { usedPackNames.Add(pack); return pack; }
                var stem = Path.GetFileNameWithoutExtension(pack);
                var ext = Path.GetExtension(pack);
                for (int i = 2; i < 1000; i++)
                {
                    var c = $"{stem}_{i}{ext}";
                    if (!usedPackNames.Contains(c)) { usedPackNames.Add(c); return c; }
                }
                return null;   // 唯一化失败（需 998+ 同名文件，理论不可达；失败即放弃该文件不入包）
            }

            // 引用来源汇总（widget ImagePath + 图片列表 items）
            var imageRefs = new List<string>();
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                {
                    if (w is ImageWidget img) imageRefs.Add(img.ImagePath);
                    else if (w is FrameWidget fr) imageRefs.Add(fr.ImagePath);
                }
            foreach (var list in project.Lists.Where(l => l.Type == ListType.Image))
                imageRefs.AddRange(list.Items);

            // P-6：Frame 视频源汇总（仅视频模式 Frame——ShowVideo 且 VideoSource 非空才收集；
            // 非视频模式 Frame 即使残留 VideoSource（GUI 勾选关闭后旧源可仍在模型里）也不入列；
            // RTSP/网络流引用会入列但 ResolveFile 显式拦截不入包不改写——设备端直连原值）
            var videoRefs = new List<string>();
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                    if (w is FrameWidget frv && frv.ShowVideo && !string.IsNullOrWhiteSpace(frv.VideoSource))
                        videoRefs.Add(frv.VideoSource);
            // S-4/S-5：Video 型列表项（源地址）入视频收集——本地源打包 media（ASCII 化同单源），RTSP 项 ResolveFile 拦截不入包原样
            foreach (var lst in project.Lists.Where(l => l.Type == ListType.Video))
                videoRefs.AddRange(lst.Items);

            // 两遍制收集（审查 🟡 修复：目录内引用优先——相对/规范名优先级高，防「目录内 a.png 与目录外 a.png 共存
            // 时由遍历序决定谁活」的收集次序依赖错图）：
            //   第一遍目录内（pack=工程内相对路径，规范名直接占用）；第二遍目录外绝对引用（basename，对已占名唯一化）。
            // pathMap **无条件登记**（审查 🟡 修复：同一文件被相对/绝对两种拼写各引用一次时，pack 相同、
            // resources 按 pack 去重第二次 TryAdd 失败——但两条引用都必须改写为包内路径，否则 .navihmi 残留
            // 盘符路径 → FW 拼错缺图（N+20 根因②模式））。
            // 目录外文件按 abs 缓存包名（复审 🟡：同一目录外文件被多条引用时复用同一包名——不按出现次数重复
            // 唯一化产生 name_2 孤儿条目，设备端不受影响但浪费体积/上传）
            var absPackMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            void CollectPass(List<string> refs, HashSet<string> exts,
                             Dictionary<string, string> pathMap,
                             Dictionary<string, (string Abs, string Rel)> res,
                             bool wantInside)
            {
                foreach (var modelPath in refs)
                {
                    var f = ResolveFile(modelPath, exts);
                    if (f == null) continue;
                    if (f.Value.Inside != wantInside) continue;
                    string pack;
                    if (f.Value.Inside)
                    {
                        pack = Path.GetRelativePath(projectDir, f.Value.Abs).Replace('\\', '/');
                        usedPackNames.Add(pack);   // 目录内规范名占用（同文件 rel+abs 双拼写重复 Add 幂等）
                    }
                    else
                    {
                        if (!absPackMap.TryGetValue(f.Value.Abs, out pack!))
                        {
                            var p = UniquePack(Path.GetFileName(f.Value.Abs));
                            if (p == null) continue;
                            pack = p;
                            absPackMap[f.Value.Abs] = pack;
                        }
                        // 复用已有包名（同文件多引用不重复入包）
                    }
                    pathMap[modelPath] = pack;                 // map 无条件登记（多拼写各自登记）
                    res.TryAdd(pack, (f.Value.Abs, pack));    // 资源按 pack 去重（同 pack 同文件共用一份）
                }
            }
            CollectPass(imageRefs, imageExts, imagePathMap, resources, wantInside: true);
            CollectPass(imageRefs, imageExts, imagePathMap, resources, wantInside: false);

            // P-6：视频两遍收集（入包 media/ 前缀——FW resolveVideoPath 拼 <工程目录>/media/<rel> 加载）
            // Q 循环 Check（2026-09-05 黑屏第三层根因）：media 包名**强制 ASCII**——Qt6.4 ffmpeg 后端
            // (qffmpegdecoder.cpp:1041) 用 media.toEncoded(PreferLocalFile) 给 avformat_open_input——
            // 中文名被 QUrl 百分号编码 %E5%A4%A7... 而 ffmpeg file 协议**不解码 %XX** → 字面 % 串打开失败
            // 「Could not open file」。图片中文名无恙（Qt 自解码）；仅视频走 ffmpeg 受影响 → 打包期转 ASCII
            // 名（video_N.ext），DTO 引用同步改写 → 设备端路径全 ASCII。
            var videos = new Dictionary<string, (string Abs, string Rel)>();
            var videoPathMap = new Dictionary<string, string>();
            var videoAbsPack = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // 视频 abs → 最终包名（含 ASCII 化后；防同文件多引用重复入包）
            int videoSeq = 0;
            void CollectVideos(List<string> refs, bool wantInside)
            {
                foreach (var modelPath in refs)
                {
                    var f = ResolveFile(modelPath, videoExts);
                    if (f == null) continue;
                    if (f.Value.Inside != wantInside) continue;
                    string pack;
                    if (videoAbsPack.TryGetValue(f.Value.Abs, out var cachedPack)) { pack = cachedPack; }
                    else
                    {
                        if (f.Value.Inside)
                        {
                            pack = Path.GetRelativePath(projectDir, f.Value.Abs).Replace('\\', '/');
                        }
                        else
                        {
                            var p = UniquePack(Path.GetFileName(f.Value.Abs));
                            if (p == null) continue;
                            pack = p;
                        }
                        // ASCII 化（中文/非 ASCII 名 → video_N.ext；ASCII 原名保留可读性）
                        if (pack.Any(c => c > 127))
                        {
                            var ext = Path.GetExtension(pack);
                            if (string.IsNullOrEmpty(ext)) ext = "";
                            string asciiPack;
                            do { asciiPack = $"video_{++videoSeq}{ext}"; }
                            while (usedPackNames.Contains(asciiPack));
                            usedPackNames.Add(asciiPack);
                            pack = asciiPack;
                        }
                        else if (f.Value.Inside)
                        {
                            // 目录内规范名占用——ASCII 化改名可能已先占同名（真实名 video_1.mp4 与中文改名
                            // video_1.mp4 撞——同 pass 内次序相关，reviewer 🟡-1）→ UniquePack 唯一化防静默错媒体
                            if (usedPackNames.Contains(pack))
                            {
                                var u = UniquePack(pack);
                                if (u == null) continue;
                                pack = u;
                            }
                            usedPackNames.Add(pack);
                        }
                        videoAbsPack[f.Value.Abs] = pack;   // 缓存**最终包名**（审查 🟡：防同文件多引用/双拼写二次 ASCII 化出新名双份——GUI 多控件共用同视频常见，N×体积可撞 64MB 总和拒收）
                    }
                    videoPathMap[modelPath] = pack;   // map 无条件登记（多拼写各自登记）
                    videos.TryAdd(pack, (f.Value.Abs, pack));  // 资源按 pack 去重（同 pack 同文件共用一份）
                }
            }
            CollectVideos(videoRefs, wantInside: true);
            CollectVideos(videoRefs, wantInside: false);

            // P-6 大小护栏（用户裁决 2026-09-02：本地视频 ≤64MB，超限**报错**——抛异常阻断打包，
            // 与瓦片/图片的 Trace 警告不同：视频文件大、超限静默入包会让 FW 拒收整包且难排查）
            foreach (var kv in videos)
            {
                var len = new FileInfo(kv.Value.Abs).Length;
                if (len > MaxUploadBytes)
                    throw new InvalidOperationException(
                        $"视频文件超过 64MB 上传上限（{kv.Value.Abs}，{(len + 1024 * 1024 - 1) / (1024 * 1024)}MB）——请压缩视频或改用 RTSP 流地址");
            }

            // 审查 🟡：跳过引用汇总 Trace（文件缺失/RTSP/目录外非图片扩展名/.. 逃逸——避免静默缺图无反馈，对齐瓦片 >64MB Trace 先例）
            int skippedRefs = imageRefs.Count(r => !string.IsNullOrEmpty(r) && !imagePathMap.ContainsKey(r));
            if (skippedRefs > 0)
                System.Diagnostics.Trace.WriteLine($"[DeploymentPackageBuilder] 警告: {skippedRefs} 条图片引用未收集入包（文件缺失/RTSP 流/目录外非图片/.. 逃逸）——设备端将缺图，请检查引用路径");

            // P-6：视频跳过引用 Trace——排除网络流（rtsp/http/https 是**正常不入包**模式，设备端直连，不算缺失）
            int skippedVideos = videoRefs.Count(r => !string.IsNullOrEmpty(r)
                && !r.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                && !r.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !r.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !videoPathMap.ContainsKey(r));
            if (skippedVideos > 0)
                System.Diagnostics.Trace.WriteLine($"[DeploymentPackageBuilder] 警告: {skippedVideos} 条视频引用未收集入包（文件缺失/目录外非视频扩展名/.. 逃逸）——设备端视频将无法播放，请检查视频源路径");

            // N-1：锁定视角底图（PC 编译时拼好的单张 PNG，worldmap_bg.png 在工程目录）——作为 res 随包下发，
            // FW 落盘工程目录后 HmiWorldMap 探测加载（有底图时瓦片层/模拟底图隐藏）
            bool hasBackground = false;
            {
                string bgPath = Path.Combine(projectDir, WorldMapScreenshotGenerator.BackgroundFileName);
                if (File.Exists(bgPath))
                {
                    resources.TryAdd(WorldMapScreenshotGenerator.BackgroundFileName, (bgPath, WorldMapScreenshotGenerator.BackgroundFileName));
                    hasBackground = true;
                }
            }

            // M-3 ①：收集工程目录瓦片（tiles/ 子目录 z/x/y.png——世界地图离线瓦片，随工程包下发；
            // 与 FW resolveProjectPackage ZIP 直启同格式（tiles/ 根级），HTTP 下载链路落盘后单文件加载也按此探测）
            // N-1：有锁定底图时不收集瓦片（底图替代瓦片铺贴——设备端显示单张底图，无需 z/x/y 瓦片；避免体积超 64MB）
            if (!hasBackground)
                CollectTiles(projectDir, tiles);

            // 2. manifest + zip 容器（内存构建 → 原子落盘）
            var manifest = new List<ManifestEntry>();
            byte[] zipBytes;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // app 主包（O-A1：图片引用改写为包内路径——反序列化 NavihmiProject 遍历图片列表 items + 控件 ImagePath，
                    // 命中收集映射则替换为包内相对 res 路径；失败回退原样（旧包/契约异常不阻塞部署，Trace 告警））
                    byte[] appBytes;
                    try
                    {
                        NavihmiProject dto;
                        using (var inMs = new MemoryStream(File.ReadAllBytes(navihmiPath)))
                            dto = Serializer.Deserialize<NavihmiProject>(inMs);
                        bool changed = false;
                        foreach (var lst in dto.Lists.Where(l => l.Type == ListType.Image))
                            for (int i = 0; i < lst.Items.Count; i++)
                                if (imagePathMap.TryGetValue(lst.Items[i], out var packName))
                                { lst.Items[i] = packName; changed = true; }
                        foreach (var lst in dto.Lists.Where(l => l.Type == ListType.Video))   // S-4/S-5：Video 列表项（本地源）改写为 ASCII 包名——FW resolveVideoPath 拼 media/ 加载
                            for (int i = 0; i < lst.Items.Count; i++)
                                if (videoPathMap.TryGetValue(lst.Items[i], out var vPack))
                                { lst.Items[i] = vPack; changed = true; }
                        foreach (var sc in dto.Screens)
                            foreach (var w in sc.Widgets)
                            {
                                if (!string.IsNullOrEmpty(w.ImagePath) && imagePathMap.TryGetValue(w.ImagePath, out var packName))
                                { w.ImagePath = packName; changed = true; }
                                // P-6：Frame 视频源改写为包内相对路径（media 前缀由 FW resolveVideoPath 拼装；
                                // RTSP/网络流不在 videoPathMap（ResolveFile 拦截）→ 原样保留，设备端直连）
                                if (!string.IsNullOrEmpty(w.VideoSource) && videoPathMap.TryGetValue(w.VideoSource, out var vpack))
                                { w.VideoSource = vpack; changed = true; }
                            }
                        if (changed)
                        {
                            using var outMs = new MemoryStream();
                            Serializer.Serialize(outMs, dto);
                            appBytes = outMs.ToArray();
                        }
                        else appBytes = File.ReadAllBytes(navihmiPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"[DeploymentPackageBuilder] .navihmi 图片引用改写失败(回退原样): {ex.Message}");
                        appBytes = File.ReadAllBytes(navihmiPath);
                    }
                    var appEntry = zip.CreateEntry("app/app.navihmi");
                    using (var es = appEntry.Open())
                        es.Write(appBytes, 0, appBytes.Length);
                    // manifest 校验按**入包内容**算（改写后 appBytes 的 sha/size——FW 端按 manifest 校验落盘内容）
                    var appSha = Convert.ToHexString(SHA256.HashData(appBytes)).ToLowerInvariant();
                    manifest.Add(new ManifestEntry
                    {
                        Name = "app",
                        Type = TypeApp,
                        Target = "app/app.navihmi",
                        Size = appBytes.Length,
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
                            Sha256 = Sha256OfFile(kv.Value.Abs)   // N-7：键已改 rel 路径，manifest 哈希须单独计算（原 kv.Key 即 sha256）
                        });
                    }

                    // P-6：视频资源（target 保留 "media/..." 前缀——FW 落盘 <工程目录>/media/<rel>，
                    // qmlgenerator resolveVideoPath 拼 media/ 加载；与 res/ 分开防图片同名资源冲突）
                    long videoBytes = 0;
                    foreach (var kv in videos)
                    {
                        var target = "media/" + kv.Value.Rel.Replace('\\', '/');
                        var entry = zip.CreateEntry(target);
                        using (var es = entry.Open())
                        using (var fs = File.OpenRead(kv.Value.Abs))
                            fs.CopyTo(es);
                        videoBytes += new FileInfo(kv.Value.Abs).Length;
                        manifest.Add(new ManifestEntry
                        {
                            Name = Path.GetFileName(kv.Value.Abs),
                            Type = TypeRes,
                            Target = target,
                            Size = new FileInfo(kv.Value.Abs).Length,
                            Sha256 = Sha256OfFile(kv.Value.Abs)
                        });
                    }
                    // 视频总和护栏 Trace（单文件 >64MB 已在上游抛异常阻断；总和超限 FW 整包拒收——尽早提示）
                    if (videoBytes > MaxUploadBytes)
                        System.Diagnostics.Trace.WriteLine($"[DeploymentPackageBuilder] 警告: 视频共 {videoBytes / (1024 * 1024)}MB 超 64MB 上传上限，FW 将拒收——请压缩视频");

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
                    if (tilesBytes > MaxUploadBytes)
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
