using System.ComponentModel.DataAnnotations;

namespace SmartParking.Application.DTOs.Operations;

public sealed class ParkingIntakeRequestDto : IValidatableObject
{
    [Required]
    [RegularExpression("^(WALK_IN|BOOKING)$", ErrorMessage = "Nguồn tiếp nhận phải là WALK_IN hoặc BOOKING.")]
    public string Source { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "ID bãi đỗ xe không hợp lệ.")]
    public int? LotId { get; set; }

    public Guid? EventId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "ID khu vực không hợp lệ.")]
    public int? ZoneId { get; set; }

    [MaxLength(40)]
    public string? TicketCode { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "ID hình ảnh không hợp lệ.")]
    public long? ImageId { get; set; }

    [MaxLength(500)]
    public string? ManualReason { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "ID booking không hợp lệ.")]
    public long? BookingId { get; set; }

    [MaxLength(20)]
    public string? PlateNumber { get; set; }

    [MaxLength(500)]
    public string? CheckInImageUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.Equals(Source, "WALK_IN", StringComparison.OrdinalIgnoreCase))
        {
            if (LotId is not > 0)
                yield return new ValidationResult("Bãi đỗ xe là bắt buộc khi tiếp nhận xe vãng lai.", [nameof(LotId)]);
            if (!EventId.HasValue || EventId.Value == Guid.Empty)
                yield return new ValidationResult("EventId hợp lệ là bắt buộc khi tiếp nhận xe vãng lai.", [nameof(EventId)]);
            if (ZoneId is not > 0)
                yield return new ValidationResult("Khu vực là bắt buộc khi tiếp nhận xe vãng lai.", [nameof(ZoneId)]);
            if (string.IsNullOrWhiteSpace(TicketCode))
                yield return new ValidationResult("Mã vé là bắt buộc khi tiếp nhận xe vãng lai.", [nameof(TicketCode)]);
        }
        else if (string.Equals(Source, "BOOKING", StringComparison.OrdinalIgnoreCase))
        {
            if (BookingId is not > 0)
                yield return new ValidationResult("BookingId là bắt buộc khi tiếp nhận booking.", [nameof(BookingId)]);
            if (string.IsNullOrWhiteSpace(PlateNumber))
                yield return new ValidationResult("Biển số xe là bắt buộc khi tiếp nhận booking.", [nameof(PlateNumber)]);
            if (string.IsNullOrWhiteSpace(CheckInImageUrl))
                yield return new ValidationResult("Ảnh check-in là bắt buộc khi tiếp nhận booking.", [nameof(CheckInImageUrl)]);
        }
    }
}
