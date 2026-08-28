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
            if (project.WorldMap != null)
            {
                foreach (var wp in project.WorldMap.WorkPoints)
                    if (!string.IsNullOrEmpty(wp.BoundTag) && !tagNames.Contains(wp.BoundTag))
                        result.Errors.Add($"作业点绑定的变量 \"{wp.BoundTag}\" 不存在");
                foreach (var rp in project.WorldMap.WorkRangePoints)
                    if (!string.IsNullOrEmpty(rp.BoundTag) && !tagNames.Contains(rp.BoundTag))
                        result.Errors.Add($"范围点绑定的变量 \"{rp.BoundTag}\" 不存在");
            }

            // 3b. 校验（K-3 P0 补强）：ListRef 存在性 + 列表类型匹配——图片/文本列表/框架引用列表不存在或类型不符
            var listDefs = project.Lists.ToDictionary(l => l.Name, l => l.Type);
            foreach (var screen in project.Screens)
                foreach (var w in screen.Widgets)
                {
                    if (w is not (ImageWidget or TextListWidget or FrameWidget)) continue;
                    var listRef = (w as ImageWidget)?.ListRef ?? (w as TextListWidget)?.ListRef ?? (w as FrameWidget)?.ListRef;
                    if (string.IsNullOrEmpty(listRef)) continue;
                    if (!listDefs.TryGetValue(listRef, out var listType))
                    {
                        result.Errors.Add($"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 引用的列表 \"{listRef}\" 不存在");
                        continue;
                    }
                    var needImage = w is ImageWidget or FrameWidget;
                    if (needImage && listType != ListType.Image)
                        result.Errors.Add($"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 引用 \"{listRef}\" 为文本列表（应为图片列表）");
                    if (!needImage && listType != ListType.Text)
                        result.Errors.Add($"画面 \"{screen.Name}\" 控件 \"{w.ObjectName}\" 引用 \"{listRef}\" 为图片列表（应为文本列表）");
                }

            // 3c. 校验（K-3 P0 补强）：画面名全局重复
            foreach (var g in project.Screens.GroupBy(s => s.Name).Where(g => g.Count() > 1))
                result.Errors.Add($"画面 \"{g.Key}\" 重复定义 ({g.Count()} 次)");

            // 3d. 校验（K-3 P0 补强）：StartScreen 存在性
            if (!string.IsNullOrEmpty(project.StartScreen) && !project.Screens.Any(s => s.Name == project.StartScreen))
                result.Errors.Add($"启动画面 \"{project.StartScreen}\" 不存在");

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
                WorldMap = p.WorldMap,
                Screens = p.Screens.Select(ToScreen).ToList(),
                Tags = new List<Tag>(p.Tags),
                Alarms = p.Alarms,
                Devices = p.Devices,
                Lists = p.Lists,
                Users = p.Users,      // W1 用户系统（编译进 .navihmi，FW 登录/权限/密码策略）
                Groups = p.Groups,
                Security = p.Security,
            };
        }

        private static NavihmiScreen ToScreen(Screen s)
            => new NavihmiScreen
            {
                Name = s.Name, Width = s.Width, Height = s.Height, Type = s.Type,
                IsGlobal = s.IsGlobal, ShowInNav = s.ShowInNav, NavOrder = s.NavOrder,
                Widgets = s.Widgets.Select(ToWidget).ToList(),
            };

        /// <summary>控件 → 扁平 DTO：基类字段 + 类型判别 + 按类型填子类字段。</summary>
        private static NavihmiWidget ToWidget(Widget w)
        {
            var dto = new NavihmiWidget
            {
                X = w.X, Y = w.Y, Width = w.Width, Height = w.Height,
                ObjectName = w.ObjectName, BoundTag = w.BoundTag, Events = w.Events,
            };
            switch (w)
            {
                case ButtonWidget b:
                    dto.Type = NavihmiWidgetType.Button; dto.Text = b.Text;
                    CopyFont(dto, b); dto.TextColor = b.TextColor; dto.FillColor = b.FillColor;
                    break;
                case TextWidget t:
                    dto.Type = NavihmiWidgetType.Text; dto.Content = t.Content;
                    dto.FillColor = t.FillColor; dto.HAlign = t.HAlign;
                    CopyFont(dto, t); dto.TextColor = t.TextColor;
                    break;
                case LabelWidget l:
                    dto.Type = NavihmiWidgetType.Label; dto.Text = l.Text;
                    dto.HAlign = l.HAlign; dto.FillColor = l.FillColor;
                    CopyFont(dto, l); dto.TextColor = l.TextColor;
                    break;
                case RectangleWidget r:
                    dto.Type = NavihmiWidgetType.Rectangle; dto.FillColor = r.FillColor;
                    break;
                case ImageWidget img:
                    dto.Type = NavihmiWidgetType.Image; dto.ImagePath = img.ImagePath;
                    dto.StretchMode = img.StretchMode; dto.FillColor = img.FillColor;
                    dto.ListRef = img.ListRef; dto.DefaultIndex = img.DefaultIndex;
                    break;
                case NumericDisplayWidget nd:
                    dto.Type = NavihmiWidgetType.NumericDisplay; dto.Value = nd.Value;
                    CopyFont(dto, nd); dto.TextColor = nd.TextColor; dto.FillColor = nd.FillColor;
                    break;
                case SwitchWidget sw:
                    dto.Type = NavihmiWidgetType.Switch; dto.IsOn = sw.IsOn;
                    dto.OnText = sw.OnText; dto.OffText = sw.OffText;
                    CopyFont(dto, sw); dto.TextColor = sw.TextColor; dto.FillColor = sw.FillColor;
                    break;
                case LineWidget ln:
                    dto.Type = NavihmiWidgetType.Line; dto.X2 = ln.X2; dto.Y2 = ln.Y2;
                    dto.StrokeColor = ln.StrokeColor; dto.StrokeThickness = ln.StrokeThickness;
                    break;
                case CircleWidget ci:
                    dto.Type = NavihmiWidgetType.Circle;
                    dto.FillColor = ci.FillColor; dto.StrokeColor = ci.StrokeColor; dto.StrokeThickness = ci.StrokeThickness;
                    break;
                case EllipseWidget el:
                    dto.Type = NavihmiWidgetType.Ellipse;
                    dto.FillColor = el.FillColor; dto.StrokeColor = el.StrokeColor; dto.StrokeThickness = el.StrokeThickness;
                    break;
                case IOFieldWidget io:
                    dto.Type = NavihmiWidgetType.IOField; dto.Content = io.Content;
                    dto.IsReadOnly = io.IsReadOnly; dto.FillColor = io.FillColor;
                    CopyFont(dto, io); dto.TextColor = io.TextColor;
                    break;
                case CheckBoxWidget cb:
                    dto.Type = NavihmiWidgetType.CheckBox; dto.IsChecked = cb.IsChecked; dto.Text = cb.Text;
                    CopyFont(dto, cb); dto.TextColor = cb.TextColor; dto.FillColor = cb.FillColor;
                    break;
                case TextListWidget tl:
                    dto.Type = NavihmiWidgetType.TextList;
                    dto.ListRef = tl.ListRef; dto.DefaultIndex = tl.DefaultIndex;
                    CopyFont(dto, tl); dto.TextColor = tl.TextColor; dto.FillColor = tl.FillColor;
                    break;
                case FrameWidget fr:
                    dto.Type = NavihmiWidgetType.Frame; dto.Title = fr.Title;
                    dto.FillColor = fr.FillColor; dto.ImagePath = fr.ImagePath;
                    dto.ListRef = fr.ListRef; dto.DefaultIndex = fr.DefaultIndex;
                    CopyFont(dto, fr);
                    break;
                case ProgressBarWidget pb:
                    dto.Type = NavihmiWidgetType.ProgressBar; dto.Value = pb.Value;
                    dto.Min = pb.Min; dto.Max = pb.Max; dto.FillStyle = pb.FillStyle; dto.FillColor = pb.FillColor;
                    break;
                case DateTimeWidget dt:
                    dto.Type = NavihmiWidgetType.DateTime; dto.DtText = dt.Text; dto.DtFormat = dt.Format;
                    break;
                case PolygonWidget pg:
                    dto.Type = NavihmiWidgetType.Polygon;
                    dto.FillColor = pg.FillColor; dto.StrokeColor = pg.StrokeColor; dto.StrokeThickness = pg.StrokeThickness;
                    dto.Points = pg.Points;
                    break;
                case WindowWidget ww:
                    dto.Type = NavihmiWidgetType.Window; dto.WindowType = (int)ww.Type;
                    dto.WinTitle = ww.Title; dto.ShowTitleBar = ww.ShowTitleBar;
                    dto.FillColor = ww.FillColor; dto.Title = ww.BorderColor;   // Title 槽位复用承载边框色（proto W_WINDOW 用 border 字段）
                    dto.ShowHistory = ww.ShowHistory; dto.SelectedTag = ww.SelectedTag;
                    dto.CardWidth = ww.CardWidth; dto.CardHeight = ww.CardHeight;
                    dto.ShowUserName = ww.ShowUserName; dto.ShowRole = ww.ShowRole; dto.ShowMode = ww.ShowMode;
                    dto.CardShowNumber = ww.CardShowNumber; dto.CardShowStatus = ww.CardShowStatus; dto.CardShowLocation = ww.CardShowLocation;
                    dto.BoundDevice = ww.BoundDevice; dto.RobotSlots = ww.RobotSlots;
                    break;
                default:
                    throw new InvalidOperationException($"未映射的控件类型: {w.GetType().Name}（新增控件需同步 navihmi.proto 与 NavihmiDto）");
            }
            return dto;
        }

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
