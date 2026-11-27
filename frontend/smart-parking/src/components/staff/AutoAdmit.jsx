import { useEffect, useRef, useState } from "react";
import { isClearRefusal } from "../../services/gateAuto.js";
import { staffError } from "../../services/staffApi.js";
import { Notice } from "./StaffUI.jsx";

/**
 * Hệ thống tự cho xe vào sau vài giây. Staff vẫn kiểm soát: có thể cho vào ngay, hoặc hủy để sửa biển số.
 * plan.run() dùng một eventId cố định, nên bấm "Thử lại" sau lỗi mạng không tạo thêm lượt xe.
 */
export default function AutoAdmit({ plan, seconds, onDone, onCancel, onManual, onLockChange, onBusyChange }) {
  const [remaining, setRemaining] = useState(seconds);
  const [phase, setPhase] = useState("waiting"); // waiting | sending | failed
  const [failure, setFailure] = useState(null);
  const started = useRef(false);

  const send = async () => {
    if (started.current) return;
    started.current = true;
    setPhase("sending");
    setFailure(null);
    onLockChange?.(true);
    onBusyChange?.(true);
    try {
      const result = await plan.run();
      onLockChange?.(false);
      onBusyChange?.(false);
      onDone(result);
    } catch (error) {
      started.current = false;
      onBusyChange?.(false);
      // Core từ chối rõ ràng: chưa có lượt nào, mở khóa để Staff xử lý tay. Lỗi mạng: giữ khóa để gửi lại đúng yêu cầu.
      if (isClearRefusal(error)) onLockChange?.(false);
      setFailure(error);
      setPhase("failed");
    }
  };

  useEffect(() => {
    if (phase !== "waiting") return undefined;
    if (remaining <= 0) {
      void send();
      return undefined;
    }
    const timer = setTimeout(() => setRemaining((value) => value - 1), 1000);
    return () => clearTimeout(timer);
    // send() chỉ dùng ref và props ổn định trong một kế hoạch; mỗi kế hoạch mount một AutoAdmit mới (theo key).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [remaining, phase]);

  if (phase === "failed") {
    const refused = isClearRefusal(failure);
    return (
      <Notice kind="error">
        <strong>Chưa tự cho xe {plan.plate} vào được.</strong> {staffError(failure)}{" "}
        {refused ? (
          <>
            <button type="button" className="staff-button staff-button--primary" onClick={() => onManual(staffError(failure))}>Xử lý thủ công</button>{" "}
            <button type="button" className="staff-button" onClick={onCancel}>Hủy, sửa biển số</button>
          </>
        ) : (
          <>
            Chưa rõ yêu cầu đã được ghi nhận chưa; gửi lại dùng cùng mã sự kiện nên không tạo thêm lượt.{" "}
            <button type="button" className="staff-button staff-button--primary" onClick={() => void send()}>Thử lại</button>{" "}
            <button type="button" className="staff-button" onClick={() => onManual(staffError(failure))}>Xử lý thủ công</button>
          </>
        )}
      </Notice>
    );
  }

  return (
    <Notice kind="success">
      <strong>{plan.title}</strong> · {plan.plate} · {plan.summary}
      <br />
      {phase === "sending" ? (
        "Đang cho xe vào…"
      ) : (
        <>
          Tự động cho vào sau <strong>{remaining}</strong> giây.{" "}
          <button type="button" className="staff-button staff-button--primary" onClick={() => void send()}>Cho vào ngay</button>{" "}
          <button type="button" className="staff-button" onClick={onCancel}>Hủy, sửa biển số</button>
        </>
      )}
    </Notice>
  );
}
