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
      <div className="workspace-heading" style={{ marginBottom: 4 }}>
        <div>
          <h2 style={{ fontSize: 18, fontWeight: 700, margin: "0 0 4px" }}>
            📊 Tổng quan vận hành ca & tiền mặt
          </h2>
          <p style={{ margin: 0, color: "#64748b", fontSize: 13 }}>
            Tổng hợp lịch ca và tình hình đối soát quỹ
          </p>
        </div>
        <span className="ops-badge ops-badge--neutral" style={{ fontSize: 12, padding: "5px 12px" }}>
          Hệ thống: {todayStr}
        </span>
      </div>

      {loading && <p style={{ color: "#64748b", fontSize: 13, fontStyle: "italic" }}>Đang tải dữ liệu tổng quan...</p>}

      <div className="ops-dashboard-grid">
        {/* CARD 1: CA TRỰC */}
        <div className="ops-dashboard-card">
          <div className="ops-dashboard-card__header">
            <h3>🗓️ Ca trực hôm nay</h3>
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
              <p style={{ margin: 0, color: "#94a3b8", fontSize: 12 }}>Chưa có ca trực nào được tạo cho hôm nay.</p>
            ) : (
              todayShifts.map((s) => {
                const badge = SHIFT_STATUS_MAP[s.status] || { label: s.status, color: "ops-badge--neutral" };
                const count = s.assignments ? s.assignments.length : 0;
                return (
                  <div
                    key={s.id}
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      padding: "6px 8px",
                      background: "#f8fafc",
                      borderRadius: 6,
                      fontSize: 12,
                    }}
                  >
                    <span>
                      <strong>{s.code === "MORNING" ? "Sáng" : s.code === "AFTERNOON" ? "Chiều" : "Đêm"}</strong>
                      {" · "}
                      <span style={{ color: "#64748b" }}>{count > 0 ? `${count} nhân viên` : "Chưa xếp"}</span>
                    </span>
                    <span className={`ops-badge ${badge.color}`}>{badge.label}</span>
                  </div>
                );
              })
            )}
          </div>

          <Link to={`/manager/shifts?lotId=${lotId}`} className="ops-dashboard-card__link">
            Xem Timetable lịch tuần &rarr;
          </Link>
        </div>

        {/* CARD 2: ĐỐI SOÁT QUỸ */}
        <div className="ops-dashboard-card">
          <div className="ops-dashboard-card__header">
            <h3>⚖️ Đối soát quỹ tiền</h3>
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
              <strong style={{ color: pendingReconciliations.length > 0 ? "#b45309" : "#166534" }}>
                {pendingReconciliations.length}
              </strong>
            </div>
          </div>

          <div className="ops-dashboard-card__content">
            <span style={{ fontSize: 11, color: "#64748b", fontWeight: 600 }}>PHIẾU GẦN NHẤT:</span>
            {latestRecon ? (
              <div
                style={{
                  padding: "8px 10px",
                  background: "#f8fafc",
                  borderRadius: 6,
                  display: "flex",
                  flexDirection: "column",
                  gap: 4,
                  fontSize: 12,
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between" }}>
                  <span>
                    <strong>{latestRecon.shiftCode}</strong> · {latestRecon.businessDate}
                  </span>
                  <span className={`ops-badge ${RECON_STATUS_MAP[latestRecon.status]?.color || "ops-badge--neutral"}`}>
                    {RECON_STATUS_MAP[latestRecon.status]?.label || latestRecon.status}
                  </span>
                </div>
                <div style={{ display: "flex", justifyContent: "space-between", color: "#64748b" }}>
                  <span>Thực đếm: {formatMoney(latestRecon.countedClosingCash)}</span>
                  <span>Dự kiến: {formatMoney(latestRecon.expectedClosingCash)}</span>
                </div>
                <div style={{ fontSize: 11, fontWeight: 600 }}>
                  Chênh lệch:{" "}
                  <span style={{ color: Number(latestRecon.variance) === 0 ? "#166534" : "#dc2626" }}>
                    {formatMoney(latestRecon.variance)}
                  </span>
                </div>
              </div>
            ) : (
              <p style={{ margin: 0, color: "#94a3b8", fontSize: 12 }}>Chưa có phiếu đối soát nào.</p>
            )}
          </div>

          <Link to={`/manager/reconciliation?lotId=${lotId}`} className="ops-dashboard-card__link">
            Xem Sổ đối soát quỹ &rarr;
          </Link>
        </div>
      </div>
    </div>
  );
}
