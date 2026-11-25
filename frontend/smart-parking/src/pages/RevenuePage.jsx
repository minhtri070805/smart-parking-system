import { useEffect, useState } from "react";
import RequireRole from "../components/RequireRole.jsx";
import {
  Feedback,
  dateLabel,
  useResource,
} from "../components/WorkspaceUI.jsx";
import "../workspace.css";

const money = (value) =>
  new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
  }).format(value || 0);

function RevenueDashboard() {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [selectedLotId, setSelectedLotId] = useState("");
  const [revision, setRevision] = useState(0);

  // Load only assigned lots for Manager
  const myAssignedLots = useResource(
    "/v1/staff-assignments/my-lots",
    revision,
  );

  const lotsList = Array.isArray(myAssignedLots.data)
    ? myAssignedLots.data
    : myAssignedLots.data?.items || [];

  // When lots load for manager, if manager only has 1 lot, auto-select it
  useEffect(() => {
    if (lotsList.length === 1 && !selectedLotId) {
      setSelectedLotId(String(lotsList[0].lotId || lotsList[0].id));
    }
  }, [lotsList, selectedLotId]);

  // Load monthly revenue & traffic stats
  const statsPath = `/v1/parking-sessions/monthly-stats?year=${year}&month=${month}${selectedLotId ? `&lotId=${selectedLotId}` : ""
    }`;

  const statsResource = useResource(statsPath, revision);
  const stats = statsResource.data?.data || statsResource.data;

  const days = stats?.days || [];
  const maxDayRevenue = Math.max(...days.map((d) => d.revenue || 0), 1);
  const maxHourCheckIns = Math.max(...(stats?.checkInsByHour || [1]), 1);

  const daysCount = days.length || DateTimeDays(year, month);
  const avgDailyRevenue = stats?.totalRevenue ? Math.round(stats.totalRevenue / daysCount) : 0;

  function DateTimeDays(y, m) {
    return new Date(y, m, 0).getDate();
  }

  const selectedLotName = () => {
    if (!selectedLotId) {
      return "Tất cả các bãi đang quản lý";
    }
    const found = lotsList.find(
      (l) => String(l.id || l.lotId) === String(selectedLotId),
    );
    return found?.name || found?.lotName || `Bãi #${selectedLotId}`;
  };

  return (
    <section className="workspace container">
      {/* Header and Filter Bar */}
      <div className="workspace-heading">
        <div>
          <h2>Báo cáo Doanh thu</h2>
          <p>
            Theo dõi doanh thu và lưu lượng tại các bãi xe bạn được phân công quản lý.
          </p>
        </div>
        <div className="workspace-actions">
          <button
            type="button"
            className="btn btn--outline"
            disabled={statsResource.loading}
            onClick={() => setRevision((n) => n + 1)}
          >
            {statsResource.loading ? "Đang tải..." : "Làm mới"}
          </button>
        </div>
      </div>

      {/* Filter panel */}
      <section className="workspace-panel">
        <div className="workspace-filters" style={{ flexWrap: "wrap", gap: "16px" }}>
          <label style={{ minWidth: 260 }}>
            Bãi đỗ xe
            <select
              value={selectedLotId}
              onChange={(e) => setSelectedLotId(e.target.value)}
              disabled={lotsList.length <= 1}
            >
              {lotsList.length > 1 && (
                <option value="">Tất cả các bãi đang quản lý</option>
              )}
              {lotsList.map((lot) => (
                <option key={lot.lotId || lot.id} value={lot.lotId || lot.id}>
                  {lot.lotName || lot.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            Tháng
            <select
              value={month}
              onChange={(e) => setMonth(Number(e.target.value))}
            >
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <option key={m} value={m}>
                  Tháng {m}
                </option>
              ))}
            </select>
          </label>

          <label>
            Năm
            <select
              value={year}
              onChange={(e) => setYear(Number(e.target.value))}
            >
              {[now.getFullYear() - 2, now.getFullYear() - 1, now.getFullYear(), now.getFullYear() + 1].map((y) => (
                <option key={y} value={y}>
                  Năm {y}
                </option>
              ))}
            </select>
          </label>
        </div>

        {!myAssignedLots.loading && lotsList.length === 0 && (
          <p className="customer-message customer-message--error" style={{ marginTop: 16 }}>
            Bạn chưa được phân công quản lý bãi đỗ nào. Vui lòng liên hệ Quản trị viên để được cấp quyền trước khi xem doanh thu.
          </p>
        )}
      </section>

      <Feedback
        loading={statsResource.loading}
        error={statsResource.error}
      />

      {/* KPI Metrics */}
      {stats && (
        <>
          <div className="manager-metrics" style={{ margin: "20px 0" }}>
            <article style={{ borderLeft: "4px solid #2563eb" }}>
              <span>Tổng doanh thu {month}/{year}</span>
              <strong style={{ color: "#1d4ed8" }}>{money(stats.totalRevenue)}</strong>
              <small style={{ color: "#64748b" }}>{selectedLotName()}</small>
            </article>

            <article style={{ borderLeft: "4px solid #059669" }}>
              <span>Lượt xe vào bãi</span>
              <strong style={{ color: "#047857" }}>
                {stats.totalCheckIns ? stats.totalCheckIns.toLocaleString("vi-VN") : 0}
              </strong>
              <small style={{ color: "#64748b" }}>Lượt vào trong tháng</small>
            </article>

            <article style={{ borderLeft: "4px solid #d97706" }}>
              <span>Lượt xe ra bãi</span>
              <strong style={{ color: "#b45309" }}>
                {stats.totalCheckOuts ? stats.totalCheckOuts.toLocaleString("vi-VN") : 0}
              </strong>
              <small style={{ color: "#64748b" }}>Lượt ra trong tháng</small>
            </article>

            <article style={{ borderLeft: "4px solid #7c3aed" }}>
              <span>Doanh thu trung bình / ngày</span>
              <strong style={{ color: "#6d28d9" }}>{money(avgDailyRevenue)}</strong>
              <small style={{ color: "#64748b" }}>Tính trên {daysCount} ngày</small>
            </article>
          </div>

          {/* Hourly Traffic Distribution */}
          {stats.checkInsByHour && stats.checkInsByHour.length === 24 && (
            <section className="workspace-panel" style={{ marginBottom: 20 }}>
              <div className="workspace-heading">
                <div>
                  <h3>Phân bổ lưu lượng xe vào theo khung giờ (0h - 23h)</h3>
                  <p>Quan sát các khung giờ cao điểm để điều phối nhân sự trực cổng và khu vực hợp lý.</p>
                </div>
              </div>
              <div
                style={{
                  display: "flex",
                  alignItems: "flex-end",
                  gap: "6px",
                  height: 140,
                  padding: "16px 8px 0",
                  overflowX: "auto",
                }}
              >
                {stats.checkInsByHour.map((count, hour) => {
                  const heightPercent = maxHourCheckIns > 0 ? (count / maxHourCheckIns) * 100 : 0;
                  return (
                    <div
                      key={hour}
                      style={{
                        flex: "1 0 28px",
                        display: "flex",
                        flexDirection: "column",
                        alignItems: "center",
                        height: "100%",
                        justifyContent: "flex-end",
                      }}
                      title={`${hour}h:00: ${count} lượt vào`}
                    >
                      <span style={{ fontSize: 10, color: "#64748b", marginBottom: 4 }}>
                        {count > 0 ? count : ""}
                      </span>
                      <div
                        style={{
                          width: "100%",
                          maxWidth: 22,
                          height: `${Math.max(heightPercent, 4)}%`,
                          background: count > 0 ? "linear-gradient(180deg, #3b82f6 0%, #1d4ed8 100%)" : "#e2e8f0",
                          borderRadius: "4px 4px 0 0",
                          transition: "height .2s ease",
                        }}
                      />
                      <span style={{ fontSize: 10, color: "#475569", marginTop: 6 }}>
                        {hour}h
                      </span>
                    </div>
                  );
                })}
              </div>
            </section>
          )}

          {/* Daily Breakdown Table */}
          <section className="workspace-panel" style={{ marginBottom: 20 }}>
            <div className="workspace-heading">
              <div>
                <h3>Chi tiết doanh thu theo từng ngày trong tháng</h3>
                <p>Doanh thu ghi nhận từ các phiên đỗ xe đã thanh toán.</p>
              </div>
            </div>

            <div className="workspace-table">
              <table>
                <thead>
                  <tr>
                    <th style={{ width: 140 }}>Ngày</th>
                    <th style={{ width: 110, textAlign: "center" }}>Lượt vào</th>
                    <th style={{ width: 110, textAlign: "center" }}>Lượt ra</th>
                    <th style={{ width: 180, textAlign: "right" }}>Doanh thu</th>
                    <th>Biểu đồ doanh thu ngày</th>
                  </tr>
                </thead>
                <tbody>
                  {days.map((day) => {
                    const dayDate = new Date(`${day.date}T00:00:00`);
                    const percent = maxDayRevenue > 0 ? (day.revenue / maxDayRevenue) * 100 : 0;
                    const isToday =
                      dayDate.toDateString() === now.toDateString();

                    return (
                      <tr
                        key={day.date}
                        style={isToday ? { background: "#f0f9ff" } : undefined}
                      >
                        <td>
                          <strong>
                            {dayDate.toLocaleDateString("vi-VN", {
                              weekday: "short",
                              day: "2-digit",
                              month: "2-digit",
                            })}
                          </strong>
                          {isToday && (
                            <span
                              className="workspace-badge"
                              style={{
                                marginLeft: 6,
                                background: "#dbeafe",
                                color: "#1d4ed8",
                                fontSize: 10,
                              }}
                            >
                              Hôm nay
                            </span>
                          )}
                        </td>
                        <td style={{ textAlign: "center" }}>
                          {day.checkIns > 0 ? (
                            <span style={{ color: "#047857", fontWeight: 600 }}>
                              {day.checkIns}
                            </span>
                          ) : (
                            <span style={{ color: "#94a3b8" }}>0</span>
                          )}
                        </td>
                        <td style={{ textAlign: "center" }}>
                          {day.checkOuts > 0 ? (
                            <span style={{ color: "#b45309", fontWeight: 600 }}>
                              {day.checkOuts}
                            </span>
                          ) : (
                            <span style={{ color: "#94a3b8" }}>0</span>
                          )}
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <strong style={{ color: day.revenue > 0 ? "#1e293b" : "#94a3b8" }}>
                            {money(day.revenue)}
                          </strong>
                        </td>
                        <td>
                          <div
                            style={{
                              width: "100%",
                              maxWidth: 240,
                              height: 10,
                              background: "#f1f5f9",
                              borderRadius: 6,
                              overflow: "hidden",
                            }}
                          >
                            <div
                              style={{
                                width: `${percent}%`,
                                height: "100%",
                                background: "linear-gradient(90deg, #3b82f6, #10b981)",
                                borderRadius: 6,
                              }}
                            />
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>

          {/* Recent Paid Transactions */}
          {stats.recentTransactions && stats.recentTransactions.length > 0 && (
            <section className="workspace-panel">
              <div className="workspace-heading">
                <div>
                  <h3>Giao dịch thanh toán gần đây ({stats.recentTransactions.length} phiên)</h3>
                  <p>Các phiên gửi xe đã hoàn tất thanh toán trong tháng này.</p>
                </div>
              </div>

              <div className="workspace-table">
                <table>
                  <thead>
                    <tr>
                      <th>Mã phiên</th>
                      <th>Biển số xe</th>
                      <th>Bãi đỗ</th>
                      <th>Vị trí ô</th>
                      <th>Thời gian vào</th>
                      <th>Thanh toán lúc</th>
                      <th style={{ textAlign: "right" }}>Số tiền</th>
                    </tr>
                  </thead>
                  <tbody>
                    {stats.recentTransactions.map((tx) => (
                      <tr key={tx.sessionId}>
                        <td>
                          <strong>#{tx.sessionId}</strong>
                        </td>
                        <td>
                          <span style={{ fontWeight: 700, letterSpacing: "0.03em" }}>
                            {tx.plateNumber}
                          </span>
                        </td>
                        <td>{tx.lotName || `Bãi #${tx.lotId}`}</td>
                        <td>{tx.slotCode || "—"}</td>
                        <td>{dateLabel(tx.checkInTime)}</td>
                        <td>{dateLabel(tx.paidAt)}</td>
                        <td style={{ textAlign: "right" }}>
                          <strong style={{ color: "#047857" }}>
                            {money(tx.totalFee)}
                          </strong>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}
    </section>
  );
}

export default function RevenuePage() {
  return (
    <RequireRole role="MANAGER">
      <RevenueDashboard />
    </RequireRole>
  );
}
