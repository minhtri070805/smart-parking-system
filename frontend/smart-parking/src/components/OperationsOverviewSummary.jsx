import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { operationsRequest } from "../services/operationsApi.js";
import { itemsOf } from "./WorkspaceUI.jsx";

const formatMoney = (val) =>
  val == null
    ? "0 đ"
    : new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND", maximumFractionDigits: 0 }).format(val);

const SHIFT_NAMES = {
  MORNING: "Ca sáng (05:00 - 13:00)",
  AFTERNOON: "Ca chiều (13:00 - 21:00)",
  NIGHT: "Ca đêm (21:00 - 05:00)",
};

const SHIFT_STATUS_MAP = {
  PLANNED: { label: "Đã xếp lịch", color: "ops-badge--neutral" },
  IN_PROGRESS: { label: "Đang diễn ra", color: "ops-badge--warning" },
  ACTIVE: { label: "Đang diễn ra", color: "ops-badge--warning" },
  COMPLETED: { label: "Hoàn thành", color: "ops-badge--success" },
  CANCELLED: { label: "Đã hủy", color: "ops-badge--danger" },
};

const RECON_STATUS_MAP = {
  DRAFT: { label: "Bản nháp", color: "ops-badge--neutral" },
  PENDING_REVIEW: { label: "Chờ duyệt", color: "ops-badge--warning" },
  APPROVED: { label: "Đã duyệt", color: "ops-badge--success" },
  REJECTED: { label: "Từ chối", color: "ops-badge--danger" },
};

export default function OperationsOverviewSummary({ lotId, revision }) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [shifts, setShifts] = useState([]);
  const [reconciliations, setReconciliations] = useState([]);

  useEffect(() => {
    if (!lotId) return;
    let active = true;
    setLoading(true);
    setError("");

    Promise.allSettled([
      operationsRequest(`/shifts/workspace?lotId=${lotId}`),
      operationsRequest(`/reconciliations/workspace?lotId=${lotId}`),
    ]).then(([shiftRes, reconRes]) => {
      if (!active) return;
      if (shiftRes.status === "fulfilled") {
        setShifts(itemsOf(shiftRes.value?.data?.shifts));
      }
      if (reconRes.status === "fulfilled") {
        setReconciliations(itemsOf(reconRes.value?.data?.reconciliations));
      }
      setLoading(false);
    });

    return () => {
      active = false;
    };
  }, [lotId, revision]);

  // Today reference: 2026-11-17 as simulated system date
  const todayStr = "2026-11-17";
  const todayShifts = shifts.filter((s) => s.businessDate === todayStr);

  // Stats calculation
  const totalAssignedStaffToday = todayShifts.reduce(
    (acc, s) => acc + (s.assignments ? s.assignments.length : 0),
    0
  );
  const activeShift = shifts.find(
    (s) => s.businessDate === todayStr && (s.status === "IN_PROGRESS" || s.status === "ACTIVE")
  );

  const pendingReconciliations = reconciliations.filter((r) => r.status === "PENDING_REVIEW");
  const latestRecon = reconciliations[0] || null;

  return (
    <div className="ops-dashboard">
      <header className="ops-overview-heading">
        <div>
          <span className="ops-overview-heading__label">Vận hành hôm nay</span>
          <h2>Ca trực và đối soát tiền mặt</h2>
          <p>Lịch nhân sự và trạng thái quỹ tại bãi đang chọn.</p>
        </div>
        <span className="ops-badge ops-badge--neutral">
          Dữ liệu ngày {todayStr}
        </span>
      </header>

      {loading && <p className="ops-overview-loading">Đang tải dữ liệu tổng quan…</p>}
      {error && <p className="workspace-error" role="alert">{error}</p>}

      <div className="ops-dashboard-grid">
        <article className="ops-dashboard-card">
          <div className="ops-dashboard-card__header">
            <div>
              <span className="ops-dashboard-card__label">Lịch nhân sự</span>
              <h3>Ca trực hôm nay</h3>
            </div>
            <span className="ops-badge ops-badge--neutral">
              {todayShifts.length} ca trong ngày
            </span>
          </div>

          <div className="ops-dashboard-card__stats">
            <div className="ops-stat-item">
              <span>Đang trực</span>
              <strong>{activeShift ? activeShift.code : "—"}</strong>
            </div>
            <div className="ops-stat-item">
              <span>Nhân sự xếp lịch</span>
              <strong>{totalAssignedStaffToday} lượt</strong>
            </div>
          </div>

          <div className="ops-dashboard-card__content">
            {todayShifts.length === 0 ? (
              <p className="ops-dashboard-card__empty">Chưa có ca trực nào được tạo cho hôm nay.</p>
            ) : (
              todayShifts.map((s) => {
                const badge = SHIFT_STATUS_MAP[s.status] || { label: s.status, color: "ops-badge--neutral" };
                const count = s.assignments ? s.assignments.length : 0;
                return (
                  <div className="ops-overview-row" key={s.id}>
                    <div className="ops-overview-row__copy">
                      <strong>{SHIFT_NAMES[s.code] || s.code}</strong>
                      <span>{count > 0 ? `${count} nhân viên` : "Chưa xếp nhân viên"}</span>
                    </div>
                    <span className={`ops-badge ${badge.color}`}>{badge.label}</span>
                  </div>
                );
              })
            )}
          </div>

          <Link to={`/manager/shifts?lotId=${lotId}`} className="ops-dashboard-card__link">
            Xem lịch tuần <span aria-hidden="true">→</span>
          </Link>
        </article>

        <article className="ops-dashboard-card">
          <div className="ops-dashboard-card__header">
            <div>
              <span className="ops-dashboard-card__label">Quỹ tiền mặt</span>
              <h3>Đối soát cuối ca</h3>
            </div>
            {pendingReconciliations.length > 0 ? (
              <span className="ops-badge ops-badge--warning">
                {pendingReconciliations.length} chờ duyệt
              </span>
            ) : (
              <span className="ops-badge ops-badge--success">Đã đối soát</span>
            )}
          </div>

          <div className="ops-dashboard-card__stats">
            <div className="ops-stat-item">
              <span>Đã hoàn thành</span>
              <strong>{reconciliations.filter((r) => r.status === "APPROVED").length}</strong>
            </div>
            <div className="ops-stat-item">
              <span>Chờ duyệt</span>
              <strong className={pendingReconciliations.length > 0 ? "is-warning" : "is-success"}>
                {pendingReconciliations.length}
              </strong>
            </div>
          </div>

          <div className="ops-dashboard-card__content">
            <span className="ops-dashboard-card__label">Phiếu gần nhất</span>
            {latestRecon ? (
              <div className="ops-reconciliation-summary">
                <div className="ops-reconciliation-summary__head">
                  <span>
                    <strong>{latestRecon.shiftCode}</strong> · {latestRecon.businessDate}
                  </span>
                  <span className={`ops-badge ${RECON_STATUS_MAP[latestRecon.status]?.color || "ops-badge--neutral"}`}>
                    {RECON_STATUS_MAP[latestRecon.status]?.label || latestRecon.status}
                  </span>
                </div>
                <div className="ops-reconciliation-summary__amounts">
                  <span>Thực đếm: {formatMoney(latestRecon.countedClosingCash)}</span>
                  <span>Dự kiến: {formatMoney(latestRecon.expectedClosingCash)}</span>
                </div>
                <div className="ops-reconciliation-summary__variance">
                  Chênh lệch:{" "}
                  <span className={Number(latestRecon.variance) === 0 ? "is-success" : "is-danger"}>
                    {formatMoney(latestRecon.variance)}
                  </span>
                </div>
              </div>
            ) : (
              <p className="ops-dashboard-card__empty">Chưa có phiếu đối soát nào.</p>
            )}
          </div>

          <Link to={`/manager/reconciliation?lotId=${lotId}`} className="ops-dashboard-card__link">
            Xem sổ đối soát <span aria-hidden="true">→</span>
          </Link>
        </article>
      </div>
    </div>
  );
}
