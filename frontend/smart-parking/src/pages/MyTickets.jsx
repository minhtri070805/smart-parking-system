import { useState } from "react";
import RequireCustomer from "../components/RequireCustomer.jsx";
import {
  Feedback,
  Pager,
  dateLabel,
  itemsOf,
  query,
  statusLabel,
  useResource,
  useMutation,
  write,
} from "../components/WorkspaceUI.jsx";

const bookingStatusLabels = {
  PENDING: "Chờ xe đến",
  CHECKED_IN: "Đã qua cổng · chờ điều phối",
  PARKED: "Đang đỗ",
  CANCELLED: "Đã hủy",
  EXPIRED: "Đã hết hạn",
};

const statusColor = (status) =>
  ({
    PENDING: "#2563eb",
    CHECKED_IN: "#d97706",
    PARKED: "#059669",
    CANCELLED: "#94a3b8",
    EXPIRED: "#ef4444",
  })[status] || "#64748b";

function canCancel(booking) {
  return (
    booking.status === "PENDING" && new Date(booking.startTime) > new Date()
  );
}

function Tickets() {
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState({ status: "", fromDate: "", toDate: "" });
  const [revision, refresh] = useState(0);
  const [selected, setSelected] = useState(null);
  const [cancelReason, setCancelReason] = useState("");
  const cancel = useMutation();

  const invalidDates = Boolean(
    filter.fromDate && filter.toDate && filter.fromDate > filter.toDate,
  );

  const list = useResource(
    invalidDates
      ? null
      : `/v1/bookings/my?${query({
          ...filter,
          fromDate: filter.fromDate
            ? new Date(`${filter.fromDate}T00:00:00`).toISOString()
            : "",
          toDate: filter.toDate
            ? new Date(`${filter.toDate}T23:59:59.999`).toISOString()
            : "",
          pageNumber: page,
          pageSize: 10,
        })}`,
    revision,
  );

  const detail = useResource(
    selected ? `/v1/bookings/${selected}` : null,
    revision,
  );

  const detailData = detail.data;

  const update = (key, value) => {
    setFilter((f) => ({ ...f, [key]: value }));
    setPage(1);
    setSelected(null);
  };

  const handleCancel = (bookingId) => {
    cancel.run(
      () =>
        write(`/v1/bookings/${bookingId}/cancel`, {
          reason: cancelReason.trim() || null,
        }),
      () => {
        setCancelReason("");
        setSelected(null);
        refresh((n) => n + 1);
      },
    );
  };

  return (
    <section className="workspace container">
      <section className="workspace-panel">
        <div className="workspace-heading">
          <div>
            <h2>Vé của tôi</h2>
            <p style={{ color: "#64748b", margin: "4px 0 0" }}>
              Lịch sử đặt vé và quản lý vé đã đặt.
            </p>
          </div>
          <button
            className="btn btn--outline"
            disabled={list.loading}
            onClick={() => refresh((n) => n + 1)}
          >
            Làm mới
          </button>
        </div>

        <div className="workspace-filters">
          <label>
            Trạng thái
            <select
              value={filter.status}
              onChange={(e) => update("status", e.target.value)}
            >
              <option value="">Tất cả</option>
              <option value="PENDING">Chờ xe đến</option>
              <option value="CHECKED_IN">Đã qua cổng</option>
              <option value="PARKED">Đang đỗ</option>
              <option value="CANCELLED">Đã hủy</option>
              <option value="EXPIRED">Đã hết hạn</option>
            </select>
          </label>
          <label>
            Từ ngày
            <input
              type="date"
              value={filter.fromDate}
              onChange={(e) => update("fromDate", e.target.value)}
            />
          </label>
          <label>
            Đến ngày
            <input
              type="date"
              value={filter.toDate}
              onChange={(e) => update("toDate", e.target.value)}
            />
          </label>
        </div>

        <Feedback
          loading={list.loading}
          error={
            invalidDates
              ? "Ngày kết thúc phải từ ngày bắt đầu trở đi."
              : list.error
          }
        />

        {!list.loading &&
          !list.error &&
          !invalidDates &&
          itemsOf(list.data).length === 0 && (
            <div className="workspace-empty">
              <h2>Chưa có vé nào</h2>
              <p>Vé sẽ xuất hiện khi bạn đặt chỗ tại một bãi đỗ xe.</p>
            </div>
          )}

        <div className="workspace-table">
          <table>
            <thead>
              <tr>
                <th>Mã vé</th>
                <th>Biển số · Bãi đỗ</th>
                <th>Thời gian hẹn</th>
                <th>Trạng thái</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {itemsOf(list.data).map((b) => (
                <tr key={b.id}>
                  <td>
                    <strong>#{b.id}</strong>
                  </td>
                  <td>
                    <strong>{b.plateNumber}</strong>
                    <small>{b.lotName || `Bãi #${b.lotId}`}</small>
                  </td>
                  <td>
                    {dateLabel(b.startTime)}
                    {b.endTime && (
                      <small>→ {dateLabel(b.endTime)}</small>
                    )}
                  </td>
                  <td>
                    <span
                      className="workspace-badge"
                      style={{
                        background: statusColor(b.status) + "18",
                        color: statusColor(b.status),
                        border: `1px solid ${statusColor(b.status)}40`,
                      }}
                    >
                      {bookingStatusLabels[b.status] || statusLabel(b.status)}
                    </span>
                  </td>
                  <td>
                    <button
                      className="btn btn--outline"
                      onClick={() => {
                        setSelected(b.id);
                        setCancelReason("");
                      }}
                    >
                      Chi tiết
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <Pager
          page={page}
          data={list.data}
          onChange={setPage}
          disabled={list.loading || invalidDates}
        />
      </section>

      {selected && (
        <section className="workspace-panel">
          <div className="workspace-heading">
            <h2>Vé #{selected}</h2>
            <button
              className="btn btn--outline"
              onClick={() => setSelected(null)}
            >
              Đóng
            </button>
          </div>

          <Feedback loading={detail.loading} error={detail.error} />

          {detailData && (
            <>
              <dl className="workspace-detail">
                {Object.entries({
                  "Mã vé": `#${detailData.id}`,
                  "Biển số xe": detailData.plateNumber,
                  "Bãi đỗ": detailData.lotName || `Bãi #${detailData.lotId}`,
                  "Địa chỉ": detailData.lotAddress || "—",
                  "Trạng thái":
                    bookingStatusLabels[detailData.status] ||
                    statusLabel(detailData.status),
                  "Thanh toán": detailData.paymentMode === "PREPAY"
                    ? `${detailData.paymentStatus === "PAID" ? "Đã trả cọc" : "Chờ thanh toán cọc"}${detailData.prepaidAmount != null ? ` · ${Number(detailData.prepaidAmount).toLocaleString("vi-VN")} ₫` : ""}`
                    : "Thanh toán tại quầy khi checkout",
                  "Dự kiến vào": dateLabel(detailData.startTime),
                  "Dự kiến ra": dateLabel(detailData.endTime),
                  "Đặt lúc": dateLabel(detailData.createdAt),
                  ...(detailData.checkedInAt
                    ? { "Qua cổng lúc": dateLabel(detailData.checkedInAt) }
                    : {}),
                  ...(detailData.cancelledAt
                    ? { "Hủy lúc": dateLabel(detailData.cancelledAt) }
                    : {}),
                  ...(detailData.cancellationReason
                    ? { "Lý do hủy": detailData.cancellationReason }
                    : {}),
                  ...(detailData.notes
                    ? { "Ghi chú": detailData.notes }
                    : {}),
                  ...(detailData.vehicleBrand || detailData.vehicleModel
                    ? {
                        "Xe": [detailData.vehicleBrand, detailData.vehicleModel]
                          .filter(Boolean)
                          .join(" "),
                      }
                    : {}),
                  ...(detailData.vehicleColor
                    ? { "Màu xe": detailData.vehicleColor }
                    : {}),
                }).map(([k, v]) => (
                  <div key={k}>
                    <dt>{k}</dt>
                    <dd>{v}</dd>
                  </div>
                ))}
              </dl>

              {detailData.paymentInvoiceId && (
                <section className="workspace-panel workspace-invoice" aria-label="Hóa đơn đặt cọc">
                  <div className="workspace-heading">
                    <div>
                      <h3>Hóa đơn thanh toán cọc</h3>
                      <p>Hóa đơn VNPay liên kết với vé #{detailData.id}.</p>
                    </div>
                    <span className="workspace-badge">{detailData.paymentStatus === "PAID" ? "Đã thanh toán" : "Chờ thanh toán"}</span>
                  </div>
                  <dl className="workspace-detail">
                    <div><dt>Mã hóa đơn</dt><dd>{detailData.paymentInvoiceId}</dd></div>
                    <div><dt>Tiền cọc</dt><dd>{detailData.prepaidAmount != null ? `${Number(detailData.prepaidAmount).toLocaleString("vi-VN")} ₫` : "—"}</dd></div>
                    <div><dt>Mã giao dịch VNPay</dt><dd>{detailData.paymentTransactionRef || "Chưa thanh toán"}</dd></div>
                    <div><dt>Thanh toán lúc</dt><dd>{dateLabel(detailData.paymentPaidAt)}</dd></div>
                  </dl>
                </section>
              )}

              <Feedback
                error={cancel.error}
                message={cancel.message}
              />

              {canCancel(detailData) && (
                <div
                  style={{
                    marginTop: 16,
                    padding: 16,
                    background: "#fef2f2",
                    borderRadius: 12,
                    border: "1px solid #fecaca",
                  }}
                >
                  <strong style={{ color: "#b91c1c" }}>Hủy vé này?</strong>
                  <p
                    style={{
                      color: "#64748b",
                      fontSize: 14,
                      margin: "4px 0 12px",
                    }}
                  >
                    Bạn chỉ có thể hủy vé khi chưa tới giờ hẹn. Hành động này
                    không thể hoàn tác.
                  </p>
                  <label>
                    Lý do hủy (không bắt buộc)
                    <textarea
                      value={cancelReason}
                      onChange={(e) => setCancelReason(e.target.value)}
                      maxLength={500}
                      rows={2}
                      style={{ width: "100%", marginTop: 4 }}
                    />
                  </label>
                  <button
                    className="btn btn--primary"
                    style={{
                      marginTop: 8,
                      background: "#dc2626",
                      borderColor: "#dc2626",
                    }}
                    disabled={cancel.busy}
                    onClick={() => handleCancel(detailData.id)}
                  >
                    {cancel.busy ? "Đang hủy..." : "Xác nhận hủy vé"}
                  </button>
                </div>
              )}

              {detailData.status === "PENDING" &&
                new Date(detailData.startTime) <= new Date() && (
                  <p
                    className="workspace-note"
                    style={{ color: "#d97706", marginTop: 12 }}
                  >
                    Vé này đã qua giờ hẹn, không thể hủy. Liên hệ nhân viên nếu
                    cần hỗ trợ.
                  </p>
                )}

              {detailData.status === "EXPIRED" && (
                <p
                  className="workspace-note"
                  style={{ color: "#dc2626", marginTop: 12 }}
                >
                  Vé tự động hết hạn vì xe không đến trong thời gian cho phép
                  sau giờ hẹn. Vé hết hạn không được hoàn tiền; biển số xe này
                  có thể đặt vé mới.
                </p>
              )}
            </>
          )}
        </section>
      )}
    </section>
  );
}

export default function MyTickets() {
  return (
    <RequireCustomer>
      <Tickets />
    </RequireCustomer>
  );
}
