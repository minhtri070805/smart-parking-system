using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartParking.Application.Common.Helpers;
using SmartParking.Application.Common.Interfaces;
using SmartParking.Application.DTOs.Operations;
using SmartParking.Domain.Constants;
using SmartParking.Domain.Entities;

namespace SmartParking.Application.UseCases.ParkingOperations;

public sealed class ParkingOperationsService(IApplicationDbContext db,
    IParkingAuthorizationService auth, IParkingOperationsWriter writer)
{
    private static ParkingOperationException Error(int status, string code, string message) => new(status, code, message);
    private static void Require(bool condition, string message, string code = "INVALID_OPERATION", int status = 409)
    {
        if (!condition) throw Error(status, code, message);
    }
    private async Task Access(int lotId, long actor, string role, string duty, CancellationToken ct)
    {
        var permitted = duty switch {
            "ADMIN" => role == Roles.Admin && await db.Users.AnyAsync(u => u.Id == actor && u.IsActive && u.Role.Code == Roles.Admin, ct),
            "GATE" => await auth.CanOperateGateAsync(actor, role, lotId, ct),
            "GATE_IN" => await auth.CanOperateGateInAsync(actor, role, lotId, ct),
            "VERIFY" => await auth.CanOperateSlotVerificationAsync(actor, role, lotId, ct),
            _ => await auth.HasLotAccessAsync(actor, role, lotId, ct)
        };
        Require(actor > 0 && permitted, "Bạn không có quyền hoặc nhiệm vụ tại bãi này.", "FORBIDDEN", 403);
        Require(await db.ParkingLots.AnyAsync(l => l.Id == lotId, ct), "Không tìm thấy bãi.", "NOT_FOUND", 404);
    }
    private void Audit(string action, string entity, object id, long actor, object? before, object after)
        => db.AuditLogs.Add(new AuditLog { Action = action, TargetEntity = entity,
            TargetId = id.ToString(), ActorId = actor,
            OldValues = before == null ? null : JsonSerializer.Serialize(before),
            NewValues = JsonSerializer.Serialize(after), CreatedAt = DateTime.UtcNow });

    public async Task<object> Zones(int lotId, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        return await db.ParkingZones.AsNoTracking().Where(z => z.LotId == lotId).OrderBy(z => z.Id)
            .Select(z => new { z.Id, z.LotId, z.Code, z.Name, z.Type, z.Layout, z.IsActive, z.UpdatedAt,
                z.FloorId, FloorName = z.Floor.Name }).ToListAsync(ct);
    }
    public async Task<object> Layout(int lotId, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        return new {
            Floors = await db.ParkingFloors.AsNoTracking().Where(f => f.LotId == lotId).OrderBy(f => f.FloorNumber)
                .Select(f => new { f.Id, f.LotId, f.Name, f.FloorNumber, f.IsActive }).ToListAsync(ct),
            Slots = await db.ParkingSlots.AsNoTracking().Where(s => s.Floor.LotId == lotId).OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.FloorId, s.ZoneId, ZoneName = s.Zone.Name, ZoneType = s.Zone.Type, FloorName = s.Floor.Name, FloorActive = s.Floor.IsActive,
                    ZoneActive = s.Zone.IsActive, s.SlotCode, s.PhysicalStatus, s.AllowedFuelType,
                    s.IsActive, s.PositionX, s.PositionY, s.PositionZ, s.UpdatedAt }).ToListAsync(ct)
        };
    }
    public async Task<object> Evidence(int lotId, long sessionId, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        var imageId = await db.ParkingSessions.Where(s => s.Id == sessionId && s.LotId == lotId).Select(s => s.EntryImageId).FirstOrDefaultAsync(ct);
        return (object?)await db.StoredImages.AsNoTracking().Where(i => i.Id == imageId).Select(i => new { i.Id, i.Url, i.CreatedAt }).FirstOrDefaultAsync(ct)
            ?? throw Error(404, "NOT_FOUND", "Không có ảnh đã lưu cho lượt này.");
    }
    public async Task<object> SaveZone(int lotId, int? id, ZoneRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "ADMIN", ct);
        Require(!string.IsNullOrWhiteSpace(r.Code) && r.Code.Trim().Length <= 30 &&
            !string.IsNullOrWhiteSpace(r.Name) && r.Name.Trim().Length <= 100 &&
            r.Type is "TRANSIENT" or "FIXED" && r.Layout is "INSIDE" or "EXTERNAL",
            "Cần mã/tên khu và loại/bố cục hợp lệ.", status: 400);
        Require(r.FloorId is > 0, "Phải chọn tầng trước khi tạo khu vực.", "FLOOR_REQUIRED", 400);
        return await writer.ExecuteAsync<object>(lotId, async () => {
            var z = id == null ? new ParkingZone { LotId = lotId } :
                await db.ParkingZones.FirstOrDefaultAsync(z => z.Id == id && z.LotId == lotId, ct)
                    ?? throw Error(404, "NOT_FOUND", "Không tìm thấy khu.");
            var floor = await db.ParkingFloors.FirstOrDefaultAsync(f => f.Id == r.FloorId && f.LotId == lotId, ct);
            Require(floor != null, "Tầng đã chọn không thuộc bãi này.", "INVALID_FLOOR", 400);
            Require(floor!.FloorNumber != 0 && !floor.Name.Contains("tầng g", StringComparison.OrdinalIgnoreCase) || r.Type == "FIXED",
                "Tầng G chỉ dành cho khu và xe vé tháng.", "GROUND_FLOOR_MONTHLY_ONLY", 400);
            var before = new { z.Code, z.Name, z.Type, z.Layout, z.IsActive, z.FloorId };
            if (id != null && z.Type != r.Type)
                Require(!await db.ParkingSlots.AnyAsync(s => s.ZoneId == id, ct) &&
                    !await db.ParkingSessions.AnyAsync(s => s.ZoneId == id, ct) &&
                    !await db.ParkingPolicies.AnyAsync(p => p.ZoneId == id, ct),
                    "Khu đã có ô đỗ hoặc lịch sử không được đổi loại. Tạo khu mới.");
            Require(!await db.ParkingZones.AnyAsync(x => x.LotId == lotId && x.Id != z.Id && x.Code == r.Code.Trim().ToUpper(), ct), "Mã khu đã tồn tại.");
            z.Code = r.Code.Trim().ToUpperInvariant(); z.Name = r.Name.Trim(); z.FloorId = floor.Id;
            z.Type = r.Type; z.Layout = r.Layout; z.IsActive = r.IsActive; z.UpdatedAt = DateTime.UtcNow;
            if (id == null) db.ParkingZones.Add(z);
            await db.SaveChangesAsync(ct);
            await db.SaveChangesAsync(ct);
            Audit("SAVE_ZONE", "parking_zones", z.Id, actor, id == null ? null : before,
                new { z.Code, z.Name, z.Type, z.Layout, z.IsActive, z.FloorId });
            return new { z.Id, z.LotId, z.Code, z.Name, z.Type, z.Layout, z.IsActive, z.FloorId };
        }, ct);
    }
    public async Task<object> AssignFloor(int lotId, int floorId, int zoneId, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "ADMIN", ct);
        return await writer.ExecuteAsync<object>(lotId, async () => {
            var floor = await db.ParkingFloors.FirstOrDefaultAsync(f => f.Id == floorId && f.LotId == lotId, ct)
                ?? throw Error(404, "NOT_FOUND", "Không tìm thấy tầng tại bãi.");
            var zone = await db.ParkingZones.FirstOrDefaultAsync(z => z.Id == zoneId && z.LotId == lotId && z.IsActive, ct)
                ?? throw Error(404, "NOT_FOUND", "Khu phải hoạt động và thuộc cùng bãi.");
            Require((floor.FloorNumber != 0 && !floor.Name.Contains("tầng g", StringComparison.OrdinalIgnoreCase)) || zone.Type == "FIXED",
                "Tầng G chỉ dành cho khu và xe vé tháng.", "GROUND_FLOOR_MONTHLY_ONLY", 400);
            Require(!await db.ParkingSlots.AnyAsync(s => s.ZoneId == zoneId, ct) &&
                !await db.ParkingSessions.AnyAsync(s => s.ZoneId == zoneId, ct) &&
                !await db.ParkingPolicies.AnyAsync(p => p.ZoneId == zoneId, ct),
                "Khu đã có ô đỗ hoặc lịch sử, không thể chuyển sang tầng khác.");
            var old = zone.FloorId; zone.FloorId = floorId; zone.UpdatedAt = DateTime.UtcNow;
            Audit("ASSIGN_ZONE_FLOOR", "parking_zones", zoneId, actor, new { FloorId = old }, new { FloorId = floorId });
            return new { zone.Id, zone.LotId, zone.FloorId };
        }, ct);
    }
    public async Task<object> ConfigureFuel(int lotId, long slotId, SlotFuelRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "ADMIN", ct);
        Require(r.AllowedFuelType is "ANY" or "PETROL" or "ELECTRIC" or "HYBRID", "Loại nhiên liệu không hợp lệ.", status: 400);
        return await writer.ExecuteAsync<object>(lotId, async () => {
            var slot = await db.ParkingSlots.FirstOrDefaultAsync(s => s.Id == slotId && s.Floor.LotId == lotId, ct)
                ?? throw Error(404, "NOT_FOUND", "Không tìm thấy ô tại bãi.");
            Require(!await db.ParkingSessions.AnyAsync(s => s.CurrentSlotId == slotId &&
                (s.Status == "ACTIVE" || s.Status == "PAID") && r.AllowedFuelType != "ANY" && s.FuelType != r.AllowedFuelType, ct),
                "Không đổi tương thích khi còn xe không phù hợp ở ô.");
            var old = slot.AllowedFuelType; slot.AllowedFuelType = r.AllowedFuelType; slot.UpdatedAt = DateTime.UtcNow;
            Audit("CONFIGURE_SLOT_FUEL", "parking_slots", slotId, actor, new { AllowedFuelType = old }, new { slot.AllowedFuelType });
            return new { slot.Id, slot.AllowedFuelType };
        }, ct);
    }

    public async Task<object> Policies(int lotId, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        return Array.Empty<object>();
    }
    public async Task<object> EffectivePolicy(int lotId, int? zoneId, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        throw Error(410, "POLICIES_DISABLED", "Chính sách vận hành đang tạm tắt trong giai đoạn demo.");
    }
    public async Task<object> CreatePolicy(int lotId, PolicyRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "ADMIN", ct);
        throw Error(410, "POLICIES_DISABLED", "Chính sách vận hành đang tạm tắt trong giai đoạn demo.");
    }

    public async Task<OperationSessionDto> Enter(int lotId, EntryRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, StaffDuties.GateIn, ct);
        Require(r.EventId != Guid.Empty && r.ZoneId > 0, "Thiếu EventId hoặc khu.", status: 400);
        var code = TicketCode(r.TicketCode);
        // Preserve the fingerprint of manual entries issued before the intake contract changed.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { lotId, r.ZoneId, code, RecognitionId = (long?)null, r.ImageId, r.ManualReason }))));
        return await writer.ExecuteAsync(lotId, async () => {
            var previous = await db.ParkingSessions.FirstOrDefaultAsync(s => s.EntryEventId == r.EventId, ct);
            if (previous != null) {
                Require(previous.LotId == lotId && previous.EntryRequestHash == hash,
                    "EventId đã được dùng với nội dung khác.", "EVENT_MISMATCH");
                return await View(previous, ct);
            }
            Require(await db.ParkingLots.AnyAsync(l => l.Id == lotId && l.IsActive, ct), "Bãi đang đóng nhận xe.");
            var zone = await db.ParkingZones.FirstOrDefaultAsync(z => z.Id == r.ZoneId && z.LotId == lotId, ct);
            Require(await auth.CanOperateAreaAsync(actor, role, lotId, StaffDuties.GateIn, null, r.ZoneId, ct), "Bạn không có nhiệm vụ tại tầng này.", "FORBIDDEN", 403);
            Require(zone is { IsActive: true, Type: "TRANSIENT" }, "Chỉ nhận vãng lai vào khu TRANSIENT đang hoạt động.");
            Require(!await db.ParkingTickets.AnyAsync(t => t.Code == code && t.Status == "ISSUED", ct), "Vé đang dùng cho lượt khác.", "TICKET_IN_USE");
            // Historical ticket codes are immutable; a replacement/new printed code is required.
            Require(!await db.ParkingTickets.AnyAsync(t => t.Code == code, ct), "Mã vé đã có lịch sử, hãy dùng mã vé mới.", "TICKET_USED");
            var capacity = await ParkingLotCapacityHelper.CalculateAsync(db, lotId, ct: ct, zoneId: r.ZoneId);
            Require(capacity.AvailableCapacity > 0, "Khu đã hết khả năng nhận xe.", "CAPACITY_EXHAUSTED");
            var policy = await ParkingPolicyResolver.ResolveAsync(db, lotId, r.ZoneId, DateTime.UtcNow, ct);
            Require(!string.IsNullOrWhiteSpace(r.ManualReason) && r.ManualReason.Length <= 500,
                "Nhập lý do tiếp nhận xe.", status: 400);
            if (r.ImageId.HasValue)
                Require(await db.StoredImages.AnyAsync(i => i.Id == r.ImageId && i.UserId == actor, ct),
                    "Ảnh không tồn tại hoặc không thuộc người tiếp nhận.", "INVALID_IMAGE", 403);
            var session = new ParkingSession { LotId = lotId, ZoneId = r.ZoneId, PolicyId = policy?.Id,
                EntryEventId = r.EventId, EntryRequestHash = hash, EntryImageId = r.ImageId,
                PlateNumber = "", VerificationStatus = "WAITING_VERIFICATION", Status = "ACTIVE", Revision = 1 };
            db.ParkingSessions.Add(session); await db.SaveChangesAsync(ct);
            var ticket = new ParkingTicket { LotId = lotId, Code = code, SessionId = session.Id, IssuedBy = actor };
            db.ParkingTickets.Add(ticket);
            Audit("ISSUE_ENTRY_TICKET", "parking_sessions", session.Id, actor, null,
                new { session.EntryEventId, session.EntryImageId, session.ZoneId, session.PolicyId, TicketCode = code, r.ManualReason });
            await db.SaveChangesAsync(ct);
            return await View(session, ct);
        }, ct);
    }
    // ---- Xe vé tháng: tra vé theo biển số và cho xe vào khu cố định không cần vé giấy.
    private sealed record PassRow(long Id, long VehicleId, string Plate, string Status, DateTime StartsAtUtc, DateTime EndsAtUtc, string PlanName);

    /// <summary>Quyết định xe có được vào bằng vé tháng hay không. Dựa trên thời gian thực, không chờ worker đánh dấu hết hạn.</summary>
    private async Task<PassLookupDto> ResolvePass(int lotId, string plateInput, CancellationToken ct)
    {
        var plate = PlateNumberHelper.Normalize(plateInput);
        PassLookupDto No(string reason, string message, PassRow? pass = null) => new(false, reason, message, plate,
            pass?.Id, pass?.VehicleId, pass?.PlanName, pass?.Status, pass?.StartsAtUtc, pass?.EndsAtUtc);
        if (!PlateNumberHelper.IsValidPlateFormat(plate))
            return No("INVALID_PLATE", "Biển số không hợp lệ. Ví dụ đúng: 51F-123.45.");

        var now = DateTime.UtcNow;
        // Bỏ vé đã hủy và vé hết hạn từ lâu để không tải cả lịch sử của bãi; so khớp biển số ở bộ nhớ vì mỗi xe viết một kiểu.
        var rows = (await db.ParkingPasses.AsNoTracking()
            .Where(p => p.LotId == lotId && p.Status != "CANCELLED" && p.EndsAtUtc > now.AddDays(-90))
            .Select(p => new PassRow(p.Id, p.VehicleId, p.Vehicle.PlateNumber, p.Status, p.StartsAtUtc, p.EndsAtUtc, p.Plan.Name))
            .ToListAsync(ct))
            .Where(p => PlateNumberHelper.IsMatch(p.Plate, plate)).ToList();

        var valid = rows.Where(p => p.Status == "ACTIVE" && p.StartsAtUtc <= now && p.EndsAtUtc > now)
            .OrderByDescending(p => p.EndsAtUtc).FirstOrDefault();
        if (valid == null)
        {
            var upcoming = rows.Where(p => p.Status is "ACTIVE" or "SCHEDULED" && p.StartsAtUtc > now).OrderBy(p => p.StartsAtUtc).FirstOrDefault();
            var suspended = rows.FirstOrDefault(p => p.Status == "SUSPENDED" && p.EndsAtUtc > now);
            var unpaid = rows.FirstOrDefault(p => p.Status == "PENDING");
            if (suspended != null) return No("PASS_SUSPENDED", "Vé tháng của xe này đang bị tạm khóa. Liên hệ quản lý bãi.", suspended);
            if (upcoming != null) return No("PASS_NOT_STARTED", "Vé tháng của xe này chưa đến ngày bắt đầu hiệu lực.", upcoming);
            var latest = rows.Where(p => p.Status is "ACTIVE" or "EXPIRED").OrderByDescending(p => p.EndsAtUtc).FirstOrDefault();
            if (latest != null) return No("PASS_EXPIRED", "Vé tháng của xe này đã hết hạn.", latest);
            if (unpaid != null) return No("PASS_UNPAID", "Vé tháng của xe này chưa được thanh toán.", unpaid);
            return No("PASS_NOT_FOUND", "Không tìm thấy vé tháng của xe này tại bãi.");
        }

        // Biển số chỉ được có một lượt đang mở trong toàn hệ thống (khớp chỉ mục duy nhất của phiên đang mở).
        var inside = await db.ParkingSessions.AnyAsync(s => s.PlateNumber == plate && (s.Status == "ACTIVE" || s.Status == "PAID"), ct);
        if (inside) return No("ALREADY_INSIDE", "Xe này đang có một lượt gửi chưa kết thúc.", valid);
        return new PassLookupDto(true, "OK", "Vé tháng còn hiệu lực.", plate, valid.Id, valid.VehicleId, valid.PlanName,
            valid.Status, valid.StartsAtUtc, valid.EndsAtUtc);
    }

    public async Task<PassLookupDto> LookupPass(int lotId, string? plate, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "GATE", ct);
        return await ResolvePass(lotId, plate ?? "", ct);
    }

    public async Task<PassEntryResult> EnterWithPass(int lotId, PassEntryRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, StaffDuties.GateIn, ct);
        Require(r.EventId != Guid.Empty && r.ZoneId > 0, "Thiếu EventId hoặc khu.", status: 400);
        Require(r.Note is null || r.Note.Length <= 500, "Ghi chú tối đa 500 ký tự.", status: 400);
        var plate = PlateNumberHelper.Normalize(r.PlateNumber);
        Require(PlateNumberHelper.IsValidPlateFormat(plate), "Biển số không hợp lệ.", "INVALID_PLATE", 400);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { Kind = VerificationStatuses.MonthlyPass, lotId, r.ZoneId, Plate = PlateNumberHelper.NormalizeForComparison(plate), r.ImageId }))));
        return await writer.ExecuteAsync(lotId, async () => {
            var previous = await db.ParkingSessions.FirstOrDefaultAsync(s => s.EntryEventId == r.EventId, ct);
            if (previous != null) {
                Require(previous.LotId == lotId && previous.EntryRequestHash == hash,
                    "EventId đã được dùng với nội dung khác.", "EVENT_MISMATCH");
                return new PassEntryResult(await View(previous, ct), null, true);
            }
            Require(await db.ParkingLots.AnyAsync(l => l.Id == lotId && l.IsActive, ct), "Bãi đang đóng nhận xe.");
            var zone = await db.ParkingZones.FirstOrDefaultAsync(z => z.Id == r.ZoneId && z.LotId == lotId, ct);
            Require(await auth.CanOperateAreaAsync(actor, role, lotId, StaffDuties.GateIn, null, r.ZoneId, ct), "Bạn không có nhiệm vụ tại tầng này.", "FORBIDDEN", 403);
            Require(zone is { IsActive: true, Type: "FIXED" }, "Xe vé tháng chỉ vào khu cố định (FIXED) đang hoạt động.", "NOT_A_MONTHLY_ZONE");
            var pass = await ResolvePass(lotId, plate, ct);
            Require(pass.CanEnter, pass.Message, pass.Reason);
            if (r.ImageId.HasValue)
                Require(await db.StoredImages.AnyAsync(i => i.Id == r.ImageId && i.UserId == actor, ct),
                    "Ảnh không tồn tại hoặc không thuộc người tiếp nhận.", "INVALID_IMAGE", 403);
            var now = DateTime.UtcNow;
            var policy = await ParkingPolicyResolver.ResolveAsync(db, lotId, r.ZoneId, now, ct);
            var session = new ParkingSession { LotId = lotId, ZoneId = r.ZoneId, PolicyId = policy?.Id, VehicleId = pass.VehicleId,
                EntryEventId = r.EventId, EntryRequestHash = hash, EntryImageId = r.ImageId, PlateNumber = plate,
                VerificationStatus = VerificationStatuses.MonthlyPass, VerifiedAt = now, VerifiedBy = actor,
                Status = "ACTIVE", Revision = 1, CheckInTime = now };
            db.ParkingSessions.Add(session); await db.SaveChangesAsync(ct);
            Audit("MONTHLY_PASS_ENTRY", "parking_sessions", session.Id, actor, null,
                new { session.EntryEventId, session.EntryImageId, session.ZoneId, PassId = pass.PassId, pass.PlanName, pass.EndsAtUtc, Plate = plate, r.Note });
            await db.SaveChangesAsync(ct);
            return new PassEntryResult(await View(session, ct), pass, false);
        }, ct);
    }

    public async Task<object> Sessions(int lotId, bool waitingOnly, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        // Project in one query: a busy lot must not make two network round trips per vehicle.
        return await db.ParkingSessions.AsNoTracking().Where(s => s.LotId == lotId &&
            (s.Status == "ACTIVE" || s.Status == "PAID") && (!waitingOnly || s.VerificationStatus == "WAITING_VERIFICATION"))
            .OrderBy(s => s.CheckInTime).ThenBy(s => s.Id)
            .Select(s => new OperationSessionDto(s.Id, s.LotId, s.ZoneId, s.PolicyId, s.EntryEventId,
                s.PlateNumber, s.TemporaryPlateNumber, s.EntryImageId, s.VerificationStatus,
                s.Status, s.FuelType, s.CurrentSlotId, s.CurrentSlot == null ? null : s.CurrentSlot.SlotCode,
                s.Revision, s.CheckInTime, s.VerifiedAt,
                db.ParkingTickets.Where(t => t.SessionId == s.Id).OrderByDescending(t => t.Id).Select(t => t.Code).FirstOrDefault(),
                db.ParkingTickets.Where(t => t.SessionId == s.Id).OrderByDescending(t => t.Id).Select(t => t.Status).FirstOrDefault()))
            .ToListAsync(ct);
    }
    public async Task<OperationSessionDto> Lookup(int lotId, string code, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "READ", ct);
        code = TicketCode(code);
        var ticket = await db.ParkingTickets.AsNoTracking().FirstOrDefaultAsync(t => t.LotId == lotId && t.Code == code, ct)
            ?? throw Error(404, "NOT_FOUND", "Không tìm thấy vé tại bãi.");
        var s = await db.ParkingSessions.AsNoTracking().FirstAsync(s => s.Id == ticket.SessionId, ct);
        return await View(s, ct, ticket);
    }
    private static string TicketCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant() ?? "";
        Require(code.Length is >= 3 and <= 40 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'),
            "Mã vé gồm 3–40 chữ, số hoặc dấu gạch nối.", status: 400);
        return code;
    }
    public async Task<object> ChangeTicket(int lotId, string code, TicketChangeRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "GATE", ct);
        code = TicketCode(code);
        Require(r.Action is "LOST" or "REPLACE" or "REVOKE" && !string.IsNullOrWhiteSpace(r.Reason) && r.Reason.Length <= 500,
            "Cần thao tác hợp lệ và lý do.", status: 400);
        // Replacing/revoking a ticket requires a manager/admin review, beyond gate duty.
        if (r.Action != "LOST") Require(await db.Users.AnyAsync(u => u.Id == actor && u.IsActive &&
            (u.Role.Code == Roles.Admin || u.Role.Code == Roles.Manager), ct), "Thay/thu hồi vé cần Quản lý hoặc Admin.", status: 403);
        return await writer.ExecuteAsync<object>(lotId, async () => {
            var ticket = await db.ParkingTickets.Include(t => t.Session).FirstOrDefaultAsync(t => t.LotId == lotId && t.Code == code, ct)
                ?? throw Error(404, "NOT_FOUND", "Không tìm thấy vé tại bãi.");
            Require(ticket.Session.Status is "ACTIVE" or "PAID", "Lượt gửi đã đóng.");
            if (r.Action == "LOST") Require(ticket.Status == "ISSUED", "Vé không còn hiệu lực.");
            else Require(ticket.Status is "ISSUED" or "LOST" || (r.Action == "REPLACE" && ticket.Status == "REVOKED"), "Vé đã được thay hoặc thu hồi.");
            var before = ticket.Status;
            var reason = r.Reason.Trim();
            ticket.Status = r.Action switch { "LOST" => "LOST", "REPLACE" => "REPLACED", _ => "REVOKED" };
            ticket.ClosedBy = actor; ticket.ClosedAt = DateTime.UtcNow; ticket.Reason = reason;
            await db.SaveChangesAsync(ct); // releases the partial unique session binding before issuing replacement
            string? replacement = null;
            if (r.Action == "REPLACE") {
                replacement = TicketCode(r.ReplacementCode);
                Require(!await db.ParkingTickets.AnyAsync(t => t.Code == replacement, ct), "Mã vé thay thế đã được sử dụng.");
                db.ParkingTickets.Add(new ParkingTicket { LotId = lotId, Code = replacement,
                    SessionId = ticket.SessionId, IssuedBy = actor, Reason = reason });
            }
            Audit("CHANGE_TICKET", "parking_tickets", ticket.Id, actor, new { Status = before },
                new { ticket.Status, ReplacementCode = replacement, ticket.Reason, ticket.SessionId });
            return new { ticket.Code, ticket.Status, ticket.SessionId, ReplacementCode = replacement };
        }, ct);
    }

    public async Task<OperationSessionDto> Verify(int lotId, long id, VerifyVehicleRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "VERIFY", ct);
        var plate = PlateNumberHelper.NormalizeForComparison(r.PlateNumber);
        Require(PlateNumberHelper.IsValidPlateFormat(r.PlateNumber) && r.FuelType is "PETROL" or "ELECTRIC" or "HYBRID" && r.VehicleObserved,
            "Cần biển số ô tô đúng định dạng (ví dụ 51F-123.45), nhiên liệu và xác nhận đã đối chiếu xe thực tế tại ô.", status: 400);
        Require(r.Reason is null || r.Reason.Length <= 500, "Lý do tối đa 500 ký tự.", status: 400);
        return await writer.ExecuteAsync(lotId, async () => {
            var s = await LoadSession(lotId, id, r.ExpectedRevision, ct);
            Require(s.VerificationStatus == "WAITING_VERIFICATION" && s.Status == "ACTIVE", "Lượt không còn chờ xác minh.");
            if (!PlateNumberHelper.IsMatch(s.TemporaryPlateNumber, plate))
                Require(!string.IsNullOrWhiteSpace(r.Reason), "Cần ghi lý do xác minh biển số.", status: 400);
            // Canonical comparison also detects separators in legacy plates.
            var otherPlates = await db.ParkingSessions.Where(x => x.Id != id && x.VerificationStatus != "WAITING_VERIFICATION" &&
                (x.Status == "ACTIVE" || x.Status == "PAID")).Select(x => x.PlateNumber).ToListAsync(ct);
            Require(!otherPlates.Any(p => PlateNumberHelper.IsMatch(p, plate)), "Biển số chính thức đang có lượt gửi mở.", "PLATE_CONFLICT");
            var slot = await EligibleSlot(lotId, s, r.ActualSlotId, r.FuelType, ct);
            Require(await auth.CanOperateAreaAsync(actor, role, lotId, "SLOT_VERIFIER", slot.FloorId, s.ZoneId, ct), "Ô nằm ngoài tầng hoặc khu vực được phân công.", "FORBIDDEN", 403);
            s.PlateNumber = plate; s.FuelType = r.FuelType; s.VerifiedAt = DateTime.UtcNow;
            s.VerifiedBy = actor; s.VerificationStatus = "VERIFIED"; s.CurrentSlotId = slot.Id;
            s.Revision++; s.UpdatedAt = DateTime.UtcNow;
            slot.PhysicalStatus = "OCCUPIED"; slot.UpdatedAt = DateTime.UtcNow;
            await CompleteDispatch(s, slot, actor, r.Reason, ct);
            Audit("VERIFY_VEHICLE_AT_SLOT", "parking_sessions", id, actor, new { s.TemporaryPlateNumber },
                new { s.PlateNumber, s.FuelType, s.CurrentSlotId, s.Revision, s.EntryImageId, r.Reason });
            await db.SaveChangesAsync(ct);
            return await View(s, ct);
        }, ct);
    }
    public async Task<OperationSessionDto> Move(int lotId, long id, MoveVehicleRequest r, long actor, string role, CancellationToken ct)
    {
        await Access(lotId, actor, role, "VERIFY", ct);
        Require(r.OldSlotObservedEmpty && r.VehicleObservedAtDestination && !string.IsNullOrWhiteSpace(r.Reason) && r.Reason.Length <= 500,
            "Cần lý do và xác nhận xe đã rời ô cũ, có mặt tại ô mới.", status: 400);
        return await writer.ExecuteAsync(lotId, async () => {
            var s = await LoadSession(lotId, id, r.ExpectedRevision, ct);
            Require(s.VerificationStatus == "VERIFIED" && s.CurrentSlotId.HasValue && s.Status is "ACTIVE" or "PAID", "Chỉ di chuyển xe đã xác minh còn trong bãi.");
            Require(s.CurrentSlotId != r.ActualSlotId, "Ô mới phải khác ô cũ.", status: 400);
            var old = await db.ParkingSlots.FirstAsync(x => x.Id == s.CurrentSlotId, ct);
            Require(old.PhysicalStatus == "OCCUPIED", "Trạng thái ô cũ không khớp. Cần đối soát trước.");
            var next = await EligibleSlot(lotId, s, r.ActualSlotId, s.FuelType!, ct);
            Require(await auth.CanOperateAreaAsync(actor, role, lotId, "SLOT_VERIFIER", old.FloorId, s.ZoneId, ct) &&
                await auth.CanOperateAreaAsync(actor, role, lotId, "SLOT_VERIFIER", next.FloorId, s.ZoneId, ct),
                "Chỉ chuyển ô trong tầng và khu vực được phân công.", "FORBIDDEN", 403);
            old.PhysicalStatus = "EMPTY"; old.UpdatedAt = DateTime.UtcNow;
            next.PhysicalStatus = "OCCUPIED"; next.UpdatedAt = DateTime.UtcNow;
            s.CurrentSlotId = next.Id; s.Revision++; s.UpdatedAt = DateTime.UtcNow;
            await CompleteDispatch(s, next, actor, r.Reason, ct);
            Audit("MOVE_VERIFIED_VEHICLE", "parking_sessions", id, actor, new { SlotId = old.Id },
                new { SlotId = next.Id, s.Revision, r.Reason, r.OldSlotObservedEmpty, r.VehicleObservedAtDestination });
            await db.SaveChangesAsync(ct); return await View(s, ct);
        }, ct);
    }
    private async Task<ParkingSession> LoadSession(int lotId, long id, long revision, CancellationToken ct)
    {
        var s = await db.ParkingSessions.FirstOrDefaultAsync(s => s.Id == id && s.LotId == lotId, ct)
            ?? throw Error(404, "NOT_FOUND", "Không tìm thấy lượt gửi tại bãi.");
        Require(s.Revision == revision, "Lượt gửi đã thay đổi. Tải lại dữ liệu.", "STALE_DATA");
        return s;
    }
    private async Task<ParkingSlot> EligibleSlot(int lotId, ParkingSession session, long id, string fuel, CancellationToken ct)
    {
        var slot = await db.ParkingSlots.Include(s => s.Floor).Include(s => s.Zone).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw Error(404, "NOT_FOUND", "Không tìm thấy ô.");
        Require(slot.Floor.LotId == lotId && slot.ZoneId == session.ZoneId && slot.Zone is { Type: "TRANSIENT" }, "Ô phải ở cùng khu vãng lai. Khu đóng vẫn xử lý xe đã tiếp nhận.");
        Require(slot.IsActive && slot.Floor.IsActive && slot.PhysicalStatus == "EMPTY", "Ô/tầng ngừng dùng, bảo trì hoặc đã có xe.", "SLOT_UNAVAILABLE");
        Require(slot.AllowedFuelType == "ANY" || slot.AllowedFuelType == fuel, "Nhiên liệu không tương thích với ô.", "INCOMPATIBLE_FUEL");
        Require(!await db.ParkingSessions.AnyAsync(s => s.Id != session.Id && s.CurrentSlotId == id && (s.Status == "ACTIVE" || s.Status == "PAID"), ct) &&
            !await db.ParkingDispatches.AnyAsync(d => d.SessionId != session.Id && d.AssignedSlotId == id && d.Status == "ASSIGNED", ct), "Ô đang được xe khác sử dụng hoặc được phân.", "SLOT_CONFLICT");
        return slot;
    }
    private async Task CompleteDispatch(ParkingSession s, ParkingSlot slot, long actor, string? reason, CancellationToken ct)
    {
        var open = await db.ParkingDispatches.Where(d => d.SessionId == s.Id && (d.Status == "PENDING" || d.Status == "ASSIGNED")).ToListAsync(ct);
        foreach (var d in open) { d.Status = "CANCELLED"; d.Notes = "Được thay bằng xác minh thực tế tại ô."; d.UpdatedAt = DateTime.UtcNow; }
        db.ParkingDispatches.Add(new ParkingDispatch { SessionId = s.Id, AssignedSlotId = slot.Id, ActualSlotId = slot.Id,
            Status = "OCCUPIED", DispatchMode = "MANUAL", ConfirmedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow,
            Notes = $"Staff #{actor}: {reason}" });
    }
    private async Task<OperationSessionDto> View(ParkingSession s, CancellationToken ct, ParkingTicket? ticket = null)
    {
        ticket ??= await db.ParkingTickets.AsNoTracking().Where(t => t.SessionId == s.Id).OrderByDescending(t => t.Id).FirstOrDefaultAsync(ct);
        var slotCode = s.CurrentSlotId == null ? null : await db.ParkingSlots.Where(x => x.Id == s.CurrentSlotId).Select(x => x.SlotCode).FirstOrDefaultAsync(ct);
        return new(s.Id, s.LotId, s.ZoneId, s.PolicyId, s.EntryEventId, s.PlateNumber, s.TemporaryPlateNumber,
            s.EntryImageId, s.VerificationStatus, s.Status, s.FuelType, s.CurrentSlotId,
            slotCode, s.Revision, s.CheckInTime, s.VerifiedAt, ticket?.Code, ticket?.Status);
    }
}
