using System.Globalization;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace NavigatorHMI.Common
{
    /// <summary>
    /// 经纬度点（WGS84 小数度内部存储，DMS 度分秒显示）。
    /// 字符串格式：方向前缀（W/S/E/N）放最前，经度在前纬度在后，逗号分隔——
    ///   DMS：  "E104°3'30\", N30°40'20\""（西经 W/东经 E/南纬 S/北纬 N）
    ///   小数： "104.0583, 30.6722"（无前缀自动按 经度,纬度 顺序；也可 "E104.0583, N30.6722"）
    ///   基准值："(E0°0'0\", N0°0'0\")"（括号包裹，Tag.BaseValue 存储格式）
    /// 输入小数度自动转 DMS 显示；解析支持两种输入并校验范围（经度 ±180、纬度 ±90）。
    /// </summary>
    [ProtoContract]
    public class GeoPoint
    {
        /// <summary>经度（小数度，东正西负，[-180, 180]）</summary>
        [ProtoMember(1)]
        public double Longitude { get; set; }

        /// <summary>纬度（小数度，北正南负，[-90, 90]）</summary>
        [ProtoMember(2)]
        public double Latitude { get; set; }

        public GeoPoint() { }

        public GeoPoint(double longitude, double latitude)
        {
            Longitude = longitude;
            Latitude = latitude;
        }

        /// <summary>DMS 字符串（如 "E104°3'30\", N30°40'20\""；经度在前纬度在后，方向前缀放最前）。</summary>
        public string ToDmsString()
            => $"{FormatCoord(Longitude, 'E', 'W')}, {FormatCoord(Latitude, 'N', 'S')}";

        /// <summary>基准值格式（括号包裹，Tag.BaseValue 存储）："(E0°0'0\", N0°0'0\")"。</summary>
        public string ToBaseValue() => $"({ToDmsString()})";

        /// <summary>解析经纬度字符串（DMS 或小数度，可带括号）。成功返回 true 且 point 非 null。</summary>
        public static bool TryParse(string? text, out GeoPoint? point)
        {
            point = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (s.StartsWith('(') && s.EndsWith(')')) s = s[1..^1].Trim();
            // 支持英文/中文逗号分隔（经度,纬度）
            var parts = s.Split(',', '，');
            if (parts.Length != 2) return false;
            if (!TryParseCoord(parts[0].Trim(), true, out var lng)) return false;
            if (!TryParseCoord(parts[1].Trim(), false, out var lat)) return false;
            point = new GeoPoint(lng, lat);
            return true;
        }

        /// <summary>格式化单个坐标：方向前缀 + 度分秒（秒四舍五入到整数 AwayFromZero，处理 60 进位）。
        /// V-5a：public 供 GUI 输入实时换算提示（isLongitude=true → E/W 前缀，false → N/S）。</summary>
        public static string FormatDms(double value, bool isLongitude)
            => FormatCoord(value, isLongitude ? 'E' : 'N', isLongitude ? 'W' : 'S');

        /// <summary>格式化单个坐标：方向前缀 + 度分秒（秒四舍五入到整数 AwayFromZero，处理 60 进位）。</summary>
        private static string FormatCoord(double value, char positivePrefix, char negativePrefix)
        {
            char prefix = value >= 0 ? positivePrefix : negativePrefix;
            double abs = Math.Abs(value);
            int deg = (int)Math.Floor(abs);
            double minFloat = (abs - deg) * 60;
            int min = (int)Math.Floor(minFloat);
            double sec = Math.Round((minFloat - min) * 60, 0, MidpointRounding.AwayFromZero);
            if (sec >= 60) { sec = 0; min++; }
            if (min >= 60) { min = 0; deg++; }
            return $"{prefix}{deg}°{min}'{sec:0}\"";
        }

        /// <summary>解析单个坐标（DMS 或小数度），校验范围（经度 ±180 / 纬度 ±90）与前缀-位置匹配（经度仅 E/W、纬度仅 N/S）。</summary>
        private static bool TryParseCoord(string s, bool isLongitude, out double value)
        {
            value = 0;
            if (s.Length == 0) return false;
            double sign = 1;
            int idx = 0;
            switch (s[0])
            {
                case 'W' or 'w' when isLongitude: sign = -1; idx = 1; break;
                case 'E' or 'e' when isLongitude: idx = 1; break;
                case 'S' or 's' when !isLongitude: sign = -1; idx = 1; break;
                case 'N' or 'n' when !isLongitude: idx = 1; break;
                // 前缀与位置错配（经度位 N/S、纬度位 E/W）：不消费前缀，后续 numPart 含字母解析失败 → 拒绝
            }
            var numPart = s[idx..].Trim();
            if (numPart.Length == 0) return false;

            double result;
            if (numPart.Contains('°') || numPart.Contains('度'))
            {
                if (!TryParseDms(numPart, out result)) return false;
            }
            else if (!double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                return false;
            }

            value = sign * result;
            return isLongitude ? value is >= -180 and <= 180 : value is >= -90 and <= 90;
        }

        /// <summary>解析度分秒：支持 度°分'秒"、度°分'、度°（符号也接受 度/分/秒/′/″ 中文变体）。</summary>
        private static bool TryParseDms(string s, out double value)
        {
            value = 0;
            // 完整：度 分 秒
            var m = Regex.Match(s, @"^\s*(\d+(?:\.\d+)?)\s*[°度]\s*(\d+(?:\.\d+)?)\s*['′分]\s*(\d+(?:\.\d+)?)\s*[""″秒]\s*$");
            double deg, min = 0, sec = 0;
            if (m.Success)
            {
                deg = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                sec = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                // 度分（无秒）
                m = Regex.Match(s, @"^\s*(\d+(?:\.\d+)?)\s*[°度]\s*(\d+(?:\.\d+)?)\s*['′分]\s*$");
                if (m.Success)
                {
                    deg = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    // 仅度
                    m = Regex.Match(s, @"^\s*(\d+(?:\.\d+)?)\s*[°度]\s*$");
                    if (!m.Success) return false;
                    deg = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }
            if (min >= 60 || sec >= 60) return false;   // 分/秒合法范围
            value = deg + min / 60 + sec / 3600;
            return true;
        }
    }
}
