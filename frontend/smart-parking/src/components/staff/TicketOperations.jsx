import { useCallback, useRef, useState } from "react";
import { operationsApi } from "../../services/operationsApi.js";
import { apiRequest } from "../../services/api.js";
import { validatePhoto } from "../../services/staffApi.js";
import { formatVietnameseCarPlateInput, validateVietnameseCarPlate } from "../../services/plateNumber.js";
import { Notice, StaffError, useStaffAction, useStaffQuery, SafePhoto } from "./StaffUI.jsx";
import StaffVerification from "./StaffVerification.jsx";
import StaffParkingMap from "./StaffParkingMap.jsx";

const Field = ({ label, children }) => <label className="staff-field">{label}{children}</label>;

export function TicketEntry({ lot, onChanged, onBusyChange, initialFile = null, onLockChange, onNext }) {
  const load = useCallback(signal => operationsApi(lot.lotId, "zones", null, null, signal), [lot.lotId]);
  const zones = useStaffQuery(load);
  const [zoneId, setZone] = useState("");
  const [ticket, setTicket] = useState("");
  const [file, setFile] = useState(initialFile);
  const [imageId, setImageId] = useState(null);
  const [reason, setReason] = useState("");
  const [created, setCreated] = useState(null);
  const request = useRef(null);
  const action = useStaffAction(onBusyChange);
  const submit = e => {
    e.preventDefault();
    action.run(async () => {
      // Keep the exact request across uncertain network failures. Editing only resumes after lookup/reset.
      if (!request.current) {
        let savedId = imageId;
        if (file && !savedId) {
          const body = new FormData(); body.append("file", file);
          const result = await apiRequest("/v1/images", { method: "POST", body });
          savedId = result.data.id; setImageId(savedId);
        }
        request.current = { eventId: crypto.randomUUID(), zoneId: Number(zoneId), ticketCode: ticket,
          imageId: savedId, manualReason: reason || null };
      }
      onLockChange?.(true);
      let result;
      try { result = await operationsApi(lot.lotId, "entries", request.current); }
      catch (error) {
        // Server từ chối rõ ràng (4xx): chưa có lượt nào được tạo, mở khóa để sửa mã vé/khu mà không mất ảnh.
        // Lỗi mạng/5xx là kết quả chưa rõ: giữ nguyên eventId để gửi lại an toàn.
        if (error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
          request.current = null; onLockChange?.(false);
        }
        throw error;
      }
      onLockChange?.(false); setCreated(result); onChanged();
    });
  };
  const reset = () => { request.current = null; setCreated(null); setTicket(""); setFile(null); setImageId(null); setReason(""); action.setError(null); onLockChange?.(false); onNext?.(); };
  const locked = action.busy || Boolean(request.current);
  return <section className="staff-panel">
    <h2>Cấp vé và tiếp nhận xe</h2>
    <p>Nhân viên tại ô sẽ đối chiếu xe thực tế và ghi nhận biển số chính thức.</p>
    <StaffError error={zones.error || action.error} />
    {zones.error && <button className="staff-button" onClick={zones.reload}>Tải lại khu</button>}
    {created ? <>
      <Notice kind="success">Đã cấp vé {created.ticketCode}. Lượt #{created.id} đang chờ xác minh.</Notice>
      <p>Ô sẽ chuyển sang Có xe sau khi nhân viên xác minh.</p>
      <button className="staff-button staff-button--primary" onClick={reset}>Tiếp nhận xe tiếp theo</button>
    </> : <form onSubmit={submit}>
      <fieldset disabled={locked || !lot.isActive} className="operations-fields">
        <Field label="Khu bình thường"><select required value={zoneId} onChange={e => setZone(e.target.value)}>
          <option value="">Chọn khu</option>{zones.data?.filter(z => z.type === "TRANSIENT" && z.isActive).map(z => <option key={z.id} value={z.id}>{z.code} · {z.name}</option>)}
        </select></Field>
        <Field label="Mã vé"><input required maxLength={40} value={ticket} onChange={e => setTicket(e.target.value)} autoComplete="off" /></Field>
        <Field label="Ảnh xe"><input type="file" accept="image/jpeg,image/png,image/webp" capture="environment" onChange={e => {
          const next = e.target.files?.[0]; e.target.value = "";
          if (!next) return;
          try { validatePhoto(next); setFile(next); setImageId(null); action.setError(null); }
          catch (error) { action.setError(error); }
        }} /></Field>
        {file && <p>{file.name}</p>}
        <Field label="Lý do tiếp nhận"><textarea required maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></Field>
      </fieldset>
      <button type="submit" className="staff-button staff-button--primary" disabled={action.busy || !lot.isActive || zones.loading || Boolean(zones.error)}>
        {action.busy ? "Đang tiếp nhận…" : request.current ? "Gửi lại cùng lượt xe" : "Cấp vé và đưa vào hàng chờ"}
      </button>
      {request.current && <Notice kind="warning">Yêu cầu đã gửi. Kiểm tra trạng thái lượt xe trước khi gửi lại; lần gửi lại sẽ dùng cùng mã sự kiện và nội dung.</Notice>}
      {request.current && <button className="staff-button" type="button" disabled={action.busy} onClick={reset}>Đã đối chiếu vé, nhập lượt khác</button>}
    </form>}
  </section>;
}

export function TicketVerification({ lot, revision, onChanged, onBusyChange, initialSessionId }) {
  const load = useCallback(async signal => ({
    sessions: await operationsApi(lot.lotId, "sessions", null, null, signal),
    layout: await operationsApi(lot.lotId, "layout", null, null, signal),
  }), [lot.lotId, revision]);
  const query = useStaffQuery(load);
  const [selected, select] = useState(null);
  const [plate, setPlate] = useState("");
  const [fuel, setFuel] = useState("");
  const [slotId, setSlot] = useState("");
  const [reason, setReason] = useState("");
  const [observed, setObserved] = useState(false);
  const [oldEmpty, setOldEmpty] = useState(false);
  const [evidence, setEvidence] = useState(null);
  const action = useStaffAction(onBusyChange);
  const choose = session => {
    select(session); setPlate(session.temporaryPlateNumber || session.plateNumber || "");
    setFuel(session.fuelType || ""); setSlot(""); setReason(""); setObserved(false); setOldEmpty(false); setEvidence(null); action.setError(null);
  };
  const session = query.data?.sessions.find(s => s.id === selected?.id && s.revision === selected.revision);
  const move = session?.verificationStatus === "VERIFIED";
  const hasModernSessions = query.data?.sessions.some(s => s.eventId && s.verificationStatus !== "MONTHLY_PASS");
  const canSelectSlot = slot => Boolean(
    session &&
    slot.physicalStatus === "EMPTY" &&
    slot.isActive &&
    slot.floorActive &&
    slot.zoneId === session.zoneId &&
    (slot.allowedFuelType === "ANY" ||
      slot.allowedFuelType === (move ? session.fuelType : fuel)),
  );
  const submit = e => { e.preventDefault(); action.run(async () => {
    if (!session) throw new Error("Lượt đã thay đổi. Chọn lại trong danh sách.");
    const confirmedPlate = move ? undefined : validateVietnameseCarPlate(plate);
    await operationsApi(lot.lotId, `sessions/${session.id}/${move ? "move" : "verify"}`, move
      ? { actualSlotId: Number(slotId), expectedRevision: session.revision, reason, oldSlotObservedEmpty: oldEmpty, vehicleObservedAtDestination: observed }
      : { actualSlotId: Number(slotId), expectedRevision: session.revision, plateNumber: confirmedPlate, fuelType: fuel, reason, vehicleObserved: observed });
    select(null); setEvidence(null); onChanged(); query.reload();
  }); };
  return <>{query.data && (!query.data.sessions.length || hasModernSessions) && <section className="staff-panel staff-map-panel" aria-label="Sơ đồ 2D bãi đỗ">
    <div className="staff-panel-heading">
      <div>
        <span className="staff-eyebrow">SƠ ĐỒ BÃI ĐỖ</span>
        <h2>Chọn ô xe thực tế trên sơ đồ 2D</h2>
        <p>{session ? `Đang xác minh xe ${session.temporaryPlateNumber || session.plateNumber || "chưa có biển số"}. Chọn ô phù hợp, nhập biển số và xác nhận xe.` : "Sơ đồ theo bố trí bãi đỗ. Chọn lượt xe bên dưới để bật các ô phù hợp và xác minh biển số."}</p>
      </div>
    </div>
    <div className="staff-slot-legend">
      <span><i className="is-empty" /> Trống</span>
      <span><i className="is-occupied" /> Có xe</span>
      <span><i className="is-blocked" /> Đã giữ / không thể chọn</span>
    </div>
    <StaffParkingMap
      floors={query.data.layout.floors}
      slots={query.data.layout.slots}
      sessions={query.data.sessions}
      session={session}
      mode="verify"
      selectedSlotId={slotId}
      disabled={action.busy || query.loading || Boolean(query.error)}
      canSelectSlot={canSelectSlot}
      onSelect={slot => setSlot(String(slot.id))}
    />
  </section>}
  <div className="staff-work-grid">
    <section className="staff-panel"><h2>Hàng chờ và xe đã xác minh</h2>
      <StaffError error={query.error} />
      <button className="staff-button" disabled={action.busy || query.loading} onClick={query.reload}>Tải lại danh sách</button>
      {query.loading && <Notice>Đang tải dữ liệu…</Notice>}
      {!query.loading && !query.error && !query.data?.sessions.length && <p>Không có lượt xe đang mở.</p>}
      <div className="operations-session-list">{!query.error && query.data?.sessions.filter(s => s.eventId && s.verificationStatus !== "MONTHLY_PASS").map(s => <button className={`operations-session ${s.id === selected?.id ? "is-selected" : ""}`} disabled={action.busy} key={s.id} onClick={() => choose(s)}>
        <strong>{s.ticketCode} · #{s.id}</strong><span>{s.plateNumber || s.temporaryPlateNumber || "Chưa đọc được biển số"}</span>
        <small>{s.verificationStatus === "WAITING_VERIFICATION" ? "Chờ xác minh" : `Đã đỗ · ${s.slotCode}`}</small>
      </button>)}</div>
    </section>
    <section className="staff-panel"><h2>{move ? "Chuyển xe đã đỗ" : "Xác minh xe tại ô"}</h2>
      <StaffError error={action.error} />
      {!session ? <p>Chọn lượt gửi từ danh sách hiện tại.</p> : <form onSubmit={submit}>
        <fieldset disabled={action.busy || query.loading || Boolean(query.error)} className="operations-fields">
          <p>Vé {session.ticketCode} · Lượt #{session.id} · Khu #{session.zoneId}</p>
          {session.imageId ? <><button className="staff-button" type="button" onClick={() => action.run(async () => setEvidence(await operationsApi(lot.lotId, `sessions/${session.id}/evidence`)))}>Xem ảnh lúc vào</button>
            {evidence && <SafePhoto url={evidence.url} alt="Xe lúc vào bãi" />}</> : <Notice kind="warning">Lượt này chưa có ảnh. Đối chiếu trực tiếp xe và vé trước khi xác nhận.</Notice>}
          {!move && <><Field label="Biển số đã đối chiếu"><input required maxLength={12} placeholder="Ví dụ: 51F-123.45" pattern="[0-9]{2}[A-Za-z][ -]?[0-9]{3}[.]?[0-9]{2}" title="Nhập theo dạng biển số ô tô Việt Nam, ví dụ 51F-123.45" value={plate} onChange={e => setPlate(formatVietnameseCarPlateInput(e.target.value))} /></Field>
            <Field label="Nhiên liệu đã xác nhận"><select required value={fuel} onChange={e => setFuel(e.target.value)}><option value="">Chọn nhiên liệu</option><option value="PETROL">Xăng/dầu</option><option value="ELECTRIC">Điện</option><option value="HYBRID">Hybrid</option></select></Field></>}
          <Field label="Ô thực tế"><select required value={slotId} onChange={e => setSlot(e.target.value)}><option value="">Chọn ô đã đối chiếu</option>{query.data?.layout.slots.filter(s => s.physicalStatus === "EMPTY" && s.isActive && s.floorActive && s.zoneId === session.zoneId && (s.allowedFuelType === "ANY" || s.allowedFuelType === (move ? session.fuelType : fuel))).map(s => <option key={s.id} value={s.id}>{s.floorName} · {s.slotCode}</option>)}</select></Field>
          <Field label="Lý do nhập tay, sửa biển số hoặc chuyển ô"><textarea required={move || !session.temporaryPlateNumber} value={reason} maxLength={500} onChange={e => setReason(e.target.value)} /></Field>
          <label className="staff-check"><input type="checkbox" required checked={observed} onChange={e => setObserved(e.target.checked)} />Đã đối chiếu xe thực tế, vé và ô được chọn.</label>
          {move && <label className="staff-check"><input type="checkbox" required checked={oldEmpty} onChange={e => setOldEmpty(e.target.checked)} />Đã kiểm tra xe rời ô cũ, ô cũ trống.</label>}
          <button className="staff-button staff-button--primary">{action.busy ? "Đang lưu…" : move ? "Xác nhận chuyển ô" : "Xác minh và ghi nhận Có xe"}</button>
        </fieldset>
      </form>}
    </section>
  </div>
    {query.data?.sessions.some(s => !s.eventId) && <section aria-label="Lượt gửi trước nâng cấp">
      <h2>Xác minh lượt gửi trước nâng cấp</h2>
      <StaffVerification lot={lot} revision={revision} onChanged={onChanged} onBusyChange={onBusyChange} initialSessionId={initialSessionId} />
    </section>}
  </>;
}

