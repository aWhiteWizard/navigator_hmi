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
    public partial class PropertyViewModel : INotifyPropertyChanged
    {

        #region 原 绑定变量（72daf0e^ 第 1377-1582 行，×16 段）

        /// <summary>「无绑定」哨兵项（非 null，Name=""）。用哨兵替代 null 作下拉首项：
        /// WPF ComboBox 的 ItemsSource 含 null 项时，点击 null 项可能不触发 SelectedItem 回写
        /// （null 与"未选中"状态无法区分）→ 解绑无效果。哨兵是真实对象，点击必然触发绑定。</summary>
        private static readonly Tag NoBindingSentinel = new() { Name = "" };

        /// <summary>绑定下拉数据源：第一项哨兵 = 无绑定，其后为按控件类型过滤的工程变量。</summary>
        public System.Collections.ObjectModel.ObservableCollection<Tag> BindableTags { get; } = new();

        // S-1（2026-09-05 用户 Check）：Frame 播放控制专用下拉——**仅布尔变量**（播放/暂停语义只对布尔成立；
        // BindableTags 按 Frame Numeric 兼容含 INT/FLOAT，非布尔不可选）——哨兵 + 工程 BOOL 变量子集
        public System.Collections.ObjectModel.ObservableCollection<Tag> FramePlayTagOptions { get; } = new();

        /// <summary>刷新 FramePlayTagOptions（哨兵 + BOOL 兼容变量子集；选中 Frame 时随 RefreshBindableTags 联动）。</summary>
        private void RefreshFramePlayTagOptions()
        {
            FramePlayTagOptions.Clear();
            FramePlayTagOptions.Add(NoBindingSentinel);
            if (Project == null) return;
            foreach (var t in Project.Tags)
                if (TagCompatibility.IsBoolCompatible(t.DataType))
                    FramePlayTagOptions.Add(t);
            var cur = _framePlayTag;
            if (cur.Length > 0 && !FramePlayTagOptions.Any(x => x.Name == cur))
            {
                var t = Project.Tags.FirstOrDefault(x => x.Name == cur);
                if (t != null) FramePlayTagOptions.Add(t);
                else FramePlayTagOptions.Add(new Tag { Name = $"(缺失变量: {cur})" });
            }
        }

        // S-5：视频源选择索引变量专用选项——**UINT16/INT32**（2026-09-05 Check 修正：原 INT16/UINT16/INT32——去 INT16 防负值；索引非负语义；排除 BOOL/FLOAT/STRING）
        public System.Collections.ObjectModel.ObservableCollection<Tag> FrameVideoIndexTagOptions { get; } = new();

        private void RefreshFrameVideoIndexTagOptions()
        {
            FrameVideoIndexTagOptions.Clear();
            FrameVideoIndexTagOptions.Add(NoBindingSentinel);
            if (Project == null) return;
            foreach (var t in Project.Tags)
                if (t.DataType is TagDataType.UINT16 or TagDataType.INT32)   // Check ③（2026-09-05）：UINT16/INT32（INT16 可负已排除）
                    FrameVideoIndexTagOptions.Add(t);
            var cur = _frameVideoIndexTag;
            if (cur.Length > 0 && !FrameVideoIndexTagOptions.Any(x => x.Name == cur))
            {
                var t = Project.Tags.FirstOrDefault(x => x.Name == cur);
                if (t != null) FrameVideoIndexTagOptions.Add(t);
                else FrameVideoIndexTagOptions.Add(new Tag { Name = $"(缺失变量: {cur})" });
            }
        }

        /// <summary>重建绑定下拉（哨兵 + 按控件类型过滤的工程变量），选中控件时调用。</summary>
        public void RefreshBindableTags()
        {
            BindableTags.Clear();
            BindableTags.Add(NoBindingSentinel);   // 无绑定哨兵（非 null）
            RefreshGpsTagNames();   // W-2b：先于 Project==null 早退刷新（关闭工程时清空 GPS 选项，防残留旧工程变量）
            if (Project == null) return;            // 类型过滤：数值/索引控件只显示数字变量，文本控件只显示 STRING，None（Label）不显示任何变量，其他不限
            var req = _selectedWidget != null ? TagCompatibility.GetRequirement(_selectedWidget) : TagRequirement.Any;
            var current = _selectedWidget?.BoundTag ?? "";
            bool currentIncluded = false;
            foreach (var t in Project.Tags)
            {
                bool ok = req switch
                {
                    TagRequirement.Numeric => TagCompatibility.IsNumericCompatible(t.DataType),
                    TagRequirement.StringPath => TagCompatibility.IsStringPathCompatible(t.DataType),
                    TagRequirement.DateTime => TagCompatibility.IsDateTimeCompatible(t.DataType),
                    TagRequirement.Bool => TagCompatibility.IsBoolCompatible(t.DataType),
                    TagRequirement.Geo => TagCompatibility.IsGeoCompatible(t.DataType),
                    TagRequirement.None => false,   // 禁止绑定：无变量可选
                    // Any（普通控件）：默认排除 GPS——E 循环（2026-08-22 用户）放宽 GPS→IOField 坐标输入
                    // （与 TagCompatibility.Check 一致）；IOField 下拉保留 GPS, 其余控件排除
                    _ => t.DataType != TagDataType.GPS || _selectedWidget is IOFieldWidget,
                };
                if (ok)
                {
                    BindableTags.Add(t);
                    if (t.Name == current) currentIncluded = true;
                }
            }
            // 当前绑定变量被类型过滤掉（历史/CLI 不兼容绑定）：保留该项
            // → SelectedItem 不失配 → 不回写 null → 不静默解绑（wpf-combobox-style §3 场景 B 防护）
            if (!currentIncluded && current.Length > 0)
            {
                var cur = Project.Tags.FirstOrDefault(t => t.Name == current);
                if (cur != null) BindableTags.Add(cur);
                else
                    BindableTags.Add(new Tag { Name = $"(缺失变量: {current})" });   // 变量不存在（历史/外部工程）：占位可见，防下拉空白静默
            }
            RefreshFramePlayTagOptions();   // S-1：播放控制布尔子集联动刷新
            RefreshFrameVideoIndexTagOptions();   // S-5：视频源选择整型子集联动刷新
        }

        /// <summary>解析绑定目标：变量存在→Tag；变量缺失（历史/外部工程）→缺失占位项；无绑定→哨兵。
        /// 防 SelectedItem 失配导致下拉空白/误显「无绑定」（wpf-combobox-style §3 场景 C/D）。</summary>
        private Tag? ResolveBoundTarget(string? boundTag)
        {
            if (string.IsNullOrEmpty(boundTag)) return NoBindingSentinel;
            var t = Project?.Tags.FirstOrDefault(x => x.Name == boundTag);
            if (t != null) return t;
            return BindableTags.FirstOrDefault(x => x.Name == $"(缺失变量: {boundTag})") ?? NoBindingSentinel;
        }

        private Tag? _boundTag;

        /// <summary>选中控件切换时的同步/失配回写抑制标志（ComboBox ItemsSource 更新导致的 SelectedItem 回写可能延迟到 _syncingFromModel 块外）。</summary>
        private bool _suppressBindTagCommands;

        /// <summary>列表下拉失配回写抑制窗口（ListRef 三 setter 共用）：RefreshListOptions 增量同步移除占位项时
        /// ComboBox SelectedItem 失配回写 ""（哨兵）会真解绑——窗口期拦截；与 BoundTag 同款模式。</summary>
        private bool _suppressListRefWrites;

        private void BeginSuppressListRefWrites()
        {
            _suppressListRefWrites = true;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => _suppressListRefWrites = false));
        }

        /// <summary>开启回写抑制窗口：置位后在 Dispatcher 后台优先级延迟清除（覆盖 ComboBox 失配回写的布局批次）。</summary>
        private void BeginSuppressBindTagCommands()
        {
            _suppressBindTagCommands = true;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => _suppressBindTagCommands = false));
        }

        /// <summary>是否已绑定变量（哨兵 Name 为空视为未绑定——BoundTag 用非 null 哨兵归一，禁引用判空）。</summary>
        public bool IsBound => BoundTag != null && !string.IsNullOrEmpty(BoundTag.Name);

        /// <summary>设计态值字段（TextWidget.Content）是否可编辑/显示：绑定变量后由变量控制，隐藏。</summary>
        public bool IsValueEditable => !IsBound;

        /// <summary>选中控件绑定的变量（NoBindingSentinel 哨兵 = 未绑定）；变更走 CommandService.bind_tag（空 tag_name = 解绑）。</summary>
        public Tag? BoundTag
        {
            get => _boundTag;
            set
            {
                if (_boundTag == value) return;
                _boundTag = value ?? NoBindingSentinel;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsValueEditable));   // 绑定状态 → TextWidget.Content 可编辑性
                if (!_syncingFromModel && _selectedWidget != null && CurrentScreen != null && CommandService != null)
                {
                    // 哨兵/空名 → 空 tag_name = 解绑
                    var tagName = (value == null || string.IsNullOrEmpty(value.Name)) ? "" : value.Name;
                    // 防同步回写走命令：选中控件时属性面板 ComboBox 初始化/时序可能回传模型同值，
                    // 若走 bind_tag 命令会触发命令副作用（WidgetDesignValue.Clear 清设计态值/重绑开销），
                    // 且 null/哨兵回写可能真解绑——目标名与模型当前 BoundTag 一致时直接跳过
                    // （用户主动解绑/改绑的目标名 ≠ 模型值，不受影响）
                    if (tagName == (_selectedWidget.BoundTag ?? "")) return;
                    // 选中切换的失配回写窗口：ComboBox 回写 null/哨兵（≠模型值）也被抑制——
                    // 窗口极短（Dispatcher 后台延迟清除），用户无法在此窗口内完成真实下拉选择
                    if (_suppressBindTagCommands) return;
                    System.Diagnostics.Trace.WriteLine($"[PropertyVM] bind_tag 命令: tag='{tagName}' model='{_selectedWidget.BoundTag}' widget='{_selectedWidget.ObjectName}'");
                    BeforeModify?.Invoke();
                    var result = CommandService.Execute("bind_tag", new Dictionary<string, object?>
                    {
                        ["screen_name"] = CurrentScreen.Name,
                        ["widget_name"] = _selectedWidget.ObjectName,
                        ["tag_name"] = tagName,
                    });
                    // 失败（如变量已被删/GPS 非法绑定）：回滚 UI 选中态并提示，防静默不一致
                    if (!result.Success)
                    {
                        _boundTag = ResolveBoundTarget(_selectedWidget.BoundTag);
                        // 回弹通知经 Dispatcher 延后一次：ComboBox 失配回写/绑定管道与 MessageBox 模态循环错开，
                        // 确保下拉即时回到旧选中（Check 1-2：同步通知实测不刷新，关面板重开才恢复）
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null)
                        {
                            dispatcher.BeginInvoke(new Action(() =>
                            {
                                OnPropertyChanged(nameof(BoundTag));
                                OnPropertyChanged(nameof(IsValueEditable));
                            }));
                        }
                        else
                        {
                            OnPropertyChanged(nameof(BoundTag));
                            OnPropertyChanged(nameof(IsValueEditable));
                        }
                        OnModifyFailed?.Invoke();   // 弹出已推但未生效的空撤销快照
                        System.Windows.MessageBox.Show(result.ErrorMessage ?? "绑定变量失败", "绑定",
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
        }
        #endregion

    }
}