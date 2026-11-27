using Application.Services;

namespace Application.Tests;

/// <summary>
/// Core gửi giờ vào/ra dạng UTC; bảng giá theo giờ Việt Nam (UTC+7). Các test dùng giờ Việt Nam làm chuẩn rồi đổi sang UTC
/// như Core thật sự gửi, để bắt lỗi xếp nhầm khung giờ.
/// </summary>
public class CounterFeeCalculatorTests
{
    private static DateTime Utc(DateTime vietnam) => DateTime.SpecifyKind(vietnam.AddHours(-7), DateTimeKind.Utc);
    private static decimal Fee(DateTime vnIn, DateTime vnOut) =>
        CounterFeeCalculator.Calculate(new BillingEngine(), "51F12345", Utc(vnIn), Utc(vnOut));
    private static DateTime Vn(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0);

    [Theory]
    [InlineData(27, 10, 0, 27, 11, 0, 15000)]    // ban ngay, 1 luot 2 gio (truoc day bi tinh qua dem 100.000)
    [InlineData(27, 9, 0, 27, 12, 0, 30000)]     // 3 gio ban ngay = 2 luot
    [InlineData(27, 20, 0, 27, 22, 0, 20000)]    // buoi toi (truoc day bi tinh nhu ban ngay 15.000)
    [InlineData(27, 14, 0, 27, 14, 10, 0)]       // do duoi 15 phut: mien phi
    [InlineData(27, 6, 30, 27, 7, 30, 15000)]    // 06:30 VN = 23:30 UTC hom truoc: khong duoc tinh qua dem
    [InlineData(27, 17, 30, 27, 18, 30, 35000)]  // 30 phut ban ngay + 30 phut buoi toi
    [InlineData(27, 23, 0, 28, 1, 0, 120000)]    // 1 gio buoi toi (20.000) + qua dem (100.000)
    public void Fee_UsesVietnamTimeFrames(int d1, int h1, int m1, int d2, int h2, int m2, decimal expected)
    {
        Assert.Equal(expected, Fee(Vn(d1, h1, m1), Vn(d2, h2, m2)));
    }

    [Fact]
    public void FromUtc_AddsSevenHours_AndTreatsUnspecifiedAsUtc()
    {
        var utc = new DateTime(2026, 11, 27, 3, 0, 0, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 11, 27, 3, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateTime(2026, 11, 27, 10, 0, 0), VietnamTime.FromUtc(utc));
        Assert.Equal(new DateTime(2026, 11, 27, 10, 0, 0), VietnamTime.FromUtc(unspecified));
    }

    [Fact]
    public void FromUtc_RollsOverTheDayAtUtcEvening()
    {
        var lateUtc = new DateTime(2026, 11, 27, 18, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 11, 28, 1, 30, 0), VietnamTime.FromUtc(lateUtc));
    }

    [Fact]
    public void FromUtc_ConvertsLocalKindThroughUtc()
    {
        var local = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime().AddHours(7), VietnamTime.FromUtc(local));
    }

    [Fact]
    public void Calculate_RejectsCheckOutBeforeCheckIn()
    {
        Assert.Throws<ArgumentException>(() => Fee(Vn(27, 11), Vn(27, 10)));
    }
}
