import { useCallback, useRef, useState } from "react";
import { apiRequest } from "../../services/api.js";
import { operationsApi } from "../../services/operationsApi.js";
import { formatVietnameseCarPlateInput, validateVietnameseCarPlate } from "../../services/plateNumber.js";
import { validatePhoto } from "../../services/staffApi.js";
import { Notice, StaffError, useStaffAction, useStaffQuery } from "./StaffUI.jsx";

const Field = ({ label, children }) => <label className="staff-field">{label}{children}</label>;

// Lý do Core từ chối vé -> việc nhân viên nên làm tiếp.
const nextStep = {
  PASS_NOT_FOUND: "Xe chưa đăng ký vé tháng tại bãi này. Hướng dẫn xe vào làn vãng lai (màn Xe vào).",
  PASS_EXPIRED: "Vé đã hết hạn. Khách có thể gia hạn trên ứng dụng, hoặc cho xe vào làn vãng lai.",
  PASS_NOT_STARTED: "Vé chưa đến ngày hiệu lực. Cho xe vào làn vãng lai nếu khách cần gửi ngay.",
  PASS_SUSPENDED: "Vé đang bị tạm khóa. Liên hệ quản lý bãi trước khi quyết định.",
  PASS_UNPAID: "Khách chưa thanh toán vé. Nhờ khách hoàn tất thanh toán trên ứng dụng.",
  ALREADY_INSIDE: "Xe này đang có một lượt gửi chưa kết thúc. Kiểm tra lại ở màn Xe ra.",
  INVALID_PLATE: "Nhập lại biển số ô tô, ví dụ 51F-123.45.",
};

const day = (value) => (value ? new Date(value).toLocaleDateString("vi-VN") : "—");
const daysLeft = (value) => Math.max(0, Math.ceil((new Date(value).getTime() - Date.now()) / 86_400_000));

export function PassEntry({ lot, onChanged, onBusyChange }) {
  const loadZones = useCallback((signal) => operationsApi(lot.lotId, "zones", null, null, signal), [lot.lotId]);
  const zones = useStaffQuery(loadZones);
  const fixedZones = (zones.data || []).filter((zone) => zone.type === "FIXED" && zone.isActive);
  const [plate, setPlate] = useState("");
  const [pass, setPass] = useState(null);
  const [zoneId, setZoneId] = useState("");
  const [file, setFile] = useState(null);
  const [imageId, setImageId] = useState(null);
  const [note, setNote] = useState("");
  const [created, setCreated] = useState(null);
  const request = useRef(null);
  const check = useStaffAction(onBusyChange);
  const enter = useStaffAction(onBusyChange);
  const busy = check.busy || enter.busy;
  // Sau khi đã gửi yêu cầu vào bãi mà chưa rõ kết quả, khóa lựa chọn để gửi lại đúng nội dung (cùng mã sự kiện).
  const locked = Boolean(request.current);
  const selectedZone = zoneId || (fixedZones.length === 1 ? String(fixedZones[0].id) : "");

  const reset = () => {
    request.current = null;
    setCreated(null);
    setPass(null);
    setPlate("");
    setFile(null);
    setImageId(null);
    setNote("");
    setZoneId("");
    check.setError(null);
    enter.setError(null);
  };

  const lookup = (event) => {
    event.preventDefault();
    check.run(async () => {
      enter.setError(null);
      const valid = validateVietnameseCarPlate(plate);
      setPass(await operationsApi(lot.lotId, `passes/lookup?plate=${encodeURIComponent(valid)}`, null));
    });
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
      let result;
      try {
        result = await operationsApi(lot.lotId, "pass-entries", request.current);
      } catch (error) {
        // 4xx: Core từ chối rõ ràng, chưa có lượt nào được tạo -> mở khóa để sửa. Lỗi mạng/5xx: giữ nguyên để gửi lại an toàn.
        if (error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) request.current = null;
        throw error;
      }
      setCreated(result);
      onChanged();
    });
  };

  const canEnter = pass?.canEnter;
  const refused = pass && !pass.canEnter;
  return (
    <section className="staff-panel">
      <h2>Xe vé tháng vào</h2>
      <p>Nhập biển số để kiểm tra vé tháng. Xe có vé còn hiệu lực vào khu cố định, không cần cấp vé giấy và không thu phí.</p>
      <StaffError error={zones.error || check.error || enter.error} />
      {zones.error && <button className="staff-button" onClick={zones.reload}>Tải lại khu</button>}

      {created ? (
        <>
          <Notice kind="success">
            Đã cho xe {created.session.plateNumber} vào bằng vé tháng. Lượt #{created.session.id}.
            {created.replay && " (Yêu cầu này đã được ghi nhận trước đó, không tạo thêm lượt mới.)"}
          </Notice>
          <p>Xe ra cổng chỉ cần đối chiếu biển số ở màn Xe ra, không thu phí.</p>
          <button className="staff-button staff-button--primary" onClick={reset}>Tiếp nhận xe tiếp theo</button>
        </>
      ) : (
        <>
          <form className="staff-search-form" onSubmit={lookup}>
            <Field label="Biển số xe">
              <input
                required
                disabled={busy || locked}
                value={plate}
                maxLength={12}
                placeholder="Ví dụ: 51F-123.45"
                autoComplete="off"
                onChange={(event) => {
                  setPlate(formatVietnameseCarPlateInput(event.target.value));
                  setPass(null);
                  check.setError(null);
                }}
              />
            </Field>
            <button type="submit" className="staff-button staff-button--primary" disabled={busy || locked || !lot.isActive}>
              {check.busy ? "Đang kiểm tra…" : "Kiểm tra vé tháng"}
            </button>
          </form>

          {refused && (
            <Notice kind="warning">
              <strong>{pass.message}</strong>
              {pass.endsAtUtc && pass.reason !== "ALREADY_INSIDE" && <> Hiệu lực đến {day(pass.endsAtUtc)}.</>}
              {nextStep[pass.reason] && <> {nextStep[pass.reason]}</>}
            </Notice>
          )}

          {canEnter && (
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
              {locked && (
                <>
                  <Notice kind="warning">Yêu cầu đã gửi. Kiểm tra màn Xe ra trước khi gửi lại; lần gửi lại dùng cùng mã sự kiện nên không tạo thêm lượt.</Notice>
                  <button className="staff-button" type="button" disabled={busy} onClick={reset}>Đã đối chiếu, nhập xe khác</button>
                </>
              )}
            </form>
          )}
        </>
      )}
    </section>
  );
}
