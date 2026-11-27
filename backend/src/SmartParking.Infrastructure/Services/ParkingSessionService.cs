using SmartParking.Persistence.Data; // ParkingLotLockHelper
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartParking.Application.Common.Helpers;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.Common.Models;
using SmartParking.Application.DTOs.Sessions;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;

namespace SmartParking.Infrastructure.Services;

public class ParkingSessionService : IParkingSessionService
{
    private readonly IApplicationDbContext _context;
    private readonly IParkingAuthorizationService _authService;
    private readonly ILogger<ParkingSessionService> _logger;
    private readonly IExitRecognitionLookup? _exitRecognition;
    private readonly IBookingPaymentGateway? _bookingPaymentGateway;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public ParkingSessionService(
        IApplicationDbContext context,
        IParkingAuthorizationService authService,
        ILogger<ParkingSessionService> logger,
        IExitRecognitionLookup? exitRecognition = null,
        IBookingPaymentGateway? bookingPaymentGateway = null)
    {
        _context = context;
        _authService = authService;
        _logger = logger;
        _exitRecognition = exitRecognition;
        _bookingPaymentGateway = bookingPaymentGateway;
    }

    public async Task<(bool Success, string Message, CounterPaymentReceipt? Data)> RecordCounterPaymentAsync(
        long sessionId, long? actorId, string actorRole, CancellationToken ct = default)
    {
        var session = await _context.ParkingSessions.Include(s => s.Booking)
            .Include(s => s.CurrentSlot).ThenInclude(slot => slot!.Floor)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session == null) return (false, "Không tìm thấy phiên gửi xe.", null);
        if (actorId == null || !await _authService.CanOperateGateOutAsync(actorId.Value, actorRole, session.LotId, ct))
            return (false, "Bạn không được phân công xử lý thanh toán tại cổng ra của bãi này.", null);
        if (!await _authService.CanOperateAreaAsync(actorId.Value, actorRole, session.LotId, StaffDuties.GateOut,
                session.CurrentSlot?.FloorId, session.ZoneId ?? session.CurrentSlot?.ZoneId, ct))
            return (false, "Phiên xe nằm ngoài tầng hoặc khu vực được phân công.", null);
        if (session.VerificationStatus == VerificationStatuses.MonthlyPass)
            return (false, "Xe vé tháng không phát sinh phí; không thu tiền tại quầy.", null);
        if (session.Status == SessionStatuses.Paid)
            return (true, "Phiên xe đã thanh toán trước đó.", new CounterPaymentReceipt("", "", session.TotalFee, 0));
        if (session.Status != SessionStatuses.Active)
            return (false, $"Không thể thu tiền cho phiên ở trạng thái {session.Status}.", null);
        if (session.EntryEventId.HasValue && session.VerificationStatus != "VERIFIED")
            return (false, "Xe chưa được xác minh đã đỗ; chưa thể thanh toán khi checkout.", null);
        if (_bookingPaymentGateway == null)
            return (false, "Chưa cấu hình kết nối Billing Service.", null);

        var prepaidAmount = session.Booking?.PaymentStatus == "PAID"
            ? session.Booking.PrepaidAmount ?? 0m
            : 0m;
        try
        {
            var receipt = await _bookingPaymentGateway.RecordCounterPaymentAsync(
                session.Id, session.BookingId, session.PlateNumber, session.CheckInTime, prepaidAmount, ct);
            return (true, "Đã ghi nhận thanh toán tại quầy qua Billing Service.", receipt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to record counter payment for session {SessionId}", session.Id);
            return (false, "Không ghi nhận được thanh toán tại quầy. Hãy kiểm tra Billing Service rồi thử lại.", null);
        }
    }

    public async Task<MonthlyTrafficStatsDto> GetMonthlyTrafficStatsAsync(
        int year,
        int month,
        int? lotId,
        IReadOnlyList<int>? allowedLotIds = null,
        CancellationToken ct = default)
    {
        // Dữ liệu lưu UTC, thống kê theo ngày giờ Việt Nam (UTC+7).
        var offset = TimeSpan.FromHours(7);
        var startLocal = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endLocal = startLocal.AddMonths(1);
        var startUtc = startLocal - offset;
        var endUtc = endLocal - offset;

        var query = _context.ParkingSessions.AsNoTracking().Where(s => s.Status != "CANCELLED");
        if (lotId.HasValue)
        {
            query = query.Where(s => s.LotId == lotId.Value);
        }
        else if (allowedLotIds != null && allowedLotIds.Count > 0)
        {
            query = query.Where(s => allowedLotIds.Contains(s.LotId));
        }

        var rows = await query
            .Where(s => (s.CheckInTime >= startUtc && s.CheckInTime < endUtc)
                     || (s.CheckOutTime != null && s.CheckOutTime >= startUtc && s.CheckOutTime < endUtc)
                     || (s.PaidAt != null && s.PaidAt >= startUtc && s.PaidAt < endUtc))
            .Select(s => new { s.CheckInTime, s.CheckOutTime, s.PaidAt, s.TotalFee })
            .ToListAsync(ct);

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var days = Enumerable.Range(1, daysInMonth)
            .Select(d => new DailyTrafficDto { Date = new DateTime(year, month, d).ToString("yyyy-MM-dd") })
            .ToList();
        var byHour = new int[24];

        bool InMonth(DateTime utc) => utc >= startUtc && utc < endUtc;
        foreach (var r in rows)
        {
            if (InMonth(r.CheckInTime))
            {
                var local = r.CheckInTime + offset;
                days[local.Day - 1].CheckIns++;
                byHour[local.Hour]++;
            }
            if (r.CheckOutTime is { } outTime && InMonth(outTime))
                days[(outTime + offset).Day - 1].CheckOuts++;
            if (r.PaidAt is { } paid && InMonth(paid))
                days[(paid + offset).Day - 1].Revenue += r.TotalFee;
        }

        // Lấy 50 giao dịch đã thanh toán gần nhất trong tháng
        var recentTransactions = await query
            .Where(s => s.PaidAt != null && s.PaidAt >= startUtc && s.PaidAt < endUtc && s.TotalFee > 0)
            .OrderByDescending(s => s.PaidAt)
            .Take(50)
            .Select(s => new MonthlyRevenueTransactionDto
            {
                SessionId = s.Id,
                LotId = s.LotId,
                LotName = s.Lot.Name,
                PlateNumber = s.PlateNumber,
                CheckInTime = s.CheckInTime,
                CheckOutTime = s.CheckOutTime,
                PaidAt = s.PaidAt,
                TotalFee = s.TotalFee,
                SlotCode = s.CurrentSlot != null ? s.CurrentSlot.SlotCode : null,
                Status = s.Status
            })
            .ToListAsync(ct);

        return new MonthlyTrafficStatsDto
        {
            Year = year,
            Month = month,
            LotId = lotId,
            TotalCheckIns = days.Sum(d => d.CheckIns),
            TotalCheckOuts = days.Sum(d => d.CheckOuts),
            TotalRevenue = days.Sum(d => d.Revenue),
            Days = days,
            CheckInsByHour = byHour,
            RecentTransactions = recentTransactions
        };
    }

    public const string LegacyCheckInRetiredMessage =
        "Check-in theo biển số đã ngừng. Tiếp nhận xe vãng lai bằng vé tại khu TRANSIENT qua API operations/entries.";

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> CheckInAsync(
        CheckInRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var normalizedPlate = PlateNumberHelper.Normalize(request.PlateNumber);
        if (!PlateNumberHelper.IsValidPlateFormat(normalizedPlate))
        {
            return (false, "Biển số ô tô không đúng định dạng. Ví dụ hợp lệ: 51F-123.45.", null);
        }

        var lot = await _context.ParkingLots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot == null)
        {
            return (false, $"Không tìm thấy bãi đỗ xe với ID: {request.LotId}.", null);
        }

        if (!lot.IsActive)
        {
            return (false, $"Bãi đỗ xe '{lot.Name}' hiện đang đóng hoặc tạm ngừng hoạt động.", null);
        }

        if (!await _authService.CanOperateGateInAsync(actorId ?? 0, actorRole, request.LotId, ct))
        {
            return (false, "Bạn không có quyền thực hiện nghiệp vụ cổng (GATE) tại bãi đỗ xe này.", null);
        }

        // Từ khi ô đỗ bắt buộc thuộc một khu (ParkingSlot.ZoneId), check-in theo biển số kiểu cũ không còn đường hợp lệ:
        // bãi có khu phải tiếp nhận bằng vé/EventId, bãi chưa có khu thì sức chứa vận hành = 0. Trả lỗi rõ ràng thay vì
        // báo "hết chỗ" gây hiểu nhầm. Không tạo phiên, không đụng ô.
        return (false, LegacyCheckInRetiredMessage, null);
    }

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> CheckInBookingAtGateAsync(
        long bookingId,
        CheckInBookingRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var normalizedPlate = PlateNumberHelper.Normalize(request.PlateNumber);
        if (!PlateNumberHelper.IsValidPlateFormat(normalizedPlate))
            return (false, "Biển số ô tô không đúng định dạng. Ví dụ hợp lệ: 51F-123.45.", null);

        var imageUrl = request.CheckInImageUrl?.Trim() ?? string.Empty;
        var databaseImage = imageUrl.StartsWith("/api/v1/images/content/", StringComparison.Ordinal)
            && Guid.TryParseExact(imageUrl["/api/v1/images/content/".Length..], "N", out _);
        var externalImage = Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri)
            && (imageUri.Scheme == Uri.UriSchemeHttps || imageUri.Scheme == Uri.UriSchemeHttp);
        if (!databaseImage && !externalImage)
            return (false, "Ảnh biển số xe chưa được lưu hợp lệ.", null);
        if (databaseImage && !await _context.StoredImages.AnyAsync(
                image => image.Url == imageUrl && image.Content != null && image.UserId == actorId, ct))
            return (false, "Ảnh biển số chưa được lưu hoặc không thuộc nhân viên xác nhận.", null);

        var bookingLotId = await _context.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => (int?)b.LotId)
            .FirstOrDefaultAsync(ct);
        if (!bookingLotId.HasValue)
            return (false, $"Không tìm thấy booking với ID: {bookingId}.", null);
        if (!await _authService.CanOperateGateInAsync(actorId ?? 0, actorRole, bookingLotId.Value, ct))
            return (false, "Bạn không có quyền thực hiện nghiệp vụ cổng vào (GATE) tại bãi này.", null);

        var lotLock = ParkingLotLockHelper.GetSemaphore(bookingLotId.Value);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                    await _context.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0});",
                        new object[] { ParkingLotLockHelper.LotLockBase + bookingLotId.Value },
                        cancellationToken: ct);

                var booking = await _context.Bookings
                    .Include(b => b.Lot)
                    .Include(b => b.Vehicle)
                        .ThenInclude(v => v!.User)
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
                if (booking == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy booking với ID: {bookingId}.", null);
                }
                if (booking.Status != BookingStatuses.Pending)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Booking #{booking.Id} không còn chờ xe đến (trạng thái: {booking.Status}).", null);
                }
                if (booking.PaymentMode == "PREPAY" && booking.PaymentStatus != "PAID")
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Booking chưa thanh toán trước thành công; chưa thể xác nhận xe đến.", null);
                }
                if (!PlateNumberHelper.IsMatch(booking.PlateNumber, normalizedPlate))
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Biển số xe thực tế không khớp với booking. Không thể xác nhận xe.", null);
                }
                if (!booking.Lot.IsActive)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Bãi đỗ xe '{booking.Lot.Name}' hiện đang đóng.", null);
                }

                var now = DateTime.UtcNow;
                var graceMinutes = BookingStatuses.NoShowGracePeriodMinutes;
                if (booking.StartTime <= now.AddMinutes(-graceMinutes))
                {
                    booking.Status = BookingStatuses.Expired;
                    booking.UpdatedAt = now;
                    await LogAuditAsync(
                        action: "EXPIRE_BOOKING_NO_SHOW",
                        targetEntity: "bookings",
                        targetId: booking.Id.ToString(),
                        oldValues: new { Status = BookingStatuses.Pending },
                        newValues: new { Status = BookingStatuses.Expired, booking.Id, ExpiredAt = now },
                        actorId: actorId,
                        ct: ct);
                    await _context.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return (false, $"Booking đã hết hạn vì quá {graceMinutes} phút kể từ giờ bắt đầu.", null);
                }

                var activePlateSession = await _context.ParkingSessions.AnyAsync(
                    s => s.PlateNumber == normalizedPlate
                        && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);
                if (activePlateSession)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Xe '{normalizedPlate}' đã có phiên gửi đang hoạt động.", null);
                }

                var vehicle = booking.Vehicle;
                var session = new ParkingSession
                {
                    LotId = booking.LotId,
                    VehicleId = vehicle.Id,
                    Vehicle = vehicle,
                    BookingId = booking.Id,
                    Booking = booking,
                    PolicyId = booking.PolicyId,
                    PlateNumber = normalizedPlate,
                    CheckInTime = now,
                    Status = SessionStatuses.Active,
                    VerificationStatus = "WAITING_SLOT_ASSIGNMENT",
                    CheckInImageUrl = databaseImage ? imageUrl : imageUri!.AbsoluteUri,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                booking.Status = BookingStatuses.CheckedIn;
                booking.CheckedInAt = now;
                booking.UpdatedAt = now;
                _context.ParkingSessions.Add(session);

                await LogAuditAsync(
                    action: "CHECK_IN_BOOKING_AT_GATE",
                    targetEntity: "bookings",
                    targetId: booking.Id.ToString(),
                    oldValues: new { Status = BookingStatuses.Pending },
                    newValues: new
                    {
                        BookingId = booking.Id,
                        SessionId = session.Id,
                        session.PlateNumber,
                        session.CheckInTime,
                        session.CheckInImageUrl,
                        BookingStatus = booking.Status,
                        session.VerificationStatus,
                        ActorRole = actorRole,
                        IpAddress = ipAddress
                    },
                    actorId: actorId,
                    ct: ct);
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return (true, "Đã xác nhận xe qua cổng và chuyển sang hàng chờ điều phối.", MapToDto(session, booking.Lot.Name, null, null));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi check-in booking {BookingId} tại cổng", bookingId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> AssignBookingSlotAsync(
        long sessionId,
        AssignBookingSlotRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var lotId = await _context.ParkingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (int?)s.LotId)
            .FirstOrDefaultAsync(ct);
        if (!lotId.HasValue)
            return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
        if (!await _authService.CanOperateSlotVerificationAsync(actorId ?? 0, actorRole, lotId.Value, ct))
            return (false, "Bạn không có quyền điều phối ô đỗ (SLOT_VERIFIER) tại bãi này.", null);

        var lotLock = ParkingLotLockHelper.GetSemaphore(lotId.Value);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                    await _context.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0});",
                        new object[] { ParkingLotLockHelper.LotLockBase + lotId.Value },
                        cancellationToken: ct);

                var session = await _context.ParkingSessions
                    .Include(s => s.Lot)
                    .Include(s => s.Vehicle)
                        .ThenInclude(v => v!.User)
                    .Include(s => s.Booking)
                        .ThenInclude(b => b!.User)
                    .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
                if (session == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
                }
                if (session.Status != SessionStatuses.Active || session.BookingId == null
                    || session.Booking?.Status != BookingStatuses.CheckedIn || session.CurrentSlotId.HasValue)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Phiên xe đặt qua web không còn chờ điều phối ô đỗ.", null);
                }

                var slot = await _context.ParkingSlots
                    .Include(s => s.Floor)
                    .Include(s => s.Zone)
                    .FirstOrDefaultAsync(s => s.Id == request.SlotId, ct);
                if (slot == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy ô đỗ với ID: {request.SlotId}.", null);
                }
                if (!slot.IsActive || !slot.Floor.IsActive || !slot.Zone.IsActive
                    || slot.Floor.LotId != session.LotId || slot.PhysicalStatus != SlotPhysicalStatuses.Empty)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Ô đỗ không còn trống, không hoạt động hoặc không thuộc bãi của booking.", null);
                }
                if (!await _authService.CanOperateAreaAsync(actorId ?? 0, actorRole, session.LotId,
                    StaffDuties.SlotVerifier, slot.FloorId, slot.ZoneId, ct))
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Ô nằm ngoài tầng hoặc khu vực được phân công.", null);
                }

                var slotInUse = await _context.ParkingSessions.AnyAsync(
                    s => s.Id != session.Id && s.CurrentSlotId == slot.Id
                        && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);
                var slotAssigned = await _context.ParkingDispatches.AnyAsync(
                    d => d.AssignedSlotId == slot.Id && d.Status == DispatchStatuses.Assigned, ct);
                if (slotInUse || slotAssigned)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' vừa được một phương tiện khác sử dụng.", null);
                }

                var now = DateTime.UtcNow;
                var booking = session.Booking!;
                session.CurrentSlotId = slot.Id;
                session.CurrentSlot = slot;
                session.ZoneId = slot.ZoneId;
                session.VerificationStatus = "VERIFIED";
                session.VerifiedBy = actorId;
                session.VerifiedAt = now;
                session.UpdatedAt = now;
                booking.Status = BookingStatuses.Parked;
                booking.UpdatedAt = now;
                slot.PhysicalStatus = SlotPhysicalStatuses.Occupied;
                slot.UpdatedAt = now;
                _context.ParkingDispatches.Add(new ParkingDispatch
                {
                    SessionId = session.Id,
                    AssignedSlotId = slot.Id,
                    ActualSlotId = slot.Id,
                    Status = DispatchStatuses.Occupied,
                    DispatchMode = DispatchModes.Manual,
                    DispatchedAt = now,
                    ConfirmedAt = now,
                    Notes = request.Notes?.Trim() ?? "Xe đặt qua web được điều phối vào ô thực tế.",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await LogAuditAsync(
                    action: "ASSIGN_BOOKING_PARKING_SLOT",
                    targetEntity: "bookings",
                    targetId: booking.Id.ToString(),
                    oldValues: new { BookingStatus = BookingStatuses.CheckedIn, SlotPhysicalStatus = SlotPhysicalStatuses.Empty },
                    newValues: new
                    {
                        BookingId = booking.Id,
                        SessionId = session.Id,
                        session.PlateNumber,
                        BookingStatus = booking.Status,
                        SlotId = slot.Id,
                        slot.SlotCode,
                        slot.PhysicalStatus,
                        ActorRole = actorRole,
                        IpAddress = ipAddress
                    },
                    actorId: actorId,
                    ct: ct);
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return (true, $"Đã gán xe {session.PlateNumber} vào ô {slot.SlotCode}.",
                    MapToDto(session, session.Lot.Name, slot.SlotCode, slot.Floor.FloorNumber));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi gán ô đỗ cho phiên booking {SessionId}", sessionId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> CheckOutAsync(
        long sessionId,
        CheckOutRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var session = await _context.ParkingSessions
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
        }

        if (session.Status == SessionStatuses.Completed || session.Status == SessionStatuses.Cancelled)
        {
            return (false, $"Phiên gửi xe này đã kết thúc trước đó với trạng thái: {session.Status}.", null);
        }

        if (!await _authService.CanOperateGateOutAsync(actorId ?? 0, actorRole, session.LotId, ct))
        {
            return (false, "Bạn không có quyền thực hiện nghiệp vụ cổng ra (GATE) tại bãi đỗ xe này.", null);
        }
        if (!await _authService.CanOperateAreaAsync(actorId ?? 0, actorRole, session.LotId, StaffDuties.GateOut,
            session.CurrentSlot?.FloorId, session.ZoneId ?? session.CurrentSlot?.ZoneId, ct))
            return (false, "Phiên xe nằm ngoài tầng hoặc khu vực được phân công.", null);
        if (request.ExitRecognitionId.HasValue)
        {
            // Kết quả nhận diện thuộc Recognition Service: Core chỉ hỏi qua API, không đọc DB của service khác.
            if (_exitRecognition is null)
                return (false, "Chưa cấu hình tra cứu kết quả nhận diện (Recognition Service); cho xe ra không kèm kết quả AI.", null);
            var lookup = await _exitRecognition.FindAsync(request.ExitRecognitionId.Value, ct);
            if (!lookup.ServiceAvailable)
                return (false, "Không xác minh được kết quả AI tại cổng ra vì Recognition Service không phản hồi. Thử lại sau.", null);
            var recognition = lookup.Record;
            if (recognition is null || recognition.Purpose != "EXIT"
                || recognition.ParkingLotId != session.LotId
                || recognition.StaffUserId != actorId
                || recognition.MatchedParkingSessionId != session.Id
                || !PlateNumberHelper.IsMatch(recognition.ConfirmedPlateNumber ?? string.Empty, session.PlateNumber)
                || !recognition.ConfirmedAt.HasValue)
                return (false, "Kết quả AI tại cổng ra chưa được staff xác nhận cho phiên xe này.", null);
        }
        var pendingEntryWorkflow = await _context.StaffVehicleEntries.AnyAsync(x => x.ParkingSessionId == session.Id && x.Status == "PARKING", ct);
        if (pendingEntryWorkflow)
            return (false, "Xe chưa được staff xác nhận đã đỗ; chưa thể checkout.", null);

        // Staff bắt buộc xác nhận biển số tại cổng ra cho MỌI lượt (vé, booking, vãng lai); so khớp đã chuẩn hóa.
        if (string.IsNullOrWhiteSpace(request.ConfirmedPlateNumber))
        {
            return (false, "Cần xác nhận biển số xe tại cổng ra trước khi cho xe ra.", null);
        }
        if (!PlateNumberHelper.IsMatch(request.ConfirmedPlateNumber, session.PlateNumber))
        {
            return (false, $"Biển số xe xác nhận tại cổng ra ({request.ConfirmedPlateNumber.Trim()}) không khớp với phiên gửi xe #{session.Id} (Biển số: {session.PlateNumber}).", null);
        }

        var lotLock = ParkingLotLockHelper.GetSemaphore(session.LotId);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    long lockKey = ParkingLotLockHelper.LotLockBase + session.LotId;
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { lockKey }, cancellationToken: ct);
                }

                // Double-checked locking: Re-check session status within lock to prevent duplicate checkout race
                if (_context is DbContext dbContext)
                {
                    await dbContext.Entry(session).ReloadAsync(ct);
                }
                else
                {
                    var freshStatus = await _context.ParkingSessions
                        .Where(s => s.Id == session.Id)
                        .Select(s => s.Status)
                        .FirstOrDefaultAsync(ct);
                    if (freshStatus != null)
                    {
                        session.Status = freshStatus;
                    }
                }

                if (session.Status == SessionStatuses.Completed || session.Status == SessionStatuses.Cancelled)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Phiên gửi xe này đã kết thúc trước đó với trạng thái: {session.Status}.", null);
                }

                // P0: chỉ cho xe ra khi Payment Service đã xác nhận thanh toán (status PAID, đặt qua
                // RecordPaymentClearanceAsync trusted flow). Kiểm tra lại dưới lock/transaction sau reload.
                // AllowExitWithoutPayment từ client KHÔNG phải bypass; ngoại lệ phí 0 cần contract được duyệt.
                // Ngoại lệ duy nhất: xe vào bằng vé tháng (đã được staff đối chiếu vé còn hiệu lực lúc vào) không phát sinh phí.
                // Giá trị MONTHLY_PASS chỉ được đặt bởi ParkingOperationsService.EnterWithPass.
                var isPassSession = session.VerificationStatus == VerificationStatuses.MonthlyPass;
                if (session.Status != SessionStatuses.Paid && !isPassSession)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Chưa có xác nhận thanh toán (payment clearance) từ Payment Service; không thể cho xe ra hoặc giải phóng ô đỗ.", null);
                }

                var oldStatus = session.Status;
                // Ticketed admissions cannot exit before slot verification and a valid ticket check.
                if (session.EntryEventId.HasValue && !isPassSession)
                {
                    if (session.VerificationStatus != "VERIFIED" || !PlateNumberHelper.IsMatch(request.ConfirmedPlateNumber, session.PlateNumber))
                        return (false, "Cần xác minh xe tại ô và đối chiếu biển số trước khi ra bãi.", null);
                    var exitTicket = await _context.ParkingTickets.FirstOrDefaultAsync(t => t.SessionId == session.Id && t.Status == "ISSUED", ct);
                    if (exitTicket == null || exitTicket.Code != request.TicketCode?.Trim().ToUpperInvariant())
                        return (false, "Vé thiếu, mất, sai hoặc đã thu hồi. Cần xử lý vé trước khi cho xe ra.", null);
                    exitTicket.Status = "REVOKED";
                    exitTicket.ClosedBy = actorId;
                    exitTicket.ClosedAt = DateTime.UtcNow;
                    exitTicket.Reason = "Thu hồi sau khi xác nhận xe thực sự ra bãi.";
                    session.Revision++;
                }
                session.CheckOutTime = DateTime.UtcNow;
                session.Status = SessionStatuses.Completed;
                if (!string.IsNullOrWhiteSpace(request.CheckOutImageUrl))
                {
                    session.CheckOutImageUrl = request.CheckOutImageUrl.Trim();
                }
                session.UpdatedAt = DateTime.UtcNow;

                // Không để dispatch PENDING/ASSIGNED tồn đọng sau khi session COMPLETED
                var activeDispatches = await _context.ParkingDispatches
                    .Where(d => d.SessionId == session.Id && (d.Status == DispatchStatuses.Pending || d.Status == DispatchStatuses.Assigned))
                    .ToListAsync(ct);

                foreach (var d in activeDispatches)
                {
                    d.Status = DispatchStatuses.Cancelled;
                    d.Notes = string.IsNullOrWhiteSpace(d.Notes)
                        ? "[CHECK-OUT] Tự động hủy lệnh điều phối do phiên gửi xe đã hoàn tất check-out."
                        : $"{d.Notes} | [CHECK-OUT] Tự động hủy do phiên check-out.";
                    d.UpdatedAt = DateTime.UtcNow;
                }

                // Giải phóng đúng ô đỗ mà xe thực sự đỗ nếu trước đó ô đỗ đã được ghi nhận OCCUPIED
                var slotToRelease = session.CurrentSlotId.HasValue
                    ? await _context.ParkingSlots.FirstOrDefaultAsync(s => s.Id == session.CurrentSlotId.Value, ct)
                    : null;
                if (slotToRelease != null && _context is DbContext slotContext)
                    await slotContext.Entry(slotToRelease).ReloadAsync(ct);

                if (slotToRelease != null && slotToRelease.PhysicalStatus == SlotPhysicalStatuses.Occupied)
                {
                    slotToRelease.PhysicalStatus = SlotPhysicalStatuses.Empty;
                    slotToRelease.UpdatedAt = DateTime.UtcNow;
                }

                await LogAuditAsync(
                    action: "CHECK_OUT_SESSION",
                    targetEntity: "parking_sessions",
                    targetId: session.Id.ToString(),
                    oldValues: new { session.Id, Status = oldStatus, session.CheckInTime },
                    newValues: new
                    {
                        session.Id,
                        session.Status,
                        session.CheckOutTime,
                        ReleasedSlotId = session.CurrentSlotId,
                        ActorRole = actorRole,
                        IpAddress = ipAddress,
                        ConfirmationType = isPassSession ? "MONTHLY_PASS" : "MANUAL_STAFF_VERIFIED",
                        PassCovered = isPassSession,
                        AllowExitWithoutPayment = request.AllowExitWithoutPayment,
                        ExemptionReason = request.ExemptionReason
                    },
                    actorId: actorId,
                    ct: ct
                );

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                var dto = MapToDto(session, session.Lot?.Name ?? string.Empty, session.CurrentSlot?.SlotCode, session.CurrentSlot?.Floor?.FloorNumber);
                return (true, "Xác nhận xe rời bãi thành công (thao tác thủ công của nhân viên/quản trị viên, chờ tích hợp cảm biến cổng ra).", dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi giao dịch khi check-out phiên gửi xe {SessionId}", sessionId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<PagedResult<ParkingSessionDto>> GetActiveSessionsAsync(
        ParkingSessionFilterDto filter,
        long? currentUserId,
        string currentUserRole,
        CancellationToken ct = default)
    {
        var query = _context.ParkingSessions
            .AsNoTracking()
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .Include(s => s.Vehicle)
                .ThenInclude(v => v!.User)
            .Include(s => s.Booking)
                .ThenInclude(b => b!.User)
            .Where(s => s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid);

        if (currentUserRole == Roles.Admin)
        {
            if (filter.LotId.HasValue)
            {
                query = query.Where(s => s.LotId == filter.LotId.Value);
            }
        }
        else if (currentUserRole == Roles.Manager || currentUserRole == Roles.Staff)
        {
            var assignedLotIds = await _authService.GetAssignedLotIdsAsync(currentUserId ?? 0, currentUserRole, ct);
            query = query.Where(s => assignedLotIds.Contains(s.LotId));

            if (filter.LotId.HasValue)
            {
                if (!assignedLotIds.Contains(filter.LotId.Value))
                {
                    return PagedResult<ParkingSessionDto>.Create([], 0, filter.Page, filter.PageSize);
                }
                query = query.Where(s => s.LotId == filter.LotId.Value);
            }
        }
        else
        {
            if (currentUserId == null)
            {
                return PagedResult<ParkingSessionDto>.Create([], 0, filter.Page, filter.PageSize);
            }
            query = query.Where(s => s.Vehicle != null && s.Vehicle.UserId == currentUserId.Value);

            if (filter.LotId.HasValue)
            {
                query = query.Where(s => s.LotId == filter.LotId.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.PlateNumber))
        {
            var normalizedPlate = PlateNumberHelper.Normalize(filter.PlateNumber);
            query = query.Where(s => s.PlateNumber.Contains(normalizedPlate));
        }

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(s => s.CheckInTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ParkingSessionDto
            {
                Id = s.Id,
                LotId = s.LotId,
                LotName = s.Lot.Name,
                VehicleId = s.VehicleId,
                PlateNumber = s.PlateNumber,
                BookingId = s.BookingId,
                BookingStatus = s.Booking == null ? null : s.Booking.Status,
                CustomerName = s.Booking != null
                    ? s.Booking.User.FullName
                    : s.Vehicle != null && s.Vehicle.User != null ? s.Vehicle.User.FullName : null,
                CustomerPhoneNumber = s.Booking != null
                    ? s.Booking.User.PhoneNumber
                    : s.Vehicle != null && s.Vehicle.User != null ? s.Vehicle.User.PhoneNumber : null,
                CustomerEmail = s.Booking != null
                    ? s.Booking.User.Email
                    : s.Vehicle != null && s.Vehicle.User != null ? s.Vehicle.User.Email : null,
                VehicleBrand = s.Vehicle == null ? null : s.Vehicle.Brand,
                VehicleModel = s.Vehicle == null ? null : s.Vehicle.Model,
                VehicleColor = s.Vehicle == null ? null : s.Vehicle.Color,
                CheckInTime = s.CheckInTime,
                CheckOutTime = s.CheckOutTime,
                CurrentSlotId = s.CurrentSlotId,
                SlotCode = s.CurrentSlot != null ? s.CurrentSlot.SlotCode : null,
                SlotPhysicalStatus = s.CurrentSlot != null ? s.CurrentSlot.PhysicalStatus : null,
                FloorNumber = s.CurrentSlot != null ? s.CurrentSlot.Floor.FloorNumber : (int?)null,
                Status = s.Status,
                TotalFee = s.TotalFee,
                PaidAt = s.PaidAt,
                CheckInImageUrl = s.CheckInImageUrl,
                CheckOutImageUrl = s.CheckOutImageUrl,
                DurationMinutes = (DateTime.UtcNow - s.CheckInTime).TotalMinutes,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(ct);

        return PagedResult<ParkingSessionDto>.Create(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<ParkingSessionDto>> GetUserSessionsAsync(
        long userId,
        ParkingSessionFilterDto filter,
        CancellationToken ct = default)
    {
        var query = _context.ParkingSessions
            .AsNoTracking()
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .Include(s => s.Vehicle)
            .Where(s => s.Vehicle != null && s.Vehicle.UserId == userId);

        if (filter.LotId.HasValue)
        {
            query = query.Where(s => s.LotId == filter.LotId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            query = query.Where(s => s.Status == filter.Status.ToUpperInvariant());
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(s => s.CheckInTime >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(s => s.CheckInTime <= filter.ToDate.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(s => s.CheckInTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new ParkingSessionDto
            {
                Id = s.Id,
                LotId = s.LotId,
                LotName = s.Lot.Name,
                VehicleId = s.VehicleId,
                PlateNumber = s.PlateNumber,
                CheckInTime = s.CheckInTime,
                CheckOutTime = s.CheckOutTime,
                CurrentSlotId = s.CurrentSlotId,
                SlotCode = s.CurrentSlot != null ? s.CurrentSlot.SlotCode : null,
                FloorNumber = s.CurrentSlot != null ? s.CurrentSlot.Floor.FloorNumber : (int?)null,
                Status = s.Status,
                TotalFee = s.TotalFee,
                PaidAt = s.PaidAt,
                CheckInImageUrl = s.CheckInImageUrl,
                CheckOutImageUrl = s.CheckOutImageUrl,
                DurationMinutes = ((s.CheckOutTime ?? DateTime.UtcNow) - s.CheckInTime).TotalMinutes,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(ct);

        return PagedResult<ParkingSessionDto>.Create(items, totalCount, page, pageSize);
    }

    public async Task<ParkingSessionDto?> GetSessionByIdAsync(long id, long? currentUserId, string currentUserRole, CancellationToken ct = default)
    {
        var query = _context.ParkingSessions
            .AsNoTracking()
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .Include(s => s.Vehicle)
                .ThenInclude(v => v!.User)
            .Include(s => s.Booking)
                .ThenInclude(b => b!.User)
            .Where(s => s.Id == id);

        var session = await query.FirstOrDefaultAsync(ct);
        if (session == null)
        {
            return null;
        }

        if (currentUserRole != Roles.Admin)
        {
            if (currentUserRole == Roles.Manager || currentUserRole == Roles.Staff)
            {
                var hasAccess = await _authService.HasLotAccessAsync(currentUserId ?? 0, currentUserRole, session.LotId, ct);
                if (!hasAccess)
                {
                    return null;
                }
            }
            else
            {
                if (currentUserId == null || session.Vehicle == null || session.Vehicle.UserId != currentUserId.Value)
                {
                    return null;
                }
            }
        }

        return MapToDto(session, session.Lot?.Name ?? string.Empty, session.CurrentSlot?.SlotCode, session.CurrentSlot?.Floor?.FloorNumber);
    }

    public async Task<ParkingSessionDto?> GetActiveSessionByPlateAsync(string plateNumber, long? currentUserId, string currentUserRole, CancellationToken ct = default)
    {
        var normalizedPlate = PlateNumberHelper.Normalize(plateNumber);
        if (string.IsNullOrWhiteSpace(normalizedPlate))
        {
            return null;
        }

        var query = _context.ParkingSessions
            .AsNoTracking()
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .Include(s => s.Vehicle)
            .Where(s => s.PlateNumber == normalizedPlate && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid));

        var session = await query.FirstOrDefaultAsync(ct);
        if (session == null)
        {
            return null;
        }

        if (currentUserRole != Roles.Admin)
        {
            if (currentUserRole == Roles.Manager || currentUserRole == Roles.Staff)
            {
                var hasAccess = await _authService.HasLotAccessAsync(currentUserId ?? 0, currentUserRole, session.LotId, ct);
                if (!hasAccess)
                {
                    return null;
                }
            }
            else
            {
                if (currentUserId == null || session.Vehicle == null || session.Vehicle.UserId != currentUserId.Value)
                {
                    return null;
                }
            }
        }

        return MapToDto(session, session.Lot?.Name ?? string.Empty, session.CurrentSlot?.SlotCode, session.CurrentSlot?.Floor?.FloorNumber);
    }

    public async Task<IReadOnlyList<UnmatchedSessionDto>> GetUnmatchedSessionsAsync(
        int lotId,
        long? actorId,
        string actorRole,
        CancellationToken ct = default)
    {
        if (!await _authService.CanOperateSlotVerificationAsync(actorId ?? 0, actorRole, lotId, ct))
        {
            return Array.Empty<UnmatchedSessionDto>();
        }

        var sessions = await _context.ParkingSessions
            .AsNoTracking()
            .Include(s => s.Lot)
            .Include(s => s.Booking)
                .ThenInclude(b => b!.User)
            .Where(s => s.LotId == lotId && s.Status == SessionStatuses.Active && s.CurrentSlotId == null)
            .OrderBy(s => s.CheckInTime)
            .Select(s => new UnmatchedSessionDto
            {
                SessionId = s.Id,
                LotId = s.LotId,
                LotName = s.Lot.Name,
                PlateNumber = s.PlateNumber,
                CheckInTime = s.CheckInTime,
                Status = s.Status,
                CheckInImageUrl = s.CheckInImageUrl,
                BookingId = s.BookingId,
                CustomerName = s.Booking == null ? null : s.Booking.User.FullName,
                CustomerPhoneNumber = s.Booking == null ? null : s.Booking.User.PhoneNumber
            })
            .ToListAsync(ct);

        return sessions;
    }

    public async Task<IReadOnlyList<UnmatchedSlotDto>> GetUnmatchedOccupiedSlotsAsync(
        int lotId,
        long? actorId,
        string actorRole,
        CancellationToken ct = default)
    {
        if (!await _authService.CanOperateSlotVerificationAsync(actorId ?? 0, actorRole, lotId, ct))
        {
            return Array.Empty<UnmatchedSlotDto>();
        }

        var assignedSlotIds = await _context.ParkingSessions
            .AsNoTracking()
            .Where(s => s.LotId == lotId && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid) && s.CurrentSlotId != null)
            .Select(s => s.CurrentSlotId!.Value)
            .ToListAsync(ct);

        var slots = await _context.ParkingSlots
            .AsNoTracking()
            .Include(sl => sl.Floor)
                .ThenInclude(f => f.Lot)
            .Where(sl => sl.Floor.LotId == lotId && sl.PhysicalStatus == SlotPhysicalStatuses.Occupied && !assignedSlotIds.Contains(sl.Id))
            .OrderBy(sl => sl.Floor.FloorNumber)
            .ThenBy(sl => sl.SlotCode)
            .Select(sl => new UnmatchedSlotDto
            {
                SlotId = sl.Id,
                LotId = sl.Floor.LotId,
                LotName = sl.Floor.Lot.Name,
                SlotCode = sl.SlotCode,
                FloorNumber = sl.Floor.FloorNumber,
                PhysicalStatus = sl.PhysicalStatus,
                LastStatusChangedAt = sl.UpdatedAt
            })
            .ToListAsync(ct);

        return slots;
    }

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> ConfirmBookingParkedAsync(
        long bookingId,
        ConfirmBookingParkedRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var normalizedPlate = PlateNumberHelper.Normalize(request.PlateNumber);
        if (!PlateNumberHelper.IsValidPlateFormat(normalizedPlate))
            return (false, "Biển số ô tô không đúng định dạng. Ví dụ hợp lệ: 51F-123.45.", null);
        var imageUrl = request.CheckInImageUrl?.Trim() ?? "";
        var databaseImage = imageUrl.StartsWith("/api/v1/images/content/", StringComparison.Ordinal)
            && Guid.TryParseExact(imageUrl["/api/v1/images/content/".Length..], "N", out _);
        var externalImage = Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri)
            && (imageUri.Scheme == Uri.UriSchemeHttps || imageUri.Scheme == Uri.UriSchemeHttp);
        if (!databaseImage && !externalImage)
            return (false, "Ảnh biển số xe chưa được lưu hợp lệ.", null);
        if (databaseImage && !await _context.StoredImages.AnyAsync(
                image => image.Url == imageUrl && image.Content != null && image.UserId == actorId, ct))
            return (false, "Ảnh biển số chưa được lưu hoặc không thuộc nhân viên xác nhận.", null);

        var bookingLotId = await _context.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => (int?)b.LotId)
            .FirstOrDefaultAsync(ct);
        if (!bookingLotId.HasValue)
            return (false, $"Không tìm thấy booking với ID: {bookingId}.", null);

        if (!await _authService.CanOperateSlotVerificationAsync(actorId ?? 0, actorRole, bookingLotId.Value, ct))
            return (false, "Bạn không có quyền xác minh xe và ô đỗ (SLOT_VERIFIER) tại bãi này.", null);

        var lotLock = ParkingLotLockHelper.GetSemaphore(bookingLotId.Value);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    long lockKey = ParkingLotLockHelper.LotLockBase + bookingLotId.Value;
                    await _context.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock({0});",
                        new object[] { lockKey },
                        cancellationToken: ct);
                }

                var booking = await _context.Bookings
                    .Include(b => b.Lot)
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
                if (booking == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy booking với ID: {bookingId}.", null);
                }
                if (booking.Status != BookingStatuses.Pending)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Booking #{booking.Id} không còn chờ xác minh xe (trạng thái: {booking.Status}).", null);
                }
                if (!PlateNumberHelper.IsMatch(booking.PlateNumber, normalizedPlate))
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Biển số xe thực tế không khớp với booking. Không thể xác nhận xe.", null);
                }
                normalizedPlate = PlateNumberHelper.Normalize(booking.PlateNumber);
                if (!booking.Lot.IsActive)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Bãi đỗ xe '{booking.Lot.Name}' hiện đang đóng.", null);
                }

                var now = DateTime.UtcNow;
                var graceMinutes = BookingStatuses.NoShowGracePeriodMinutes;
                if (booking.StartTime <= now.AddMinutes(-graceMinutes))
                {
                    booking.Status = BookingStatuses.Expired;
                    booking.UpdatedAt = now;
                    _context.AuditLogs.Add(new AuditLog
                    {
                        Action = "EXPIRE_BOOKING_NO_SHOW",
                        TargetEntity = "bookings",
                        TargetId = booking.Id.ToString(),
                        OldValues = JsonSerializer.Serialize(new { Status = BookingStatuses.Pending }, JsonOptions),
                        NewValues = JsonSerializer.Serialize(new
                        {
                            Status = BookingStatuses.Expired,
                            Reason = $"Không xác nhận xe trong vòng {graceMinutes} phút kể từ giờ bắt đầu.",
                            ExpiredAt = now
                        }, JsonOptions),
                        CreatedAt = now
                    });
                    await _context.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return (false, $"Booking đã hết hạn vì quá {graceMinutes} phút kể từ giờ bắt đầu.", null);
                }

                var activePlateSession = await _context.ParkingSessions
                    .AnyAsync(s => s.PlateNumber == normalizedPlate
                        && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);
                if (activePlateSession)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Xe '{normalizedPlate}' đã có phiên gửi đang hoạt động.", null);
                }

                var slot = await _context.ParkingSlots
                    .Include(s => s.Floor)
                    .FirstOrDefaultAsync(s => s.Id == request.SlotId, ct);
                if (slot == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy ô đỗ với ID: {request.SlotId}.", null);
                }
                if (!slot.IsActive || !slot.Floor.IsActive || slot.Floor.LotId != booking.LotId)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Ô đỗ không hoạt động hoặc không thuộc bãi của booking.", null);
                }
                if (slot.PhysicalStatus is SlotPhysicalStatuses.Maintenance or SlotPhysicalStatuses.Unavailable or SlotPhysicalStatuses.Reserved)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' đang bảo trì hoặc không khả dụng.", null);
                }

                if (!await _authService.CanOperateAreaAsync(actorId ?? 0, actorRole, booking.LotId,
                    StaffDuties.SlotVerifier, slot.FloorId, slot.ZoneId, ct))
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Ô nằm ngoài tầng hoặc khu vực được phân công.", null);
                }

                var slotSession = await _context.ParkingSessions
                    .FirstOrDefaultAsync(s => s.CurrentSlotId == slot.Id
                        && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);
                if (slotSession != null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' đang được xe {slotSession.PlateNumber} sử dụng.", null);
                }
                var assignedDispatch = await _context.ParkingDispatches
                    .AnyAsync(d => d.AssignedSlotId == slot.Id
                        && d.Status == DispatchStatuses.Assigned, ct);
                if (assignedDispatch)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' đã được phân cho xe khác.", null);
                }

                var vehicle = await _context.Vehicles
                    .Include(v => v.User)
                    .FirstOrDefaultAsync(v => v.Id == booking.VehicleId, ct);
                if (vehicle == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, "Không tìm thấy phương tiện của booking.", null);
                }

                var bookingStatusBefore = booking.Status;
                var physicalStatusBefore = slot.PhysicalStatus;
                var session = new ParkingSession
                {
                    LotId = booking.LotId,
                    VehicleId = vehicle.Id,
                    Vehicle = vehicle,
                    BookingId = booking.Id,
                    Booking = booking,
                    PolicyId = booking.PolicyId,
                    ZoneId = slot.ZoneId,
                    VerificationStatus = "VERIFIED",
                    VerifiedBy = actorId,
                    VerifiedAt = now,
                    PlateNumber = normalizedPlate,
                    CheckInTime = now,
                    CurrentSlotId = slot.Id,
                    CurrentSlot = slot,
                    Status = SessionStatuses.Active,
                    CheckInImageUrl = databaseImage ? imageUrl : imageUri!.AbsoluteUri,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                booking.Status = BookingStatuses.Parked;
                booking.CheckedInAt = now;
                booking.UpdatedAt = now;
                slot.PhysicalStatus = SlotPhysicalStatuses.Occupied;
                slot.UpdatedAt = now;
                _context.ParkingSessions.Add(session);
                _context.ParkingDispatches.Add(new ParkingDispatch
                {
                    Session = session,
                    AssignedSlotId = slot.Id,
                    ActualSlotId = slot.Id,
                    Status = DispatchStatuses.Occupied,
                    DispatchMode = DispatchModes.Manual,
                    DispatchedAt = now,
                    ConfirmedAt = now,
                    Notes = "Staff xác nhận booking và xe đã đỗ tại ô thực tế.",
                    CreatedAt = now,
                    UpdatedAt = now
                });

                await LogAuditAsync(
                    action: "CONFIRM_BOOKING_PARKED",
                    targetEntity: "bookings",
                    targetId: booking.Id.ToString(),
                    oldValues: new
                    {
                        Status = bookingStatusBefore,
                        SlotPhysicalStatus = physicalStatusBefore
                    },
                    newValues: new
                    {
                        BookingId = booking.Id,
                        session.Id,
                        session.PlateNumber,
                        SlotId = slot.Id,
                        slot.SlotCode,
                        FloorNumber = slot.Floor.FloorNumber,
                        BookingStatus = booking.Status,
                        PhysicalStatus = slot.PhysicalStatus,
                        session.CheckInImageUrl,
                        ActorRole = actorRole,
                        IpAddress = ipAddress
                    },
                    actorId: actorId,
                    ct: ct);

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                var dto = MapToDto(session, booking.Lot.Name, slot.SlotCode, slot.Floor.FloorNumber);
                return (true, $"Đã xác nhận xe '{session.PlateNumber}' đỗ tại ô '{slot.SlotCode}'.", dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi xác nhận booking {BookingId} đã đỗ tại ô {SlotId}", bookingId, request.SlotId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> VerifySessionSlotAsync(
        long sessionId,
        VerifySessionSlotRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var session = await _context.ParkingSessions
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
        }

        if (session.EntryEventId.HasValue)
            return (false, "Lượt có vé phải dùng API operations để xác minh xe, nhiên liệu và ô thực tế.", null);
        if (session.Status != SessionStatuses.Active)
        {
            return (false, $"Chỉ có thể xác minh ô đỗ cho phiên gửi xe đang hoạt động (ACTIVE). Trạng thái hiện tại: {session.Status}.", null);
        }

        if (!await _authService.CanOperateSlotVerificationAsync(actorId ?? 0, actorRole, session.LotId, ct))
        {
            return (false, "Bạn không có quyền thực hiện nghiệp vụ xác minh ô đỗ (SLOT_VERIFIER) tại bãi đỗ xe này.", null);
        }

        var lotLock = ParkingLotLockHelper.GetSemaphore(session.LotId);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    long lockKey = ParkingLotLockHelper.LotLockBase + session.LotId;
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { lockKey }, cancellationToken: ct);
                }

                var slot = await _context.ParkingSlots
                    .Include(sl => sl.Floor)
                    .FirstOrDefaultAsync(sl => sl.Id == request.SlotId, ct);

                if (slot == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy ô đỗ với ID: {request.SlotId}.", null);
                }

                if (!slot.IsActive)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' hiện đang tạm ngừng hoạt động.", null);
                }

                if (slot.Floor.LotId != session.LotId)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' không thuộc cùng bãi đỗ xe với phiên gửi xe này.", null);
                }

                if (slot.PhysicalStatus == SlotPhysicalStatuses.Maintenance || slot.PhysicalStatus == SlotPhysicalStatuses.Unavailable)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' hiện đang bảo trì hoặc không khả dụng ({slot.PhysicalStatus}). Không thể xác minh ô đỗ.", null);
                }

                var conflictingSession = await _context.ParkingSessions
                    .FirstOrDefaultAsync(s => s.Id != session.Id && s.CurrentSlotId == slot.Id && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);

                if (conflictingSession != null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ '{slot.SlotCode}' hiện đang được liên kết với phiên gửi xe #{conflictingSession.Id} (Biển số: {conflictingSession.PlateNumber}). Không thể gán trùng lặp.", null);
                }

                var oldSlotId = session.CurrentSlotId;
                var previousSensorStatus = slot.PhysicalStatus;
                slot.PhysicalStatus = SlotPhysicalStatuses.Occupied;
                slot.UpdatedAt = DateTime.UtcNow;
                session.CurrentSlotId = slot.Id;
                session.UpdatedAt = DateTime.UtcNow;

                var dispatch = await _context.ParkingDispatches
                    .Where(d => d.SessionId == session.Id && (d.Status == DispatchStatuses.Pending || d.Status == DispatchStatuses.Assigned))
                    .OrderByDescending(d => d.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (dispatch != null)
                {
                    dispatch.ActualSlotId = slot.Id;
                    dispatch.Status = DispatchStatuses.Occupied;
                    dispatch.ConfirmedAt = DateTime.UtcNow;
                    dispatch.Notes = string.IsNullOrWhiteSpace(request.Notes)
                        ? dispatch.Notes
                        : (string.IsNullOrWhiteSpace(dispatch.Notes) ? request.Notes : $"{dispatch.Notes} | {request.Notes}");
                    dispatch.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    dispatch = new ParkingDispatch
                    {
                        SessionId = session.Id,
                        AssignedSlotId = slot.Id,
                        ActualSlotId = slot.Id,
                        Status = DispatchStatuses.Occupied,
                        DispatchMode = DispatchModes.Manual,
                        DispatchedAt = DateTime.UtcNow,
                        ConfirmedAt = DateTime.UtcNow,
                        Notes = request.Notes ?? "Xác minh khớp phiên và ô đỗ bởi nhân viên kiểm tra ô.",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.ParkingDispatches.Add(dispatch);
                }

                await LogAuditAsync(
                    action: "VERIFY_SESSION_SLOT",
                    targetEntity: "parking_sessions",
                    targetId: session.Id.ToString(),
                    oldValues: new
                    {
                        session.Id,
                        PreviousSlotId = oldSlotId,
                        SensorPhysicalStatusBeforeVerification = previousSensorStatus
                    },
                    newValues: new
                    {
                        session.Id,
                        VerifiedSlotId = slot.Id,
                        slot.SlotCode,
                        FloorNumber = slot.Floor?.FloorNumber,
                        SensorStatusBeforeVerification = previousSensorStatus,
                        PhysicalStatusAfterVerification = slot.PhysicalStatus,
                        DiscrepancyWithSensor = previousSensorStatus != SlotPhysicalStatuses.Occupied,
                        ActorRole = actorRole,
                        IpAddress = ipAddress,
                        Notes = request.Notes
                    },
                    actorId: actorId,
                    ct: ct
                );

                await _context.SaveChangesAsync(ct);
                var entryWorkflow = await _context.StaffVehicleEntries
                    .Where(x => x.ParkingSessionId == session.Id && x.Status == "PARKING")
                    .OrderByDescending(x => x.StartedAt)
                    .FirstOrDefaultAsync(ct);
                if (entryWorkflow is not null)
                {
                    entryWorkflow.Status = "CLOSED_PARKED";
                    entryWorkflow.ParkedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                var dto = MapToDto(session, session.Lot?.Name ?? string.Empty, slot.SlotCode, slot.Floor?.FloorNumber);
                return (true, $"Xác nhận liên kết xe '{session.PlateNumber}' vào ô đỗ '{slot.SlotCode}' thành công.", dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi giao dịch khi xác minh ô đỗ cho phiên {SessionId}", sessionId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> ReportSessionWrongSlotAsync(
        long sessionId,
        ReportWrongSlotSessionRequestDto request,
        long? actorId,
        string actorRole,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var session = await _context.ParkingSessions
            .Include(s => s.Lot)
            .Include(s => s.CurrentSlot)
                .ThenInclude(sl => sl!.Floor)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

        if (session == null)
        {
            return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
        }

        if (session.EntryEventId.HasValue)
            return (false, "Lượt có vé phải dùng API operations để xác minh xe, nhiên liệu và ô thực tế.", null);
        if (session.Status != SessionStatuses.Active)
        {
            return (false, $"Chỉ có thể báo sai ô đỗ cho phiên gửi xe đang hoạt động (ACTIVE). Trạng thái hiện tại: {session.Status}.", null);
        }

        if (!await _authService.CanOperateSlotVerificationAsync(actorId ?? 0, actorRole, session.LotId, ct))
        {
            return (false, "Bạn không có quyền thực hiện nghiệp vụ kiểm tra ô đỗ (SLOT_VERIFIER) tại bãi đỗ xe này.", null);
        }

        var lotLock = ParkingLotLockHelper.GetSemaphore(session.LotId);
        await lotLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                {
                    long lockKey = ParkingLotLockHelper.LotLockBase + session.LotId;
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { lockKey }, cancellationToken: ct);
                }

                var actualSlot = await _context.ParkingSlots
                    .Include(sl => sl.Floor)
                    .FirstOrDefaultAsync(sl => sl.Id == request.ActualSlotId, ct);

                if (actualSlot == null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Không tìm thấy ô đỗ thực tế với ID: {request.ActualSlotId}.", null);
                }

                if (!actualSlot.IsActive)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ thực tế '{actualSlot.SlotCode}' hiện đang tạm ngừng hoạt động.", null);
                }

                if (actualSlot.Floor.LotId != session.LotId)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ thực tế '{actualSlot.SlotCode}' không thuộc cùng bãi đỗ xe.", null);
                }

                if (actualSlot.PhysicalStatus == SlotPhysicalStatuses.Maintenance || actualSlot.PhysicalStatus == SlotPhysicalStatuses.Unavailable)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ thực tế '{actualSlot.SlotCode}' hiện đang bảo trì hoặc không khả dụng ({actualSlot.PhysicalStatus}). Không thể chuyển xe vào ô này.", null);
                }

                var conflictingSession = await _context.ParkingSessions
                    .FirstOrDefaultAsync(s => s.Id != session.Id && s.CurrentSlotId == actualSlot.Id && (s.Status == SessionStatuses.Active || s.Status == SessionStatuses.Paid), ct);

                if (conflictingSession != null)
                {
                    await transaction.RollbackAsync(ct);
                    return (false, $"Ô đỗ thực tế '{actualSlot.SlotCode}' đang được liên kết với phiên #{conflictingSession.Id} (Biển số: {conflictingSession.PlateNumber}). Vui lòng xử lý xung đột của phiên này trước.", null);
                }

                var previousActualSlotStatus = actualSlot.PhysicalStatus;
                actualSlot.PhysicalStatus = SlotPhysicalStatuses.Occupied;
                actualSlot.UpdatedAt = DateTime.UtcNow;

                var oldSlotId = session.CurrentSlotId;
                ParkingSlot? oldSlot = null;
                string? conflictNote = null;
                if (oldSlotId.HasValue && oldSlotId.Value != actualSlot.Id)
                {
                    oldSlot = await _context.ParkingSlots.FirstOrDefaultAsync(sl => sl.Id == oldSlotId.Value, ct);
                    if (oldSlot != null)
                    {
                        // Invariant: Không tự ý đặt ô cũ EMPTY khi chưa có xác nhận/telemetry vật lý ô đã trống.
                        // Ô cũ giữ nguyên PhysicalStatus, ghi nhận conflict/audit trail chờ cảm biến hoặc nhân viên xác nhận thực tế.
                        conflictNote = $"Phiên #{session.Id} chuyển sang ô {actualSlot.SlotCode}. Ô cũ {oldSlot.SlotCode} giữ nguyên trạng thái vật lý '{oldSlot.PhysicalStatus}' chờ đối soát thực địa.";
                        oldSlot.UpdatedAt = DateTime.UtcNow;
                    }
                }

                session.CurrentSlotId = actualSlot.Id;
                session.UpdatedAt = DateTime.UtcNow;

                var dispatch = await _context.ParkingDispatches
                    .Where(d => d.SessionId == session.Id && (d.Status == DispatchStatuses.Pending || d.Status == DispatchStatuses.Assigned || d.Status == DispatchStatuses.Occupied))
                    .OrderByDescending(d => d.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (dispatch != null)
                {
                    dispatch.ActualSlotId = actualSlot.Id;
                    dispatch.Status = DispatchStatuses.WrongSlot;
                    dispatch.ConfirmedAt = DateTime.UtcNow;
                    dispatch.Notes = string.IsNullOrWhiteSpace(request.Reason)
                        ? $"{dispatch.Notes} | [WRONG_SLOT] Phát hiện đỗ sai ô, điều chỉnh sang {actualSlot.SlotCode}"
                        : $"{dispatch.Notes} | [WRONG_SLOT] {request.Reason} (Chuyển sang {actualSlot.SlotCode})";
                    dispatch.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    dispatch = new ParkingDispatch
                    {
                        SessionId = session.Id,
                        AssignedSlotId = actualSlot.Id,
                        ActualSlotId = actualSlot.Id,
                        Status = DispatchStatuses.WrongSlot,
                        DispatchMode = DispatchModes.Manual,
                        DispatchedAt = DateTime.UtcNow,
                        ConfirmedAt = DateTime.UtcNow,
                        Notes = $"[WRONG_SLOT] Phát hiện đỗ sai ô: {request.Reason ?? "Không có lý do chi tiết"}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.ParkingDispatches.Add(dispatch);
                }

                await LogAuditAsync(
                    action: "REPORT_SESSION_WRONG_SLOT",
                    targetEntity: "parking_sessions",
                    targetId: session.Id.ToString(),
                    oldValues: new
                    {
                        session.Id,
                        PreviousSlotId = oldSlotId,
                        PreviousSlotCode = oldSlot?.SlotCode,
                        PreviousActualSlotPhysicalStatus = previousActualSlotStatus,
                        PreviousSlotPhysicalStatus = oldSlot?.PhysicalStatus
                    },
                    newValues: new
                    {
                        session.Id,
                        ActualSlotId = actualSlot.Id,
                        actualSlot.SlotCode,
                        SensorStatusBeforeVerification = previousActualSlotStatus,
                        PhysicalStatusAfterVerification = actualSlot.PhysicalStatus,
                        DiscrepancyWithSensor = previousActualSlotStatus != SlotPhysicalStatuses.Occupied,
                        Reason = request.Reason,
                        ConflictNote = conflictNote,
                        ActorRole = actorRole,
                        IpAddress = ipAddress
                    },
                    actorId: actorId,
                    ct: ct
                );

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                var dto = MapToDto(session, session.Lot?.Name ?? string.Empty, actualSlot.SlotCode, actualSlot.Floor?.FloorNumber);
                return (true, $"Cập nhật đỗ sai ô cho xe '{session.PlateNumber}' sang ô thực tế '{actualSlot.SlotCode}' thành công.", dto);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Lỗi giao dịch khi báo sai ô đỗ cho phiên {SessionId}", sessionId);
                throw;
            }
        }
        finally
        {
            lotLock.Release();
        }
    }


    private static ParkingSessionDto MapToDto(ParkingSession session, string lotName, string? slotCode, int? floorNumber)
    {
        var duration = ((session.CheckOutTime ?? DateTime.UtcNow) - session.CheckInTime).TotalMinutes;

        return new ParkingSessionDto
        {
            Id = session.Id,
            LotId = session.LotId,
            LotName = lotName,
            VehicleId = session.VehicleId,
            BookingId = session.BookingId,
            BookingStatus = session.Booking?.Status,
            CustomerName = session.Booking?.User?.FullName ?? session.Vehicle?.User?.FullName,
            CustomerPhoneNumber = session.Booking?.User?.PhoneNumber ?? session.Vehicle?.User?.PhoneNumber,
            CustomerEmail = session.Booking?.User?.Email ?? session.Vehicle?.User?.Email,
            VehicleBrand = session.Vehicle?.Brand,
            VehicleModel = session.Vehicle?.Model,
            VehicleColor = session.Vehicle?.Color,
            PlateNumber = session.PlateNumber,
            VerificationStatus = session.VerificationStatus,
            ZoneId = session.ZoneId,
            PolicyId = session.PolicyId,
            Revision = session.Revision,
            CheckInTime = session.CheckInTime,
            CheckOutTime = session.CheckOutTime,
            CurrentSlotId = session.CurrentSlotId,
            SlotCode = slotCode,
            SlotPhysicalStatus = session.CurrentSlot?.PhysicalStatus,
            FloorNumber = floorNumber,
            Status = session.Status,
            TotalFee = session.TotalFee,
            PaidAt = session.PaidAt,
            CheckInImageUrl = session.CheckInImageUrl,
            CheckOutImageUrl = session.CheckOutImageUrl,
            DurationMinutes = Math.Round(duration, 2),
            CreatedAt = session.CreatedAt
        };
    }

    public async Task<PaymentBillingContextDto?> GetPaymentBillingContextAsync(long sessionId, CancellationToken ct = default) =>
        await _context.ParkingSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new PaymentBillingContextDto(
                s.Id, s.LotId, s.ZoneId, s.PolicyId, s.BookingId, s.PlateNumber, s.Status, s.VerificationStatus,
                s.EntryEventId != null, s.CheckInTime, s.VerifiedAt, s.CheckOutTime, s.TotalFee, s.PaidAt, DateTime.UtcNow))
            .FirstOrDefaultAsync(ct);

    public async Task<(bool Success, string Message, ParkingSessionDto? Data)> RecordPaymentClearanceAsync(
        long sessionId,
        decimal amountPaid,
        string transactionRef,
        string? paymentMethod,
        long? actorId,
        string actorRole,
        string? ipAddress,
        bool isTrustedPaymentServerFlow = false,
        CancellationToken ct = default,
        decimal? totalFee = null)
    {
        // 0. Fail-Closed Boundary: Không chấp nhận thanh toán tự khai báo từ client / người dùng / staff / admin.
        // Bắt buộc phải đến từ luồng Server-to-Server đáng tin cậy (Trusted Server Flow) của module Payment do Nam Anh sở hữu.
        if (!isTrustedPaymentServerFlow)
        {
            return (false, "PAYMENT INTEGRATION BLOCKED: Luồng xác nhận thanh toán phía máy chủ (Server-side Trusted Payment Flow) của Nam Anh chưa được tích hợp. Không chấp nhận yêu cầu thanh toán tự khai báo từ client hoặc tài khoản người dùng/nhân viên/quản trị viên.", null);
        }

        // 1. Phân quyền: Chỉ Quản trị viên (ADMIN) hoặc Dịch vụ Thanh toán hệ thống mới có quyền ghi nhận xác nhận tài chính
        if (actorRole != Roles.Admin && actorRole != SmartParking.Application.Common.Security.PaymentServiceSignature.ServiceActorRole)
        {
            return (false, "Chỉ Quản trị viên (ADMIN) hoặc Dịch vụ Thanh toán của hệ thống mới có quyền ghi nhận xác nhận tài chính (Financial Clearance). Nhân viên cổng hoặc tài khoản khác không có thẩm quyền.", null);
        }

        // 2. Kiểm tra số tiền thanh toán hợp lệ (> 0)
        if (amountPaid < 0 || (amountPaid == 0 && (!isTrustedPaymentServerFlow || !totalFee.HasValue)))
        {
            return (false, "Số tiền thanh toán phải hợp lệ theo hóa đơn Payment Service.", null);
        }

        if (string.IsNullOrWhiteSpace(transactionRef))
        {
            return (false, "Mã giao dịch thanh toán (transactionRef) không được để trống.", null);
        }

        var trimmedRef = transactionRef.Trim();
        if (trimmedRef.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(trimmedRef, @"^[A-Za-z0-9._:\-]+$"))
        {
            return (false, "Mã giao dịch thanh toán (transactionRef) chỉ gồm chữ, số, '.', '_', ':', '-' và tối đa 100 ký tự.", null);
        }

        var lotId = await _context.ParkingSessions.Where(s => s.Id == sessionId).Select(s => (int?)s.LotId).FirstOrDefaultAsync(ct);
        if (lotId == null)
        {
            return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
        }

        // Thứ tự khóa cố định: (1) khóa clearance toàn cục (ref là duy nhất xuyên bãi; DB chưa có unique constraint
        // vì chưa được duyệt migration) -> (2) khóa bãi (cùng khóa với checkout/verify). Checkout chỉ lấy khóa bãi nên không deadlock.
        await ClearanceGate.WaitAsync(ct);
        try
        {
            var lotLock = ParkingLotLockHelper.GetSemaphore(lotId.Value);
            await lotLock.WaitAsync(ct);
            try
            {
                await using var transaction = await _context.BeginTransactionAsync(ct);
                try
                {
                    if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { PaymentClearanceLockKey }, cancellationToken: ct);
                        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0});", new object[] { ParkingLotLockHelper.LotLockBase + lotId.Value }, cancellationToken: ct);
                    }

                    var session = await _context.ParkingSessions
                        .Include(s => s.Lot)
                        .Include(s => s.Booking)
                        .Include(s => s.CurrentSlot)
                            .ThenInclude(slot => slot != null ? slot.Floor : null!)
                        .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
                    if (session == null)
                    {
                        return (false, $"Không tìm thấy phiên gửi xe với ID: {sessionId}.", null);
                    }
                    if (totalFee.HasValue)
                    {
                        if (totalFee.Value < 0 || (session.TotalFee > 0 && session.TotalFee != totalFee.Value))
                            return (false, "Tổng phí Payment Service không khớp với phí đã ghi nhận trên phiên.", null);
                        if (session.TotalFee == 0) session.TotalFee = totalFee.Value;
                    }
                    // Đọc lại trạng thái mới nhất dưới khóa (có thể vừa bị checkout/clearance khác đổi).
                    if (_context is DbContext freshCtx) await freshCtx.Entry(session).ReloadAsync(ct);

                    var lotName = session.Lot?.Name ?? string.Empty;
                    var slotCode = session.CurrentSlot?.SlotCode;
                    var floorNumber = session.CurrentSlot?.Floor?.FloorNumber;

                    if (session.Status == SessionStatuses.Completed)
                        return (false, $"Phiên gửi xe #{sessionId} đã kết thúc trước đó, không thể ghi nhận thanh toán.", null);
                    if (session.Status == SessionStatuses.Cancelled)
                        return (false, $"Phiên gửi xe #{sessionId} đã bị hủy, không thể ghi nhận thanh toán.", null);

                    // Exact-match theo TransactionRef (không dùng Contains thô: "TX-1" không được khớp "TX-10").
                    // Prefilter Contains an toàn vì ref đã bị giới hạn ký tự không bị JSON-escape.
                    var candidates = await _context.AuditLogs
                        .Where(a => a.Action == "PAYMENT_CLEARED" && a.NewValues != null && a.NewValues.Contains(trimmedRef))
                        .Select(a => new { a.TargetId, a.NewValues })
                        .ToListAsync(ct);
                    var sameRef = candidates
                        .Select(a => new { a.TargetId, Parsed = ParseClearance(a.NewValues, trimmedRef) })
                        .Where(x => x.Parsed.Matched)
                        .ToList();

                    if (sameRef.Any(x => x.TargetId != session.Id.ToString()))
                        return (false, $"Mã giao dịch '{trimmedRef}' đã được sử dụng cho một phiên gửi xe khác.", null);

                    if (session.VerificationStatus == VerificationStatuses.MonthlyPass)
                        return (false, $"Phiên #{session.Id} là xe vé tháng, không phát sinh thanh toán.", null);
                    if (session.Status == SessionStatuses.Paid)
                    {
                        var replay = sameRef.FirstOrDefault();
                        if (replay == null)
                            return (false, $"Phiên gửi xe #{session.Id} đã hoàn tất thanh toán trước đó với một giao dịch khác.", null);
                        if (replay.Parsed.AmountPaid.HasValue && replay.Parsed.AmountPaid.Value != amountPaid)
                            return (false, $"Mã giao dịch '{trimmedRef}' đã ghi nhận với số tiền khác ({replay.Parsed.AmountPaid.Value:N0} VNĐ), không chấp nhận {amountPaid:N0} VNĐ.", null);
                        return (true, $"Phiên gửi xe #{session.Id} đã được ghi nhận thanh toán hợp lệ trước đó với mã giao dịch '{trimmedRef}'.", MapToDto(session, lotName, slotCode, floorNumber));
                    }

                    // Vé điện tử chưa được staff xác minh xe đã đỗ thì chưa nhận clearance
                    // (Verify chỉ nhận ACTIVE nên PAID sớm sẽ làm kẹt phiên).
                    if (session.EntryEventId.HasValue && session.VerificationStatus != "VERIFIED")
                        return (false, $"Phiên #{session.Id} chưa được staff xác minh xe đã đỗ; chưa nhận clearance.", null);

                    // Phí do Core đã tính (nếu có) phải khớp tuyệt đối: thiếu hoặc thừa đều bị từ chối (fail-closed).
                    // Core chưa có bảng giá/quote nên TotalFee=0 nghĩa là "chưa tính"; khi đó chỉ chấp nhận số tiền > 0 đã ký.
                    var prepaidDeposit = session.Booking?.PaymentStatus == "PAID"
                        ? session.Booking.PrepaidAmount ?? 0m
                        : 0m;
                    var amountDue = Math.Max(0m, session.TotalFee - prepaidDeposit);
                    if (session.TotalFee > 0 && amountPaid != amountDue)
                        return (false, $"Số tiền thanh toán ({amountPaid:N0} VNĐ) không khớp số tiền còn lại sau khi trừ cọc ({amountDue:N0} VNĐ).", null);

                    var feeDue = session.TotalFee;
                    var oldState = new { session.Status, session.TotalFee, session.PaidAt };

                    // Không ghi đè phí đã tính bằng số tiền trả. Chỉ khi Core chưa tính phí (0) mới ghi số tiền đã thu vào TotalFee
                    // để báo cáo doanh thu không mất (chưa có cột AmountPaid riêng; cần migration được duyệt).
                    if (feeDue == 0m) session.TotalFee = amountPaid;
                    session.PaidAt = DateTime.UtcNow;
                    session.Status = SessionStatuses.Paid;
                    session.UpdatedAt = DateTime.UtcNow;

                    var newState = new { session.Status, session.TotalFee, session.PaidAt, TransactionRef = trimmedRef,
                        AmountPaid = amountPaid, FeeDue = feeDue, PrepaidDepositApplied = prepaidDeposit, PaymentMethod = paymentMethod };
                    await LogAuditAsync("PAYMENT_CLEARED", nameof(ParkingSession), session.Id.ToString(), oldState, newState, actorId, ct);
                    await _context.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);

                    _logger.LogInformation("Financial clearance recorded for session {SessionId}: Amount {Amount:N0} VNĐ, Ref {Ref}, Method {Method}",
                        session.Id, amountPaid, trimmedRef, paymentMethod ?? "N/A");

                    return (true, $"Ghi nhận thanh toán hoàn tất cho phiên #{session.Id}.", MapToDto(session, lotName, slotCode, floorNumber));
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }
            }
            finally
            {
                lotLock.Release();
            }
        }
        finally
        {
            ClearanceGate.Release();
        }
    }

    private const long PaymentClearanceLockKey = 699_999_999L;
    private static readonly SemaphoreSlim ClearanceGate = new(1, 1);

    /// <summary>Đọc audit PAYMENT_CLEARED: khớp khi TransactionRef bằng đúng (ordinal) mã đang xét.</summary>
    private static (bool Matched, decimal? AmountPaid) ParseClearance(string? json, string transactionRef)
    {
        if (string.IsNullOrEmpty(json)) return (false, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (false, null);
            string? txn = null;
            decimal? amount = null;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(p.Name, "transactionRef", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    txn = p.Value.GetString();
                else if (string.Equals(p.Name, "amountPaid", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number)
                    amount = p.Value.GetDecimal();
            }
            return (string.Equals(txn, transactionRef, StringComparison.Ordinal), amount);
        }
        catch (JsonException) { return (false, null); }
    }

    private async Task LogAuditAsync(string action, string targetEntity, string targetId, object? oldValues, object? newValues, long? actorId, CancellationToken ct)
    {
        var audit = new AuditLog
        {
            Action = action,
            TargetEntity = targetEntity,
            TargetId = targetId,
            ActorId = actorId,
            OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues, JsonOptions) : null,
            NewValues = newValues != null ? JsonSerializer.Serialize(newValues, JsonOptions) : null,
            CreatedAt = DateTime.UtcNow
        };

        _context.AuditLogs.Add(audit);
    }
}
