namespace InstaShare.Services
{
    internal static class Format
    {
        public static string Bytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
        }

        /// <summary>
        /// Short, two-unit duration: "45 s", "3 min 20 s", "1 h 5 min", "2 days 4 h".
        /// </summary>
        public static string Duration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;
            if (span.TotalMinutes < 1)
                return $"{Math.Max(1, (int)Math.Ceiling(span.TotalSeconds))} s";
            if (span.TotalHours < 1)
                return span.Seconds == 0 ? $"{span.Minutes} min" : $"{span.Minutes} min {span.Seconds} s";
            if (span.TotalDays < 1)
                return span.Minutes == 0 ? $"{span.Hours} h" : $"{span.Hours} h {span.Minutes} min";
            var days = span.Days == 1 ? "1 day" : $"{span.Days} days";
            return span.Hours == 0 ? days : $"{days} {span.Hours} h";
        }

        public static string ExpiresIn(DateTime expiresUtc, DateTime nowUtc)
        {
            var left = expiresUtc - nowUtc;
            return left <= TimeSpan.Zero ? "Expired" : $"Expires in {Duration(left)}";
        }

        public static string Expiry(TimeSpan expiry) => expiry.TotalHours switch
        {
            1 => "1 hour",
            < 24 => $"{expiry.TotalHours:0} hours",
            24 => "1 day",
            _ => $"{expiry.TotalDays:0} days",
        };
    }
}
