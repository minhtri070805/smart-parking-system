import { useEffect, useMemo, useState } from "react";
import { staffApi } from "../../services/staffApi.js";
import {
  formatTime,
  Notice,
  SafePhoto,
  StaffError,
  useStaffAction,
} from "./StaffUI.jsx";

const POLL_INTERVAL_MS = 15_000;

export default function BookingDispatchQueue({
  lot,
  revision,
  onChanged,
  onBusyChange,
}) {
  const [bookings, setBookings] = useState(null);
  const [slots, setSlots] = useState(null);
  const [queueError, setQueueError] = useState(null);
  const [slotError, setSlotError] = useState(null);
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [zoneId, setZoneId] = useState("");
  const [slotId, setSlotId] = useState("");
  const [notes, setNotes] = useState("");
  const [message, setMessage] = useState("");
  const [retry, setRetry] = useState(0);
  const action = useStaffAction(onBusyChange);

  useEffect(() => {
    let active = true;
    let timeoutId;
    let controller;
    const load = async () => {
      controller = new AbortController();
      try {
        const result = await staffApi.checkedInBookings(lot.lotId, controller.signal);
        if (!active) return;
        setBookings(result.items);
        setQueueError(null);
        if (selectedId && !result.items.some((booking) => String(booking.id) === selectedId)) {
          setSelectedId("");
          setZoneId("");
          setSlotId("");
        }
      } catch (error) {
        if (active && !controller.signal.aborted) setQueueError(error);
      } finally {
        if (active) timeoutId = window.setTimeout(load, POLL_INTERVAL_MS);
      }
    };
    load();
    return () => {
      active = false;
      controller?.abort();
      window.clearTimeout(timeoutId);
    };
  }, [lot.lotId, revision, retry, selectedId]);

  useEffect(() => {
    if (!selectedId) {
      setSlots(null);
      setSlotError(null);
      return undefined;
    }
    const controller = new AbortController();
    setSlots(null);
    setSlotError(null);
    staffApi
      .lotSlots(lot.lotId, controller.signal)
      .then(({ slots: result }) => {
        if (!controller.signal.aborted) setSlots(result);
      })
      .catch((error) => {
        if (!controller.signal.aborted) setSlotError(error);
      });
    return () => controller.abort();
  }, [lot.lotId, selectedId, revision]);

  const selectedBooking = bookings?.find(
    (booking) => String(booking.id) === selectedId,
  );
  const openSlots = useMemo(
    () =>
      (slots || []).filter(
        (slot) =>
          slot.isActive &&
          slot.physicalStatus === "EMPTY" &&
          (slot.isAvailable || Number(slot.id) === Number(selectedBooking?.parkingSlotId)),
      ),
    [slots],
  );
  const zones = useMemo(
    () =>
      [...new Map(
        openSlots
          .filter((slot) => slot.zoneId != null)
          .map((slot) => [String(slot.zoneId), {
            id: slot.zoneId,
            name: slot.zoneName || `Khu ${slot.zoneId}`,
          }]),
      ).values()],
    [openSlots],
  );
  const visibleBookings = (bookings || []).filter((booking) => {
    const value = search.trim().toLocaleUpperCase();
    return !value ||
      String(booking.id).includes(value) ||
      booking.plateNumber?.toLocaleUpperCase().includes(value);
  });
  const zoneSlots = openSlots.filter(
    (slot) => String(slot.zoneId) === String(zoneId),
  );

  const selectBooking = (booking) => {
    setSelectedId(String(booking.id));
    setZoneId("");
    setSlotId("");
    setNotes("");
    setMessage("");
    action.setError(null);
  };
  const submit = (event) => {
    event.preventDefault();
    action.run(async () => {
      if (!selectedBooking?.sessionId)
        throw new Error("Phiên xe không còn trong hàng chờ điều phối.");
      const selectedSlot = zoneSlots.find(
        (slot) => String(slot.id) === String(slotId),
      );
      if (!selectedSlot) throw new Error("Chọn khu vực và ô đỗ còn trống.");
      const result = await staffApi.assignBookingSlot(
        selectedBooking.sessionId,
        selectedSlot.id,
        notes.trim(),
      );
      if (
        Number(result?.data?.bookingId) !== Number(selectedBooking.id) ||
        Number(result?.data?.currentSlotId) !== Number(selectedSlot.id)
      )
        throw new Error("Chưa cập nhật được ô đỗ. Hãy tải lại hàng chờ trước khi thử lại.");
      setMessage(
        `Đã gán xe ${selectedBooking.plateNumber} vào ${selectedSlot.zoneName || selectedSlot.floorName} · ${selectedSlot.slotCode}.`,
      );
      setSelectedId("");
      setZoneId("");
      setSlotId("");
      setNotes("");
      onChanged?.();
    });
  };

  return (
    <div className="staff-side-stack">
      {message && <Notice kind="success">{message}</Notice>}
      <section className="staff-panel staff-bookings" aria-labelledby="booking-dispatch-title">
        <div className="staff-panel-heading">
          <div>
            <span className="staff-eyebrow">HÀNG CHỜ ĐIỀU PHỐI</span>
            <h2 id="booking-dispatch-title">Xe đặt qua web chờ phân ô</h2>
            <p>Danh sách cập nhật tự động mỗi 15 giây sau khi nhân viên cổng check-in.</p>
          </div>
          <span className="staff-count">{bookings?.length ?? "—"}</span>
        </div>
        <label className="staff-field">
          Tìm theo biển số hoặc mã đặt chỗ
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Biển số hoặc mã đặt chỗ"
            disabled={action.busy}
          />
        </label>
        <StaffError error={queueError} />
        {queueError && (
          <button
            className="staff-button staff-button--secondary"
            onClick={() => setRetry((value) => value + 1)}
          >
            Thử tải lại hàng chờ
          </button>
        )}
        {bookings?.length === 0 && (
          <p className="staff-empty-small">Chưa có xe đặt chỗ nào chờ phân ô.</p>
        )}
        {bookings?.length > 0 && visibleBookings.length === 0 && (
          <p className="staff-empty-small">Không tìm thấy xe phù hợp.</p>
        )}
        {visibleBookings.length > 0 && (
          <div className="staff-booking-list" aria-label="Xe đặt chỗ chờ điều phối">
            {visibleBookings.map((booking) => (
              <article
                className={`staff-booking-item ${selectedId === String(booking.id) ? "is-new" : ""}`}
                key={booking.id}
              >
                <div className="staff-booking-vehicle">
                  <div>
                    <span className="staff-eyebrow">ĐẶT CHỖ #{booking.id}</span>
                    <strong className="staff-plate">{booking.plateNumber}</strong>
                  </div>
                  <span className="staff-booking-new">CHỜ PHÂN Ô</span>
                </div>
                <dl>
                  <div>
                    <dt>Người đặt</dt>
                    <dd>{booking.fullName || booking.username || "—"}</dd>
                  </div>
                  <div>
                    <dt>Điện thoại</dt>
                    <dd>{booking.phoneNumber || "—"}</dd>
                  </div>
                  <div>
                    <dt>Giờ qua cổng</dt>
                    <dd>{formatTime(booking.checkedInAt || booking.startTime)}</dd>
                  </div>
                  <div>
                    <dt>Bãi đã đặt</dt>
                    <dd>{booking.lotName || lot.lotName}</dd>
                  </div>
                </dl>
                <SafePhoto
                  url={booking.checkInImageUrl}
                  alt={`Ảnh biển số xe ${booking.plateNumber} tại cổng`}
                />
                {selectedId === String(booking.id) ? (
                  <form className="staff-booking-checkin" onSubmit={submit}>
                    <StaffError error={slotError} />
                    <label className="staff-field">
                      Khu vực
                      <select
                        required
                        value={zoneId}
                        disabled={action.busy || !slots}
                        onChange={(event) => {
                          setZoneId(event.target.value);
                          setSlotId("");
                        }}
                      >
                        <option value="">
                          {!slots ? "Đang tải khu vực…" : "Chọn khu vực"}
                        </option>
                        {zones.map((zone) => (
                          <option key={zone.id} value={zone.id}>{zone.name}</option>
                        ))}
                      </select>
                    </label>
                    <label className="staff-field">
                      Ô đỗ còn trống
                      <select
                        required
                        value={slotId}
                        disabled={!zoneId || action.busy}
                        onChange={(event) => setSlotId(event.target.value)}
                      >
                        <option value="">Chọn ô đỗ</option>
                        {zoneSlots.map((slot) => (
                          <option key={slot.id} value={slot.id}>
                            {slot.floorName} · {slot.slotCode}
                          </option>
                        ))}
                      </select>
                    </label>
                    {slots && openSlots.length === 0 && (
                      <Notice kind="warning">Không có ô trống khả dụng trong bãi này.</Notice>
                    )}
                    <label className="staff-field">
                      Ghi chú bàn giao (không bắt buộc)
                      <input
                        value={notes}
                        maxLength={255}
                        onChange={(event) => setNotes(event.target.value)}
                        disabled={action.busy}
                      />
                    </label>
                    <StaffError error={action.error} />
                    <div className="staff-inline-actions">
                      <button
                        type="submit"
                        className="staff-button staff-button--primary"
                        disabled={action.busy || !zoneId || !slotId}
                      >
                        {action.busy ? "Đang cập nhật ô đỗ…" : "Cập nhật ô đỗ"}
                      </button>
                      <button
                        type="button"
                        className="staff-text-button"
                        disabled={action.busy}
                        onClick={() => setSelectedId("")}
                      >
                        Hủy
                      </button>
                    </div>
                  </form>
                ) : (
                  <button
                    type="button"
                    className="staff-button staff-button--secondary"
                    disabled={action.busy}
                    onClick={() => selectBooking(booking)}
                  >
                    Chọn xe để phân ô
                  </button>
                )}
              </article>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
