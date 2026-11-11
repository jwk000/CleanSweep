using System;
using System.Globalization;

namespace CleanSweep.Common
{
    public static class Format
    {
        private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

        public static string Bytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < ByteUnits.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            if (unit == 0)
            {
                return bytes.ToString("N0", CultureInfo.CurrentCulture) + " B";
            }

            string pattern = value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
            return value.ToString(pattern, CultureInfo.CurrentCulture) + " " + ByteUnits[unit];
        }

        public static string Age(DateTime? time)
        {
            if (!time.HasValue)
            {
                return "时间未知";
            }

            TimeSpan age = DateTime.Now - time.Value;
            if (age.TotalMinutes < 1)
            {
                return "刚刚";
            }

            if (age.TotalHours < 1)
            {
                return Math.Max(1, (int)age.TotalMinutes) + " 分钟前";
            }

            if (age.TotalDays < 1)
            {
                return (int)age.TotalHours + " 小时前";
            }

            if (age.TotalDays < 30)
            {
                return (int)age.TotalDays + " 天前";
            }

            if (age.TotalDays < 365)
            {
                return (int)(age.TotalDays / 30) + " 个月前";
            }

            return time.Value.ToString("yyyy-MM-dd");
        }

        public static string Count(int count, string unit)
        {
            return count.ToString("N0", CultureInfo.CurrentCulture) + " " + unit;
        }

        public static string Duration(TimeSpan span)
        {
            if (span.TotalSeconds < 60)
            {
                return Math.Max(1, (int)span.TotalSeconds) + " 秒";
            }

            if (span.TotalMinutes < 60)
            {
                return (int)span.TotalMinutes + " 分 " + span.Seconds + " 秒";
            }

            return (int)span.TotalHours + " 小时 " + span.Minutes + " 分";
        }

        public static string Number(long value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }
    }
}
