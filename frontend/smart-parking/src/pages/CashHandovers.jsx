import { useEffect, useMemo, useState } from "react";
import { apiRequest } from "../services/api.js";
import { getAuthSession } from "../services/authSession.js";
import { operationsApiBaseConfigured, operationsRequest } from "../services/operationsApi.js";
import { Feedback, itemsOf } from "../components/WorkspaceUI.jsx";
import "../operations.css";

const shifts = [
  ["MORNING", "Ca sáng · 05:00–13:00"],
  ["AFTERNOON", "Ca chiều · 13:00–21:00"],
  ["NIGHT", "Ca đêm · 21:00–05:00"],
];
const shiftLabels = Object.fromEntries(shifts);
const statusLabels = {
  AWAITING_CONFIRMATIONS: "Chờ xác nhận",
  COMPLETED: "Đã bàn giao",
  DISPUTED: "Lệch tiền",
  REJECTED: "Bị từ chối",
};

const nextShift = (shift, all) => {
  if (!shift) return null;
  const nextCode = { MORNING: "AFTERNOON", AFTERNOON: "NIGHT", NIGHT: "MORNING" }[shift.code];
  const [year, month, day] = shift.businessDate.split("-").map(Number);
  const expectedDate = new Date(Date.UTC(year, month - 1, day + (shift.code === "NIGHT" ? 1 : 0)))
    .toISOString()
    .slice(0, 10);
  return (
    all.find(
      (item) => item.lotId === shift.lotId && item.code === nextCode && item.businessDate === expectedDate
    ) || null
  );
};

const money = (value) =>
  value == null
    ? "Chưa đếm"
    : new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(
        value
      );

function actorId() {
  const id = Number(getAuthSession()?.userProfile?.id);
  return Number.isInteger(id) ? id : null;
}

export default function CashHandovers() {
  const role = getAuthSession()?.userProfile?.role;
  const manager = role === "MANAGER";
  const currentId = actorId();

  const [lots, setLots] = useState([]);
  const [lotId, setLotId] = useState("");
  const [shiftRecords, setShiftRecords] = useState([]);
  const [items, setItems] = useState([]);
  const [statusFilter, setStatusFilter] = useState("ALL");
  const [showCreateForm, setShowCreateForm] = useState(false);

  const [outgoingShiftId, setOutgoingShiftId] = useState("");
  const [outgoingStaffId, setOutgoingStaffId] = useState("");
  const [incomingStaffId, setIncomingStaffId] = useState("");

  const [actionAmounts, setActionAmounts] = useState({});
  const [actionReasons, setActionReasons] = useState({});

  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);

  const outgoingShift = shiftRecords.find((item) => item.id === outgoingShiftId);
  const incomingShift = useMemo(() => nextShift(outgoingShift, shiftRecords), [outgoingShift, shiftRecords]);
  const outgoingStaffChoices = outgoingShift?.assignments || [];
  const incomingStaffChoices = incomingShift?.assignments || [];

  useEffect(() => {
    let active = true;
    apiRequest("/v1/staff-assignments/my-lots")
      .then((result) => {
        if (!active) return;
        const assignedLots = itemsOf(result?.data);
        setLots(assignedLots);
        setLotId((current) => current || String(assignedLots[0]?.lotId || ""));
      })
      .catch((e) => {
        if (active) setError(e.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (!lotId) return;
    let active = true;
    setError("");
    operationsRequest(`/handovers/workspace?lotId=${lotId}`)
      .then((result) => {
        if (!active) return;
        setItems(itemsOf(result?.data?.handovers));
        const shiftValues = itemsOf(result?.data?.shifts);
        setShiftRecords(shiftValues);
        const eligibleOutgoing = shiftValues.find(
          (item) => item.status === "IN_PROGRESS" && nextShift(item, shiftValues)?.status === "PLANNED"
        );
        setOutgoingShiftId((current) =>
          shiftValues.some((item) => item.id === current) ? current : eligibleOutgoing?.id || ""
        );
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, [lotId, manager, revision]);

  async function run(operation, successMessage) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await operation();
      setNotice(successMessage || "Đã cập nhật handover.");
      setRevision((n) => n + 1);
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  }

  const filteredItems = useMemo(() => {
    if (statusFilter === "ALL") return items;
    return items.filter((x) => x.status === statusFilter);
  }, [items, statusFilter]);

  if (!operationsApiBaseConfigured())
    return (
      <section className="workspace-panel">
        <h2>Chưa cấu hình Operations API</h2>
        <p>Thiết lập VITE_OPERATIONS_API_BASE_URL và chạy Operations Service để dùng handover.</p>
      </section>
    );

  return (
    <div className="ops-page-container">
      {/* TOOLBAR / FILTER GỌN GÀNG (Không có header to) */}
      <div className="ops-toolbar">
        <div className="ops-toolbar__filters">
          <div className="ops-toolbar__item">
            <span>Bãi:</span>
            <select
              value={lotId}
              disabled={loading || busy}
              onChange={(e) => setLotId(e.target.value)}
            >
              {lots.map((lot) => (
                <option key={lot.lotId} value={lot.lotId}>
                  {lot.lotName}
                </option>
              ))}
            </select>
          </div>

          <div className="ops-toolbar__item">
            <span>Trạng thái:</span>
            <select
              value={statusFilter}
              disabled={loading || busy}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="ALL">Tất cả ({items.length})</option>
              <option value="AWAITING_CONFIRMATIONS">Chờ xác nhận</option>
              <option value="COMPLETED">Đã bàn giao</option>
              <option value="DISPUTED">Lệch tiền</option>
            </select>
          </div>
        </div>

        <div className="ops-toolbar__actions">
          {manager && (
            <button
              type="button"
              className="ops-btn-sm ops-btn-sm--primary"
              disabled={busy || loading}
              onClick={() => setShowCreateForm((prev) => !prev)}
            >
              {showCreateForm ? "Đóng form" : "+ Tạo bàn giao mới"}
            </button>
          )}

          <button
            type="button"
            className="ops-btn-sm ops-btn-sm--outline"
            disabled={busy || loading}
            onClick={() => setRevision((n) => n + 1)}
          >
            Làm mới
          </button>
        </div>
      </div>

      <Feedback loading={loading} error={error} message={notice} />

      {/* FORM TẠO BÀN GIAO MỚI (Thu gọn được) */}
      {showCreateForm && manager && lotId && (
        <section className="workspace-panel" style={{ padding: "16px 20px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
            <h3 style={{ margin: 0, fontSize: "15px" }}>Tạo biên bản bàn giao ca</h3>
            <span style={{ fontSize: "12px", color: "#64748b" }}>
              Khởi tạo lượt bàn giao tiền mặt giữa 2 ca liên tiếp
            </span>
          </div>

          <form
            className="workspace-form"
            onSubmit={(e) => {
              e.preventDefault();
              run(async () => {
                await operationsRequest("/handovers", {
                  method: "POST",
                  body: JSON.stringify({
                    outgoingShiftId,
                    incomingShiftId: incomingShift?.id,
                    outgoingStaffId: Number(outgoingStaffId),
                    incomingStaffId: Number(incomingStaffId),
                  }),
                });
                setShowCreateForm(false);
              }, "Đã tạo handover; chờ người giao và nhận xác nhận.");
            }}
          >
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "10px" }}>
              <label>
                <span>Ca giao</span>
                <select
                  required
                  value={outgoingShiftId}
                  onChange={(e) => {
                    setOutgoingShiftId(e.target.value);
                    setOutgoingStaffId("");
                    setIncomingStaffId("");
                  }}
                >
                  <option value="">Chọn ca đang diễn ra</option>
                  {shiftRecords
                    .filter(
                      (item) =>
                        item.status === "IN_PROGRESS" && nextShift(item, shiftRecords)?.status === "PLANNED"
                    )
                    .map((item) => (
                      <option key={item.id} value={item.id}>
                        {shiftLabels[item.code]} · {item.businessDate}
                      </option>
                    ))}
                </select>
              </label>

              <label>
                <span>Ca nhận</span>
                <select required value={incomingShift?.id || ""} disabled>
                  <option value="">
                    {incomingShift
                      ? `${shiftLabels[incomingShift.code]} · ${incomingShift.businessDate}`
                      : "Chưa có ca kế tiếp đã lên lịch"}
                  </option>
                </select>
              </label>

              <label>
                <span>Nhân viên giao</span>
                <select
                  required
                  value={outgoingStaffId}
                  onChange={(e) => setOutgoingStaffId(e.target.value)}
                >
                  <option value="">Chọn người trong ca giao</option>
                  {outgoingStaffChoices.map((person) => (
                    <option key={person.staffId} value={person.staffId}>
                      Nhân viên #{person.staffId}
                    </option>
                  ))}
                </select>
              </label>

              <label>
                <span>Nhân viên nhận</span>
                <select
                  required
                  value={incomingStaffId}
                  onChange={(e) => setIncomingStaffId(e.target.value)}
                >
                  <option value="">Chọn người trong ca nhận</option>
                  {incomingStaffChoices.map((person) => (
                    <option
                      key={person.staffId}
                      value={person.staffId}
                      disabled={String(person.staffId) === outgoingStaffId}
                    >
                      Nhân viên #{person.staffId}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "10px" }}>
              <button
                type="button"
                className="ops-btn-sm ops-btn-sm--outline"
                onClick={() => setShowCreateForm(false)}
              >
                Hủy
              </button>
              <button
                type="submit"
                className="ops-btn-sm ops-btn-sm--primary"
                disabled={
                  busy ||
                  !outgoingShift ||
                  !incomingShift ||
                  !outgoingStaffId ||
                  !incomingStaffId ||
                  outgoingStaffId === incomingStaffId
                }
              >
                Tạo bàn giao
              </button>
            </div>
          </form>
        </section>
      )}

      {/* DANH SÁCH LỊCH SỬ BÀN GIAO DẠNG BẢNG GỌN GÀNG DỄ NHÌN */}
      <div className="ops-table-container">
        <table className="ops-table">
          <thead>
            <tr>
              <th>Ngày & Ca bàn giao</th>
              <th>Người giao</th>
              <th>Đếm khi giao</th>
              <th>Người nhận</th>
              <th>Đếm khi nhận</th>
              <th>Chênh lệch</th>
              <th>Trạng thái</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {filteredItems.length === 0 ? (
              <tr>
                <td colSpan="8" style={{ textAlign: "center", padding: "24px", color: "#64748b" }}>
                  Chưa có lượt bàn giao nào phù hợp với bộ lọc.
                </td>
              </tr>
            ) : (
              filteredItems.map((item) => {
                const isOutgoing = currentId === item.outgoingStaffId;
                const isIncoming = currentId === item.incomingStaffId;
                const canRespond = item.status === "AWAITING_CONFIRMATIONS" && (isOutgoing || isIncoming);
                const ownAmount = isOutgoing ? item.outgoingCountedAmount : item.incomingCountedAmount;

                return (
                  <tr key={item.id}>
                    <td>
                      <strong>{item.businessDate}</strong>
                      <div style={{ fontSize: "11px", color: "#64748b" }}>
                        {shiftLabels[item.fromShiftCode]} → {shiftLabels[item.toShiftCode]}
                      </div>
                    </td>
                    <td>
                      <span style={{ fontWeight: 600 }}>NV #{item.outgoingStaffId}</span>
                      {isOutgoing && <span style={{ fontSize: "10px", color: "#2563eb", marginLeft: "4px" }}>(Bạn)</span>}
                    </td>
                    <td>
                      <span style={{ fontWeight: 600 }}>{money(item.outgoingCountedAmount)}</span>
                    </td>
                    <td>
                      <span style={{ fontWeight: 600 }}>NV #{item.incomingStaffId}</span>
                      {isIncoming && <span style={{ fontSize: "10px", color: "#2563eb", marginLeft: "4px" }}>(Bạn)</span>}
                    </td>
                    <td>
                      <span style={{ fontWeight: 600 }}>{money(item.incomingCountedAmount)}</span>
                    </td>
                    <td>
                      <span
                        style={{
                          fontWeight: 650,
                          color: item.variance == null ? "#64748b" : item.variance === 0 ? "#166534" : "#dc2626",
                        }}
                      >
                        {money(item.variance)}
                      </span>
                      {item.disputeReason && (
                        <div style={{ fontSize: "11px", color: "#b91c1c" }}>
                          Lý do: {item.disputeReason}
                        </div>
                      )}
                    </td>
                    <td>
                      <span
                        className={`ops-badge ${
                          item.status === "COMPLETED"
                            ? "ops-badge--success"
                            : item.status === "AWAITING_CONFIRMATIONS"
                            ? "ops-badge--warning"
                            : "ops-badge--danger"
                        }`}
                      >
                        {statusLabels[item.status] || item.status}
                      </span>
                    </td>
                    <td>
                      {canRespond && ownAmount == null ? (
                        <div style={{ display: "flex", flexDirection: "column", gap: "4px" }}>
                          <input
                            type="number"
                            min="0"
                            step="1"
                            style={{
                              padding: "4px 6px",
                              fontSize: "12px",
                              border: "1px solid #cbd5e1",
                              borderRadius: "4px",
                              width: "120px",
                            }}
                            placeholder="Số tiền bạn đếm"
                            value={actionAmounts[item.id] || ""}
                            onChange={(e) =>
                              setActionAmounts((v) => ({ ...v, [item.id]: e.target.value }))
                            }
                          />
                          <div style={{ display: "flex", gap: "4px" }}>
                            <button
                              type="button"
                              className="ops-btn-sm ops-btn-sm--primary"
                              style={{ padding: "4px 8px", fontSize: "11px" }}
                              disabled={busy || !actionAmounts[item.id]}
                              onClick={() =>
                                run(async () => {
                                  await operationsRequest(`/handovers/${item.id}/confirm`, {
                                    method: "POST",
                                    body: JSON.stringify({ countedAmount: Number(actionAmounts[item.id]) }),
                                  });
                                  setActionAmounts((v) => ({ ...v, [item.id]: "" }));
                                }, "Đã xác nhận số tiền bàn giao.")
                              }
                            >
                              Khớp tiền
                            </button>
                            <button
                              type="button"
                              className="ops-btn-sm ops-btn-sm--outline"
                              style={{ padding: "4px 8px", fontSize: "11px", color: "#dc2626" }}
                              disabled={busy}
                              onClick={() => {
                                const reason = prompt("Nhập lý do không khớp / từ chối bàn giao:");
                                if (reason && reason.trim().length >= 3) {
                                  run(async () => {
                                    await operationsRequest(`/handovers/${item.id}/dispute`, {
                                      method: "POST",
                                      body: JSON.stringify({ reason: reason.trim() }),
                                    });
                                  }, "Đã báo có vấn đề bàn giao.");
                                }
                              }}
                            >
                              Báo lỗi
                            </button>
                          </div>
                        </div>
                      ) : canRespond && ownAmount != null ? (
                        <span style={{ fontSize: "11px", color: "#166534" }}>Đã xác nhận, chờ đối phương</span>
                      ) : (
                        <span style={{ fontSize: "12px", color: "#94a3b8" }}>—</span>
                      )}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
