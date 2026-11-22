import { useEffect, useRef, useState } from "react";
import { staffApi, platesMatch, validatePlate } from "../../services/staffApi.js";
import {
  formatTime,
  Notice,
  StaffError,
  useStaffAction,
} from "./StaffUI.jsx";
import PlateCapture, {
  emptyCapture,
  prepareCapture,
} from "./PlateCapture.jsx";

const POLL_INTERVAL_MS = 15_000;

export default function StaffBookings({
  lotId,
  lotIsActive,
  revision,
  onChanged,
  onBusyChange,
  onNewBookingsChange,
}) {
  const [bookings, setBookings] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);
  const [updatedAt, setUpdatedAt] = useState(null);
  const [newBookingIds, setNewBookingIds] = useState([]);
  const [retry, setRetry] = useState(0);
  const [search, setSearch] = useState("");
  const [selectedBookingId, setSelectedBookingId] = useState(null);
  const [capture, setCapture] = useState(emptyCapture);
  const [captureBusy, setCaptureBusy] = useState(false);
  const [completedBooking, setCompletedBooking] = useState(null);
  const previousIds = useRef(null);
  const dataRef = useRef(null);
  const action = useStaffAction(onBusyChange);

  useEffect(() => {
    let active = true;
    let timeoutId;
    let controller;

    const load = async () => {
      controller = new AbortController();
      try {
        const result = await staffApi.pendingBookings(lotId, controller.signal);
        if (!active) return;
        const nextIds = new Set(result.items.map((booking) => String(booking.id)));
        const unseen =
          previousIds.current === null
            ? []
            : result.items
                .filter((booking) => !previousIds.current.has(String(booking.id)))
                .map((booking) => String(booking.id));
        setNewBookingIds((current) => [
          ...new Set([...current.filter((id) => nextIds.has(id)), ...unseen]),
        ]);
        previousIds.current = nextIds;
        dataRef.current = result;
        setBookings(result);
        setError(null);
        setUpdatedAt(new Date());
      } catch (requestError) {
        if (active && !controller.signal.aborted) setError(requestError);
      } finally {
        if (active) {
          setLoading(false);
          timeoutId = window.setTimeout(load, POLL_INTERVAL_MS);
        }
      }
    };

    setLoading(dataRef.current === null);
    load();
    return () => {
      active = false;
      controller?.abort();
      window.clearTimeout(timeoutId);
    };
  }, [lotId, revision, retry]);

  const items = bookings?.items || [];
  const normalizedSearch = search.trim().toLocaleUpperCase();
  const filteredItems = items.filter(
    (booking) =>
      !normalizedSearch ||
      String(booking.id).includes(normalizedSearch) ||
      booking.plateNumber?.toLocaleUpperCase().includes(normalizedSearch),
  );
  const newBookings = items.filter((booking) =>
    newBookingIds.includes(String(booking.id)),
  );
  useEffect(() => {
    onNewBookingsChange?.(newBookings.length);
  }, [newBookings.length, onNewBookingsChange]);
  const selectedBooking = items.find(
    (booking) => String(booking.id) === String(selectedBookingId),
  );
  const busy = action.busy || captureBusy;

  const beginCheckIn = (booking) => {
    action.setError(null);
    setCompletedBooking(null);
    setSelectedBookingId(String(booking.id));
    setCapture({ ...emptyCapture(), plate: booking.plateNumber || "" });
  };

  const confirmCheckIn = (event) => {
    event.preventDefault();
    action.run(async () => {
      if (!selectedBooking) throw new Error("Đặt chỗ này không còn chờ xử lý.");
      if (!lotIsActive)
        throw new Error("Bãi đang đóng nhận xe. Không thể check-in booking.");
      if (!capture.file)
        throw new Error("Cần chụp ảnh biển số trước khi nhận xe đã đặt chỗ.");

      const plate = validatePlate(capture.plate);
      if (!platesMatch(plate, selectedBooking.plateNumber))
        throw new Error(
          `Biển số đã nhập không khớp với booking (${selectedBooking.plateNumber}). Hãy đối chiếu lại xe và ảnh trước khi xác nhận.`,
        );
      const imageUrl = await prepareCapture(capture, setCapture, lotId);
      if (!imageUrl) throw new Error("Chưa lưu được ảnh biển số xe.");

      const result = await staffApi.checkInBookingAtGate(
        selectedBooking.id,
        plate,
        imageUrl,
      );
      const session = result?.data;
      if (
        !session?.id ||
        Number(session.bookingId) !== Number(selectedBooking.id) ||
        session.currentSlotId
      )
        throw new Error(
          "Chưa chuyển được xe vào hàng chờ điều phối. Hãy tải lại danh sách trước khi thử lại.",
        );

      setCompletedBooking(selectedBooking);
      setSelectedBookingId(null);
      setCapture(emptyCapture());
      setNewBookingIds((current) =>
        current.filter((id) => id !== String(selectedBooking.id)),
      );
      onChanged?.();
    });
  };

  return (
    <section className="staff-panel staff-bookings" aria-labelledby="staff-bookings-title">
      <div className="staff-panel-heading">
        <div>
          <span className="staff-eyebrow">ĐẶT CHỖ TRỰC TUYẾN</span>
          <h2 id="staff-bookings-title">Xe đặt chỗ qua web</h2>
          <p>Đối chiếu booking, chụp biển số thực tế và chuyển xe sang hàng chờ điều phối.</p>
        </div>
        <span className="staff-count" aria-label={`${bookings?.totalCount ?? 0} đặt chỗ chờ`}>
          {bookings?.totalCount ?? "—"}
        </span>
      </div>
      {newBookings.length > 0 && (
        <Notice kind="warning">
          Có {newBookings.length} đặt chỗ mới. Danh sách được cập nhật tự động mỗi 15 giây.
        </Notice>
      )}
      {completedBooking && (
        <Notice kind="success">
          Đã xác nhận xe {completedBooking.plateNumber} qua cổng từ booking #
          {completedBooking.id}. Phiên xe đang chờ Staff Điều phối chọn ô đỗ.
        </Notice>
      )}
      <label className="staff-field">
        Tìm nhanh theo biển số hoặc mã đặt chỗ
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder="Ví dụ: 51A-12345 hoặc 1201"
          disabled={busy}
        />
      </label>
      <StaffError error={error} />
      {error && (
        <button
          className="staff-button staff-button--secondary"
          onClick={() => setRetry((value) => value + 1)}
        >
          Thử tải lại đặt chỗ
        </button>
      )}
      {loading && !bookings && <Notice>Đang tải đặt chỗ của bãi…</Notice>}
      {bookings && items.length === 0 && (
        <p className="staff-empty-small">Hiện không có đặt chỗ đang chờ xe đến.</p>
      )}
      {bookings && items.length > 0 && filteredItems.length === 0 && (
        <p className="staff-empty-small">Không tìm thấy biển số hoặc mã đặt chỗ phù hợp.</p>
      )}
      {filteredItems.length > 0 && (
        <div className="staff-booking-list" aria-label="Danh sách xe đã đặt chỗ">
          {filteredItems.map((booking) => {
            const isNew = newBookingIds.includes(String(booking.id));
            const vehicleName = [booking.vehicleBrand, booking.vehicleModel]
              .filter(Boolean)
              .join(" ");
            return (
              <article
                className={`staff-booking-item ${isNew ? "is-new" : ""}`}
                key={booking.id}
              >
                <div className="staff-booking-vehicle">
                  <div>
                    <span className="staff-eyebrow">ĐẶT CHỖ #{booking.id}</span>
                    <strong className="staff-plate">{booking.plateNumber}</strong>
                  </div>
                  {isNew && <span className="staff-booking-new">MỚI</span>}
                </div>
                <dl>
                  <div>
                    <dt>Người đặt</dt>
                    <dd>{booking.fullName || booking.username || "—"}</dd>
                  </div>
                  <div>
                    <dt>Điện thoại</dt>
                    <dd>
                      {booking.phoneNumber ? (
                        <a href={`tel:${booking.phoneNumber}`}>{booking.phoneNumber}</a>
                      ) : "—"}
                    </dd>
                  </div>
                  <div>
                    <dt>Loại xe</dt>
                    <dd>{vehicleName || "Chưa khai báo"}</dd>
                  </div>
                  <div>
                    <dt>Dự kiến đến</dt>
                    <dd>{formatTime(booking.startTime)}</dd>
                  </div>
                  <div>
                    <dt>Bãi đã đặt</dt>
                    <dd>{booking.lotName || `Bãi #${booking.lotId}`}</dd>
                  </div>
                </dl>
                {String(selectedBookingId) === String(booking.id) ? (
                  <form
                    className="staff-booking-checkin"
                    onSubmit={confirmCheckIn}
                    aria-label={`Xác nhận xe tại cổng booking ${booking.id}`}
                  >
                    <PlateCapture
                      value={capture}
                      onChange={setCapture}
                      disabled={busy}
                      requiredPhoto
                      title="Ảnh biển số xe tại cổng"
                      onWorking={(value) => {
                        setCaptureBusy(value);
                        onBusyChange?.(value);
                      }}
                      allowRecognition={false}
                    />
                    <p className="staff-hint">
                      Biển số đặt chỗ: <strong>{booking.plateNumber}</strong>.
                      Nhân viên cổng chỉ xác nhận xe đến; ô đỗ do Staff Điều phối gán.
                    </p>
                    <StaffError error={action.error} />
                    <div className="staff-inline-actions">
                      <button
                        type="submit"
                        className="staff-button staff-button--primary"
                        disabled={busy || !lotIsActive || !capture.file}
                      >
                        {action.busy ? "Đang xác nhận xe qua cổng…" : "Xác nhận xe đến & Chụp biển số"}
                      </button>
                      <button
                        type="button"
                        className="staff-text-button"
                        disabled={busy}
                        onClick={() => {
                          setSelectedBookingId(null);
                          setCapture(emptyCapture());
                          action.setError(null);
                        }}
                      >
                        Hủy
                      </button>
                    </div>
                  </form>
                ) : (
                  <button
                    type="button"
                    className="staff-button staff-button--secondary"
                    disabled={busy || !lotIsActive}
                    onClick={() => beginCheckIn(booking)}
                  >
                    Xác nhận xe đến & Chụp biển số
                  </button>
                )}
              </article>
            );
          })}
        </div>
      )}
      {updatedAt && (
        <p className="staff-booking-updated">
          Cập nhật lúc {updatedAt.toLocaleTimeString("vi-VN", {
            hour: "2-digit",
            minute: "2-digit",
          })}
        </p>
      )}
    </section>
  );
}
