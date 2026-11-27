import { useEffect, useMemo, useState } from "react";
import { apiRequest } from "../services/api.js";
import { operationsRequest } from "../services/operationsApi.js";
import { Feedback, itemsOf } from "../components/WorkspaceUI.jsx";
import "../operations.css";

const SHIFT_CONFIGS = [
  { code: "MORNING", label: "Ca sáng", time: "05:00 – 13:00", icon: "🌅" },
  { code: "AFTERNOON", label: "Ca chiều", time: "13:00 – 21:00", icon: "☀️" },
  { code: "NIGHT", label: "Ca đêm", time: "21:00 – 05:00", icon: "🌙" },
];

const DUTY_LABELS = {
  GATE: "Trực cổng",
  SLOT_VERIFIER: "Trực khu vực",
  BOTH: "Trực cổng & khu vực",
};

const SHIFT_STATUS_LABELS = {
  PLANNED: "Lên lịch",
  IN_PROGRESS: "Đang trực",
  COMPLETED: "Đã kết thúc",
};

function getShiftWindowRange(dateStr, code) {
  const [y, m, d] = dateStr.split("-").map(Number);
  const startHour = code === "MORNING" ? 5 : code === "AFTERNOON" ? 13 : 21;
  const endHour = code === "MORNING" ? 13 : code === "AFTERNOON" ? 21 : 5;
  const endDayOffset = code === "NIGHT" ? 1 : 0;
  // Giờ vận hành VN (UTC+7)
  const startUtc = Date.UTC(y, m - 1, d, startHour - 7, 0, 0);
  const endUtc = Date.UTC(y, m - 1, d + endDayOffset, endHour - 7, 0, 0);
  return { startUtc, endUtc };
}

function toDateString(d) {
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function getMonday(date) {
  const d = new Date(date);
  const day = d.getDay();
  const diff = d.getDate() - day + (day === 0 ? -6 : 1);
  return new Date(d.setDate(diff));
}

function getWeeksInMonth(year, month) {
  const weeks = [];
  const firstDay = new Date(year, month - 1, 1);
  const lastDay = new Date(year, month, 0);

  let currentMonday = getMonday(firstDay);
  let weekNum = 1;

  while (currentMonday <= lastDay || (currentMonday.getMonth() === month - 1)) {
    const sunday = new Date(currentMonday);
    sunday.setDate(sunday.getDate() + 6);

    const label = `Tuần ${weekNum} (${currentMonday.getDate()}/${currentMonday.getMonth() + 1} – ${sunday.getDate()}/${sunday.getMonth() + 1})`;
    weeks.push({
      weekNum,
      monday: new Date(currentMonday),
      label,
    });

    currentMonday.setDate(currentMonday.getDate() + 7);
    weekNum++;
    if (weekNum > 6) break;
  }
  return weeks;
}

export default function ShiftManagement() {
  const [lots, setLots] = useState([]);
  const [lotId, setLotId] = useState("");
  const [staff, setStaff] = useState([]);
  const [records, setRecords] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [revision, setRevision] = useState(0);

  // Bộ lọc Thời gian: Năm, Tháng, Tuần
  const today = useMemo(() => new Date(), []);
  const [currentYear, setCurrentYear] = useState(today.getFullYear());
  const [currentMonth, setCurrentMonth] = useState(today.getMonth() + 1);
  const [selectedMonday, setSelectedMonday] = useState(() => getMonday(today));

  // Modal phân công
  const [activeModal, setActiveModal] = useState(null); // { dateStr, config, shift }
  const [selectedStaffId, setSelectedStaffId] = useState("");
  const [selectedDuty, setSelectedDuty] = useState("GATE");

  // Load danh sách bãi xe
  useEffect(() => {
    let active = true;
    apiRequest("/v1/staff-assignments/my-lots")
      .then((result) => {
        if (!active) return;
        const values = itemsOf(result?.data);
        setLots(values);
        setLotId((current) => current || String(values[0]?.lotId || ""));
      })
      .catch((e) => { if (active) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  const weekFrom = useMemo(() => toDateString(selectedMonday), [selectedMonday]);
  const weekTo = useMemo(() => {
    const sunday = new Date(selectedMonday);
    sunday.setDate(sunday.getDate() + 6);
    return toDateString(sunday);
  }, [selectedMonday]);

  // Load ca trực từ Operations Service theo tuần đang xem
  useEffect(() => {
    if (!lotId) return;
    let active = true;
    setError("");
    operationsRequest(`/shifts/workspace?lotId=${lotId}&from=${weekFrom}&to=${weekTo}`)
      .then((result) => {
        if (!active) return;
        setRecords(itemsOf(result?.data?.shifts));
        setStaff(itemsOf(result?.data?.staff));
      })
      .catch((e) => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [lotId, weekFrom, weekTo, revision]);

  // Tính 7 ngày trong tuần từ selectedMonday
  const weekDays = useMemo(() => {
    const days = [];
    const dayNames = ["Thứ 2", "Thứ 3", "Thứ 4", "Thứ 5", "Thứ 6", "Thứ 7", "Chủ nhật"];
    for (let i = 0; i < 7; i++) {
      const d = new Date(selectedMonday);
      d.setDate(d.getDate() + i);
      days.push({
        name: dayNames[i],
        date: d,
        dateStr: toDateString(d),
        isToday: toDateString(d) === toDateString(today),
      });
    }
    return days;
  }, [selectedMonday, today]);

  // Danh sách tuần trong tháng đã chọn
  const weeksList = useMemo(() => {
    return getWeeksInMonth(currentYear, currentMonth);
  }, [currentYear, currentMonth]);

  // Khi chọn dropdown Tuần
  function handleWeekChange(monday) {
    setSelectedMonday(new Date(monday));
    setCurrentYear(monday.getFullYear());
    setCurrentMonth(monday.getMonth() + 1);
  }

  // Điều hướng tuần trước / sau
  function changeWeek(deltaWeeks) {
    const nextMonday = new Date(selectedMonday);
    nextMonday.setDate(nextMonday.getDate() + deltaWeeks * 7);
    setSelectedMonday(nextMonday);
    setCurrentYear(nextMonday.getFullYear());
    setCurrentMonth(nextMonday.getMonth() + 1);
  }

  function goToToday() {
    const mon = getMonday(today);
    setSelectedMonday(mon);
    setCurrentYear(today.getFullYear());
    setCurrentMonth(today.getMonth() + 1);
  }

  const staffNameMap = useMemo(() => {
    const map = new Map();
    staff.forEach((p) => {
      map.set(p.userId, p.userFullName || `NV #${p.userId}`);
    });
    return map;
  }, [staff]);

  // Map ca trực theo key: `${dateStr}_${code}`
  const shiftMap = useMemo(() => {
    const map = new Map();
    records.forEach((s) => {
      map.set(`${s.businessDate}_${s.code}`, s);
    });
    return map;
  }, [records]);

  // Tính màu sắc ca:
  // - Ngày đã qua (< 17/11): 🔴 Màu đỏ (dù có hay chưa phân công)
  // - Từ hôm nay trở đi (>= 17/11): 🟢 Xanh lá cây nếu Ca trống | 🟡 Vàng nếu đã phân công (chưa tới giờ / đang trực)
  function getShiftStatusStyle(shift, dateStr, code) {
    const { endUtc } = getShiftWindowRange(dateStr, code);
    const isPast = endUtc <= Date.now();
    const assignments = shift?.assignments || [];
    const hasAssignments = assignments.length > 0;

    if (isPast) {
      return {
        type: "ended",
        isPast: true,
        className: "shift-slot--ended",
        badge: hasAssignments ? "Đã kết thúc" : "Đã qua (Trống)",
      };
    }

    if (!hasAssignments) {
      return {
        type: "empty",
        isPast: false,
        className: "shift-slot--empty",
        badge: "Ca trống",
      };
    }

    if (shift?.status === "COMPLETED") {
      return {
        type: "ended",
        isPast: false,
        className: "shift-slot--ended",
        badge: "Đã chốt",
      };
    }

    return {
      type: "upcoming",
      isPast: false,
      className: "shift-slot--upcoming",
      badge: shift?.status === "IN_PROGRESS" ? "Đang trực" : "Sắp tới",
    };
  }

  // Mở popup xem / phân công cho ô ca
  function openSlotModal(dateStr, config) {
    const shift = shiftMap.get(`${dateStr}_${config.code}`) || null;
    setActiveModal({ dateStr, config, shift });
    setSelectedStaffId("");
    setSelectedDuty("GATE");
  }

  // Đồng bộ lại modal khi records cập nhật
  useEffect(() => {
    if (!activeModal) return;
    const shift = shiftMap.get(`${activeModal.dateStr}_${activeModal.config.code}`) || null;
    setActiveModal((prev) => (prev ? { ...prev, shift } : null));
  }, [shiftMap]);

  // Thực hiện phân công nhân viên
  async function handleAssignStaff(e) {
    e.preventDefault();
    if (!selectedStaffId || !activeModal || !lotId) return;

    setBusy(true);
    setError("");
    setNotice("");

    try {
      let shiftId = activeModal.shift?.id;

      // Nếu ca chưa có trong DB, tự động tạo ca trước
      if (!shiftId) {
        const createRes = await operationsRequest("/shifts", {
          method: "POST",
          body: JSON.stringify({
            lotId: Number(lotId),
            businessDate: activeModal.dateStr,
            shiftCode: activeModal.config.code,
          }),
        });
        shiftId = createRes?.data?.id;
      }

      if (!shiftId) throw new Error("Không thể khởi tạo ca trực.");

      // Thêm nhân viên vào ca
      await operationsRequest(`/shifts/${shiftId}/assignments`, {
        method: "POST",
        body: JSON.stringify({
          staffId: Number(selectedStaffId),
          dutyCode: selectedDuty,
        }),
      });

      setNotice(`Đã phân công nhân viên vào ${activeModal.config.label} thành công.`);
      setSelectedStaffId("");
      setRevision((n) => n + 1);
    } catch (err) {
      setError(err.message || "Không thể phân công ca trực.");
    } finally {
      setBusy(false);
    }
  }

  // Bắt đầu / Chốt ca
  async function handleShiftAction(action) {
    if (!activeModal?.shift?.id) return;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await operationsRequest(`/shifts/${activeModal.shift.id}/${action}`, { method: "POST" });
      setNotice(action === "start" ? "Đã bắt đầu ca trực." : "Đã chốt ca trực thành công.");
      setRevision((n) => n + 1);
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="ops-page-container">
      {/* THANH FILTER / TOOLBAR NHỎ GỌN TRÊN CÙNG (Không có header to) */}
      <div className="ops-toolbar">
        <div className="ops-toolbar__filters">
          <div className="ops-toolbar__item">
            <span>Bãi:</span>
            <select
              value={lotId}
              disabled={loading || busy}
              onChange={(e) => setLotId(e.target.value)}
            >
              {lots.map((l) => (
                <option key={l.lotId} value={l.lotId}>
                  {l.lotName}
                </option>
              ))}
            </select>
          </div>

          <div className="ops-toolbar__item">
            <span>Năm:</span>
            <select
              value={currentYear}
              disabled={loading || busy}
              onChange={(e) => {
                const y = Number(e.target.value);
                setCurrentYear(y);
                const firstMon = getMonday(new Date(y, currentMonth - 1, 1));
                setSelectedMonday(firstMon);
              }}
            >
              {[2025, 2026, 2027].map((y) => (
                <option key={y} value={y}>
                  Năm {y}
                </option>
              ))}
            </select>
          </div>

          <div className="ops-toolbar__item">
            <span>Tháng:</span>
            <select
              value={currentMonth}
              disabled={loading || busy}
              onChange={(e) => {
                const m = Number(e.target.value);
                setCurrentMonth(m);
                const firstMon = getMonday(new Date(currentYear, m - 1, 1));
                setSelectedMonday(firstMon);
              }}
            >
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <option key={m} value={m}>
                  Tháng {m}
                </option>
              ))}
            </select>
          </div>

          <div className="ops-toolbar__item">
            <span>Tuần:</span>
            <select
              value={weeksList.findIndex(
                (w) => toDateString(w.monday) === toDateString(selectedMonday)
              )}
              disabled={loading || busy}
              onChange={(e) => {
                const idx = Number(e.target.value);
                if (weeksList[idx]) handleWeekChange(weeksList[idx].monday);
              }}
            >
              {weeksList.map((w, idx) => (
                <option key={idx} value={idx}>
                  {w.label}
                </option>
              ))}
            </select>
          </div>

          <button
            type="button"
            className="ops-btn-sm ops-btn-sm--outline"
            onClick={() => changeWeek(-1)}
            title="Tuần trước"
          >
            ◀
          </button>
          <button
            type="button"
            className="ops-btn-sm ops-btn-sm--outline"
            onClick={goToToday}
          >
            Hôm nay
          </button>
          <button
            type="button"
            className="ops-btn-sm ops-btn-sm--outline"
            onClick={() => changeWeek(1)}
            title="Tuần sau"
          >
            ▶
          </button>
        </div>

        <div className="ops-toolbar__actions">
          {/* Chú thích màu sắc */}
          <div className="shift-legend">
            <div className="shift-legend__item">
              <span className="shift-legend__dot shift-legend__dot--empty"></span>
              <span>Ca trống</span>
            </div>
            <div className="shift-legend__item">
              <span className="shift-legend__dot shift-legend__dot--upcoming"></span>
              <span>Chưa làm / Tương lai</span>
            </div>
            <div className="shift-legend__item">
              <span className="shift-legend__dot shift-legend__dot--ended"></span>
              <span>Đã kết thúc</span>
            </div>
          </div>

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

      {/* BẢNG TIMETABLE 1 TUẦN */}
      <div className="timetable-wrapper">
        <table className="timetable-grid">
          <thead>
            <tr>
              <th className="shift-header-col">Khung ca</th>
              {weekDays.map((d) => (
                <th key={d.dateStr} className={d.isToday ? "is-today" : ""}>
                  <div>{d.name}</div>
                  <small style={{ fontWeight: "normal", color: "#64748b" }}>
                    {d.date.getDate()}/{d.date.getMonth() + 1}
                  </small>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {SHIFT_CONFIGS.map((cfg) => (
              <tr key={cfg.code}>
                <td className="shift-header-col">
                  <div style={{ fontSize: "14px" }}>{cfg.icon}</div>
                  <strong>{cfg.label}</strong>
                  <div style={{ fontSize: "11px", color: "#64748b" }}>{cfg.time}</div>
                </td>
                {weekDays.map((d) => {
                  const shift = shiftMap.get(`${d.dateStr}_${cfg.code}`);
                  const statusInfo = getShiftStatusStyle(shift, d.dateStr, cfg.code);
                  const assignments = shift?.assignments || [];
                  const isDisabledPastEmpty = statusInfo.isPast && assignments.length === 0;

                  return (
                    <td key={d.dateStr}>
                      <div
                        className={`shift-slot ${statusInfo.className}`}
                        style={isDisabledPastEmpty ? { opacity: 0.65, cursor: "not-allowed" } : undefined}
                        onClick={() => {
                          if (!isDisabledPastEmpty) openSlotModal(d.dateStr, cfg);
                        }}
                        title={
                          isDisabledPastEmpty
                            ? "Ca đã qua, không thể phân công"
                            : statusInfo.isPast
                            ? "Bấm để xem thông tin ca đã qua"
                            : "Bấm để xem và phân công nhân viên"
                        }
                      >
                        <div className="shift-slot__top">
                          <span>{cfg.label}</span>
                          <span className="shift-slot__badge">{statusInfo.badge}</span>
                        </div>

                        <div className="shift-slot__roster">
                          {assignments.length === 0 ? (
                            <span style={{ fontStyle: "italic", opacity: 0.8 }}>
                              {statusInfo.isPast ? "Đã qua" : "+ Bấm để phân công"}
                            </span>
                          ) : (
                            assignments.slice(0, 2).map((a) => (
                              <div key={a.id} className="shift-slot__person">
                                <span>👤 {staffNameMap.get(a.staffId) || `NV #${a.staffId}`}</span>
                                <span className="shift-slot__person-role">
                                  ({DUTY_LABELS[a.dutyCode] || a.dutyCode})
                                </span>
                              </div>
                            ))
                          )}
                          {assignments.length > 2 && (
                            <div style={{ fontSize: "10px", fontWeight: "bold" }}>
                              +{assignments.length - 2} nhân viên khác
                            </div>
                          )}
                        </div>

                        <div className="shift-slot__bottom">
                          {assignments.length > 0
                            ? `${assignments.length} nhân viên`
                            : "Trống"}
                        </div>
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* POPUP / MODAL PHÂN CÔNG CA TRỰC KHI CLICK VÀO Ô */}
      {activeModal && (
        <div
          className="ops-modal-backdrop"
          onMouseDown={(e) => {
            if (e.target === e.currentTarget) setActiveModal(null);
          }}
        >
          <div className="ops-modal">
            <div className="ops-modal__head">
              <div>
                <h3>
                  {activeModal.config.icon} {activeModal.config.label} · Ngày{" "}
                  {activeModal.dateStr}
                </h3>
                <small style={{ color: "#64748b" }}>
                  Giờ làm việc: {activeModal.config.time} · Trạng thái:{" "}
                  <strong>
                    {activeModal.shift?.status === "COMPLETED"
                      ? "Đã kết thúc"
                      : activeModal.shift?.status === "IN_PROGRESS"
                      ? "Đang trực"
                      : "Lên lịch"}
                  </strong>
                </small>
              </div>
              <button
                type="button"
                className="ops-modal__close"
                onClick={() => setActiveModal(null)}
              >
                ✕
              </button>
            </div>

            <div className="ops-modal__body">
              {/* Danh sách nhân viên hiện tại trong ca */}
              <div>
                <h4 style={{ margin: "0 0 8px", fontSize: "13px", color: "#334155" }}>
                  Danh sách nhân viên trực ca ({activeModal.shift?.assignments?.length || 0}):
                </h4>
                {(!activeModal.shift?.assignments ||
                  activeModal.shift.assignments.length === 0) && (
                  <p className="workspace-note" style={{ margin: 0 }}>
                    Ca này hiện chưa có nhân viên trực. Hãy phân công bên dưới.
                  </p>
                )}
                {activeModal.shift?.assignments?.length > 0 && (
                  <div style={{ display: "grid", gap: "6px" }}>
                    {activeModal.shift.assignments.map((a) => (
                      <div
                        key={a.id}
                        style={{
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "space-between",
                          padding: "8px 12px",
                          background: "#f8fafc",
                          borderRadius: "6px",
                          border: "1px solid #e2e8f0",
                          fontSize: "13px",
                        }}
                      >
                        <div>
                          <strong>{staffNameMap.get(a.staffId) || `Nhân viên #${a.staffId}`}</strong>
                          <span style={{ color: "#64748b", marginLeft: "8px" }}>
                            {DUTY_LABELS[a.dutyCode] || a.dutyCode}
                          </span>
                        </div>
                        <span className="ops-badge ops-badge--neutral">
                          {SHIFT_STATUS_LABELS[activeModal.shift?.status] || "Lên lịch"}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              {/* Form thêm nhân viên vào ca: ẩn với ca quá khứ hoặc ca không ở trạng thái PLANNED */}
              {(() => {
                const { startUtc, endUtc } = getShiftWindowRange(activeModal.dateStr, activeModal.config.code);
                const nowMs = Date.now();
                const isPastShift = endUtc <= nowMs;
                const canAssign = !isPastShift && (!activeModal.shift || activeModal.shift.status === "PLANNED");
                const canStartNow =
                  activeModal.shift?.status === "PLANNED" &&
                  activeModal.shift.assignments?.length > 0 &&
                  nowMs >= startUtc &&
                  nowMs < endUtc;

                return (
                  <>
                    {isPastShift && (
                      <p className="workspace-note" style={{ margin: 0, color: "#991b1b" }}>
                        Ca này đã qua thời gian làm việc, chỉ xem thông tin và không thể thao tác.
                      </p>
                    )}

                    {canAssign && (
                      <form
                        onSubmit={handleAssignStaff}
                        style={{
                          display: "flex",
                          flexDirection: "column",
                          gap: "12px",
                          padding: "14px",
                          background: "#f1f5f9",
                          borderRadius: "8px",
                        }}
                      >
                        <div style={{ fontWeight: 600, fontSize: "13px", color: "#1e293b" }}>
                          + Thêm nhân viên vào ca này:
                        </div>

                        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px" }}>
                            <span>Chọn nhân viên</span>
                            <select
                              required
                              value={selectedStaffId}
                              onChange={(e) => setSelectedStaffId(e.target.value)}
                              style={{ padding: "6px 8px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                            >
                              <option value="">-- Chọn Staff --</option>
                              {staff.map((p) => (
                                <option key={p.userId} value={p.userId}>
                                  {p.userFullName} (#{p.userId})
                                </option>
                              ))}
                            </select>
                          </label>

                          <label style={{ display: "flex", flexDirection: "column", gap: "4px", fontSize: "12px" }}>
                            <span>Nhiệm vụ trực</span>
                            <select
                              value={selectedDuty}
                              onChange={(e) => setSelectedDuty(e.target.value)}
                              style={{ padding: "6px 8px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                            >
                              <option value="GATE">Trực cổng (GATE)</option>
                              <option value="SLOT_VERIFIER">Trực khu vực (SLOT_VERIFIER)</option>
                              <option value="BOTH">Trực cổng & khu vực (BOTH)</option>
                            </select>
                          </label>
                        </div>

                        <div style={{ display: "flex", justifyContent: "flex-end", marginTop: "4px" }}>
                          <button
                            type="submit"
                            className="ops-btn-sm ops-btn-sm--primary"
                            disabled={busy || !selectedStaffId}
                          >
                            {busy ? "Đang lưu…" : "Xác nhận phân công"}
                          </button>
                        </div>
                      </form>
                    )}

                    {/* Các thao tác ca */}
                    {activeModal.shift && !isPastShift && (
                      <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end", alignItems: "center" }}>
                        {activeModal.shift.status === "PLANNED" &&
                          activeModal.shift.assignments?.length > 0 && (
                            <button
                              type="button"
                              className="ops-btn-sm ops-btn-sm--primary"
                              disabled={busy || !canStartNow}
                              title={canStartNow ? "Bắt đầu ca trực" : "Chỉ có thể bắt đầu khi đang trong khung giờ ca"}
                              onClick={() => handleShiftAction("start")}
                            >
                              Bắt đầu ca
                            </button>
                          )}
                        {activeModal.shift.status === "IN_PROGRESS" && (
                          <button
                            type="button"
                            className="ops-btn-sm ops-btn-sm--outline"
                            disabled={busy}
                            onClick={() => handleShiftAction("complete")}
                          >
                            Chốt ca
                          </button>
                        )}
                      </div>
                    )}
                  </>
                );
              })()}
            </div>

            <div className="ops-modal__foot">
              <button
                type="button"
                className="ops-btn-sm ops-btn-sm--outline"
                onClick={() => setActiveModal(null)}
              >
                Đóng
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
