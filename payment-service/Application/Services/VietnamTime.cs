namespace Application.Services
{
    /// <summary>
    /// Bảng giá theo khung giờ Việt Nam (ngày 06:00–18:00, tối 18:00–24:00, đêm 00:00–06:00), nhưng Core lưu và gửi
    /// giờ vào/ra dạng UTC. Phải đổi sang giờ Việt Nam trước khi tính phí, nếu không xe bị xếp nhầm khung (lệch 7 tiếng).
    /// </summary>
    public static class VietnamTime
    {
        // Việt Nam là UTC+7 và không có giờ mùa hè, nên dùng độ lệch cố định (không phụ thuộc tzdata trong container).
        public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

        /// <summary>
        /// Đổi một thời điểm UTC sang giờ treo tường Việt Nam. Giá trị Kind=Unspecified được coi là UTC
        /// (Core luôn gửi UTC); Kind=Local được đổi về UTC trước.
        /// </summary>
        public static DateTime FromUtc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return DateTime.SpecifyKind(utc.Add(Offset), DateTimeKind.Unspecified);
        }
    }
}
