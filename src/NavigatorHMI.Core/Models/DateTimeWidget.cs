using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>C13 日期时间控件：显示/编辑日期时间（可绑定 DATETIME 变量）。</summary>
    [ProtoContract]
    public class DateTimeWidget : Widget
    {
        private string _text = "2026-01-01 00:00:00";
        /// <summary>显示文本（D2 起不再参与未绑定显示——属性面板已删编辑入口，未绑定恒 FormatNow 实时；字段保留仅序列化/旧工程兼容）。</summary>
        [ProtoMember(1)]
        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); } }   // setter 通知 DisplayText 保留（旧序列化加载触发刷新，无害）
        }

        private string _format = "yyyy-MM-dd HH:mm:ss";
        /// <summary>显示格式。</summary>
        [ProtoMember(2)]
        public string Format
        {
            get => _format;
            set { if (_format != value) { _format = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); } }   // 改格式立即刷新显示
        }

        /// <summary>按 Format 格式化（非法格式回退默认，防 IsEditable 输入异常值抛 FormatException）。</summary>
        private string FormatNow()
        {
            try { return DateTime.Now.ToString(Format); }
            catch (FormatException) { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }
        }

        /// <summary>安全格式（非法回退默认）。</summary>
        private string SafeFormat()
        {
            try { _ = DateTime.Now.ToString(Format); return Format; }
            catch (FormatException) { return "yyyy-MM-dd HH:mm:ss"; }
        }

        /// <summary>D6/P2-2/任务8/D2 设计态显示文本：绑定变量 → 基准值按 Format 格式化（可解析则格式化；全 0 字面按 Format 字面替换——0000 年无效 TryParse 失败）；未绑定 → 1Hz 实时当前时间（D2：不再保留可手改的 Text 残留，恒实时）。</summary>
        [ProtoIgnore]
        public string DisplayText
        {
            get
            {
                if (!string.IsNullOrEmpty(BoundTag))
                {
                    var bv = TagResolver.ResolveBaseValue(BoundTag);
                    if (DateTime.TryParse(bv, out var dt)) return dt.ToString(SafeFormat());   // P2-2：改格式立即重格式化基准值
                    if (IsZeroDate(bv)) return ZeroByFormat(SafeFormat());   // 任务8：全 0 基准值（0000 年无效）按 Format 字面替换生成（yyyy→0000/MM→00/dd→00…，分隔符跟控件格式）
                    if (!string.IsNullOrEmpty(bv)) return bv;   // 解析失败原样返回
                }
                return FormatNow();   // 未绑定：1Hz 实时当前时间（D2 删 Text 编辑能力后恒实时，无手改残留）
            }
        }

        /// <summary>任务8：判断基准值是否为"全 0 日期"字面（仅 0/数字/冒号/空格/横线/斜杠，且无任何非 0 数字——与命令层 IsZeroDateLiteral 同规则）。</summary>
        private static bool IsZeroDate(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!s!.All(c => char.IsDigit(c) || c is ':' or ' ' or '-' or '/')) return false;   // 字符白名单：数字/冒号/空格/横线/斜杠
            var digits = s.Where(char.IsDigit).ToList();
            if (digits.Count == 0) return false;
            return digits.All(d => d == '0');
        }

        /// <summary>任务8：按 Format 字面替换生成全 0 显示（yyyy→0000/MM→00/dd→00/HH→00/mm→00/ss→00，分隔符与字面字符保留）。</summary>
        private static string ZeroByFormat(string format)
        {
            return format
                .Replace("yyyy", "0000")
                .Replace("MM", "00")
                .Replace("dd", "00")
                .Replace("HH", "00")
                .Replace("mm", "00")
                .Replace("ss", "00");
        }

        /// <summary>画布 1Hz 时钟回调：通知 DisplayText 变化（未绑定控件实时刷新）。</summary>
        public void RefreshDisplay() => OnPropertyChanged(nameof(DisplayText));
    }
}
