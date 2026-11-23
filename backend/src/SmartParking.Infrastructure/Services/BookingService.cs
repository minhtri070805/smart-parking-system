using SmartParking.Persistence.Data;
using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmartParking.Application.Common.Helpers;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Models;
using SmartParking.Application.DTOs.Bookings;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;

namespace SmartParking.Infrastructure.Services;

public class BookingService : IBookingService
{
    private readonly IApplicationDbContext _context;
    private readonly IParkingAuthorizationService _authService;
    private readonly ILogger<BookingService> _logger;
    private readonly IBookingPaymentGateway _bookingPaymentGateway;
    private readonly IEmailService _emailService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public BookingService(
        IApplicationDbContext context,
        IParkingAuthorizationService authService,
        ILogger<BookingService> logger,
        IBookingPaymentGateway bookingPaymentGateway,
        IEmailService emailService)
    {
        _context = context;
        _authService = authService;
        _logger = logger;
        _bookingPaymentGateway = bookingPaymentGateway;
        _emailService = emailService;
    }

    public async Task<(bool Success, int StatusCode, string Message, BookingDto? Data)> CreateBookingAsync(
        CreateBookingRequestDto request,
        long userId,
        string userRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var lot = await _context.ParkingLots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot == null)
        {
            return (false, 404, $"Không tìm thấy bãi đỗ xe với ID: {request.LotId}.", null);
        }

        if (!lot.IsActive)
        {
            return (false, 400, $"Bãi đỗ xe '{lot.Name}' hiện đang đóng cửa hoặc tạm ngừng hoạt động.", null);
        }

        var paymentMode = userRole == Roles.User
            ? "PREPAY"
            : (request.PaymentMode ?? "PREPAY").Trim().ToUpperInvariant();
        if (paymentMode is not ("PREPAY" or "PAY_AT_COUNTER"))
            return (false, 400, "Phương thức thanh toán đặt chỗ không hợp lệ.", null);

        // Chính sách vận hành tạm tắt trong demo; dùng khoản cọc cố định để luồng VNPay tiếp tục chạy.
        const decimal demoDepositAmount = 50000m;
        ParkingPolicy? bookingPolicy = null;

        var startTime = request.StartTime == default ? DateTime.UtcNow : request.StartTime;
        // Existing unzoned data retains its legacy tolerance. Configured sites read the approved source.
        var pastTolerance = bookingPolicy == null ? 15 : 0;
        if (request.StartTime != default && (DateTime.UtcNow - request.StartTime).TotalMinutes > pastTolerance)
        {
            return (false, 400, "Thời gian bắt đầu đặt chỗ không được ở quá khứ.", null);
        }

        if (request.EndTime.HasValue && request.EndTime.Value <= startTime)
        {
            return (false, 400, "Thời gian kết thúc đặt chỗ phải sau thời gian bắt đầu.", null);
        }

        await ExpireNoShowBookingsAsync(request.LotId, ct);

        // 1. Resolve vehicle and validate ownership
        Vehicle? vehicle = null;
        string normalizedPlate = string.Empty;

        // An explicit plate is a booking-only choice and takes precedence over a stale vehicle id.
        if (!string.IsNullOrWhiteSpace(request.PlateNumber))
        {
            normalizedPlate = PlateNumberHelper.Normalize(request.PlateNumber);
            if (!PlateNumberHelper.IsValidPlateFormat(normalizedPlate))
            {
                return (false, 400, "Biển số ô tô không đúng định dạng. Ví dụ hợp lệ: 51F-123.45.", null);
            }

            vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.PlateNumber == normalizedPlate, ct);
            if (vehicle == null)
            {
                vehicle = new Vehicle
                {
                    PlateNumber = normalizedPlate,
                    // Booking-only plates must not become registered vehicles on the customer's account.
                    UserId = null,
                    // Keep the internal plate reference inactive so a later vehicle registration can claim it.
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Vehicles.Add(vehicle);
                await _context.SaveChangesAsync(ct);
            }
            else if (vehicle.UserId == userId && string.IsNullOrWhiteSpace(vehicle.Brand) &&
                string.IsNullOrWhiteSpace(vehicle.Model) && string.IsNullOrWhiteSpace(vehicle.Color))
            {
                // Remove legacy booking-only records that were incorrectly attached to this account.
                vehicle.UserId = null;
                vehicle.IsActive = false;
                vehicle.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
        }
        else if (request.VehicleId.HasValue)
        {
            vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId.Value, ct);
            if (vehicle == null)
            {
                return (false, 404, $"Không tìm thấy phương tiện với ID: {request.VehicleId.Value}.", null);
            }

            if (!vehicle.IsActive)
            {
                return (false, 400, "Phương tiện này hiện đang bị vô hiệu hóa.", null);
            }

            if (userRole == Roles.User && vehicle.UserId.HasValue && vehicle.UserId.Value != userId)
            {
                return (false, 403, "Bạn không có quyền đặt chỗ cho phương tiện của người dùng khác.", null);
            }

            normalizedPlate = vehicle.PlateNumber;
        }
        else
        {
            return (false, 400, "Vui lòng chọn phương tiện hoặc nhập biển số xe cần đặt chỗ.", null);
        }

        // 2. Anti-Passback check: Vehicle cannot create booking if it has an open session
        var hasOpenSession = await _context.ParkingSessions
            .AnyAsync(s => s.PlateNumber == normalizedPlate && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);

        if (hasOpenSession)
        {
            return (false, 409, $"Phương tiện mang biển số '{normalizedPlate}' hiện đang có phiên đỗ xe chưa kết thúc trong bãi. Không thể tạo đặt chỗ mới.", null);
        }

        // 3. Duplicate booking check: Vehicle cannot have more than one PENDING booking
        var hasActiveBooking = await _context.Bookings
            .AnyAsync(b => b.VehicleId == vehicle.Id && b.Status == BookingStatuses.Pending, ct);

        if (hasActiveBooking)
        {
            return (false, 409, $"Phương tiện mang biển số '{normalizedPlate}' đã có một lượt đặt chỗ đang có hiệu lực (PENDING).", null);
        }

        // 4. Concurrency lock per lot to prevent race conditions & overbooking
        var lotLock = ParkingLotLockHelper.GetSemaphore(lot.Id);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    long lockKey = ParkingLotLockHelper.LotLockBase + lot.Id;
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { lockKey }, cancellationToken: ct);
                }

                var calc = await ParkingLotCapacityHelper.CalculateAsync(_context, lot.Id, lot, ct);
                if (calc.AvailableCapacity <= 0)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, 409, $"Bãi đỗ xe '{lot.Name}' đã hết chỗ khả dụng để đặt trước (Sức chứa: {calc.TotalCapacity}, Đang sử dụng hiệu dụng: {calc.EffectiveOccupied}, Đang giữ chỗ: {calc.PendingBookingsCount}).", null);
                }

                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
                var booking = new Booking
                {
                    LotId = lot.Id,
                    ParkingSlotId = null,
                    PolicyId = bookingPolicy?.Id,
                    UserId = userId,
                    VehicleId = vehicle.Id,
                    PlateNumber = normalizedPlate,
                    StartTime = startTime,
                    EndTime = request.EndTime,
                    Status = BookingStatuses.Pending,
                    PaymentMode = paymentMode,
                    PaymentStatus = paymentMode == "PREPAY" ? "PENDING" : "PAY_AT_COUNTER",
                    PrepaidAmount = paymentMode == "PREPAY" ? demoDepositAmount : null,
                    Notes = request.Notes?.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync(ct);

                var audit = new AuditLog
                {
                    Action = "CREATE_BOOKING",
                    TargetEntity = "bookings",
                    TargetId = booking.Id.ToString(),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        booking.Id,
                        booking.LotId,
                        booking.UserId,
                        booking.VehicleId,
                        booking.ParkingSlotId,
                        booking.PlateNumber,
                        booking.StartTime,
                        booking.EndTime,
                        booking.Status,
                        IpAddress = ipAddress
                    }, JsonOptions),
                    ActorId = userId,
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow
                };
                _context.AuditLogs.Add(audit);
                await _context.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);

                var paymentUrl = (string?)null;
                if (paymentMode == "PREPAY")
                {
                    var deposit = demoDepositAmount;

                    try
                    {
                        var payment = await _bookingPaymentGateway.CreateBookingPaymentLinkAsync(
                            booking.Id, deposit, normalizedPlate, startTime, ct);
                        booking.PaymentInvoiceId = payment.InvoiceId;
                        await _context.SaveChangesAsync(ct);
                        paymentUrl = payment.PaymentUrl;
                    }
                    catch (Exception paymentError) when (paymentError is not OperationCanceledException)
                    {
                        booking.Status = BookingStatuses.Cancelled;
                        booking.PaymentStatus = "FAILED";
                        booking.CancelledAt = DateTime.UtcNow;
                        booking.CancellationReason = "Không tạo được giao dịch thanh toán trước.";
                        booking.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                        _logger.LogError(paymentError, "Unable to create payment link for booking {BookingId}", booking.Id);
                        return (false, 503, "Không kết nối được Payment Service để tạo thanh toán. Chỗ giữ tạm đã được giải phóng; hãy thử lại.", null);
                    }
                }

                _logger.LogInformation("User {UserId} reserved capacity at lot {LotId} for vehicle {PlateNumber}. Booking ID: {BookingId}",
                    userId, lot.Id, normalizedPlate, booking.Id);

                var dto = MapToDto(booking, lot.Name, lot.Address, user);
                dto.PaymentUrl = paymentUrl;
                return (true, 201, "Đặt chỗ thành công. Vị trí đỗ sẽ được xác định khi xe đến bãi.", dto);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                if (IsPostgresUniqueViolation(ex, "uq_bookings_pending_vehicle"))
                {
                    return (false, 409, $"Phương tiện mang biển số '{normalizedPlate}' đã có một lượt đặt chỗ đang có hiệu lực (PENDING).", null);
                }
                _logger.LogError(ex, "Lỗi cơ sở dữ liệu khi đặt chỗ cho phương tiện {PlateNumber}", normalizedPlate);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi khi đặt chỗ cho phương tiện {PlateNumber}", normalizedPlate);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<(bool Success, string Message)> RecordBookingPaymentAsync(
        long bookingId, decimal amount, string transactionRef, string? paymentMethod, DateTime? paidAt,
        CancellationToken ct = default)
    {
        var booking = await _context.Bookings.Include(b => b.User).Include(b => b.Lot)
            .Include(b => b.Vehicle).FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking == null) return (false, "Không tìm thấy lượt đặt chỗ.");
        if (booking.PaymentStatus == "PAID")
            return booking.PaymentTransactionRef == transactionRef && booking.PrepaidAmount == amount
                ? (true, "Thanh toán đặt chỗ đã được xác nhận trước đó.")
                : (false, "Lượt đặt chỗ đã được thanh toán bằng giao dịch khác.");
        if (booking.PaymentMode != "PREPAY" || booking.PaymentStatus != "PENDING")
            return (false, "Lượt đặt chỗ không chờ thanh toán trước.");
        if (booking.Status != BookingStatuses.Pending && booking.Status != BookingStatuses.Expired)
            return (false, "Lượt đặt chỗ đã bị hủy hoặc không còn chờ xe đến.");
        if (booking.PrepaidAmount != amount)
            return (false, "Số tiền thanh toán không khớp khoản đặt cọc của lượt đặt chỗ.");
        if (string.IsNullOrWhiteSpace(transactionRef) || transactionRef.Length > 100)
            return (false, "Mã giao dịch không hợp lệ.");

        booking.PaymentStatus = "PAID";
        booking.PaymentTransactionRef = transactionRef.Trim();
        booking.PaymentPaidAt = paidAt?.ToUniversalTime() ?? DateTime.UtcNow;
        booking.UpdatedAt = DateTime.UtcNow;
        _context.AuditLogs.Add(new AuditLog
        {
            Action = "BOOKING_PAYMENT_CLEARED", TargetEntity = "bookings", TargetId = booking.Id.ToString(),
            CreatedAt = DateTime.UtcNow,
            NewValues = JsonSerializer.Serialize(new { booking.PaymentStatus, booking.PrepaidAmount,
                TransactionRef = booking.PaymentTransactionRef, PaymentMethod = paymentMethod, booking.PaymentPaidAt,
                NoRefund = booking.Status == BookingStatuses.Expired }, JsonOptions)
        });
        await _context.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(booking.User?.Email))
        {
            var emailSent = await _emailService.SendBookingPaymentReceiptAsync(
                booking.User.Email, booking.Id, booking.PlateNumber, booking.Lot?.Name ?? $"Bãi #{booking.LotId}",
                amount, booking.PaymentTransactionRef, booking.PaymentPaidAt.Value, ct);
            if (!emailSent)
                _logger.LogWarning("Booking payment was confirmed but receipt email could not be delivered for booking {BookingId}.", booking.Id);
        }
        return (true, booking.Status == BookingStatuses.Expired
            ? "Thanh toán đã ghi nhận sau khi lượt đặt chỗ hết hạn; khoản tiền không được hoàn."
            : "Thanh toán trước cho lượt đặt chỗ đã được xác nhận.");
    }

    public async Task<int> ExpireNoShowBookingsAsync(int? lotId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var lots = await _context.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatuses.Pending && b.StartTime <= now && (!lotId.HasValue || b.LotId == lotId))
            .Select(b => b.LotId).Distinct().OrderBy(id => id).ToListAsync(ct);
        var expired = 0;
        foreach (var id in lots)
        {
            var gate = ParkingLotLockHelper.GetSemaphore(id);
            await gate.WaitAsync(ct);
            try
            {
                await using var transaction = await _context.BeginTransactionAsync(ct);
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { ParkingLotLockHelper.LotLockBase + id }, ct);
                var pending = await _context.Bookings
                    .Where(b => b.LotId == id && b.Status == BookingStatuses.Pending && b.StartTime <= now).ToListAsync(ct);
                foreach (var booking in pending)
                {
                    var grace = BookingStatuses.NoShowGracePeriodMinutes;
                    if (booking.StartTime.AddMinutes(grace) > now) continue;
                    booking.Status = BookingStatuses.Expired;
                    booking.UpdatedAt = now;
                    _context.AuditLogs.Add(new AuditLog { Action = "EXPIRE_BOOKING_NO_SHOW", TargetEntity = "bookings",
                        TargetId = booking.Id.ToString(), CreatedAt = now,
                        OldValues = JsonSerializer.Serialize(new { Status = BookingStatuses.Pending }, JsonOptions),
                        NewValues = JsonSerializer.Serialize(new
                        {
                            Status = BookingStatuses.Expired,
                            Reason = "Khách không đến trong thời gian cho phép sau giờ hẹn.",
                            GraceMinutes = grace,
                            NoRefund = true,
                            booking.PolicyId
                        }, JsonOptions) });
                    expired++;
                }
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            finally { gate.Release(); }
        }
        return expired;
    }

    public async Task<PagedResult<BookingDto>> GetMyBookingsAsync(
        long userId,
        BookingFilterDto filter,
        CancellationToken ct = default)
    {
        await ExpireNoShowBookingsAsync(ct: ct);
        var query = _context.Bookings
            .Include(b => b.Lot)
            .Include(b => b.User)
            .Include(b => b.Vehicle)
            .Include(b => b.Session)
            .AsNoTracking()
            .Where(b => b.UserId == userId);

        if (filter.LotId.HasValue)
        {
            query = query.Where(b => b.LotId == filter.LotId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim().ToUpperInvariant();
            query = query.Where(b => b.Status == status);
            if (status == BookingStatuses.CheckedIn)
            {
                query = query.Where(b => b.Session != null
                    && b.Session.Status == SessionStatuses.Active
                    && b.Session.CurrentSlotId == null);
            }
        }

        if (filter.FromDate.HasValue)
            query = query.Where(b => b.StartTime >= filter.FromDate.Value);
        if (filter.ToDate.HasValue)
            query = query.Where(b => b.StartTime <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var page = filter.PageNumber < 1 ? 1 : filter.PageNumber;
        var pageSize = filter.PageSize < 1 ? 10 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = bookings.Select(b => MapToDto(b, b.Lot.Name, b.Lot.Address, b.User)).ToList();
        return PagedResult<BookingDto>.Create(dtos, totalCount, page, pageSize);
    }

    public async Task<(bool Success, int StatusCode, string Message, BookingDetailDto? Data)> GetBookingByIdAsync(
        long bookingId,
        long currentUserId,
        string currentUserRole,
        CancellationToken ct = default)
    {
        await ExpireNoShowBookingsAsync(ct: ct);
        var booking = await _context.Bookings
            .Include(b => b.Lot)
            .Include(b => b.User)
            .Include(b => b.Vehicle)
            .Include(b => b.Session)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null)
        {
            return (false, 404, $"Không tìm thấy lượt đặt chỗ với ID: {bookingId}.", null);
        }

        // Authorization check
        if (currentUserRole == Roles.User)
        {
            if (booking.UserId != currentUserId)
            {
                return (false, 403, "Bạn không có quyền xem thông tin lượt đặt chỗ của người khác.", null);
            }
        }
        else if (currentUserRole == Roles.Manager || currentUserRole == Roles.Staff)
        {
            var hasAccess = await _authService.HasLotAccessAsync(currentUserId, currentUserRole, booking.LotId, ct);
            if (!hasAccess)
            {
                return (false, 403, "Bạn không được phân công quản lý bãi đỗ xe của lượt đặt chỗ này.", null);
            }
        }

        var detail = MapToDetailDto(booking);
        return (true, 200, "Lấy thông tin lượt đặt chỗ thành công.", detail);
    }

    public async Task<(bool Success, int StatusCode, string Message, BookingDto? Data)> CancelBookingAsync(
        long bookingId,
        CancelBookingRequestDto request,
        long currentUserId,
        string currentUserRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var booking = await _context.Bookings
            .Include(b => b.Lot)
            .Include(b => b.User)
            .Include(b => b.Vehicle)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

        if (booking == null)
        {
            return (false, 404, $"Không tìm thấy lượt đặt chỗ với ID: {bookingId}.", null);
        }

        // Authorization check
        if (currentUserRole == Roles.User)
        {
            if (booking.UserId != currentUserId)
            {
                return (false, 403, "Bạn không có quyền hủy lượt đặt chỗ của người dùng khác.", null);
            }
        }
        else if (currentUserRole == Roles.Manager || currentUserRole == Roles.Staff)
        {
            var hasAccess = await _authService.HasLotAccessAsync(currentUserId, currentUserRole, booking.LotId, ct);
            if (!hasAccess)
            {
                return (false, 403, "Bạn không được phân công quản lý bãi đỗ xe của lượt đặt chỗ này.", null);
            }
        }

        var lotLock = ParkingLotLockHelper.GetSemaphore(booking.LotId);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    long lockKey = ParkingLotLockHelper.LotLockBase + booking.LotId;
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { lockKey }, cancellationToken: ct);
                }

                // Re-fetch booking inside transaction to guard against race with CheckIn
                var lockedBooking = await _context.Bookings
                    .Include(b => b.Lot)
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b => b.Id == bookingId, ct);

                if (lockedBooking == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, 404, $"Không tìm thấy lượt đặt chỗ với ID: {bookingId}.", null);
                }

                // State machine validation
                if (lockedBooking.Status != BookingStatuses.Pending)
                {
                    await transaction.RollbackAsync(ct);
                    if (lockedBooking.Status == BookingStatuses.CheckedIn)
                    {
                        return (false, 400, "Lượt đặt chỗ này đã được sử dụng (phương tiện đã check-in vào bãi), không thể hủy.", null);
                    }
                    if (lockedBooking.Status == BookingStatuses.Cancelled)
                    {
                        return (false, 400, "Lượt đặt chỗ này đã bị hủy trước đó.", null);
                    }
                    if (lockedBooking.Status == BookingStatuses.Expired)
                    {
                        return (false, 400, "Lượt đặt chỗ này đã hết hạn, không thể hủy.", null);
                    }
                    return (false, 400, $"Không thể hủy đặt chỗ ở trạng thái '{lockedBooking.Status}'.", null);
                }

                if (currentUserRole == Roles.User && lockedBooking.StartTime <= DateTime.UtcNow)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, 409, "Chỉ có thể hủy vé trước giờ hẹn.", null);
                }

                var reason = string.IsNullOrWhiteSpace(request.Reason)
                    ? (currentUserRole == Roles.User ? "Người dùng yêu cầu hủy đặt chỗ." : "Nhân sự quản lý hủy đặt chỗ theo yêu cầu vận hành.")
                    : request.Reason.Trim();

                lockedBooking.Status = BookingStatuses.Cancelled;
                lockedBooking.CancelledAt = DateTime.UtcNow;
                lockedBooking.CancellationReason = reason;
                lockedBooking.UpdatedAt = DateTime.UtcNow;

                var audit = new AuditLog
                {
                    Action = "CANCEL_BOOKING",
                    TargetEntity = "bookings",
                    TargetId = lockedBooking.Id.ToString(),
                    OldValues = JsonSerializer.Serialize(new { Status = BookingStatuses.Pending }, JsonOptions),
                    NewValues = JsonSerializer.Serialize(new
                    {
                        Status = BookingStatuses.Cancelled,
                        CancellationReason = reason,
                        CancelledByUserId = currentUserId,
                        CancelledByRole = currentUserRole,
                        IpAddress = ipAddress
                    }, JsonOptions),
                    ActorId = currentUserId,
                    IpAddress = ipAddress,
                    CreatedAt = DateTime.UtcNow
                };
                _context.AuditLogs.Add(audit);

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                _logger.LogInformation("Booking {BookingId} for vehicle {PlateNumber} cancelled by user {UserId} ({UserRole})",
                    lockedBooking.Id, lockedBooking.PlateNumber, currentUserId, currentUserRole);

                var dto = MapToDto(lockedBooking, lockedBooking.Lot.Name, lockedBooking.Lot.Address, lockedBooking.User);
                return (true, 200, "Hủy lượt đặt chỗ thành công. Sức chứa giữ chỗ đã được giải phóng.", dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi khi hủy đặt chỗ ID {BookingId}", bookingId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<PagedResult<BookingDto>> GetBookingsAsync(
        BookingFilterDto filter,
        long currentUserId,
        string currentUserRole,
        CancellationToken ct = default)
    {
        await ExpireNoShowBookingsAsync(ct: ct);
        var query = _context.Bookings
            .Include(b => b.Lot)
            .Include(b => b.User)
            .Include(b => b.Vehicle)
            .Include(b => b.Session)
            .AsNoTracking()
            .AsQueryable();

        // RBAC lot scope enforcement
        if (currentUserRole == Roles.Manager || currentUserRole == Roles.Staff)
        {
            var assignedLots = await _authService.GetAssignedLotIdsAsync(currentUserId, currentUserRole, ct);
            if (assignedLots.Count == 0)
            {
                return PagedResult<BookingDto>.Create([], 0, filter.PageNumber, filter.PageSize);
            }

            if (filter.LotId.HasValue)
            {
                if (!assignedLots.Contains(filter.LotId.Value))
                {
                    return PagedResult<BookingDto>.Create([], 0, filter.PageNumber, filter.PageSize);
                }
                query = query.Where(b => b.LotId == filter.LotId.Value);
            }
            else
            {
                query = query.Where(b => assignedLots.Contains(b.LotId));
            }
        }
        else if (filter.LotId.HasValue)
        {
            query = query.Where(b => b.LotId == filter.LotId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim().ToUpperInvariant();
            query = query.Where(b => b.Status == status);
            if (status == BookingStatuses.CheckedIn)
            {
                query = query.Where(b => b.Session != null
                    && b.Session.Status == SessionStatuses.Active
                    && b.Session.CurrentSlotId == null);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(b => b.PlateNumber.ToLower().Contains(search) || b.User.Username.ToLower().Contains(search));
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(b => b.StartTime >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(b => b.StartTime <= filter.ToDate.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var page = filter.PageNumber < 1 ? 1 : filter.PageNumber;
        var pageSize = filter.PageSize < 1 ? 10 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var bookings = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = bookings.Select(b => MapToDto(b, b.Lot.Name, b.Lot.Address, b.User)).ToList();
        return PagedResult<BookingDto>.Create(dtos, totalCount, page, pageSize);
    }

    public async Task<LotCapacityDto?> GetLotCapacityAsync(
        int lotId,
        long? currentUserId = null,
        string? currentUserRole = null,
        CancellationToken ct = default)
    {
        await ExpireNoShowBookingsAsync(lotId, ct);
        var lot = await _context.ParkingLots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId, ct);
        if (lot == null)
        {
            return null;
        }

        var includeDiagnostics = false;
        if (currentUserId.HasValue
            && (currentUserRole == Roles.Admin || currentUserRole == Roles.Manager || currentUserRole == Roles.Staff))
        {
            includeDiagnostics = await _authService.HasLotAccessAsync(currentUserId.Value, currentUserRole, lotId, ct);
        }

        var calc = await ParkingLotCapacityHelper.CalculateAsync(_context, lotId, lot, ct);
        var physicalSlotsCount = await _context.ParkingSlots.CountAsync(s => s.Floor.LotId == lotId, ct);
        var physicallyAvailableSlots = await _context.ParkingSlots.CountAsync(s =>
            s.Floor.LotId == lotId && s.IsActive && s.Floor.IsActive && s.Zone.IsActive &&
            s.PhysicalStatus == SlotPhysicalStatuses.Empty &&
            !s.CurrentSessions.Any(session => session.Status == SessionStatuses.Active || session.Status == SessionStatuses.Paid) &&
            !s.AssignedDispatches.Any(dispatch => dispatch.Status == DispatchStatuses.Assigned) &&
            !_context.Bookings.Any(booking => booking.ParkingSlotId == s.Id &&
                (booking.Status == BookingStatuses.Pending || booking.Status == BookingStatuses.CheckedIn)), ct);
        var unassignedPendingBookings = await _context.Bookings.CountAsync(b => b.LotId == lotId &&
            b.Status == BookingStatuses.Pending && b.ParkingSlotId == null, ct);
        var bookableTotalCapacity = await _context.ParkingSlots.CountAsync(s => s.Floor.LotId == lotId &&
            s.IsActive && s.Floor.IsActive && s.Zone.IsActive &&
            s.PhysicalStatus != SlotPhysicalStatuses.Maintenance &&
            s.PhysicalStatus != SlotPhysicalStatuses.Unavailable, ct);
        var availableForWalkIn = lot.IsActive ? calc.AvailableCapacity : 0;
        var availableForBooking = lot.IsActive
            ? Math.Max(0, physicallyAvailableSlots - unassignedPendingBookings)
            : 0;

        return new LotCapacityDto
        {
            PolicyId = calc.PolicyId,
            NearFullThreshold = calc.NearFullThreshold,
            IsNearFull = calc.NearFullThreshold.HasValue ? availableForBooking <= calc.NearFullThreshold.Value : null,
            LotId = lot.Id,
            LotName = lot.Name,
            LotAddress = lot.Address,
            TotalCapacity = bookableTotalCapacity,
            AvailableForWalkIn = availableForWalkIn,
            AvailableForBooking = availableForBooking,
            PendingBookingsCount = calc.PendingBookingsCount,
            IsFull = !lot.IsActive || availableForBooking <= 0,
            PhysicalSlotsCount = physicalSlotsCount,
            ConfiguredCapacity = lot.TotalCapacity,
            ActiveSessionsCount = includeDiagnostics ? calc.ActiveSessionsCount : null,
            ActiveReservationsCount = includeDiagnostics ? calc.PendingBookingsCount : null,
            TotalOccupiedOrReserved = includeDiagnostics ? calc.TotalOccupiedOrReserved : null,
            PhysicalOccupiedSlotsCount = includeDiagnostics ? calc.PhysicalOccupiedSlotsCount : null,
            PhysicalEmptySlotsCount = includeDiagnostics ? calc.PhysicalEmptySlotsCount : null,
            UnassignedSessionsCount = includeDiagnostics ? calc.UnassignedSessionsCount : null,
            ProposedSafetyBuffer = includeDiagnostics ? calc.ProposedSafetyBuffer : null,
            UnmatchedPhysicalOccupiedCount = includeDiagnostics ? calc.UnmatchedPhysicalOccupiedCount : null,
            DiscrepancyNotes = includeDiagnostics ? calc.DiscrepancyNotes : null
        };
    }

    private static bool IsPostgresUniqueViolation(DbUpdateException ex, string constraintName)
    {
        if (ex.InnerException is PostgresException pgEx)
        {
            return pgEx.SqlState == PostgresErrorCodes.UniqueViolation &&
                   (string.IsNullOrEmpty(constraintName) || pgEx.ConstraintName?.Equals(constraintName, StringComparison.OrdinalIgnoreCase) == true);
        }

        var innerMsg = ex.InnerException?.Message ?? ex.Message;
        if (!innerMsg.Contains("23505"))
        {
            return false;
        }

        return string.IsNullOrEmpty(constraintName) || innerMsg.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
    }

    private static BookingDto MapToDto(Booking b, string lotName, string? lotAddress, User? user)
    {
        return new BookingDto
        {
            Id = b.Id,
            PolicyId = b.PolicyId,
            LotId = b.LotId,
            LotName = lotName,
            LotAddress = lotAddress,
            UserId = b.UserId,
            Username = user?.Username ?? "user",
            FullName = user?.FullName,
            Email = user?.Email,
            PhoneNumber = user?.PhoneNumber,
            VehicleId = b.VehicleId,
            ParkingSlotId = b.ParkingSlotId,
            PlateNumber = b.PlateNumber,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            Status = b.Status,
            PaymentMode = b.PaymentMode,
            PaymentStatus = b.PaymentStatus,
            PrepaidAmount = b.PrepaidAmount,
            PaymentInvoiceId = b.PaymentInvoiceId,
            PaymentPaidAt = b.PaymentPaidAt,
            SessionId = b.Session?.Id,
            CheckedInAt = b.CheckedInAt,
            CancelledAt = b.CancelledAt,
            CancellationReason = b.CancellationReason,
            Notes = b.Notes,
            VehicleBrand = b.Vehicle?.UserId is null || b.Vehicle.UserId == b.UserId ? b.Vehicle?.Brand : null,
            VehicleModel = b.Vehicle?.UserId is null || b.Vehicle.UserId == b.UserId ? b.Vehicle?.Model : null,
            CheckInImageUrl = b.Session?.CheckInImageUrl,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt
        };
    }

    private static BookingDetailDto MapToDetailDto(Booking b)
    {
        return new BookingDetailDto
        {
            Id = b.Id,
            PolicyId = b.PolicyId,
            LotId = b.LotId,
            LotName = b.Lot?.Name ?? string.Empty,
            LotAddress = b.Lot?.Address,
            UserId = b.UserId,
            Username = b.User?.Username ?? string.Empty,
            FullName = b.User?.FullName,
            Email = b.User?.Email,
            PhoneNumber = b.User?.PhoneNumber,
            VehicleId = b.VehicleId,
            PlateNumber = b.PlateNumber,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            Status = b.Status,
            PaymentMode = b.PaymentMode,
            PaymentStatus = b.PaymentStatus,
            PrepaidAmount = b.PrepaidAmount,
            PaymentInvoiceId = b.PaymentInvoiceId,
            PaymentPaidAt = b.PaymentPaidAt,
            SessionId = b.Session?.Id,
            CheckedInAt = b.CheckedInAt,
            CancelledAt = b.CancelledAt,
            CancellationReason = b.CancellationReason,
            Notes = b.Notes,
            VehicleBrand = b.Vehicle?.UserId is null || b.Vehicle.UserId == b.UserId ? b.Vehicle?.Brand : null,
            VehicleModel = b.Vehicle?.UserId is null || b.Vehicle.UserId == b.UserId ? b.Vehicle?.Model : null,
            CheckInImageUrl = b.Session?.CheckInImageUrl,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
            VehicleColor = b.Vehicle?.UserId is null || b.Vehicle.UserId == b.UserId ? b.Vehicle?.Color : null
        };
    }
}
