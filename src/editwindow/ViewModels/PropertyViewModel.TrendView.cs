using System.Collections.ObjectModel;
using System.Linq;
using NavigatorHMI.Common;

namespace NavigatorHMI.ViewModels
{
    // ═══════════════════════════════════════════
    // P-4 趋势图控件属性（TrendViewWidget，2026-09-02）
    // partial 拆分（P-2d 铺垫）：TrendView 专属属性/装载/回写独立文件，不再堆入主文件
    // ═══════════════════════════════════════════
    public partial class PropertyViewModel
    {
        /// <summary>趋势模式选项（ComboBox 数据源：时间-数据/变量A-B）。</summary>
        public Array TrendModeOptions => Enum.GetValues(typeof(TrendMode));

        /// <summary>趋势图变量 A 选项（数值型变量；BindableTags 由 RefreshBindableTags 维护——含无绑定哨兵）。</summary>
        public ObservableCollection<Tag> TrendTagAOptions => BindableTags;

        /// <summary>趋势图变量 B 选项（变量A-B 模式 Y 轴；仅数值型）。</summary>
        public ObservableCollection<Tag> TrendTagBOptions => BindableTags;

        private TrendMode _trendMode = TrendMode.TimeSeries;

        /// <summary>趋势模式（时间-数据=0 / 变量A-B=1，设计态定死——用户 2026-09-02 拍板）。</summary>
        public TrendMode TrendMode
        {
            get => _trendMode;
            set
            {
                if (_trendMode != value)
                {
                    _trendMode = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.TrendMode = value; }
                    OnPropertyChanged(nameof(IsXyMode));
                    OnPropertyChanged(nameof(IsTimeSeriesMode));
                }
            }
        }

        /// <summary>是否变量A-B 模式（属性面板据此显示 TrendTagB 行）。</summary>
        public bool IsXyMode => _trendMode == TrendMode.XY;

        /// <summary>是否时间-数据模式（属性面板据此显示 TrendTagB 隐藏）。</summary>
        public bool IsTimeSeriesMode => _trendMode == TrendMode.TimeSeries;

        private string _trendTagA = "";

        /// <summary>趋势变量 A（时间-数据主变量 / 变量A-B X 轴）。</summary>
        public string TrendTagA
        {
            get => _trendTagA;
            set
            {
                if (value == null) return;
                var mapped = value == NoBindingSentinel.Name ? "" : value;
                if (_trendTagA != mapped)
                {
                    _trendTagA = mapped;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.TrendTagA = mapped; }
                }
            }
        }

        private string _trendTagB = "";

        /// <summary>趋势变量 B（仅变量A-B 模式 Y 轴）。</summary>
        public string TrendTagB
        {
            get => _trendTagB;
            set
            {
                if (value == null) return;
                var mapped = value == NoBindingSentinel.Name ? "" : value;
                if (_trendTagB != mapped)
                {
                    _trendTagB = mapped;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.TrendTagB = mapped; }
                }
            }
        }

        private int _sampleIntervalMs = 1000;

        /// <summary>采样间隔（毫秒，默认 1000；钳制 100~60000）。</summary>
        public int SampleIntervalMs
        {
            get => _sampleIntervalMs;
            set
            {
                var v = Math.Clamp(value, 100, 60000);
                if (_sampleIntervalMs != v)
                {
                    _sampleIntervalMs = v;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.SampleIntervalMs = v; }
                }
            }
        }

        private int _timeWindowSeconds = 60;

        /// <summary>显示时间窗（秒，默认 60；钳制 5~3600）。</summary>
        public int TimeWindowSeconds
        {
            get => _timeWindowSeconds;
            set
            {
                var v = Math.Clamp(value, 5, 3600);
                if (_timeWindowSeconds != v)
                {
                    _timeWindowSeconds = v;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.TimeWindowSeconds = v; }
                }
            }
        }

        private string _lineColor = "#1E90FF";

        /// <summary>曲线颜色（CSS 格式）。</summary>
        public string LineColor
        {
            get => _lineColor;
            set
            {
                if (_lineColor != value)
                {
                    _lineColor = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.LineColor = value; }
                }
            }
        }

        private double _lineWidth = 1.5;

        /// <summary>曲线粗细（像素）。</summary>
        public double LineWidth
        {
            get => _lineWidth;
            set
            {
                if (Math.Abs(_lineWidth - value) > 0.001)
                {
                    _lineWidth = value;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.LineWidth = value; }
                }
            }
        }

        private int _refreshRateMs = 500;

        /// <summary>刷新率（毫秒，默认 500；钳制 50~10000）。</summary>
        public int RefreshRateMs
        {
            get => _refreshRateMs;
            set
            {
                var v = Math.Clamp(value, 50, 10000);
                if (_refreshRateMs != v)
                {
                    _refreshRateMs = v;
                    OnPropertyChanged();
                    if (!_syncingFromModel) { BeforeModify?.Invoke(); if (_selectedWidget is TrendViewWidget tc) tc.RefreshRateMs = v; }
                }
            }
        }

        /// <summary>P-4：TrendViewWidget 装载（SelectedWidget setter 内调用；_syncingFromModel 保护防回写）。</summary>
        private void LoadTrendViewProperties(TrendViewWidget tc)
        {
            TrendMode = tc.TrendMode;
            TrendTagA = tc.TrendTagA;
            TrendTagB = tc.TrendTagB;
            SampleIntervalMs = tc.SampleIntervalMs;
            TimeWindowSeconds = tc.TimeWindowSeconds;
            LineColor = tc.LineColor;
            LineWidth = tc.LineWidth;
            RefreshRateMs = tc.RefreshRateMs;
        }
    }
}
