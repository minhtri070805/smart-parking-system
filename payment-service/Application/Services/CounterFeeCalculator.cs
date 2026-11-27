using Application.Interfaces;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services
{
    /// <summary>Tính phí thu tại quầy từ giờ vào/ra UTC của Core, theo khung giờ Việt Nam.</summary>
    public static class CounterFeeCalculator
    {
        public static decimal Calculate(IBillingEngine engine, string licensePlate, DateTime checkInUtc, DateTime checkOutUtc)
        {
            var billing = new ParkingSessionBilling
            {
                Id = Guid.NewGuid(),
                LicensePlate = licensePlate,
                VehicleType = VehicleType.Seat4,
                CheckInTime = VietnamTime.FromUtc(checkInUtc),
                CheckOutTime = VietnamTime.FromUtc(checkOutUtc),
                IsMonthlyPass = false,
                AppliedPricingPolicy = null!
            };
            return engine.CalculateFee(billing);
        }
    }
}
