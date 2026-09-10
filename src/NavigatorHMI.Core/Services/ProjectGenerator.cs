using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 工程编译器。验证工程数据完整性，并将 <see cref="HMIProject"/> 编译输出为设备端可解析的 .navihmi 文件。
    /// </summary>
    /// <remarks>
    /// 输出格式为 ProtoBuf 二进制，与 FW 端 protobuf-cpp 兼容。
    /// 校验包括：控件 ObjectName 去重、变量名去重、报警规则引用变量存在性、BoundTag 悬空（控件/世界地图点）、
    /// ListRef 存在性与类型匹配、画面名全局重复、StartScreen 存在性（K-3 P0 补强）。
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

            // 3a. 校验（K-3 P0 补强）：BoundTag 悬空——控件绑定变量不存在（含世界地图作业点/范围点）
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                    if (!string.IsNullOrEmpty(w.BoundTag) && !tagNames.Contains(w.BoundTag))
                        result.Errors.Add($"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 绑定的变量 \"{w.BoundTag}\" 不存在");

            // 3a-P4. 校验（P-4，2026-09-02）：趋势图 TrendTagA/B 变量存在性 + 数值类型（TagCompatibility.Numeric 口径）
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                    if (w is TrendViewWidget tc)
                    {
                        var tags = project.Tags;
                        if (!string.IsNullOrEmpty(tc.TrendTagA) && !tagNames.Contains(tc.TrendTagA))
                            result.Errors.Add($"画面 \"{screen.Name}\" 趋势图 \"{w.ObjectName}\" 变量 A \"{tc.TrendTagA}\" 不存在");
                        if (!string.IsNullOrEmpty(tc.TrendTagB) && !tagNames.Contains(tc.TrendTagB))
                            result.Errors.Add($"画面 \"{screen.Name}\" 趋势图 \"{w.ObjectName}\" 变量 B \"{tc.TrendTagB}\" 不存在");
                        // XY 模式必须配置变量 B（时间-数据只需 A）
                        if (tc.TrendMode == TrendMode.XY && string.IsNullOrEmpty(tc.TrendTagB))
                            result.Errors.Add($"画面 \"{screen.Name}\" 趋势图 \"{w.ObjectName}\" 变量A-B 模式需配置变量 B");
                        // 类型：趋势绑数值变量（与 TagCompatibility 口径一致——BOOL/INT16/UINT16/INT32/FLOAT）
                        bool numericOk(Tag t) => TagCompatibility.IsNumericCompatible(t.DataType);
                        var ta = tags.FirstOrDefault(t => t.Name == tc.TrendTagA);
                        if (ta != null && !numericOk(ta))
                            result.Errors.Add($"画面 \"{screen.Name}\" 趋势图 \"{w.ObjectName}\" 变量 A \"{tc.TrendTagA}\" 类型 {ta.DataType} 非数值（趋势需 BOOL/INT16/UINT16/INT32/FLOAT）");
                        var tb = tags.FirstOrDefault(t => t.Name == tc.TrendTagB);
                        if (tb != null && !numericOk(tb))
                            result.Errors.Add($"画面 \"{screen.Name}\" 趋势图 \"{w.ObjectName}\" 变量 B \"{tc.TrendTagB}\" 类型 {tb.DataType} 非数值（趋势需 BOOL/INT16/UINT16/INT32/FLOAT）");
                    }
            if (project.WorldMap != null)
            {
                foreach (var wp in project.WorldMap.WorkPoints)
                    if (!string.IsNullOrEmpty(wp.BoundTag) && !tagNames.Contains(wp.BoundTag))
                        result.Errors.Add($"作业点绑定的变量 \"{wp.BoundTag}\" 不存在");
                foreach (var rp in project.WorldMap.WorkRangePoints)
                    if (!string.IsNullOrEmpty(rp.BoundTag) && !tagNames.Contains(rp.BoundTag))
                        result.Errors.Add($"范围点绑定的变量 \"{rp.BoundTag}\" 不存在");
            }

            // 3b. 校验（K-3 P0 补强；U-1 2026-09-06 按 (type,name) 定位——同名跨类型可共存）：
            // ListRef/VideoListRef 存在性 + 列表类型匹配——控件引用对应类型列表不存在或类型不符报错
            void CheckListRef(Screen screen, Widget w, string listRef, ListType expect)
            {
                if (string.IsNullOrEmpty(listRef)) return;
                var exists = project.Lists.Any(l => l.Type == expect && l.Name == listRef);
                if (exists) return;
                var anyName = project.Lists.Any(l => l.Name == listRef);
                var typeName = expect == ListType.Image ? "图片" : expect == ListType.Text ? "文本" : "视频";
                result.Errors.Add(anyName
                    ? $"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 引用 \"{listRef}\" 不是{typeName}列表（应为{typeName}列表——同名其它类型列表存在）"
                    : $"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 引用的{typeName}列表 \"{listRef}\" 不存在");
            }
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                {
                    if (w is ImageWidget im) CheckListRef(screen, w, im.ListRef, ListType.Image);
                    else if (w is TextListWidget tl) CheckListRef(screen, w, tl.ListRef, ListType.Text);
                    else if (w is FrameWidget fr) CheckListRef(screen, w, fr.ListRef, ListType.Image);   // Frame 图模式
                    if (w is FrameWidget fv) CheckListRef(screen, w, fv.VideoListRef, ListType.Video);   // S-5 视频列表
                }

            // 3c. 校验（K-3 P0 补强）：画面名全局重复
            foreach (var g in project.Screens.GroupBy(s => s.Name).Where(g => g.Count() > 1))
                result.Errors.Add($"画面 \"{g.Key}\" 重复定义 ({g.Count()} 次)");

            // 3d. 校验（K-3 P0 补强）：StartScreen 存在性
            if (!string.IsNullOrEmpty(project.StartScreen) && !project.Screens.Any(s => s.Name == project.StartScreen))
                result.Errors.Add($"启动画面 \"{project.StartScreen}\" 不存在");

            // 3e. 校验（P-3 B1 严格，2026-09-02 用户裁决）：设备端不含世界地图时的非法引用
            if (!project.IncludeWorldMapOnDevice)
            {
                var worldMapScreenName = project.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap)?.Name;
                var hasCustom = project.Screens.Any(s => s.Type == ScreenType.Custom);
                if (!hasCustom)
                    result.Errors.Add("设备端不含世界地图且无自定义画面——工程无可显示画面（请在「设备端显示世界地图画面」勾选或添加自定义画面）");
                if (!string.IsNullOrEmpty(worldMapScreenName))
                {
                    // 启动画面指向世界地图（设备端不含 → FW 启动兜底失效场景）
                    if (project.StartScreen == worldMapScreenName)
                        result.Errors.Add($"启动画面 \"{worldMapScreenName}\" 指向世界地图画面，但设备端不含世界地图（请在「设备端显示世界地图画面」勾选）");
                    // 画面内事件/动作 target_screen 引用世界地图（防 FW switchToName 静默失败）
                    foreach (var screen in project.Screens)
                        foreach (var w in screen.Widgets)
                            foreach (var ev in w.Events)
                                foreach (var act in ev.Actions)
                                    if (act.Parameters.TryGetValue("target_screen", out var target) && target == worldMapScreenName)
                                        result.Errors.Add($"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 事件 {ev.Type} 的跳转目标 \"{worldMapScreenName}\" 是设备端不含的世界地图");
                    if (project.WorldMap != null)
                        foreach (var ev in project.WorldMap.Events)
                            foreach (var act in ev.Actions)
                                if (act.Parameters.TryGetValue("target_screen", out var target) && target == worldMapScreenName)
                                    result.Errors.Add($"世界地图事件 {ev.Type} 的跳转目标 \"{worldMapScreenName}\" 指向设备端不含的世界地图");
                }
            }

            // 3f. 校验（Q-4，2026-09-04 用户裁决：>64MB 本地视频「打包应该编译的时候做」——编译即报错，
            //     部署打包侧护栏保留双保险）：Frame 视频模式本地源单文件 >64MB（上限 = DeploymentPackageBuilder.MaxUploadBytes，
            //     对齐 FW kMaxUploadBytes 拒收）→ 编译错误；网络流（rtsp/http/https）不入包不校验；
            //     文件缺失不报（部署链路缺失 Trace 语义不变——此处只管已存在文件的超限）
            string? projDirForVideo = Path.GetDirectoryName(project.ProjectFilePath);
            if (string.IsNullOrEmpty(projDirForVideo)) projDirForVideo = ".";
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                    if (w is FrameWidget fv && fv.ShowVideo && !string.IsNullOrWhiteSpace(fv.VideoSource)
                        && !fv.VideoSource.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                        && !fv.VideoSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        && !fv.VideoSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        string abs;
                        try
                        {
                            abs = Path.IsPathRooted(fv.VideoSource)
                                ? Path.GetFullPath(fv.VideoSource)
                                : Path.GetFullPath(Path.Combine(projDirForVideo, fv.VideoSource));
                        }
                        catch { continue; }
                        if (!File.Exists(abs)) continue;
                        var vlen = new FileInfo(abs).Length;
                        if (vlen > DeploymentPackageBuilder.MaxUploadBytes)
                            result.Errors.Add($"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 视频源 \"{fv.VideoSource}\" "
                                + $"超过单文件防呆上限（{DeploymentPackageBuilder.MaxUploadBytes / (1024 * 1024)}MB，实际 {(vlen + 1024 * 1024 - 1) / (1024 * 1024)}MB）——请压缩视频或改用 RTSP 流");
                    }

            // 3g. 校验（X-1，2026-09-08 用户规格——GPS CoordinatePicker 前置；按执行序：先范围规则后 GPS 规则）：
            //   ① 设备端含世界地图的画面 ⇒ 划定作业范围——「有地图必须划作业范围（作业范围生成地图底图）」
            //   ② GPS 变量 ⇔ 工程含世界地图画面 + 设备端包含 + 划定作业范围（≥3 点）——「无地图工程不能配置 GPS 变量」
            var worldMapScreen = project.Screens.FirstOrDefault(s => s.Type == ScreenType.WorldMap);
            var wm = project.WorldMap;
            int wmRangeCount = wm?.WorkRangePoints.Count ?? 0;
            if (worldMapScreen != null && project.IncludeWorldMapOnDevice && wmRangeCount < 3)
                result.Errors.Add("工程含世界地图画面，需先划定作业范围（至少 3 个范围点——作业范围确定地图底图与显示区域）");
            var gpsTags = project.Tags.Where(t => t.DataType == TagDataType.GPS).ToList();
            if (gpsTags.Count > 0)
            {
                bool mapReady = worldMapScreen != null && project.IncludeWorldMapOnDevice && wmRangeCount >= 3;
                if (!mapReady)
                    foreach (var gt in gpsTags)
                        result.Errors.Add($"GPS 变量 \"{gt.Name}\" 需工程含世界地图底图（勾选「设备端显示世界地图画面」并划定作业范围 ≥3 点）——无地图工程不能配置 GPS 变量");
            }

            // 3h. 校验（Y-3b 2026-09-10 + Z 循环多连接 2026-09-11：连接级——每连接 broker/Binding 引用/StatusTag；
            //   旧单份 DeviceName 语义废弃（Z-4 迁移归 Connections[0]），本校验不再查设备）
            var mqtt = project.MqttSettings;
            if (mqtt != null && mqtt.EnableMqtt)
            {
                if (mqtt.Connections.Count == 0)
                    result.Errors.Add("MQTT 总开关已启用，但未配置任何连接——请在「MQTT → 连接管理」页点「＋ 新建连接」创建（连接页填 broker 地址）");
                foreach (var c in mqtt.Connections)
                {
                    // 连接参数编译级兜底（命令层已校验格式——此处防绕过：broker 为空即不可建连接）
                    if (string.IsNullOrWhiteSpace(c.Config?.Broker))
                        result.Errors.Add($"MQTT 连接 \"{c.Name}\" 未填写 broker 地址（连接页连接参数——主机/IP 不含协议前缀）");
                    // Binding 引用对象存在：TopicName 须在本连接 Topics、TagName 须在 Tags（防悬空映射静默失效）
                    foreach (var b in c.Bindings)
                    {
                        if (!c.Topics.Any(t => t.Name == b.TopicName))
                            result.Errors.Add($"MQTT 连接 \"{c.Name}\" 绑定 {b.TagName}↔{b.FieldName} 引用的主题配置 \"{b.TopicName}\" 不存在");
                        if (!project.Tags.Any(t => t.Name == b.TagName))
                            result.Errors.Add($"MQTT 连接 \"{c.Name}\" 绑定 {b.TagName}↔{b.FieldName} 引用的变量 \"{b.TagName}\" 不存在");
                    }
                    // StatusTag 引用存在（该连接状态回写变量）
                    var statusTag = c.Config?.StatusTag;
                    if (!string.IsNullOrWhiteSpace(statusTag) && !project.Tags.Any(t => t.Name == statusTag))
                        result.Errors.Add($"MQTT 连接 \"{c.Name}\" 的 StatusTag \"{statusTag}\" 变量不存在（请在变量管理器创建或清空状态回写设置）");
                }
            }

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

            // 4.1 编译为契约 DTO（navihmi.proto 格式，FW 端 protoc 可解析；扁平化 Widget 继承）
            // K-3：原子输出（同目录 .tmp + File.Replace/Move；失败清理临时文件、原产物完好——与 P1 原子保存同套路）
            var dto = ToDto(project);
            string tmpPath = outputPath + ".tmp";
            try
            {
                using (var fs = File.Create(tmpPath))
                {
                    Serializer.Serialize(fs, dto);
                }
                if (File.Exists(outputPath))
                    File.Replace(tmpPath, outputPath, null);
                else
                    File.Move(tmpPath, outputPath);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* 清理失败不掩盖原异常 */ }
                throw;
            }

            result.OutputPath = outputPath;
            return result;
        }

        // ═══════ 模型 → 编译契约 DTO 转换（navihmi.proto） ═══════

        /// <summary>工程模型 → 契约 DTO（Tag/AlarmRule/DeviceConfig/WorldMapConfig/ListDef 字段号与 proto 一致直接复用；Screen/Widget 扁平化）。</summary>
        private static NavihmiProject ToDto(HMIProject p)
        {
            return new NavihmiProject
            {
                Name = p.Name,
                CreateTime = ToUnixSeconds(p.CreateTime),
                LastModifiedTime = ToUnixSeconds(p.LastModifiedTime),
                Version = p.Version,
                ProjectFilePath = p.ProjectFilePath,
                DeviceWidth = p.DeviceWidth,
                DeviceHeight = p.DeviceHeight,
                ShowNavigationBar = p.ShowNavigationBar,
                NavigationPosition = p.NavigationPosition,
                StartScreen = p.StartScreen,
                WorldMap = p.IncludeWorldMapOnDevice ? p.WorldMap : null,   // P-3 B1：开关关 → 地图配置死数据不下发（设备端无地图画面，减包体；审查 🟡 死数据已处理）
                Screens = p.Screens.Where(s => p.IncludeWorldMapOnDevice || s.Type != ScreenType.WorldMap).Select(ToScreen).ToList(),   // P-3 B1：开关关 → 编译产物过滤 WorldMap Screen（设备端无地图；PC 编辑态不受影响）
                Tags = new List<Tag>(p.Tags),
                Alarms = p.Alarms,
                Devices = p.Devices,
                Lists = p.Lists,
                Users = p.Users,      // W1 用户系统（编译进 .navihmi，FW 登录/权限/密码策略）
                Groups = p.Groups,
                Security = p.Security,
                DeviceModel = p.DeviceModel,   // 工程目标设备型号写入契约（设备身份由设备自身配置决定，与工程无关——2026-08-30 用户 Check 指正）
                IncludeWorldMapOnDevice = p.IncludeWorldMapOnDevice,   // P-3：透传（FW 读取可感知工程意图；FW 启动兜底靠 screens 列表本身）
                MqttSettings = p.MqttSettings != null && p.MqttSettings.EnableMqtt ? p.MqttSettings : null,
                // Y-2/Z 循环：EnableMqtt=false 或未配置 → 编译 Nullify（FW 不建任何连接对象——设计②总开关禁用语义；
                //   PC 编辑态数据保留在 p.MqttSettings，仅产物空化）
            };
        }

        private static NavihmiScreen ToScreen(Screen s)
            => new NavihmiScreen
            {
                Name = s.Name, Width = s.Width, Height = s.Height, Type = s.Type,
                IsGlobal = s.IsGlobal, ShowInNav = s.ShowInNav, NavOrder = s.NavOrder,
                Widgets = s.Widgets.Select(ToWidget).ToList(),
            };

        /// <summary>
        /// 控件 → 扁平 DTO 映射注册表（P-2a 注册表化，2026-09-02）。
        /// 键 = 源模型控件类型；值 = 填充委托（写 dto.Type 判别 + 按类型填子类字段）。
        /// 新增控件类型必须在此注册（登记完整性测试断言全部 Widget 子类已注册，未注册即测试失败）。
        /// </summary>
        private static readonly Dictionary<Type, Action<Widget, NavihmiWidget>> _toWidgetMappers = CreateToWidgetMappers();

        /// <summary>构建 ToWidget 注册表（静态初始化；各控件填充逻辑与原 switch case 一一对应，零行为变化）。</summary>
        private static Dictionary<Type, Action<Widget, NavihmiWidget>> CreateToWidgetMappers()
        {
            var map = new Dictionary<Type, Action<Widget, NavihmiWidget>>();
            void Register<T>(Action<T, NavihmiWidget> fill) where T : Widget
                => map[typeof(T)] = (w, dto) => fill((T)w, dto);

            Register<ButtonWidget>((b, dto) =>
            {
                dto.Type = NavihmiWidgetType.Button; dto.Text = b.Text;
                CopyFont(dto, b); dto.TextColor = b.TextColor; dto.FillColor = b.FillColor;
            });
            Register<TextWidget>((t, dto) =>
            {
                dto.Type = NavihmiWidgetType.Text; dto.Content = t.Content;
                dto.FillColor = t.FillColor; dto.HAlign = t.HAlign;
                CopyFont(dto, t); dto.TextColor = t.TextColor;
            });
            Register<LabelWidget>((l, dto) =>
            {
                dto.Type = NavihmiWidgetType.Label; dto.Text = l.Text;
                dto.HAlign = l.HAlign; dto.FillColor = l.FillColor;
                CopyFont(dto, l); dto.TextColor = l.TextColor;
            });
            Register<RectangleWidget>((r, dto) =>
            {
                dto.Type = NavihmiWidgetType.Rectangle; dto.FillColor = r.FillColor;
            });
            Register<ImageWidget>((img, dto) =>
            {
                dto.Type = NavihmiWidgetType.Image; dto.ImagePath = img.ImagePath;
                dto.StretchMode = img.StretchMode; dto.FillColor = img.FillColor;
                dto.ListRef = img.ListRef; dto.DefaultIndex = img.DefaultIndex;
            });
            Register<NumericDisplayWidget>((nd, dto) =>
            {
                dto.Type = NavihmiWidgetType.NumericDisplay; dto.Value = nd.Value;
                CopyFont(dto, nd); dto.TextColor = nd.TextColor; dto.FillColor = nd.FillColor;
            });
            Register<SwitchWidget>((sw, dto) =>
            {
                dto.Type = NavihmiWidgetType.Switch; dto.IsOn = sw.IsOn;
                dto.OnText = sw.OnText; dto.OffText = sw.OffText;
                CopyFont(dto, sw); dto.TextColor = sw.TextColor; dto.FillColor = sw.FillColor;
            });
            Register<LineWidget>((ln, dto) =>
            {
                dto.Type = NavihmiWidgetType.Line; dto.X2 = ln.X2; dto.Y2 = ln.Y2;
                dto.StrokeColor = ln.StrokeColor; dto.StrokeThickness = ln.StrokeThickness;
            });
            Register<CircleWidget>((ci, dto) =>
            {
                dto.Type = NavihmiWidgetType.Circle;
                dto.FillColor = ci.FillColor; dto.StrokeColor = ci.StrokeColor; dto.StrokeThickness = ci.StrokeThickness;
            });
            Register<EllipseWidget>((el, dto) =>
            {
                dto.Type = NavihmiWidgetType.Ellipse;
                dto.FillColor = el.FillColor; dto.StrokeColor = el.StrokeColor; dto.StrokeThickness = el.StrokeThickness;
            });
            Register<IOFieldWidget>((io, dto) =>
            {
                dto.Type = NavihmiWidgetType.IOField; dto.Content = io.Content;
                dto.IsReadOnly = io.IsReadOnly; dto.FillColor = io.FillColor;
                CopyFont(dto, io); dto.TextColor = io.TextColor;
            });
            Register<CheckBoxWidget>((cb, dto) =>
            {
                dto.Type = NavihmiWidgetType.CheckBox; dto.IsChecked = cb.IsChecked; dto.Text = cb.Text;
                CopyFont(dto, cb); dto.TextColor = cb.TextColor; dto.FillColor = cb.FillColor;
            });
            Register<TextListWidget>((tl, dto) =>
            {
                dto.Type = NavihmiWidgetType.TextList;
                dto.ListRef = tl.ListRef; dto.DefaultIndex = tl.DefaultIndex;
                CopyFont(dto, tl); dto.TextColor = tl.TextColor; dto.FillColor = tl.FillColor;
            });
            Register<FrameWidget>((fr, dto) =>
            {
                dto.Type = NavihmiWidgetType.Frame; dto.Title = fr.Title;
                dto.FillColor = fr.FillColor; dto.ImagePath = fr.ImagePath;
                dto.ListRef = fr.ListRef; dto.DefaultIndex = fr.DefaultIndex;
                dto.ShowVideo = fr.ShowVideo; dto.VideoSource = fr.VideoSource;   // P-6：视频模式
                dto.PlayTag = fr.PlayTag;   // R-4：播放控制布尔变量
                dto.VideoListRef = fr.VideoListRef; dto.VideoIndexTag = fr.VideoIndexTag;   // S-5：视频源列表 + 选择索引变量
                CopyFont(dto, fr);
            });
            Register<ProgressBarWidget>((pb, dto) =>
            {
                dto.Type = NavihmiWidgetType.ProgressBar; dto.Value = pb.Value;
                dto.Min = pb.Min; dto.Max = pb.Max; dto.FillStyle = pb.FillStyle; dto.FillColor = pb.FillColor;
            });
            Register<DateTimeWidget>((dt, dto) =>
            {
                dto.Type = NavihmiWidgetType.DateTime;
                // F-1（2026-09-06 用户报告 DateTime 时间不变）：D2 起 Text 不再参与未绑定显示（未绑定恒 FormatNow
                // 实时、Text 仅旧工程序列化兼容）——不再下发占位/旧残留 Text（FW 端 dtText 非空会压死 1Hz 实时刷新，
                // 显示死时间）。绑定变量同样不下发静态文本：FW 运行时按 boundTag 订阅 DataManager 实时值
                // （FW HmiDateTime 同步补绑定订阅）；未绑定由 FW 按 dtFormat 1Hz 实时系统时间。
                dto.DtText = "";
                dto.DtFormat = dt.Format;
            });
            Register<PolygonWidget>((pg, dto) =>
            {
                dto.Type = NavihmiWidgetType.Polygon;
                dto.FillColor = pg.FillColor; dto.StrokeColor = pg.StrokeColor; dto.StrokeThickness = pg.StrokeThickness;
                dto.Points = pg.Points;
            });
            Register<TrendViewWidget>((tc, dto) =>
            {
                dto.Type = NavihmiWidgetType.TrendView; dto.TrendMode = (int)tc.TrendMode;
                dto.TrendTagA = tc.TrendTagA; dto.TrendTagB = tc.TrendTagB;
                dto.SampleIntervalMs = tc.SampleIntervalMs; dto.TimeWindowSeconds = tc.TimeWindowSeconds;
                dto.LineColor = tc.LineColor; dto.LineWidth = tc.LineWidth; dto.RefreshRateMs = tc.RefreshRateMs;
            });
            Register<WindowWidget>((ww, dto) =>
            {
                dto.Type = NavihmiWidgetType.Window; dto.WindowType = (int)ww.Type;
                dto.WinTitle = ww.Title; dto.ShowTitleBar = ww.ShowTitleBar;
                dto.FillColor = ww.FillColor; dto.Title = ww.BorderColor;   // Title 槽位复用承载边框色（proto W_WINDOW 用 border 字段）
                dto.ShowHistory = ww.ShowHistory; dto.SelectedTag = ww.SelectedTag;
                dto.CardWidth = ww.CardWidth; dto.CardHeight = ww.CardHeight;
                dto.ShowUserName = ww.ShowUserName; dto.ShowRole = ww.ShowRole; dto.ShowMode = ww.ShowMode;
                dto.CardShowNumber = ww.CardShowNumber; dto.CardShowStatus = ww.CardShowStatus; dto.CardShowLocation = ww.CardShowLocation;
                dto.BoundDevice = ww.BoundDevice; dto.RobotSlots = ww.RobotSlots;
                dto.DisplayMode = (int)ww.DisplayMode;   // P-5：AlarmView 显示模式（54）
            });
            Register<HistoryViewWidget>((hv, dto) =>
            {
                dto.Type = NavihmiWidgetType.HistoryView; dto.HistoryTags = hv.Tags; dto.HistoryDbPath = hv.DbPath;
                dto.HistoryTagTitles = hv.Titles;   // Q-6：列显示名平行 Tags（空=显示变量名）
            });
            return map;
        }

        /// <summary>控件 → 扁平 DTO：基类字段 + 类型判别 + 按类型填子类字段（P-2a 注册表查表分发；未注册类型 throw 防静默）。</summary>
        private static NavihmiWidget ToWidget(Widget w)
        {
            var dto = new NavihmiWidget
            {
                X = w.X, Y = w.Y, Width = w.Width, Height = w.Height,
                ObjectName = w.ObjectName, BoundTag = w.BoundTag, Events = w.Events,
            };
            if (_toWidgetMappers.TryGetValue(w.GetType(), out var fill))
            {
                fill(w, dto);
                return dto;
            }
            throw new InvalidOperationException($"未映射的控件类型: {w.GetType().Name}（新增控件需同步 navihmi.proto、NavihmiDto 并注册 ProjectGenerator ToWidget 映射表）");
        }

        /// <summary>ToWidget 注册表覆盖性断言（登记完整性测试用）：全部 Widget 子类已注册映射。</summary>
        public static IReadOnlyCollection<Type> RegisteredWidgetTypes => _toWidgetMappers.Keys;

        /// <summary>拷贝字体五件套（各控件子类均有同名属性，dynamic 统一处理；编译期一次性调用，性能无碍）。</summary>
        private static void CopyFont(NavihmiWidget dto, dynamic w)
        {
            dto.FontFamily = (string)w.FontFamily; dto.FontSize = (double)w.FontSize;
            dto.FontWeight = (string)w.FontWeight; dto.FontStyle = (string)w.FontStyle; dto.TextDecoration = (string)w.TextDecoration;
        }

        /// <summary>DateTime → Unix 秒（MinValue/1970 前返回 0，防 DateTimeOffset 溢出）。</summary>
        private static long ToUnixSeconds(DateTime dt)
            => dt.Year <= 1970 ? 0 : new DateTimeOffset(dt).ToUnixTimeSeconds();
    }
}
