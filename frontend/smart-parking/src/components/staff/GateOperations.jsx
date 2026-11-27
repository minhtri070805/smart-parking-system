import { useCallback, useEffect, useRef, useState } from "react";
import { operationsApi } from "../../services/operationsApi.js";
import { staffApi, validatePlate } from "../../services/staffApi.js";
import PlateCapture, { emptyCapture } from "./PlateCapture.jsx";
import { PassEntry, PassRefusal } from "./PassOperations.jsx";
import { StaffExit } from "./StaffExit.jsx";
import { TicketEntry } from "./TicketOperations.jsx";
import { Notice, StaffError, StaffIcon, useStaffAction } from "./StaffUI.jsx";

// Một màn cổng cho cả xe vào lẫn xe ra. Biển số (AI đọc hoặc nhập tay) quyết định nhánh:
// đang có lượt gửi -> xe ra; vé tháng còn hạn -> vào khu cố định; còn lại -> tiếp nhận vãng lai.
export default function GateOperations({ lot, revision, onChanged, onBusyChange, initialSessionId }) {
  const [capture, setCapture] = useState(emptyCapture);
  const [captureBusy, setCaptureBusy] = useState(false);
  const [childBusy, setChildBusy] = useState(false);
  // Yêu cầu xe vào đã gửi mà chưa rõ kết quả: giữ nguyên nhánh để gửi lại đúng mã sự kiện.
  const [locked, setLocked] = useState(false);
  const [result, setResult] = useState(null);
  const [mode, setMode] = useState("ticket");
  const [code, setCode] = useState("");
  const attempt = useRef(0);
  const check = useStaffAction(onBusyChange);
  const busy = check.busy || captureBusy || childBusy;
  const disabled = busy || locked;

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

  const lookupByPlate = (event) => {
    event.preventDefault();
    check.run(async () => {
      setResult(null);
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
      if (pass.canEnter) show({ kind: "pass", pass, scanned });
      else if (pass.reason === "ALREADY_INSIDE") show({ kind: "blocked", pass, scanned });
      else show({ kind: "walk-in", pass, scanned });
    });
  };

  const lookupByCode = (event) => {
    event.preventDefault();
    check.run(async () => {
      setResult(null);
      const session = await staffApi.findSession(lot.lotId, code, mode);
      show({ kind: "exit", sessionId: session.id, ticketCode: session.ticketCode });
    });
  };

  // Đổi biển số hoặc ảnh sau khi kiểm tra thì kết quả cũ không còn đúng với xe trước cổng.
  const current =
    result && (!result.scanned || (result.scanned.plate === capture.plate && result.scanned.file === capture.file))
      ? result
      : null;

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
      {current?.kind === "pass" && (
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
      )}
      {current?.kind === "walk-in" && (
        <>
          <PassRefusal pass={current.pass} />
          <TicketEntry
            key={current.key}
            lot={lot}
            initialFile={current.scanned.file}
            onChanged={onChanged}
            onBusyChange={childBusyChange}
            onLockChange={setLocked}
            onNext={reset}
          />
        </>
      )}
      {current?.kind === "blocked" && <PassRefusal pass={current.pass} />}
      {!current && !check.busy && (
        <div className="staff-empty">
          <StaffIcon name="camera" size={38} />
          <h3>Một lần quét cho cả xe vào và xe ra</h3>
          <p>Xe vé tháng, xe vãng lai hay xe đang ra bãi đều bắt đầu từ bước kiểm tra biển số.</p>
        </div>
      )}
    </div>
  );
}
