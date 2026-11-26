import { useState } from "react";
import { sceneStatuses } from "../services/parkingScene.js";

const statusCommands = [
  { status: "MAINTENANCE", words: ["bao tri", "bao duong", "tam khoa"] },
  { status: "EMPTY", words: ["trong", "mo lai", "san sang"] },
];

const normalize = value => String(value || "")
  .normalize("NFD")
  .replace(/[\u0300-\u036f]/g, "")
  .replace(/đ/g, "d")
  .replace(/Đ/g, "d")
  .toLocaleLowerCase("vi-VN");

const compact = value => normalize(value).replace(/[^a-z0-9]/g, "");

export function interpretParkingAdminCommand(command, slots) {
  const normalized = normalize(command);
  const compactCommand = compact(command);
  const slot = slots.find(item => compactCommand.includes(compact(item.code)));
  if (!slot)
    return { kind: "error", message: "Chưa tìm thấy mã ô trong câu lệnh. Ví dụ: “xem ô A-01”." };

  const target = statusCommands.find(item => item.words.some(word => normalized.includes(word)));
  const wantsWrite = /\b(doi|chuyen|dat|khoa|mo)\b/.test(normalized) && Boolean(target);
  if (!wantsWrite)
    return {
      kind: "read",
      slot,
      message: `${slot.code} hiện ở trạng thái ${sceneStatuses[slot.status] || slot.status}.`,
    };

  if (slot.isProtected)
    return {
      kind: "error",
      message: `${slot.code} đang gắn với xe hoặc điều phối. Trợ lý không thể đề xuất thay đổi.`,
    };
  if (slot.status === target.status)
    return {
      kind: "read",
      slot,
      message: `${slot.code} đã ở trạng thái ${sceneStatuses[target.status]}. Không cần thay đổi.`,
    };

  return { kind: "write", slot, status: target.status };
}

export default function ParkingAdminAssistant({ slots, disabled, onConfirm }) {
  const [command, setCommand] = useState("");
  const [response, setResponse] = useState("Hỏi trạng thái một ô hoặc đề xuất chuyển ô trống sang bảo trì.");
  const [pending, setPending] = useState(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const analyze = event => {
    event.preventDefault();
    setError("");
    setPending(null);
    setReason("");
    const result = interpretParkingAdminCommand(command, slots);
    if (result.kind === "write") {
      setPending(result);
      setResponse(`Đề xuất: chuyển ${result.slot.code} sang ${sceneStatuses[result.status]}. Kiểm tra và xác nhận bên dưới.`);
      return;
    }
    setResponse(result.message);
  };

  const confirm = async () => {
    if (!pending || !reason.trim() || disabled) return;
    setBusy(true);
    setError("");
    try {
      const result = await onConfirm({
        slot: pending.slot,
        status: pending.status,
        reason: `Trợ lý Admin: ${reason.trim()}`,
      });
      setResponse(result?.message || `Đã cập nhật ${pending.slot.code}.`);
      setPending(null);
      setReason("");
      setCommand("");
    } catch (failure) {
      setError(failure.message || "Không thể thực hiện thay đổi.");
    } finally {
      setBusy(false);
    }
  };

  return <section className="parking-assistant" aria-labelledby="parking-assistant-title">
    <div className="parking-assistant__intro">
      <span>ADMIN ASSISTANT</span>
      <h2 id="parking-assistant-title">Tra cứu và chuẩn bị thao tác ô đỗ</h2>
      <p>Trợ lý chỉ đọc snapshot hiện tại. Mọi thay đổi đều cần lý do và xác nhận, sau đó Core kiểm tra lại quyền và trạng thái.</p>
    </div>
    <div className="parking-assistant__conversation">
      <form onSubmit={analyze}>
        <label htmlFor="parking-assistant-command">Câu lệnh</label>
        <div>
          <input
            id="parking-assistant-command"
            value={command}
            disabled={busy}
            onChange={event => setCommand(event.target.value)}
            placeholder="Ví dụ: đổi ô A-01 sang bảo trì"
          />
          <button className="btn btn--outline" disabled={busy || !command.trim()} type="submit">Phân tích lệnh</button>
        </div>
      </form>
      <p className="parking-assistant__response" role="status">{response}</p>
      {error && <p className="workspace-error" role="alert">{error}</p>}
      {pending && <div className="parking-assistant__confirm">
        <div>
          <strong>{pending.slot.code} → {sceneStatuses[pending.status]}</strong>
          <small>Chưa có dữ liệu nào được ghi.</small>
        </div>
        <label>Lý do xác nhận
          <input
            value={reason}
            maxLength={255}
            disabled={busy || disabled}
            onChange={event => setReason(event.target.value)}
            placeholder="Ví dụ: kiểm tra cảm biến tại ô"
          />
        </label>
        <div className="parking-assistant__actions">
          <button className="btn btn--primary" type="button" disabled={busy || disabled || !reason.trim()} onClick={confirm}>
            {busy ? "Đang kiểm tra…" : "Xác nhận thay đổi"}
          </button>
          <button className="btn btn--outline" type="button" disabled={busy} onClick={() => { setPending(null); setReason(""); setResponse("Đã hủy đề xuất. Không có dữ liệu nào được ghi."); }}>
            Hủy đề xuất
          </button>
        </div>
      </div>}
      {disabled && <p className="parking-assistant__offline">Trợ lý đang ở chế độ chỉ đọc vì snapshot chưa đồng bộ.</p>}
    </div>
  </section>;
}
