import { useEffect, useMemo, useState } from "react";
import { apiRequest } from "../services/api.js";
import { getAuthSession } from "../services/authSession.js";
import { operationsApiBaseConfigured, operationsRequest } from "../services/operationsApi.js";
import { Feedback, itemsOf } from "../components/WorkspaceUI.jsx";
import "../operations.css";

const labels = {
  MORNING: "Ca sáng",
  AFTERNOON: "Ca chiều",
  NIGHT: "Ca đêm",
  PENDING_REVIEW: "Chờ duyệt",
  APPROVED: "Đã duyệt",
};

const money = (v) =>
  new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(
    Number(v || 0)
  );

const isoNow = () => new Date().toISOString().slice(0, 16);

export default function CashReconciliation() {
  const [lots, setLots] = useState([]);
  const [lotId, setLotId] = useState("");
  const [shifts, setShifts] = useState([]);
  const [items, setItems] = useState([]);
  const [statusFilter, setStatusFilter] = useState("ALL");
  const [showCreateForm, setShowCreateForm] = useState(false);

  const [shiftId, setShiftId] = useState("");
  const [form, setForm] = useState({
    openingCash: "0",
    cashReceipts: "0",
    cashRefunds: "0",
    cashDisbursements: "0",
    countedClosingCash: "",
    paymentSummaryReference: "",
    paymentSummaryCapturedAtUtc: isoNow(),
    varianceReason: "",
  });

  const [approvalNotes, setApprovalNotes] = useState({});
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);

  const existingShiftIds = useMemo(() => new Set(items.map((x) => x.shiftId)), [items]);
  const eligible = shifts.filter((x) => x.status === "COMPLETED" && !existingShiftIds.has(x.id));
  const expected =
    Number(form.openingCash || 0) +
    Number(form.cashReceipts || 0) -
    Number(form.cashRefunds || 0) -
    Number(form.cashDisbursements || 0);
  const variance = Number(form.countedClosingCash || 0) - expected;
  const currentManagerId = Number(getAuthSession()?.userProfile?.id);

  useEffect(() => {
    let active = true;
    apiRequest("/v1/staff-assignments/my-lots")
      .then((r) => {
        if (!active) return;
        const values = itemsOf(r?.data);
        setLots(values);
        setLotId((v) => v || String(values[0]?.lotId || ""));
      })
      .catch((e) => active && setError(e.message))
      .finally(() => active && setLoading(false));
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (!lotId) return;
    let active = true;
    setError("");
    operationsRequest(`/reconciliations/workspace?lotId=${lotId}`)
      .then((r) => {
        if (!active) return;
        const rows = itemsOf(r?.data?.reconciliations);
        setItems(rows);
        setShifts(itemsOf(r?.data?.completedShifts));
        setShiftId("");
      })
      .catch((e) => active && setError(e.message));
    return () => { active = false; };
  }, [lotId, revision]);

  async function run(action, message) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await action();
      setNotice(message);
      setRevision((x) => x + 1);
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  }

  function update(key, value) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  const filteredItems = useMemo(() => {
    if (statusFilter === "ALL") return items;
    return items.filter((x) => x.status === statusFilter);
  }, [items, statusFilter]);

  if (!operationsApiBaseConfigured())
    return (
      <section className="workspace-panel">
        <h2>Chưa cấu hình Operations API</h2>
        <p>Thiết lập VITE_OPERATIONS_API_BASE_URL để dùng đối soát ca.</p>
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
              disabled={busy || loading}
              onChange={(e) => setLotId(e.target.value)}
            >
              {lots.map((x) => (
                <option key={x.lotId} value={x.lotId}>
                  {x.lotName}
                </option>
              ))}
            </select>
          </div>

          <div className="ops-toolbar__item">
            <span>Trạng thái:</span>
            <select
              value={statusFilter}
              disabled={busy || loading}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="ALL">Tất cả ({items.length})</option>
              <option value="PENDING_REVIEW">Chờ duyệt</option>
              <option value="APPROVED">Đã duyệt</option>
            </select>
          </div>
        </div>

        <div className="ops-toolbar__actions">
          <button
            type="button"
            className="ops-btn-sm ops-btn-sm--primary"
            disabled={busy || loading}
            onClick={() => setShowCreateForm((prev) => !prev)}
          >
            {showCreateForm ? "Đóng form lập phiếu" : "+ Lập phiếu đối soát"}
          </button>

          <button
            type="button"
            className="ops-btn-sm ops-btn-sm--outline"
            disabled={busy || loading}
            onClick={() => setRevision((x) => x + 1)}
          >
            Làm mới
          </button>
        </div>
      </div>

      <Feedback loading={loading} error={error} message={notice} />

      {/* FORM LẬP PHIẾU ĐỐI SOÁT GỌN GÀNG (Thu gọn được) */}
      {showCreateForm && lotId && (
        <section className="workspace-panel" style={{ padding: "16px 20px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
            <h3 style={{ margin: 0, fontSize: "15px" }}>Lập phiếu đối soát tiền mặt</h3>
            <span style={{ fontSize: "12px", color: "#64748b" }}>
              Nhập mã báo cáo/export Payment và kiểm đếm
            </span>
          </div>

          {eligible.length === 0 ? (
            <p className="workspace-note" style={{ margin: 0 }}>
              Chưa có ca hoàn tất nào đang chờ đối soát cho bãi này.
            </p>
          ) : (
            <form
              className="workspace-form"
              onSubmit={(e) => {
                e.preventDefault();
                run(async () => {
                  await operationsRequest("/reconciliations", {
                    method: "POST",
                    body: JSON.stringify({
                      shiftId,
                      openingCash: Number(form.openingCash),
                      cashReceipts: Number(form.cashReceipts),
                      cashRefunds: Number(form.cashRefunds),
                      cashDisbursements: Number(form.cashDisbursements),
                      countedClosingCash: Number(form.countedClosingCash),
                      paymentSummaryReference: form.paymentSummaryReference,
                      paymentSummaryCapturedAtUtc: new Date(form.paymentSummaryCapturedAtUtc).toISOString(),
                      varianceReason: form.varianceReason || null,
                    }),
                  });
                  setForm((f) => ({
                    ...f,
                    countedClosingCash: "",
                    paymentSummaryReference: "",
                    varianceReason: "",
                  }));
                  setShowCreateForm(false);
                }, "Đã gửi phiếu đối soát chờ Manager khác duyệt.");
              }}
            >
              <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: "10px" }}>
                <label>
                  <span>Ca đã hoàn tất</span>
                  <select required value={shiftId} onChange={(e) => setShiftId(e.target.value)}>
                    <option value="">Chọn ca</option>
                    {eligible.map((x) => (
                      <option key={x.id} value={x.id}>
                        {labels[x.code]} · {x.businessDate}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  <span>Tiền đầu ca</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    required
                    value={form.openingCash}
                    onChange={(e) => update("openingCash", e.target.value)}
                  />
                </label>

                <label>
                  <span>Thu tiền mặt (Payment)</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    required
                    value={form.cashReceipts}
                    onChange={(e) => update("cashReceipts", e.target.value)}
                  />
                </label>

                <label>
                  <span>Hoàn tiền mặt</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    required
                    value={form.cashRefunds}
                    onChange={(e) => update("cashRefunds", e.target.value)}
                  />
                </label>

                <label>
                  <span>Chi tiền mặt đã duyệt</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    required
                    value={form.cashDisbursements}
                    onChange={(e) => update("cashDisbursements", e.target.value)}
                  />
                </label>

                <label>
                  <span>Tiền cuối ca kiểm đếm</span>
                  <input
                    type="number"
                    min="0"
                    step="1"
                    required
                    value={form.countedClosingCash}
                    onChange={(e) => update("countedClosingCash", e.target.value)}
                  />
                </label>

                <label>
                  <span>Mã báo cáo Payment</span>
                  <input
                    required
                    minLength="3"
                    maxLength="200"
                    value={form.paymentSummaryReference}
                    onChange={(e) => update("paymentSummaryReference", e.target.value)}
                  />
                </label>

                <label>
                  <span>Thời điểm chụp</span>
                  <input
                    type="datetime-local"
                    required
                    value={form.paymentSummaryCapturedAtUtc}
                    onChange={(e) => update("paymentSummaryCapturedAtUtc", e.target.value)}
                  />
                </label>

                <label style={{ gridColumn: "1 / -1" }}>
                  <span>Lý do chênh lệch {variance !== 0 && <b style={{ color: "#ef4444" }}>· Bắt buộc khi lệch</b>}</span>
                  <input
                    maxLength="500"
                    placeholder="Giải trình lý do nếu số tiền kiểm đếm không khớp với dự kiến"
                    value={form.varianceReason}
                    onChange={(e) => update("varianceReason", e.target.value)}
                  />
                </label>
              </div>

              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "10px" }}>
                <div style={{ fontSize: "13px" }}>
                  Dự kiến: <strong>{money(expected)}</strong> · Chênh lệch:{" "}
                  <strong style={{ color: variance !== 0 ? "#ef4444" : "#16a34a" }}>
                    {money(variance)}
                  </strong>
                </div>

                <div style={{ display: "flex", gap: "8px" }}>
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
                      !shiftId ||
                      form.countedClosingCash === "" ||
                      expected < 0 ||
                      (variance !== 0 && !form.varianceReason.trim())
                    }
                  >
                    Gửi duyệt
                  </button>
                </div>
              </div>
            </form>
          )}
        </section>
      )}

      {/* DANH SÁCH LỊCH SỬ DẠNG BẢNG GỌN GÀNG DỄ NHÌN */}
      <div className="ops-table-container">
        <table className="ops-table">
          <thead>
            <tr>
              <th>Ngày & Ca</th>
              <th>Mã báo cáo</th>
              <th>Đầu ca</th>
              <th>Thu mặt</th>
              <th>Hoàn/Chi</th>
              <th>Dự kiến</th>
              <th>Thực đếm</th>
              <th>Chênh lệch</th>
              <th>Trạng thái</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {filteredItems.length === 0 ? (
              <tr>
                <td colSpan="10" style={{ textAlign: "center", padding: "24px", color: "#64748b" }}>
                  Không có phiếu đối soát nào phù hợp với bộ lọc.
                </td>
              </tr>
            ) : (
              filteredItems.map((x) => (
                <tr key={x.id}>
                  <td>
                    <strong>{x.businessDate}</strong>
                    <div style={{ fontSize: "11px", color: "#64748b" }}>
                      {labels[x.shiftCode]}
                    </div>
                  </td>
                  <td>
                    <span style={{ fontFamily: "monospace", fontSize: "12px" }}>
                      {x.paymentSummaryReference}
                    </span>
                  </td>
                  <td>{money(x.openingCash)}</td>
                  <td style={{ color: "#166534", fontWeight: 600 }}>+{money(x.cashReceipts)}</td>
                  <td style={{ color: "#991b1b" }}>
                    -{money(Number(x.cashRefunds || 0) + Number(x.cashDisbursements || 0))}
                  </td>
                  <td>{money(x.expectedClosingCash)}</td>
                  <td style={{ fontWeight: 600 }}>{money(x.countedClosingCash)}</td>
                  <td>
                    <span
                      style={{
                        fontWeight: 650,
                        color: x.variance === 0 ? "#166534" : "#dc2626",
                      }}
                    >
                      {money(x.variance)}
                    </span>
                    {x.varianceReason && (
                      <div style={{ fontSize: "11px", color: "#b91c1c" }}>
                        {x.varianceReason}
                      </div>
                    )}
                  </td>
                  <td>
                    <span
                      className={`ops-badge ${
                        x.status === "APPROVED"
                          ? "ops-badge--success"
                          : "ops-badge--warning"
                      }`}
                    >
                      {labels[x.status] || x.status}
                    </span>
                    {x.approvalNote && (
                      <div style={{ fontSize: "11px", color: "#64748b" }}>
                        {x.approvalNote}
                      </div>
                    )}
                  </td>
                  <td>
                    {x.status === "PENDING_REVIEW" &&
                    x.submittedByManagerId !== currentManagerId ? (
                      <div style={{ display: "flex", gap: "4px" }}>
                        <input
                          style={{
                            padding: "4px 6px",
                            fontSize: "12px",
                            border: "1px solid #cbd5e1",
                            borderRadius: "4px",
                            width: "110px",
                          }}
                          placeholder={x.variance !== 0 ? "Lý do duyệt" : "Ghi chú"}
                          value={approvalNotes[x.id] || ""}
                          onChange={(e) =>
                            setApprovalNotes((v) => ({ ...v, [x.id]: e.target.value }))
                          }
                        />
                        <button
                          type="button"
                          className="ops-btn-sm ops-btn-sm--primary"
                          style={{ padding: "4px 8px", fontSize: "11px" }}
                          disabled={
                            busy || (x.variance !== 0 && !(approvalNotes[x.id] || "").trim())
                          }
                          onClick={() =>
                            run(
                              () =>
                                operationsRequest(`/reconciliations/${x.id}/approve`, {
                                  method: "POST",
                                  body: JSON.stringify({
                                    approvalNote: approvalNotes[x.id] || null,
                                  }),
                                }),
                              "Đã duyệt phiếu đối soát."
                            )
                          }
                        >
                          Duyệt
                        </button>
                      </div>
                    ) : (
                      <span style={{ fontSize: "12px", color: "#94a3b8" }}>—</span>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
