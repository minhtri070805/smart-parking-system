import { useCallback, useEffect, useRef, useState } from "react";
import { operationsApi } from "../../services/operationsApi.js";
import { staffApi, validatePlate } from "../../services/staffApi.js";
import {
  AUTO_DELAY_SECONDS,
  admitBooking,
  admitPass,
  admitWalkIn,
  findBooking,
  isLowConfidence,
  newTicketCode,
  singleZone,
} from "../../services/gateAuto.js";
import AutoAdmit from "./AutoAdmit.jsx";
import GateActivity from "./GateActivity.jsx";
import PlateCapture, { emptyCapture } from "./PlateCapture.jsx";
import { PassEntry, PassRefusal } from "./PassOperations.jsx";
import { StaffExit } from "./StaffExit.jsx";
import { TicketEntry } from "./TicketOperations.jsx";
import { Notice, StaffError, StaffIcon, useStaffAction, useStaffQuery } from "./StaffUI.jsx";

const AUTO_KEY = "smart-parking.gate.auto";
const readAuto = () => {
  try {
    return globalThis.localStorage?.getItem(AUTO_KEY) !== "off";
  } catch {
    return true;
  }
};
const saveAuto = (value) => {
  try {
    globalThis.localStorage?.setItem(AUTO_KEY, value ? "on" : "off");
  } catch {
    /* Không lưu được thì chỉ mất lựa chọn ở lần mở sau. */
  }
};
const day = (value) => (value ? new Date(value).toLocaleDateString("vi-VN") : "—");

// Một màn cổng cho cả xe vào lẫn xe ra. Biển số (AI đọc hoặc nhập tay) quyết định nhánh:
// đang có lượt gửi -> xe ra; vé tháng còn hạn -> vào khu cố định; có booking chờ xe -> xe đặt chỗ; còn lại -> vãng lai.
// Chế độ tự động (mặc định): xe vào được hệ thống tự cho vào sau vài giây, Staff chỉ xem và hủy/sửa khi AI sai.
// Xe ra luôn do Staff xác nhận vì còn bước thu tiền và xác nhận xe thực sự qua cổng.
export default function GateOperations({
  lot, revision, onChanged, onBusyChange, initialSessionId, onOpen, autoAdmit, autoDelaySeconds = AUTO_DELAY_SECONDS,
}) {
  const [capture, setCapture] = useState(emptyCapture);
  const [captureBusy, setCaptureBusy] = useState(false);
  const [childBusy, setChildBusy] = useState(false);
  // Yêu cầu xe vào đã gửi mà chưa rõ kết quả: giữ nguyên nhánh để gửi lại đúng mã sự kiện.
  const [locked, setLocked] = useState(false);
  const [result, setResult] = useState(null);
  const [mode, setMode] = useState("ticket");
  const [code, setCode] = useState("");
  const [autoOn, setAutoOn] = useState(() => (typeof autoAdmit === "boolean" ? autoAdmit : readAuto()));
  const [recent, setRecent] = useState([]);
  const [lastDone, setLastDone] = useState(null);
  const attempt = useRef(0);
  const lastAutoRecognition = useRef(null);
  const check = useStaffAction(onBusyChange);
  const busy = check.busy || captureBusy || childBusy;
  const disabled = busy || locked;
  const loadZones = useCallback((signal) => operationsApi(lot.lotId, "zones", null, null, signal), [lot.lotId]);
  const zones = useStaffQuery(loadZones);

  const childBusyChange = useCallback(
    (value) => {
      setChildBusy(value);
      onBusyChange(value);
    },
    [onBusyChange],
  );

  const show = (next) => setResult({ ...next, key: ++attempt.current });

  useEffect(() => {
    if (initialSessionId) show({ kind: "exit", sessionId: initialSessionId });
  }, [initialSessionId]);

  const reset = () => {
    setCapture(emptyCapture());
    setResult(null);
    setLocked(false);
    setCode("");
    check.setError(null);
  };

  const changeAuto = (value) => {
    setAutoOn(value);
    if (typeof autoAdmit !== "boolean") saveAuto(value);
  };

  // Điều kiện để hệ thống tự cho vào. Thiếu điều kiện nào thì trả lý do để Staff xử lý tay, không đoán mò.
  const planFor = (kind, { plate, scanned, pass, booking }) => {
    if (!autoOn) return {};
    if (!lot.isActive) return { manualReason: "Bãi đang đóng nhận xe." };
    if (isLowConfidence(capture.recognition))
      return { manualReason: `AI chỉ chắc ${Math.round(capture.recognition.confidence * 100)}% về biển số. Kiểm tra biển số rồi xử lý thủ công.` };
    if (!zones.data) return { manualReason: zones.error ? "Chưa tải được danh sách khu." : "Đang tải danh sách khu." };
    const eventId = crypto.randomUUID();
    if (kind === "pass") {
      const zone = singleZone(zones.data, "FIXED");
      if (!zone) return { manualReason: "Bãi không có đúng một khu vé tháng đang hoạt động, hãy chọn khu." };
      return {
        plan: {
          plate, title: "Vé tháng còn hiệu lực", summary: `${pass.planName} · đến ${day(pass.endsAtUtc)} · vào khu ${zone.code}`,
          run: () => admitPass({ lot, plate, zoneId: zone.id, file: scanned.file, eventId }),
        },
      };
    }
    if (kind === "booking") {
      if (!scanned.file) return { manualReason: "Cần ảnh biển số để xác nhận xe đặt chỗ. Chụp ảnh xe rồi kiểm tra lại." };
      return {
        plan: {
          plate, title: "Xe có đặt chỗ", summary: `Booking #${booking.id} · biển số khớp`,
          run: () => admitBooking({ booking, plate, file: scanned.file }),
        },
      };
    }
    const zone = singleZone(zones.data, "TRANSIENT");
    if (!zone) return { manualReason: "Bãi không có đúng một khu vãng lai đang hoạt động, hãy chọn khu." };
    const ticketCode = newTicketCode();
    return {
      plan: {
        plate, title: "Xe vãng lai", summary: `Cấp vé ${ticketCode} · khu ${zone.code}`,
        run: async () => {
          try {
            return await admitWalkIn({ lot, plate, zoneId: zone.id, file: scanned.file, eventId, ticketCode });
          } catch (error) {
            // Trùng mã vé điện tử hiếm gặp: cấp mã khác với mã sự kiện khác, chỉ thử lại một lần.
            if (!["TICKET_USED", "TICKET_IN_USE"].includes(error.code)) throw error;
            return admitWalkIn({ lot, plate, zoneId: zone.id, file: scanned.file, eventId: crypto.randomUUID(), ticketCode: newTicketCode() });
          }
        },
      },
    };
  };

  const lookupByPlate = (event) => {
    event?.preventDefault();
    check.run(async () => {
      setResult(null);
      setLastDone(null);
      const plate = validatePlate(capture.plate);
      const scanned = { plate: capture.plate, file: capture.file };
      try {
        const session = await staffApi.findSession(lot.lotId, plate, "plate");
        show({ kind: "exit", sessionId: session.id, ticketCode: session.ticketCode, scanned });
        return;
      } catch (error) {
        // Chỉ "không có lượt đang mở" mới được xét tiếp là xe vào; lỗi mạng/quyền phải dừng lại.
        if (error.code !== "SESSION_NOT_FOUND") throw error;
      }
      const pass = await operationsApi(lot.lotId, `passes/lookup?plate=${encodeURIComponent(plate)}`, null);
      if (pass.canEnter) {
        show({ kind: "pass", pass, scanned, ...planFor("pass", { plate: pass.plateNumber || plate, scanned, pass }) });
        return;
      }
      if (pass.reason === "ALREADY_INSIDE") {
        show({ kind: "blocked", pass, scanned });
        return;
      }
      // Xe có thể là xe đặt chỗ. Không tra được booking thì phải dừng lại, không được coi là xe vãng lai
      // (sẽ làm booking không được ghi nhận và khách mất cọc).
      const bookings = await staffApi.pendingBookings(lot.lotId);
      const found = findBooking(bookings.items, plate);
      if (found.booking) show({ kind: "booking", pass, scanned, booking: found.booking, ...planFor("booking", { plate, scanned, booking: found.booking }) });
      else if (found.unpaid) show({ kind: "booking-unpaid", pass, scanned, booking: found.unpaid });
      else show({ kind: "walk-in", pass, scanned, ...planFor("walk-in", { plate, scanned }) });
    });
  };

  // AI vừa đọc xong biển số: ở chế độ tự động thì xử lý ngay, không chờ Staff bấm Kiểm tra.
  useEffect(() => {
    const id = capture.recognition?.id;
    if (!autoOn || !id || !capture.recognition.plateNumber || lastAutoRecognition.current === id) return;
    if (busy || locked || !capture.plate) return;
    lastAutoRecognition.current = id;
    lookupByPlate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [capture.recognition?.id, capture.plate, autoOn, busy, locked, zones.data]);

  const lookupByCode = (event) => {
    event.preventDefault();
    check.run(async () => {
      setResult(null);
      const session = await staffApi.findSession(lot.lotId, code, mode);
      show({ kind: "exit", sessionId: session.id, ticketCode: session.ticketCode });
    });
  };

  const done = (outcome) => {
    setRecent((items) => [{ ...outcome, id: `${Date.now()}-${items.length}`, at: new Date() }, ...items].slice(0, 10));
    setLastDone(outcome);
    setCapture(emptyCapture());
    setResult(null);
    setLocked(false);
    onChanged();
  };
  // Hủy: giữ ảnh và biển số để Staff sửa rồi kiểm tra lại.
  const cancelAuto = () => {
    setResult(null);
    setLocked(false);
  };
  const toManual = (message) => setResult((current) => (current ? { ...current, plan: null, manualReason: message, key: ++attempt.current } : current));

  // Đổi biển số hoặc ảnh sau khi kiểm tra thì kết quả cũ không còn đúng với xe trước cổng.
  const current =
    result && (!result.scanned || (result.scanned.plate === capture.plate && result.scanned.file === capture.file))
      ? result
      : null;
  const autoPlan = current?.plan && !current.manualReason ? current.plan : null;
  const manualNotice = current?.manualReason ? <Notice kind="warning"><strong>Cần Staff xử lý.</strong> {current.manualReason}</Notice> : null;

  return (
    <div className="staff-side-stack">
      <section className="staff-panel">
        <div className="staff-panel-heading">
          <span className="staff-icon-tile">
            <StaffIcon name="camera" size={24} />
          </span>
          <div>
            <h2>Cổng: xe vào / xe ra</h2>
            <p>Chụp biển số rồi bấm Kiểm tra. Hệ thống tự xác định xe đang vào hay đang ra tại {lot.lotName}.</p>
          </div>
        </div>
        <label className="staff-check">
          <input type="checkbox" checked={autoOn} disabled={disabled} onChange={(event) => changeAuto(event.target.checked)} />
          <span>
            Tự động cho xe vào. AI đọc biển số xong, xe vé tháng, xe đặt chỗ và xe vãng lai tự được cho vào sau vài giây;
            Staff chỉ cần xem và hủy hoặc sửa nếu sai. Xe ra vẫn do Staff xác nhận.
          </span>
        </label>
        <form onSubmit={lookupByPlate} aria-label="Kiểm tra xe tại cổng">
          {/* Dùng purpose EXIT vì lúc quét chưa biết chiều xe: chỉ luồng xe ra gửi kèm kết quả AI,
              và Core chỉ chấp nhận bản ghi EXIT khi xác nhận xe ra. */}
          <PlateCapture
            title="Ảnh xe tại cổng"
            lotId={lot.lotId}
            purpose="EXIT"
            autoDetect
            value={capture}
            onChange={setCapture}
            disabled={disabled}
            onWorking={(value) => {
              setCaptureBusy(value);
              onBusyChange(value);
            }}
          />
          <button type="submit" className="staff-button staff-button--primary" disabled={disabled}>
            {check.busy ? "Đang kiểm tra…" : "Kiểm tra"}
          </button>
        </form>
        <details>
          <summary>Không đọc được biển số, hoặc xe chưa được ghi biển số? Tra theo mã vé / mã phiên</summary>
          <form className="staff-search-form" onSubmit={lookupByCode}>
            <label className="staff-field">
              Tra cứu theo
              <select
                value={mode}
                disabled={disabled}
                onChange={(event) => {
                  setMode(event.target.value);
                  setCode("");
                }}
              >
                <option value="ticket">Mã vé</option>
                <option value="id">Mã phiên</option>
              </select>
            </label>
            <label className="staff-field">
              {mode === "id" ? "Mã phiên" : "Mã vé"} cần tìm
              <input
                required
                autoComplete="off"
                disabled={disabled}
                value={code}
                placeholder={mode === "id" ? "Ví dụ: 1024" : "Ví dụ: VE-0042"}
                onChange={(event) => setCode(event.target.value)}
              />
            </label>
            <button type="submit" className="staff-button staff-button--secondary" disabled={disabled}>
              Tìm xe đang gửi
            </button>
          </form>
        </details>
        <StaffError error={check.error} />
      </section>
      {lastDone && !current && (
        <Notice kind="success">
          Đã tự động cho xe <strong>{lastDone.plate}</strong> vào
          {lastDone.kind === "pass" ? " bằng vé tháng" : lastDone.kind === "booking" ? " theo đặt chỗ" : " như xe vãng lai"}
          {lastDone.sessionId ? ` (lượt #${lastDone.sessionId})` : ""}.
          {lastDone.replay && " Yêu cầu này đã được ghi nhận trước đó, không tạo thêm lượt mới."}
        </Notice>
      )}
      {current?.kind === "exit" && (
        <StaffExit
          key={current.key}
          lot={lot}
          revision={revision}
          onChanged={onChanged}
          onBusyChange={childBusyChange}
          initialSessionId={current.sessionId}
          initialTicketCode={current.ticketCode}
          gateCapture={current.scanned ? capture : undefined}
          onGateCapture={current.scanned ? setCapture : undefined}
          onNext={reset}
        />
      )}
      {autoPlan && (
        <AutoAdmit
          key={current.key}
          plan={autoPlan}
          seconds={autoDelaySeconds}
          onDone={done}
          onCancel={cancelAuto}
          onManual={toManual}
          onLockChange={setLocked}
          onBusyChange={childBusyChange}
        />
      )}
      {!autoPlan && current?.kind === "pass" && (
        <>
          {manualNotice}
          <PassEntry
            key={current.key}
            lot={lot}
            pass={current.pass}
            initialFile={current.scanned.file}
            onChanged={onChanged}
            onBusyChange={childBusyChange}
            onLockChange={setLocked}
            onNext={reset}
          />
        </>
      )}
      {!autoPlan && current?.kind === "walk-in" && (
        <>
          {manualNotice}
          <PassRefusal pass={current.pass} />
          <TicketEntry
            key={current.key}
            lot={lot}
            initialFile={current.scanned.file}
            plateNumber={current.scanned.plate}
            onChanged={onChanged}
            onBusyChange={childBusyChange}
            onLockChange={setLocked}
            onNext={reset}
          />
        </>
      )}
      {!autoPlan && current?.kind === "booking" && (
        <Notice kind="info">
          <strong>Xe có đặt chỗ · Booking #{current.booking.id}</strong> · {current.scanned.plate}.
          {current.manualReason && <> {current.manualReason}</>}{" "}
          Xác nhận xe đến ở màn Xe đặt qua web.{" "}
          {onOpen && (
            <button type="button" className="staff-text-button" onClick={() => onOpen("bookings")}>
              Mở màn Xe đặt qua web
            </button>
          )}
        </Notice>
      )}
      {current?.kind === "booking-unpaid" && (
        <Notice kind="warning">
          <strong>Booking #{current.booking.id} của xe này chưa thanh toán tiền cọc.</strong> Chưa thể xác nhận xe đặt chỗ.
          Nhờ khách hoàn tất thanh toán trên ứng dụng, hoặc quản lý quyết định cho vào như xe vãng lai.
        </Notice>
      )}
      {current?.kind === "blocked" && <PassRefusal pass={current.pass} />}
      {!current && !lastDone && !check.busy && (
        <div className="staff-empty">
          <StaffIcon name="camera" size={38} />
          <h3>Một lần quét cho cả xe vào và xe ra</h3>
          <p>Xe vé tháng, xe đặt chỗ, xe vãng lai hay xe đang ra bãi đều bắt đầu từ bước kiểm tra biển số.</p>
        </div>
      )}
      <GateActivity items={recent} onOpen={onOpen} />
    </div>
  );
}
