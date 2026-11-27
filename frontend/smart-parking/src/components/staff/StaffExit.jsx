import { useEffect, useRef, useState } from "react";
import {
  platesMatch,
  staffApi,
  validatePlate,
} from "../../services/staffApi.js";
import PlateCapture, { emptyCapture, prepareCapture } from "./PlateCapture.jsx";
import {
  Notice,
  SessionInfo,
  StaffError,
  StaffIcon,
  useStaffAction,
} from "./StaffUI.jsx";

// Nhánh xe ra của màn Cổng. Có gateCapture khi xe được tìm từ bước quét: dùng luôn ảnh và biển số đó.
export function StaffExit({
  lot,
  revision,
  initialSessionId,
  initialTicketCode = "",
  gateCapture,
  onGateCapture,
  onChanged,
  onBusyChange,
  onNext,
}) {
  const [exitTicketCode, setExitTicketCode] = useState(initialTicketCode || "");
  const [session, setSession] = useState(null);
  const lookupId = useRef(initialSessionId || null);
  const [lookupError, setLookupError] = useState(null);
  const [looking, setLooking] = useState(false);
  const [ownCapture, setOwnCapture] = useState(emptyCapture);
  const capture = gateCapture ?? ownCapture;
  const setCapture = onGateCapture ?? setOwnCapture;
  const [captureBusy, setCaptureBusy] = useState(false);
  const [checked, setChecked] = useState(false);
  const [rechecking, setRechecking] = useState(false);
  const [checkedAt, setCheckedAt] = useState(null);
  const action = useStaffAction(onBusyChange);
  const busy = action.busy || captureBusy || rechecking;
  // Xe vào bằng vé tháng: không có vé giấy và không thu phí; xe ra chỉ cần đối chiếu biển số.
  const isPass = session?.verificationStatus === "MONTHLY_PASS";
  const isTicketed = Boolean(session?.verificationStatus && session.verificationStatus !== "LEGACY" && !isPass);
  const canExit = isPass || session?.status === "PAID";
  useEffect(() => {
    lookupId.current = initialSessionId || null;
  }, [initialSessionId]);
  useEffect(() => {
    if (!lookupId.current) return;
    const controller = new AbortController();
    setLooking(true);
    setLookupError(null);
    staffApi
      .findSession(lot.lotId, String(lookupId.current), "id", controller.signal)
      .then(
        (result) => {
          if (!controller.signal.aborted) setSession(result);
        },
        (error) => {
          if (!controller.signal.aborted) setLookupError(error);
        },
      )
      .finally(() => {
        if (!controller.signal.aborted) setLooking(false);
      });
    return () => controller.abort();
  }, [initialSessionId, lot.lotId, revision]);
  // Đọc lại trạng thái thật từ Core sau khi Payment clearance; UI không bao giờ tự đặt PAID.
  const recheck = async () => {
    if (rechecking || !session) return;
    setRechecking(true);
    onBusyChange(true);
    setLookupError(null);
    try {
      const fresh = await staffApi.findSession(lot.lotId, String(session.id), "id");
      setSession((current) => ({
        ...fresh,
        ticketCode: fresh.ticketCode ?? current?.ticketCode,
      }));
      setCheckedAt(new Date());
    } catch (error) {
      setLookupError(error);
    } finally {
      setRechecking(false);
      onBusyChange(false);
    }
  };
  const submit = (event) => {
    event.preventDefault();
    action.run(async () => {
      if (!checked)
        throw new Error("Xác nhận đã đối chiếu xe và xe thực sự qua cổng ra.");
      validatePlate(capture.plate);
      if (!platesMatch(capture.plate, session.plateNumber))
        throw new Error(
          "Biển số tại cổng ra không khớp với phiên xe. Hãy kiểm tra lại.",
        );
      // AI chỉ được gửi kèm khi staff đã xác nhận qua confirm-exit gắn với đúng phiên này.
      const imageUrl = await prepareCapture(capture, setCapture, lot.lotId, session.id);
      const result = await staffApi.checkOut(
        session.id,
        lot.lotId,
        capture.plate,
        imageUrl,
        isTicketed ? exitTicketCode : null,
        capture.recognition?.id,
        capture.note,
      );
      const updatedSession = result?.data ?? result;
      if (!updatedSession?.id)
        throw new Error(
          "Chưa xác nhận được kết quả xe ra. Hãy tra cứu lại phiên trước khi gửi lại.",
        );
      setSession(updatedSession);
      // Ảnh quét ở cổng được giữ lại tới khi staff bấm sang xe tiếp theo.
      if (!gateCapture) setCapture(emptyCapture());
      setChecked(false);
      onChanged();
    });
  };
  return (
    <div className="staff-side-stack">
      <StaffError error={lookupError} />
      {looking && <Notice>Đang tải thông tin phiên xe…</Notice>}
      {session && !looking && (
        <div className="staff-work-grid">
          <section className="staff-panel">
            <div className="staff-panel-heading">
              <h2>Thông tin lúc xe vào</h2>
            </div>
            <SessionInfo session={session} />
            {isPass && session.status === "ACTIVE" && (
              <Notice kind="success">
                <strong>Xe vào bằng vé tháng — không thu phí.</strong> Đối chiếu biển số tại cổng ra rồi xác nhận
                xe ra. Không cần thanh toán.
              </Notice>
            )}
            {!isPass && session.status === "ACTIVE" && (
              <Notice kind="warning">
                {isTicketed && session.verificationStatus !== "VERIFIED" ? (
                  <>
                    <strong>Chưa xác minh xe tại ô.</strong> Vé điện tử cần
                    nhân viên trực khu vực xác minh xe đã đỗ trước khi thanh
                    toán và ra bãi.
                  </>
                ) : (
                  <>
                    <strong>Chưa xác nhận thanh toán.</strong> Phiên vẫn ở
                    trạng thái ACTIVE: Payment Service chưa gửi xác nhận thanh
                    toán cho Core. Màn hình này không thu phí hoặc tự đặt
                    PAID; xe chỉ được ra sau khi phiên chuyển sang PAID.
                  </>
                )}{" "}
                <button
                  type="button"
                  className="staff-text-button"
                  disabled={busy}
                  onClick={recheck}
                >
                  {rechecking ? "Đang kiểm tra…" : "Kiểm tra lại trạng thái"}
                </button>
                {checkedAt && (
                  <small>
                    {" "}
                    Kiểm tra lúc{" "}
                    {checkedAt.toLocaleTimeString("vi-VN", {
                      hour: "2-digit",
                      minute: "2-digit",
                      second: "2-digit",
                    })}
                    : trạng thái vẫn là ACTIVE.
                  </small>
                )}
              </Notice>
            )}
            {!isPass && session.status === "ACTIVE" &&
              (!isTicketed || session.verificationStatus === "VERIFIED") && (
                <div style={{ marginTop: 12 }}>
                  <p>Khách thanh toán tiền mặt tại quầy trước khi checkout.</p>
                  <button
                    type="button"
                    className="staff-button staff-button--primary"
                    disabled={busy}
                    onClick={() => action.run(async () => {
                      await staffApi.recordCounterPayment(session.id);
                      await recheck();
                      onChanged();
                    })}
                  >
                    {action.busy ? "Đang ghi nhận thanh toán…" : "Thu tiền tại quầy & xác nhận"}
                  </button>
                  <StaffError error={action.error} />
                </div>
              )}
            {session.status === "COMPLETED" && (
              <Notice kind="success">
                Đã ghi nhận xe ra bãi lúc{" "}
                {new Date(session.checkOutTime).toLocaleTimeString("vi-VN")}.
                Phiên đã kết thúc.
              </Notice>
            )}
            {session.status === "CANCELLED" && (
              <Notice kind="warning">
                Phiên đã hủy, không thể thực hiện thủ tục xe ra.
              </Notice>
            )}
          </section>
          {["ACTIVE", "PAID"].includes(session.status) && (
            <section className="staff-panel">
              <div className="staff-panel-heading">
                <h2>Đối chiếu và xác nhận xe ra</h2>
              </div>
              <StaffError error={action.error} />
              <form onSubmit={submit} aria-label="Xác nhận xe ra">
                {isTicketed && (
                  <label className="staff-field">Mã vé đã đối chiếu
                    <input required maxLength={40} disabled={busy} value={exitTicketCode} onChange={event => setExitTicketCode(event.target.value)} />
                  </label>
                )}
                {gateCapture ? (
                  <p>
                    Biển số tại cổng ra: <strong>{capture.plate}</strong>. Nếu sai, sửa ở bước quét phía trên rồi bấm
                    Kiểm tra lại.
                  </p>
                ) : (
                <PlateCapture
                  title="Ảnh tại cổng ra"
                  plateLabel="Biển số thực tế tại cổng ra"
                  lotId={lot.lotId}
                  purpose="EXIT"
                  value={capture}
                  onChange={setCapture}
                  disabled={busy}
                  onWorking={(value) => {
                    setCaptureBusy(value);
                    onBusyChange(value);
                  }}
                />
                )}
                <label className="staff-check">
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={(event) => setChecked(event.target.checked)}
                    disabled={busy || !canExit}
                    required
                  />
                  <span>
                    Đã đối chiếu xe, biển số và xác nhận xe thực sự qua cổng ra.
                  </span>
                </label>
                <button
                  type="submit"
                  className="staff-button staff-button--primary staff-submit"
                  disabled={busy || !canExit}
                >
                  <StaffIcon name="exit" />
                  {action.busy ? "Đang ghi nhận…" : "Xác nhận xe đã ra bãi"}
                </button>
              </form>
            </section>
          )}
        </div>
      )}
      {session?.status === "COMPLETED" && onNext && (
        <button type="button" className="staff-button staff-button--primary" onClick={onNext}>
          Tiếp nhận xe tiếp theo
        </button>
      )}
    </div>
  );
}
