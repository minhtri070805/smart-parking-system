namespace SmartParking.Application.DTOs.Sessions;

public class MonthlyTrafficStatsDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? LotId { get; set; }
    public int TotalCheckIns { get; set; }
    public int TotalCheckOuts { get; set; }
    public decimal TotalRevenue { get; set; }
    public IReadOnlyList<DailyTrafficDto> Days { get; set; } = [];
    // 24 phần tử, lượt vào theo giờ trong ngày (giờ Việt Nam).
    public IReadOnlyList<int> CheckInsByHour { get; set; } = [];
    public IReadOnlyList<MonthlyRevenueTransactionDto> RecentTransactions { get; set; } = [];
}

public class DailyTrafficDto
{
    public string Date { get; set; } = string.Empty;
    public int CheckIns { get; set; }
    public int CheckOuts { get; set; }
    public decimal Revenue { get; set; }
}

public class MonthlyRevenueTransactionDto
{
    public long SessionId { get; set; }
    public int LotId { get; set; }
    public string LotName { get; set; } = string.Empty;
    public string PlateNumber { get; set; } = string.Empty;
    public DateTime CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal TotalFee { get; set; }
    public string? SlotCode { get; set; }
    public string Status { get; set; } = string.Empty;
}
