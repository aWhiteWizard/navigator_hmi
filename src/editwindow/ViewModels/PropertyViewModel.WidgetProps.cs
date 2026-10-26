using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using NavigatorHMI.CommandLayer;
using NavigatorHMI.Common;
using Serilog;

namespace NavigatorHMI.ViewModels
{
    /// <summary>
    /// 属性窗口的 ViewModel，持有当前选中的 Widget 或 Screen 并暴露其属性供绑定。
    /// P-2d（2026-09-02）：改 partial——本批仅开放 partial 能力（存量区段拆分随各批落地），
    /// 新控件属性（TrendView 等）落独立 partial 文件（PropertyViewModel.TrendView.cs），
    /// 不再堆入主文件（v1.1-design §5.3「改什么拆什么」；partial 各部分同 namespace/同访问级别，
    /// INotifyPropertyChanged 在主文件一处声明即对整个类生效）。
    /// </summary>
    public partial class PropertyViewModel : INotifyPropertyChanged
    {

        // AB-6 复审（2026-09-11）：控件特有属性组（按钮/文本/列表/Frame 视频/进度条/Window/机器人槽位等） —— 自 PropertyViewModel.cs 连续区间纯搬家（基线 8a2c83d 第 374-772 行；内容零改动）
        // ── 子类特有属性 ──

        private string _buttonText = "";
        public string ButtonText
        {
            get => _buttonText;
            set
            {
                if (_buttonText != value)
                {
                    _buttonText = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ButtonWidget btn) btn.Text = value;
                }
            }
        }

        private string _textContent = "";
        public string TextContent
        {
            get => _textContent;
            set
            {
                if (_textContent != value)
                {
                    _textContent = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.Content = value;
                }
            }
        }
        /// <summary>Text 水平对齐。</summary>
        public string TextHAlign { get => _textHAlign; set { if (_textHAlign != value) { _textHAlign = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextWidget txt) txt.HAlign = value; } } }

        private string _labelText = "";

        public string LabelText { get => _labelText; set { if (_labelText != value) { _labelText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LabelWidget lbl) lbl.Text = value; } } }

        private string _imagePath = "";
        public string ImagePath { get => _imagePath; set { if (_imagePath != value) { _imagePath = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.ImagePath = value; } } }
        private string _imageFillColor = "#EEEEEE";

        // ── 列表绑定（Image/Frame/TextList 共用模式：ListRef 下拉 + DefaultIndex 缺省值） ──

        /// <summary>图片列表集合（选中 Image/Frame 时的下拉数据源，首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> ImageListOptions { get; } = new();

        /// <summary>文本列表集合（选中 TextList 时的下拉数据源，首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> TextListOptions { get; } = new();

        /// <summary>重建列表下拉（首项空串=不绑列表 + 工程对应类型列表名 + 选中控件已绑定列表占位），选中控件时调用。
        /// 用增量同步（不 Clear 重建）——Clear+Add 重建会丢 ComboBox 选中且 null 短路后无法自动恢复（ListManager 同款坑）。</summary>
        private void RefreshListOptions(Widget? w)
        {
            BeginSuppressListRefWrites();   // 增量同步移除占位项时 ComboBox 失配回写 """ 会真解绑——窗口期拦截
            var imgNames = Project?.Lists.Where(l => l.Type == ListType.Image).Select(l => l.Name) ?? Enumerable.Empty<string>();
            var textNames = Project?.Lists.Where(l => l.Type == ListType.Text).Select(l => l.Name) ?? Enumerable.Empty<string>();   // S-4：仅文本型（原 != Image 会混入 Video 型）
            var videoNames = Project?.Lists.Where(l => l.Type == ListType.Video).Select(l => l.Name) ?? Enumerable.Empty<string>();   // S-5：视频列表下拉选项（Check 2026-09-05 收紧：仅 Video 型——Text 型列表给 TextList 控件，不混入 Frame 视频列表下拉；Check ② 曾放开 Text 已收回）
            SyncOptions(ImageListOptions, imgNames);
            SyncOptions(TextListOptions, textNames);
            SyncOptions(VideoListOptions, videoNames);
            // 占位：选中控件已绑定的列表名（即使类型不符/列表已删）加入对应 Options，
            // 防 ComboBox SelectedItem 失配导致属性面板显示空（AI/CLI 建的列表 type 可能与控件期望不符，但功能正常）
            switch (w)
            {
                case ImageWidget img when !string.IsNullOrEmpty(img.ListRef) && !ImageListOptions.Contains(img.ListRef):
                    ImageListOptions.Add(img.ListRef); break;
                case FrameWidget f:
                    // 审查 🟡-2：Frame 可能同时持 ListRef（图模式残留）与 VideoListRef——占位须两集合都补（互斥 case 会漏后者）
                    if (!string.IsNullOrEmpty(f.ListRef) && !ImageListOptions.Contains(f.ListRef))
                        ImageListOptions.Add(f.ListRef);
                    if (!string.IsNullOrEmpty(f.VideoListRef) && !VideoListOptions.Contains(f.VideoListRef))
                        VideoListOptions.Add(f.VideoListRef);
                    break;
                case TextListWidget tl when !string.IsNullOrEmpty(tl.ListRef) && !TextListOptions.Contains(tl.ListRef):
                    TextListOptions.Add(tl.ListRef); break;
            }
        }

        /// <summary>增量同步选项集合：首项 "" 恒保持，后续按名称尾部增删/对齐——不 Clear 重建，保 ComboBox 选中（wpf-combobox-style 集合重建陷阱）。</summary>
        private static void SyncOptions(System.Collections.ObjectModel.ObservableCollection<string> options, IEnumerable<string> names)
        {
            if (options.Count == 0 || options[0] != "") options.Insert(0, "");   // 哨兵首项
            var keep = new HashSet<string>(names);
            for (int i = options.Count - 1; i >= 1; i--)
                if (!keep.Contains(options[i])) options.RemoveAt(i);
            var pos = 1;
            foreach (var n in names)
            {
                while (pos < options.Count && options[pos] != n) pos++;
                if (pos == options.Count && !options.Contains(n)) options.Add(n);   // 去重兜底（防乱序输入重复添加）
                pos++;
            }
        }

        private string? _imageListRef = "";
        /// <summary>ImageWidget 绑定的图片列表名（null=无）。</summary>
        public string? ImageListRef
        {
            get => _imageListRef;
            set
            {
                // null = 切换选中控件时 RefreshListOptions 重建下拉的瞬态（ComboBox SelectedItem 在空选项时变 null）——忽略防误解绑；
                // "" = 用户显式选「（无绑定）」解绑，保留
                if (value == null) return;
                if (_imageListRef != value)
                {
                    _imageListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is ImageWidget img)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（防哨兵 "" 真解绑）
                        if (value == img.ListRef) return;     // 模型同值跳过（防同步回写副作用）
                        if (!_syncingFromModel) BeforeModify?.Invoke();   // 检查全过才推快照（防失配回写推空撤销快照污染 Undo 栈）
                        img.ListRef = value;
                        if (!string.IsNullOrEmpty(value)) WidgetDesignValue.Clear(img);
                    }
                }
            }
        }
        private int _imageDefaultIndex = 0;
        /// <summary>ImageWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int ImageDefaultIndex { get => _imageDefaultIndex; set { var v = Math.Max(0, value); if (_imageDefaultIndex != v) { _imageDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ImageWidget img) img.DefaultIndex = v; } } }

        private string? _frameListRef = "";
        /// <summary>FrameWidget 绑定的图片列表名（null=无）。</summary>
        public string? FrameListRef
        {
            get => _frameListRef;
            set
            {
                if (value == null) return;   // RefreshListOptions 重建瞬态，忽略防误解绑（同 ImageListRef）
                if (_frameListRef != value)
                {
                    _frameListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is FrameWidget f)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（防哨兵 "" 真解绑）
                        if (value == f.ListRef) return;       // 模型同值跳过
                        if (!_syncingFromModel) BeforeModify?.Invoke();
                        f.ListRef = value;
                        if (!string.IsNullOrEmpty(value)) WidgetDesignValue.Clear(f);
                    }
                }
            }
        }
        private int _frameDefaultIndex = 0;
        /// <summary>FrameWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int FrameDefaultIndex { get => _frameDefaultIndex; set { var v = Math.Max(0, value); if (_frameDefaultIndex != v) { _frameDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.DefaultIndex = v; } } }

        private string? _textListListRef = "";
        /// <summary>TextListWidget 绑定的文本列表名（null=无）。</summary>
        public string? TextListListRef
        {
            get => _textListListRef;
            set
            {
                if (value == null) return;   // RefreshListOptions 重建瞬态，忽略防误解绑（同 ImageListRef）
                if (_textListListRef != value)
                {
                    _textListListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is TextListWidget tl)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（防哨兵 "" 真解绑）
                        if (value == tl.ListRef) return;      // 模型同值跳过
                        if (!_syncingFromModel) BeforeModify?.Invoke();
                        tl.ListRef = value;
                    }
                }
            }
        }
        private int _textListDefaultIndex = 0;
        /// <summary>TextListWidget 缺省值（列表项索引，0=第1项；VM 侧同步钳制非负，与模型一致）。</summary>
        public int TextListDefaultIndex { get => _textListDefaultIndex; set { var v = Math.Max(0, value); if (_textListDefaultIndex != v) { _textListDefaultIndex = v; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is TextListWidget tl) tl.DefaultIndex = v; } } }
        /// <summary>NumericDisplay 设计态数值预览。</summary>
        public double NumericValue { get => _numericValue; set { if (Math.Abs(_numericValue - value) > 0.001) { _numericValue = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is NumericDisplayWidget nd) nd.Value = value; } } }

        private bool _switchIsOn = false;
        public bool SwitchIsOn { get => _switchIsOn; set { if (_switchIsOn != value) { _switchIsOn = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.IsOn = value; } } }
        private string _switchOnText = "ON";
        public string SwitchOnText { get => _switchOnText; set { if (_switchOnText != value) { _switchOnText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.OnText = value; } } }
        private string _switchOffText = "OFF";
        public string SwitchOffText { get => _switchOffText; set { if (_switchOffText != value) { _switchOffText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is SwitchWidget sw) sw.OffText = value; } } }

        private double _lineX2 = 100;
        public double LineX2 { get => _lineX2; set { if (!double.IsFinite(value)) value = _lineX2; if (_selectedWidget is LineWidget ln) value = Math.Min(Math.Max(0, value), ln.Width); value = Math.Round(value, 3, MidpointRounding.AwayFromZero); if (Math.Abs(_lineX2 - value) > 0.001) { if (!_syncingFromModel) BeforeModify?.Invoke(); _lineX2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln2) ln2.X2 = value; } } }   // C12-11：端点存储统一 3 位
        private double _lineY2 = 0;
        public double LineY2 { get => _lineY2; set { if (!double.IsFinite(value)) value = _lineY2; if (_selectedWidget is LineWidget ln) value = Math.Min(Math.Max(0, value), ln.Height); value = Math.Round(value, 3, MidpointRounding.AwayFromZero); if (Math.Abs(_lineY2 - value) > 0.001) { if (!_syncingFromModel) BeforeModify?.Invoke(); _lineY2 = value; OnPropertyChanged(); if (_selectedWidget is LineWidget ln3) ln3.Y2 = value; } } }   // C12-11：端点存储统一 3 位
        private string _lineStrokeColor = "#000000";
        public string LineStrokeColor { get => _lineStrokeColor; set { if (_lineStrokeColor != value) { _lineStrokeColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LineWidget ln) ln.StrokeColor = value; } } }

        private double _lineStrokeThickness = 1;
        public double LineStrokeThickness { get => _lineStrokeThickness; set { if (Math.Abs(_lineStrokeThickness - value) > 0.001) { _lineStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is LineWidget ln) ln.StrokeThickness = value; } } }
        /// <summary>Ellipse 边框粗细。</summary>
        public double EllipseStrokeThickness { get => _ellipseStrokeThickness; set { if (Math.Abs(_ellipseStrokeThickness - value) > 0.001) { _ellipseStrokeThickness = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is EllipseWidget w) w.StrokeThickness = value; } } }


        private string _ioFieldContent = "";
        public string IOFieldContent { get => _ioFieldContent; set { if (_ioFieldContent != value) { _ioFieldContent = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.Content = value; } } }
        private bool _ioFieldIsReadOnly = false;
        public bool IOFieldIsReadOnly { get => _ioFieldIsReadOnly; set { if (_ioFieldIsReadOnly != value) { _ioFieldIsReadOnly = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is IOFieldWidget io) io.IsReadOnly = value; } } }
        private string _ioFieldFillColor = "#EEEEEE";

        private string _checkBoxText = "CheckBox";
        public string CheckBoxText { get => _checkBoxText; set { if (_checkBoxText != value) { _checkBoxText = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget cb) cb.Text = value; } } }
        private bool _checkBoxIsChecked = false;
        public bool CheckBoxIsChecked { get => _checkBoxIsChecked; set { if (_checkBoxIsChecked != value) { _checkBoxIsChecked = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is CheckBoxWidget cb) cb.IsChecked = value; } } }

        private string _frameTitle = "Frame";   // R-6：同步 FrameWidget 新默认（老工程值由装载覆盖）
        public string FrameTitle { get => _frameTitle; set { if (_frameTitle != value) { _frameTitle = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.Title = value; } } }
        private string _frameFillColor = "#EEEEEE";
        public string FrameFillColor { get => _frameFillColor; set { if (_frameFillColor != value) { _frameFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.FillColor = value; } } }
        private string _frameImagePath = "";
        public string FrameImagePath { get => _frameImagePath; set { if (_frameImagePath != value) { _frameImagePath = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.ImagePath = value; } } }

        /// <summary>AD（2026-10-24 用户裁决）：**Frame 恒为视频控件**——属性面板已删「显示视频」勾选框。
        /// 消费点（grep 仅此一处）：<c>PropertyViewModel.Selection.cs</c> 选中控件时回写模型做一次归一化。
        /// getter 恒 true；setter 只把旧值刷正，不再接受 UI 赋值（面板已无入口）。</summary>
        public bool FrameShowVideo
        {
            get => true;
            set { if (_selectedWidget is FrameWidget f && !f.ShowVideo) { BeforeModify?.Invoke(); f.ShowVideo = true; } }
        }

        private string _frameVideoSource = "";
        /// <summary>P-6（2026-09-02）：Frame 视频源（模型字段）。Check ①（2026-09-05）删「视频源」单源输入框 UI——
        /// 新配置源只走 VideoListRef 列表；本字段保留：遗留 P-6 工程加载/老包兼容 + set_property 契约 + 361 同步链需要。</summary>
        public string FrameVideoSource { get => _frameVideoSource; set { if (_frameVideoSource != value) { _frameVideoSource = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.VideoSource = value; } } }

        private string _framePlayTag = "";
        /// <summary>R-4（2026-09-05 用户 Check）：播放控制变量（布尔变量驱动播放/暂停；空=未绑定点击直接控制）。</summary>
        public string FramePlayTag
        {
            get => _framePlayTag;
            set
            {
                if (value == null) return;   // 审查 🟡：ComboBox SelectedValue 失配回写 null 拦截（先例 HistoryTagRowVM.Tag/RobotSlotRowVM.SelectedTag——否则模型 PlayTag 被静默清空）
                if (_framePlayTag != value) { _framePlayTag = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.PlayTag = value; }
            }
        }

        // ── S-4/S-5（2026-09-05 用户拍板）：视频源列表 + 视频源选择索引变量 ──
        private string _frameVideoListRef = "";
        /// <summary>S-5：Frame 视频列表名（Video 型列表——Check 2026-09-05 收紧仅 Video，Text 型收回；空=未选列表。
        /// 注：Check ①删单源 UI——遗留工程 VideoSource 模型保留（老 P-6 工程 FW 兜底播放），面板不再引导单源。
        /// 防御同 FrameListRef 先例（审查 🟡-3：null 拦截 + suppress 失配窗口 + 同值跳过——防哨兵回写清空 f.VideoListRef）。</summary>
        public string FrameVideoListRef
        {
            get => _frameVideoListRef;
            set
            {
                if (value == null) return;
                if (_frameVideoListRef != value)
                {
                    _frameVideoListRef = value;
                    OnPropertyChanged();
                    if (_selectedWidget is FrameWidget f)
                    {
                        if (_suppressListRefWrites) return;   // 失配回写窗口拦截（RefreshListOptions 重建瞬态——防哨兵 "" 真解绑）
                        if (value == f.VideoListRef) return;   // 模型同值跳过
                        if (!_syncingFromModel) BeforeModify?.Invoke();
                        f.VideoListRef = value;
                    }
                }
            }
        }

        private string _frameVideoIndexTag = "";
        /// <summary>S-5：视频源选择变量（索引非负——UINT16/INT32，Check ③收紧去 INT16；变量值取视频源列表对应项；空=未绑定播列表首项）。</summary>
        public string FrameVideoIndexTag
        {
            get => _frameVideoIndexTag;
            set
            {
                if (value == null) return;
                if (_frameVideoIndexTag != value) { _frameVideoIndexTag = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is FrameWidget f) f.VideoIndexTag = value; }
            }
        }

        /// <summary>S-5：视频列表名选项（工程 Video 型列表——Check 2026-09-05 收紧仅 Video；选中 Frame 时刷新——首项空串=不绑列表）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> VideoListOptions { get; } = new();

        // AD（2026-10-24）：原 IsVideoMode（= FrameShowVideo 的布尔投影）已删除——属性面板收口后 XAML 不再有
        // 任何 `Visibility="{Binding IsVideoMode}"` 消费点（grep 全仓零命中），属死代码，按"常量属性必须给出
        // 消费点否则删除"的纪律移除。

        private double _progressValue = 0;
        public double ProgressValue { get => _progressValue; set { if (Math.Abs(_progressValue - value) > 0.001) { _progressValue = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Value = value; } } }
        private double _progressMin = 0;
        public double ProgressMin { get => _progressMin; set { if (Math.Abs(_progressMin - value) > 0.001) { _progressMin = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Min = value; } } }
        private double _progressMax = 100;
        public double ProgressMax { get => _progressMax; set { if (Math.Abs(_progressMax - value) > 0.001) { _progressMax = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.Max = value; } } }
        private string _progressFillColor = "#3399FF";
        public string ProgressFillColor { get => _progressFillColor; set { if (_progressFillColor != value) { _progressFillColor = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is ProgressBarWidget pb) pb.FillColor = value; } } }

        private string _dateTimeFormat = "yyyy-MM-dd HH:mm:ss";

        // ═══ W4 窗口控件属性 ═══
        private string _windowTitle = "用户";
        public string WindowTitle { get => _windowTitle; set { if (_windowTitle != value) { _windowTitle = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.Title = value; } } } }
        private bool _windowShowTitleBar = true;
        public bool WindowShowTitleBar { get => _windowShowTitleBar; set { if (_windowShowTitleBar != value) { _windowShowTitleBar = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowTitleBar = value; } } } }
        private string _windowFillColor = "#EEEEEE";
        public string WindowFillColor { get => _windowFillColor; set { if (_windowFillColor != value) { _windowFillColor = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.FillColor = value; } } } }
        private string _windowBorderColor = "#888888";
        public string WindowBorderColor { get => _windowBorderColor; set { if (_windowBorderColor != value) { _windowBorderColor = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.BorderColor = value; } } } }
        private bool _windowShowUserName = true;
        public bool WindowShowUserName { get => _windowShowUserName; set { if (_windowShowUserName != value) { _windowShowUserName = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowUserName = value; } } } }
        private bool _windowShowRole = true;
        public bool WindowShowRole { get => _windowShowRole; set { if (_windowShowRole != value) { _windowShowRole = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowRole = value; } } } }
        private bool _windowShowMode = true;
        public bool WindowShowMode { get => _windowShowMode; set { if (_windowShowMode != value) { _windowShowMode = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowMode = value; } } } }
        private bool _windowShowHistory;
        public bool WindowShowHistory { get => _windowShowHistory; set { if (_windowShowHistory != value) { _windowShowHistory = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.ShowHistory = value; } } } }
        private AlarmDisplayMode _windowDisplayMode = AlarmDisplayMode.Current;
        /// <summary>AlarmView 显示模式（P-5，2026-09-02：当前报警=0 默认 / 报警缓冲区=1；设计态固定——要两模式放两个 AlarmView）。</summary>
        public AlarmDisplayMode WindowDisplayMode { get => _windowDisplayMode; set { if (_windowDisplayMode != value) { _windowDisplayMode = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.DisplayMode = value; } } } }
        public Array WindowDisplayModeOptions => Enum.GetValues(typeof(AlarmDisplayMode));
        private double _windowCardWidth = 120;
        public double WindowCardWidth { get => _windowCardWidth; set { if (_windowCardWidth != value) { _windowCardWidth = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardWidth = value; } } } }
        private double _windowCardHeight = 110;
        public double WindowCardHeight { get => _windowCardHeight; set { if (_windowCardHeight != value) { _windowCardHeight = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardHeight = value; } } } }
        private bool _windowCardShowNumber = true;
        public bool WindowCardShowNumber { get => _windowCardShowNumber; set { if (_windowCardShowNumber != value) { _windowCardShowNumber = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardShowNumber = value; } } } }
        private bool _windowCardShowStatus = true;
        public bool WindowCardShowStatus { get => _windowCardShowStatus; set { if (_windowCardShowStatus != value) { _windowCardShowStatus = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardShowStatus = value; } } } }
        private bool _windowCardShowLocation = true;
        public bool WindowCardShowLocation { get => _windowCardShowLocation; set { if (_windowCardShowLocation != value) { _windowCardShowLocation = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.CardShowLocation = value; } } } }
        private string _windowSelectedTag = "";
        public string WindowSelectedTag { get => _windowSelectedTag; set { if (value == null) return; if (_windowSelectedTag != value) { _windowSelectedTag = value; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.SelectedTag = value; } } } }
        private string _windowBoundDevice = "";
        public string WindowBoundDevice { get => _windowBoundDevice; set { if (value == null) return; var mapped = value == "（仅内部变量）" ? "" : value; if (_windowBoundDevice != mapped) { _windowBoundDevice = mapped; OnPropertyChanged(); if (!_syncingFromModel && _selectedWidget is WindowWidget ww) { BeforeModify?.Invoke(); ww.BoundDevice = mapped; } RefreshWindowDeviceTags(); } } }

        /// <summary>W4 设备列表（RobotList BoundDevice 两态过滤用）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<string> WindowDevices { get; } = new();
        // ── Q-7（2026-09-04 用户 Check）：Window 属性组按 windowType 显隐（PropWindow 分组 Visibility 绑）──
        public bool IsAlarmView => _selectedWidget is WindowWidget w && w.Type == WindowType.AlarmView;
        public bool IsUserView => _selectedWidget is WindowWidget w && w.Type == WindowType.UserView;
        public bool IsRobotList => _selectedWidget is WindowWidget w && w.Type == WindowType.RobotList;
        private void NotifyWindowTypeFlags()
        {
            OnPropertyChanged(nameof(IsAlarmView));
            OnPropertyChanged(nameof(IsUserView));
            OnPropertyChanged(nameof(IsRobotList));
        }

        public void RefreshWindowDevices()
        {
            WindowDevices.Clear();
            WindowDevices.Add("（仅内部变量）");
            if (Project != null)
                foreach (var d in Project.Devices) WindowDevices.Add(d.Name);
        }

        /// <summary>W4 RobotList 可用变量（按 BoundDevice 两态过滤：指定设备 → 该设备变量（Tag.DeviceName）+ 内部变量；空 → 仅内部变量）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<Tag> WindowDeviceTags { get; } = new();
        public void RefreshWindowDeviceTags()
        {
            WindowDeviceTags.Clear();
            if (Project == null) return;
            foreach (var t in Project.Tags)
            {
                if (_windowBoundDevice.Length == 0 && string.IsNullOrEmpty(t.DeviceName)) WindowDeviceTags.Add(t);
                else if (_windowBoundDevice.Length > 0 && (t.DeviceName == _windowBoundDevice || string.IsNullOrEmpty(t.DeviceName))) WindowDeviceTags.Add(t);
            }
        }

        /// <summary>W4 RobotSlots 绑定表行集合（5 槽位：编号/状态/位置/详细信息/操作）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<RobotSlotRowVM> RobotSlotRows { get; } = new();
        public void RefreshRobotSlots()
        {
            RobotSlotRows.Clear();
            if (_selectedWidget is not WindowWidget ww) return;
            var labels = new[] { "编号变量", "状态变量", "位置变量", "详细信息JSON", "操作变量" };
            var getters = new Func<RobotSlotBinding, string>[] { b => b.IdTag, b => b.StatusTag, b => b.LocationTag, b => b.DetailTag, b => b.OperTag };
            for (int i = 0; i < 5; i++)
            {
                var b = i < ww.RobotSlots.Count ? ww.RobotSlots[i] : null;   // 无槽位不补模型（行 VM 空值展示）
                RobotSlotRows.Add(new RobotSlotRowVM(this, i, labels[i], b == null ? "" : getters[i](b)));
            }
        }

        /// <summary>RobotSlotRowVM 写回：槽位 i 的绑定变量 → ww.RobotSlots[i] 对应字段。
        /// Q-2（2026-09-04 Check 审查修复）：RobotSlotBinding 为独立 INPC 对象（Widget 订阅链不覆盖）+ List<> 增补无通知
        /// → 必须显式 DirtyRequested（此前仅 BeforeModify 快照，IsDirty 不置 → 关闭无提示改动静默丢失）。</summary>
        public void SetRobotSlotTag(int index, string tag)
        {
            if (_selectedWidget is not WindowWidget ww || index < 0) return;
            BeforeModify?.Invoke();   // 快照先推（补槽/赋值前——撤销可回滚）
            while (ww.RobotSlots.Count <= index) ww.RobotSlots.Add(new RobotSlotBinding());   // 首次配置才补槽位
            var b = ww.RobotSlots[index];
            switch (index)
            {
                case 0: b.IdTag = tag; break;
                case 1: b.StatusTag = tag; break;
                case 2: b.LocationTag = tag; break;
                case 3: b.DetailTag = tag; break;
                default: b.OperTag = tag; break;
            }
            DirtyRequested?.Invoke();   // Q-2：显式标脏（POCO/嵌套对象组纪律——对齐作业点行 OnWorkPointRowChanged）
        }
        public string DateTimeFormat { get => _dateTimeFormat; set { if (_dateTimeFormat != value) { _dateTimeFormat = value; OnPropertyChanged(); if (!_syncingFromModel) BeforeModify?.Invoke(); if (_selectedWidget is DateTimeWidget dt) { dt.Format = value; } } } }   // 任务 7：改格式不写 Text——DisplayText 绑定分支按 Format 格式化基准值、未绑定分支 FormatNow 实时（两分支自动联动）

    }
}
