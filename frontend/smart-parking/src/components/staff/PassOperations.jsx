import { useCallback, useRef, useState } from "react";
import { apiRequest } from "../../services/api.js";
import { operationsApi } from "../../services/operationsApi.js";
import { validatePhoto } from "../../services/staffApi.js";
import { Notice, StaffError, useStaffAction, useStaffQuery } from "./StaffUI.jsx";

const Field = ({ label, children }) => <label className="staff-field">{label}{children}</label>;

// Lý do Core từ chối vé -> việc nhân viên nên làm tiếp.
const nextStep = {
  PASS_NOT_FOUND: "Tiếp nhận ở form bên dưới. Nếu xe đang đi ra, hãy tra theo mã vé ở bước quét.",
  PASS_EXPIRED: "Khách có thể gia hạn trên ứng dụng, hoặc tiếp nhận như xe vãng lai.",
  PASS_NOT_STARTED: "Tiếp nhận như xe vãng lai nếu khách cần gửi ngay.",
  PASS_SUSPENDED: "Liên hệ quản lý bãi trước khi quyết định.",
  PASS_UNPAID: "Nhờ khách hoàn tất thanh toán trên ứng dụng, hoặc tiếp nhận như xe vãng lai.",
  ALREADY_INSIDE: "Lượt này không tra được tại bãi đang chọn. Tra theo mã vé, hoặc liên hệ quản lý.",
};

const day = (value) => (value ? new Date(value).toLocaleDateString("vi-VN") : "—");
const daysLeft = (value) => Math.max(0, Math.ceil((new Date(value).getTime() - Date.now()) / 86_400_000));

export function PassRefusal({ pass }) {
  const walkIn = pass.reason === "PASS_NOT_FOUND";
  return (
    <Notice kind={walkIn ? "info" : "warning"}>
      <strong>{walkIn ? `Xe vãng lai · ${pass.plateNumber}` : pass.message}</strong>
      {walkIn && <> {pass.message}</>}
      {pass.endsAtUtc && pass.reason !== "ALREADY_INSIDE" && <> Hiệu lực đến {day(pass.endsAtUtc)}.</>}
      {nextStep[pass.reason] && <> {nextStep[pass.reason]}</>}
    </Notice>
  );
}

// Nhánh vé tháng của màn Cổng: biển số và vé đã được kiểm tra ở bước quét.
export function PassEntry({ lot, pass, initialFile = null, onChanged, onBusyChange, onLockChange, onNext }) {
  const loadZones = useCallback((signal) => operationsApi(lot.lotId, "zones", null, null, signal), [lot.lotId]);
  const zones = useStaffQuery(loadZones);
  const fixedZones = (zones.data || []).filter((zone) => zone.type === "FIXED" && zone.isActive);
  const [zoneId, setZoneId] = useState("");
  const [file, setFile] = useState(initialFile);
  const [imageId, setImageId] = useState(null);
  const [note, setNote] = useState("");
  const [created, setCreated] = useState(null);
  const request = useRef(null);
  const enter = useStaffAction(onBusyChange);
  const busy = enter.busy;
  // Sau khi đã gửi yêu cầu vào bãi mà chưa rõ kết quả, khóa lựa chọn để gửi lại đúng nội dung (cùng mã sự kiện).
  const locked = Boolean(request.current);
  const selectedZone = zoneId || (fixedZones.length === 1 ? String(fixedZones[0].id) : "");

  const reset = () => {
    request.current = null;
    onLockChange?.(false);
    onNext();
  };

  const submit = (event) => {
    event.preventDefault();
    enter.run(async () => {
      // Giữ nguyên yêu cầu khi mạng lỗi/chưa rõ kết quả để gửi lại không tạo thêm lượt xe.
      if (!request.current) {
        let savedId = imageId;
        if (file && !savedId) {
          const body = new FormData();
          body.append("file", file);
          const uploaded = await apiRequest("/v1/images", { method: "POST", body });
          savedId = uploaded.data.id;
          setImageId(savedId);
        }
        request.current = {
          eventId: crypto.randomUUID(),
          zoneId: Number(selectedZone),
          plateNumber: pass.plateNumber,
          imageId: savedId,
          note: note.trim() || null,
        };
      }
      onLockChange?.(true);
      let result;
      try {
        result = await operationsApi(lot.lotId, "pass-entries", request.current);
      } catch (error) {
        // 4xx: Core từ chối rõ ràng, chưa có lượt nào được tạo -> mở khóa để sửa. Lỗi mạng/5xx: giữ nguyên để gửi lại an toàn.
        if (error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
          request.current = null;
          onLockChange?.(false);
        }
        throw error;
      }
      onLockChange?.(false);
      setCreated(result);
      onChanged();
    });
  };

  return (
    <section className="staff-panel">
      <h2>Xe vé tháng vào</h2>
      <p>Xe vé tháng vào khu cố định, không cần cấp vé giấy và không thu phí.</p>
      <StaffError error={zones.error || enter.error} />
      {zones.error && <button className="staff-button" onClick={zones.reload}>Tải lại khu</button>}

      {created ? (
        <>
          <Notice kind="success">
            Đã cho xe {created.session.plateNumber} vào bằng vé tháng. Lượt #{created.session.id}.
            {created.replay && " (Yêu cầu này đã được ghi nhận trước đó, không tạo thêm lượt mới.)"}
          </Notice>
          <p>Lúc xe ra chỉ cần quét lại biển số ở màn này, không thu phí.</p>
          <button className="staff-button staff-button--primary" onClick={reset}>Tiếp nhận xe tiếp theo</button>
        </>
      ) : (
        <form onSubmit={submit} aria-label="Cho xe vé tháng vào">
          <Notice kind="success">
            <strong>Vé tháng còn hiệu lực</strong> · {pass.planName} · {pass.plateNumber} · đến {day(pass.endsAtUtc)} (còn {daysLeft(pass.endsAtUtc)} ngày)
          </Notice>
          <fieldset disabled={busy || locked || !lot.isActive} className="operations-fields">
            <Field label="Khu vé tháng">
              <select required value={selectedZone} onChange={(event) => setZoneId(event.target.value)}>
                <option value="">Chọn khu</option>
                {fixedZones.map((zone) => (
                  <option key={zone.id} value={zone.id}>{zone.code} · {zone.name}</option>
                ))}
              </select>
            </Field>
            {!zones.loading && !zones.error && !fixedZones.length && (
              <Notice kind="warning">Bãi chưa có khu vé tháng đang hoạt động. Liên hệ Admin để cấu hình.</Notice>
            )}
            <Field label="Ảnh xe (không bắt buộc)">
              <input
                type="file"
                accept="image/jpeg,image/png,image/webp"
                capture="environment"
                onChange={(event) => {
                  const next = event.target.files?.[0];
                  event.target.value = "";
                  if (!next) return;
                  try {
                    validatePhoto(next);
                    setFile(next);
                    setImageId(null);
                    enter.setError(null);
                  } catch (error) {
                    enter.setError(error);
                  }
                }}
              />
            </Field>
            {file && <p>{file.name}</p>}
            <Field label="Ghi chú (không bắt buộc)">
              <textarea maxLength={500} value={note} onChange={(event) => setNote(event.target.value)} />
            </Field>
          </fieldset>
          <button type="submit" className="staff-button staff-button--primary" disabled={busy || !lot.isActive || !selectedZone}>
            {enter.busy ? "Đang tiếp nhận…" : locked ? "Gửi lại cùng lượt xe" : "Cho xe vào"}
          </button>
          {locked && !busy && (
            <>
              <Notice kind="warning">Yêu cầu đã gửi. Lần gửi lại dùng cùng mã sự kiện nên không tạo thêm lượt. Nếu bỏ qua, hãy quét lại xe này để đối chiếu.</Notice>
              <button className="staff-button" type="button" onClick={reset}>Đã đối chiếu, nhập xe khác</button>
            </>
          )}
        </form>
      )}
    </section>
  );
}
