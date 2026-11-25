import { useCallback, useEffect, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import RequireRole from "../components/RequireRole.jsx";
import ParkingLotLocationPicker from "../components/ParkingLotLocationPicker.jsx";
import { apiRequest } from "../services/api.js";
import { operationsApi } from "../services/operationsApi.js";
import { collectPages } from "../services/staffApi.js";
import { Notice, StaffError, StaffIcon, useStaffAction, useStaffQuery } from "../components/staff/StaffUI.jsx";
import "../staff.css";
import "../operations.css";

const Field = ({ label, children }) => <label className="staff-field">{label}{children}</label>;
const views = [
  { id: "floors", name: "Tầng", icon: "floors", description: "Quản lý tầng trong bãi. Tầng G mặc định dành cho xe vé tháng." },
  { id: "zones", name: "Khu vực", icon: "pin", description: "Quản lý khu vực thuộc từng tầng: khu thường hoặc khu vé tháng." },
  { id: "slots", name: "Ô đỗ", icon: "slots", description: "Thêm, sửa, xóa ô đỗ (slot), đổi trạng thái và cấu hình loại nhiên liệu." },
];
const ticketView = { id: "tickets", name: "Xử lý vé", icon: "tickets", description: "Tra cứu, thay vé mất hoặc thu hồi vé sau đối chiếu." };
const zoneTypes = { TRANSIENT: "Bình thường", FIXED: "Vé tháng" };
const slotStatuses = { EMPTY: "Trống", OCCUPIED: "Có xe", RESERVED: "Đang giữ chỗ", MAINTENANCE: "Bảo trì" };
const emptyZone = () => ({ code: "", name: "", type: "TRANSIENT", layout: "INSIDE", isActive: true, floorId: "" });
const emptySlotDraft = (defaultFloorId = "", zoneId = "") => ({
  floorId: defaultFloorId,
  zoneId,
  quantity: 1,
  slotCode: "",
  physicalStatus: "EMPTY",
  isActive: true,
});

function generateSlotCodes(slots, zone, quantity) {
  const zoneSlots = slots.filter(slot => String(slot.zoneId) === String(zone.id));
  const numberedCodes = zoneSlots.map(slot => {
    const match = String(slot.slotCode || "").match(/^(.*?)(\d+)$/);
    return match ? { prefix: match[1], sequence: Number(match[2]), width: match[2].length } : null;
  }).filter(Boolean);
  const family = numberedCodes.reduce((best, code) => !best || code.sequence > best.sequence ? code : best, null);
  const prefix = family?.prefix || `${String(zone.code || "ZONE").trim().toUpperCase()}-`;
  const width = family?.width || 3;
  const nextSequence = family
    ? Math.max(0, ...numberedCodes.filter(code => code.prefix === prefix).map(code => code.sequence)) + 1
    : 1;
  const usedCodes = new Set(slots.map(slot => String(slot.slotCode || "").toUpperCase()));
  const generated = [];
  let sequence = nextSequence;
  while (generated.length < quantity) {
    const candidate = `${prefix}${String(sequence).padStart(width, "0")}`;
    sequence += 1;
    if (usedCodes.has(candidate.toUpperCase())) continue;
    usedCodes.add(candidate.toUpperCase());
    generated.push(candidate);
  }
  return generated;
}

function LotSettings({ lot, view: selectedView, onBusyChange }) {
  const view = selectedView.id === "tickets" ? ticketView : selectedView;
  const load = useCallback(async signal => {
    const [zones, layout] = await Promise.all(["zones", "layout"].map(path => operationsApi(lot.id, path, null, null, signal)));
    return { zones, layout };
  }, [lot.id]);
  const query = useStaffQuery(load);
  const action = useStaffAction(onBusyChange);
  const [message, setMessage] = useState("");
  const [zone, setZone] = useState(emptyZone);
  const [editId, setEdit] = useState(null);
  const [ticket, setTicket] = useState({ code: "", action: "REPLACE", replacementCode: "", reason: "" });
  const [ticketResult, setTicketResult] = useState(null);
  const [floorDraft, setFloorDraft] = useState({
    name: "Tầng G",
    floorNumber: "0",
    zoneType: "FIXED",
    zoneCount: 2,
    slotsPerZone: 50,
  });
  const [editingFloor, setEditingFloor] = useState(null);
  const [createdFloor, setCreatedFloor] = useState(null);
  const [slotDraft, setSlotDraft] = useState(() => emptySlotDraft(""));
  const [editingSlot, setEditingSlot] = useState(null);
  const [slotFloorFilter, setSlotFloorFilter] = useState("");
  const [slotZoneFilter, setSlotZoneFilter] = useState("");
  const [zoneFloorFilter, setZoneFloorFilter] = useState("");


  const allFloors = query.data?.layout?.floors || [];
  const allSlots = query.data?.layout?.slots || [];

  const getNextFloorInfo = useCallback(() => {
    const hasGround = allFloors.some(f => f.floorNumber === 0 || f.name?.toLowerCase().includes("tầng g"));
    if (!hasGround) {
      return { name: "Tầng G", floorNumber: "0", defaultZoneType: "FIXED" };
    }
    const maxNum = allFloors.reduce((max, f) => Math.max(max, f.floorNumber ?? 0), 0);
    const nextNum = maxNum + 1;
    return { name: `Tầng ${nextNum}`, floorNumber: String(nextNum), defaultZoneType: "TRANSIENT" };
  }, [allFloors]);

  const handleNewFloor = useCallback(() => {
    setEditingFloor(null);
    const next = getNextFloorInfo();
    setFloorDraft(prev => ({
      ...prev,
      name: next.name,
      floorNumber: next.floorNumber,
      zoneType: next.defaultZoneType,
      zoneCount: prev.zoneCount || 2,
      slotsPerZone: prev.slotsPerZone || 50,
    }));
  }, [getNextFloorInfo]);

  useEffect(() => {
    if (!editingFloor) {
      const next = getNextFloorInfo();
      setFloorDraft(prev => ({
        ...prev,
        name: next.name,
        floorNumber: next.floorNumber,
        zoneType: next.defaultZoneType,
      }));
    }
  }, [allFloors.length, editingFloor, getNextFloorInfo]);

  useEffect(() => { setMessage(""); action.setError(null); }, [view.id, action.setError]);
  useEffect(() => {
    setZoneFloorFilter("");
    setEdit(null);
    setZone(emptyZone());
    setSlotFloorFilter("");
    setSlotZoneFilter("");
    setSlotDraft(emptySlotDraft(""));
    setEditingSlot(null);
  }, [lot.id]);
  const busy = action.busy || query.loading || Boolean(query.error);
  const perform = work => action.run(async () => { setMessage(""); const result = await work(); setMessage(typeof result === "string" ? result : "Đã lưu vào máy chủ."); query.reload(); });

  const visibleZones = (query.data?.zones || []).filter(z => String(z.floorId ?? z.floorIds?.[0] ?? "") === String(zoneFloorFilter));
  const visibleSlots = slotFloorFilter && slotZoneFilter
    ? allSlots.filter(s => String(s.floorId) === String(slotFloorFilter) && String(s.zoneId) === String(slotZoneFilter))
    : [];

  return <>
    <div className="operations-settings-toolbar">
      <p>{view.description}</p>
      <button type="button" className="staff-button staff-button--secondary" disabled={action.busy || query.loading} onClick={query.reload}>
        <StaffIcon name="refresh" />{query.loading ? "Đang tải…" : "Tải lại cấu hình"}
      </button>
    </div>
    <StaffError error={query.error || action.error} />
    {message && <Notice kind="success">{message}</Notice>}
    <div className="operations-settings-panels">
      {view.id === "zones" && <section className="staff-panel"><div><h2>Khu bãi</h2><p className="operations-settings-hint">Đóng khu để ngừng nhận xe mới. Các lượt xe đang gửi vẫn được giữ nguyên.</p></div>
        <div className="operations-settings-columns">
        <div><h3>Danh sách khu</h3>
          <Field label="Tầng"><select value={zoneFloorFilter} onChange={e => {
            const floorId = e.target.value;
            const floor = allFloors.find(item => String(item.id) === floorId);
            setZoneFloorFilter(floorId);
            setEdit(null);
            setZone({ ...emptyZone(), floorId, type: floor && (floor.floorNumber === 0 || floor.name?.toLocaleLowerCase().includes("tầng g")) ? "FIXED" : "TRANSIENT" });
          }}><option value="">Chọn tầng để xem khu vực</option>{allFloors.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}</select></Field>
          {zoneFloorFilter && !query.loading && !query.error && !visibleZones.length && <Notice>Chưa có khu vực thuộc tầng này. Có thể thêm khu ở khung bên phải.</Notice>}
          {!zoneFloorFilter && <Notice>Chọn tầng để xem các khu vực đang thuộc tầng đó.</Notice>}
          <div className="operations-session-list">{visibleZones.map(z => {
            const assignedFloorId = z.floorId ?? z.floorIds?.[0] ?? "";
            return <button type="button" className={`operations-session ${editId === z.id ? "is-selected" : ""}`} aria-pressed={editId === z.id} disabled={action.busy} key={z.id} onClick={() => { setZoneFloorFilter(String(assignedFloorId)); setEdit(z.id); setZone({ code: z.code, name: z.name, type: z.type, layout: z.layout || "INSIDE", isActive: z.isActive, floorId: String(assignedFloorId) }); }}>
            <strong>{z.code} · {z.name}</strong><span>{zoneTypes[z.type] || z.type} · {z.isActive ? "Đang khai thác" : "Đã đóng"}</span><small>{query.data.layout.floors.find(f => String(f.id) === String(assignedFloorId))?.name || "Chưa gắn tầng"} · Chọn để chỉnh sửa</small>
          </button>;
          })}</div>
        </div>
        <form onSubmit={e => { e.preventDefault(); perform(async () => {
          const selectedFloorId = Number(zoneFloorFilter);
          await operationsApi(lot.id, `zones${editId ? `/${editId}` : ""}`, {
            ...zone,
            floorId: selectedFloorId,
            // Keep the former API contract compatible while the local API process is restarted on s4/demo.
            floorIds: [selectedFloorId],
          }, editId ? "PUT" : "POST");
          setEdit(null); setZone({ ...emptyZone(), floorId: String(selectedFloorId) });
        }); }}><fieldset className="operations-fields" disabled={busy}>
          <h3>{editId ? `Sửa khu #${editId}` : "Thêm khu"}</h3>
          {zoneFloorFilter ? <>
          <div className="operations-form-grid">
            <Field label="Mã khu"><input required maxLength={30} value={zone.code} onChange={e => setZone({ ...zone, code: e.target.value })} /></Field>
            <Field label="Tên khu"><input required maxLength={100} value={zone.name} onChange={e => setZone({ ...zone, name: e.target.value })} /></Field>
          </div>
          <Field label="Loại khu"><select value={zone.type} onChange={e => setZone({ ...zone, type: e.target.value })}><option value="TRANSIENT" disabled={allFloors.find(f => String(f.id) === String(zone.floorId))?.floorNumber === 0 || allFloors.find(f => String(f.id) === String(zone.floorId))?.name?.toLocaleLowerCase().includes("tầng g")}>Bình thường</option><option value="FIXED">Vé tháng</option></select></Field>
          <label className="staff-check"><input type="checkbox" checked={zone.isActive} onChange={e => setZone({ ...zone, isActive: e.target.checked })} />Đang khai thác</label>
          <div className="operations-settings-actions"><button className="staff-button staff-button--primary">Lưu khu</button>
          {editId && <button type="button" className="staff-button staff-button--secondary" onClick={() => { setEdit(null); setZone({ ...emptyZone(), floorId: zoneFloorFilter }); }}>Chuyển sang thêm khu</button>}</div>
          </> : <Notice>Chọn tầng ở danh sách bên trái trước khi thêm khu vực.</Notice>}
        </fieldset></form>
        </div>
      </section>}

      {view.id === "floors" && <section className="staff-panel">
        <div>
          <h2>Quản lý tầng</h2>
          <p className="operations-settings-hint">
            Tầng G mặc định dành cho xe vé tháng, các tầng khác dành cho xe bình thường (vãng lai và booking).
          </p>
        </div>
        <div className="operations-settings-columns">
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
            <h3 style={{ margin: 0 }}>Danh sách tầng ({allFloors.length})</h3>
            <button type="button" className="staff-button staff-button--secondary" onClick={handleNewFloor}>+ Thêm tầng mới</button>
          </div>
          {!query.loading && !query.error && !allFloors.length && <Notice>Chưa có tầng trong bãi này.</Notice>}
          {allFloors.map(f => {
            const floorSlotCount = allSlots.filter(s => s.floorId === f.id).length;
            const floorZones = (query.data?.zones || []).filter(z => String(z.floorId ?? z.floorIds?.[0]) === String(f.id));
            const isSelected = editingFloor?.id === f.id;
            return <div key={f.id} className="operations-floor" style={{ display: "grid", gap: "8px", background: isSelected ? "#f0f9ff" : undefined, border: isSelected ? "1px solid #bae6fd" : undefined, borderRadius: "10px", padding: "12px", marginBottom: "8px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "6px" }}>
                <strong>{f.name} · Tầng số {f.floorNumber ?? "—"} ({floorZones.length} khu, {floorSlotCount} ô)</strong>
                <span style={{ fontSize: "12px", color: f.isActive ? "#15803d" : "#64748b", fontWeight: 600 }}>
                  {f.isActive ? "Đang hoạt động" : "Ngừng dùng"}
                </span>
              </div>
              <div style={{ fontSize: "11.5px", color: "#64748b" }}>
                {floorZones.map(z => `${z.name} (${zoneTypes[z.type] || z.type})`).join(" · ") || "Chưa có khu vực"}
              </div>
              <div className="operations-settings-actions" style={{ display: "flex", gap: "8px", flexWrap: "wrap", marginTop: "4px" }}>
                <button type="button" className="staff-button staff-button--secondary" disabled={busy} onClick={() => {
                  setEditingFloor(f);
                  const firstZone = floorZones[0];
                  const zCount = floorZones.length || 2;
                  const sCount = firstZone ? allSlots.filter(s => s.floorId === f.id && s.zoneId === firstZone.id).length || 50 : 50;
                  setFloorDraft({
                    name: f.name,
                    floorNumber: String(f.floorNumber ?? 0),
                    zoneType: firstZone?.type || (f.floorNumber === 0 ? "FIXED" : "TRANSIENT"),
                    zoneCount: zCount,
                    slotsPerZone: sCount,
                  });
                }}>Sửa tầng</button>
                <button type="button" className="staff-button" disabled={busy} onClick={() => perform(() => apiRequest(`/v1/parking-floors/${f.id}`, { method: "PUT", body: JSON.stringify({ floorNumber: f.floorNumber, name: f.name, totalSlots: floorSlotCount, isActive: !f.isActive }) }))}>{f.isActive ? "Ngừng dùng tầng" : "Mở lại tầng"}</button>
                <button type="button" className="staff-button staff-button--secondary" disabled={busy || floorSlotCount > 0} title={floorSlotCount > 0 ? "Chỉ xóa được tầng chưa có ô đỗ" : "Xóa tầng"} onClick={() => perform(() => apiRequest(`/v1/parking-floors/${f.id}`, { method: "DELETE" }))}>Xóa tầng</button>
              </div>
            </div>;
          })}
        </div>

        <form onSubmit={e => { e.preventDefault(); perform(async () => {
          const zoneCount = Math.max(1, Number(floorDraft.zoneCount) || 1);
          const slotsPerZone = Math.max(1, Number(floorDraft.slotsPerZone) || 1);
          const totalSlots = zoneCount * slotsPerZone;
          const isGround = Number(floorDraft.floorNumber) === 0;
          const prefix = isGround ? "G" : String(floorDraft.floorNumber);

          if (editingFloor) {
            await apiRequest(`/v1/parking-floors/${editingFloor.id}`, {
              method: "PUT",
              body: JSON.stringify({
                name: floorDraft.name.trim(),
                floorNumber: Number(floorDraft.floorNumber),
                totalSlots,
                isActive: editingFloor.isActive,
              }),
            });

            // Đồng bộ hoặc tạo thêm khu vực / ô đỗ nếu cần
            const existingFloorZones = (query.data?.zones || []).filter(z => String(z.floorId ?? z.floorIds?.[0]) === String(editingFloor.id));

            for (let z = existingFloorZones.length + 1; z <= zoneCount; z++) {
              const letter = String.fromCharCode(64 + z);
              const code = `KHU-${prefix}-${letter}`;
              const name = `Khu ${letter} - ${floorDraft.name.trim()}`;
              try {
                const newZone = await operationsApi(lot.id, "zones", {
                  code,
                  name,
                  type: floorDraft.zoneType,
                  layout: "INSIDE",
                  isActive: true,
                  floorId: editingFloor.id,
                }, "POST");

                for (let s = 1; s <= slotsPerZone; s++) {
                  const slotCode = `${prefix}-${letter}${String(s).padStart(3, "0")}`;
                  await apiRequest(`/v1/parking-floors/${editingFloor.id}/slots`, {
                    method: "POST",
                    body: JSON.stringify({
                      zoneId: newZone.id,
                      slotCode,
                      physicalStatus: "EMPTY",
                      isActive: true,
                    }),
                  });
                }
              } catch (err) {
                console.error("Error creating additional zone/slots:", err);
              }
            }

            // Bổ sung slot vào các khu hiện có nếu cần
            for (let z = 0; z < existingFloorZones.length; z++) {
              const ez = existingFloorZones[z];
              const letter = String.fromCharCode(65 + z);
              const currentSlotsInZone = allSlots.filter(s => s.floorId === editingFloor.id && s.zoneId === ez.id);
              if (currentSlotsInZone.length < slotsPerZone) {
                for (let s = currentSlotsInZone.length + 1; s <= slotsPerZone; s++) {
                  const slotCode = `${prefix}-${letter}${String(s).padStart(3, "0")}`;
                  try {
                    await apiRequest(`/v1/parking-floors/${editingFloor.id}/slots`, {
                      method: "POST",
                      body: JSON.stringify({
                        zoneId: ez.id,
                        slotCode,
                        physicalStatus: "EMPTY",
                        isActive: true,
                      }),
                    });
                  } catch {}
                }
              }
            }

            setEditingFloor(null);
            handleNewFloor();
            return `Đã cập nhật tầng "${floorDraft.name.trim()}" (${zoneCount} khu vực, ${totalSlots} ô đỗ).`;
          }

          // Tạo tầng mới
          const floorRes = await apiRequest(`/v1/parking-lots/${lot.id}/floors`, {
            method: "POST",
            body: JSON.stringify({
              name: floorDraft.name.trim(),
              floorNumber: Number(floorDraft.floorNumber),
              totalSlots,
              isActive: true,
            }),
          });
          const createdF = floorRes.data;

          for (let z = 1; z <= zoneCount; z++) {
            const letter = String.fromCharCode(64 + z);
            const code = `KHU-${prefix}-${letter}`;
            const name = `Khu ${letter} - ${floorDraft.name.trim()}`;
            const zone = await operationsApi(lot.id, "zones", {
              code,
              name,
              type: floorDraft.zoneType,
              layout: "INSIDE",
              isActive: true,
              floorId: createdF.id,
            }, "POST");

            for (let s = 1; s <= slotsPerZone; s++) {
              const slotCode = `${prefix}-${letter}${String(s).padStart(3, "0")}`;
              await apiRequest(`/v1/parking-floors/${createdF.id}/slots`, {
                method: "POST",
                body: JSON.stringify({
                  zoneId: zone.id,
                  slotCode,
                  physicalStatus: "EMPTY",
                  isActive: true,
                }),
              });
            }
          }

          handleNewFloor();
          return `Đã tạo tầng "${floorDraft.name.trim()}" cùng ${zoneCount} khu vực và ${totalSlots} ô đỗ.`;
        }); }}>
          <fieldset disabled={busy} className="operations-fields">
            <h3>{editingFloor ? `Sửa tầng: ${editingFloor.name}` : `Thêm ${floorDraft.name}`}</h3>

            <Field label="Loại tầng / Loại khu vực">
              <select
                value={floorDraft.zoneType}
                onChange={e => setFloorDraft({ ...floorDraft, zoneType: e.target.value })}
              >
                <option value="FIXED">Khu cho xe có vé tháng (mặc định là Tầng G)</option>
                <option value="TRANSIENT">Khu bình thường (vãng lai, booking)</option>
              </select>
            </Field>

            <div className="operations-form-grid">
              <Field label="Số khu vực trong tầng">
                <input
                  type="number"
                  min={1}
                  max={10}
                  required
                  value={floorDraft.zoneCount}
                  onChange={e => setFloorDraft({ ...floorDraft, zoneCount: Math.max(1, parseInt(e.target.value) || 1) })}
                />
              </Field>
              <Field label="Số slot trong 1 khu vực">
                <input
                  type="number"
                  min={1}
                  max={500}
                  required
                  value={floorDraft.slotsPerZone}
                  onChange={e => setFloorDraft({ ...floorDraft, slotsPerZone: Math.max(1, parseInt(e.target.value) || 1) })}
                />
              </Field>
            </div>

            <div className="operations-settings-actions">
              <button className="staff-button staff-button--primary">
                {editingFloor ? "Lưu cập nhật tầng" : `Thêm ${floorDraft.name}`}
              </button>
              {editingFloor && (
                <button type="button" className="staff-button staff-button--secondary" onClick={handleNewFloor}>
                  Hủy sửa
                </button>
              )}
            </div>
          </fieldset>
        </form>
        </div>
      </section>}

      {view.id === "slots" && <section className="staff-panel"><div><h2>Quản lý ô đỗ</h2><p className="operations-settings-hint">Thêm, sửa, xóa ô đỗ (slot), cập nhật trạng thái vật lý và cấu hình loại nhiên liệu phù hợp.</p></div>
        <div className="operations-settings-columns">
        <form onSubmit={e => { e.preventDefault(); perform(async () => {
          if (editingSlot) {
            const body = {
              slotCode: slotDraft.slotCode.trim(),
              zoneId: Number(slotDraft.zoneId),
              isActive: slotDraft.isActive !== false,
            };
            await apiRequest(`/v1/parking-slots/${editingSlot.id}`, {
              method: "PUT",
              body: JSON.stringify(body),
            });
            setEditingSlot(null);
            setSlotDraft(emptySlotDraft(slotDraft.floorId, slotDraft.zoneId));
          } else {
            const selectedZone = query.data?.zones.find(zone => String(zone.id) === String(slotDraft.zoneId));
            const quantity = Math.max(1, Math.min(500, Number(slotDraft.quantity) || 1));
            if (!selectedZone) throw new Error("Không tìm thấy khu vực đã chọn trong tầng này.");
            const codes = generateSlotCodes(allSlots, selectedZone, quantity);
            let createdCount = 0;
            try {
              for (const slotCode of codes) {
                await apiRequest(`/v1/parking-floors/${slotDraft.floorId}/slots`, {
                  method: "POST",
                  body: JSON.stringify({
                    slotCode,
                    zoneId: Number(slotDraft.zoneId),
                    isActive: slotDraft.isActive !== false,
                    physicalStatus: slotDraft.physicalStatus || "EMPTY",
                  }),
                });
                createdCount += 1;
              }
            } catch (error) {
              throw new Error(`Đã tạo ${createdCount}/${quantity} ô. Lỗi khi tạo mã ${codes[createdCount] || "tiếp theo"}: ${error.message}`);
            }
            setSlotDraft({ ...emptySlotDraft(slotDraft.floorId, slotDraft.zoneId), quantity: 1 });
            return `Đã tạo ${quantity} ô (${codes[0]}–${codes[codes.length - 1]}).`;
          }
        }); }}><fieldset disabled={busy} className="operations-fields"><h3>{editingSlot ? `Sửa ô ${editingSlot.slotCode}` : "Thêm ô"}</h3>
          {!editingSlot && (
            <>
            <Field label="Tầng"><select required value={slotDraft.floorId} onChange={e => {
              const floorId = e.target.value;
              setSlotDraft({ ...slotDraft, floorId, zoneId: "" });
              setSlotFloorFilter(floorId);
              setSlotZoneFilter("");
            }}><option value="">Chọn tầng</option>{allFloors.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}</select></Field>
            <Field label="Khu vực"><select required disabled={!slotDraft.floorId} value={slotDraft.zoneId} onChange={e => {
              setSlotDraft({ ...slotDraft, zoneId: e.target.value });
              setSlotFloorFilter(slotDraft.floorId);
              setSlotZoneFilter(e.target.value);
            }}><option value="">Chọn khu vực</option>{query.data?.zones.filter(z => String(z.floorId ?? z.floorIds?.[0] ?? "") === String(slotDraft.floorId)).map(z => <option key={z.id} value={z.id}>{z.code} · {z.name}</option>)}</select></Field>
            </>
          )}
          {editingSlot
            ? <Field label="Mã ô"><input required maxLength={20} value={slotDraft.slotCode} onChange={e => setSlotDraft({ ...slotDraft, slotCode: e.target.value })} /></Field>
            : <Field label="Số ô cần tạo"><input type="number" min="1" max="500" required value={slotDraft.quantity} onChange={e => setSlotDraft({ ...slotDraft, quantity: e.target.value })} /></Field>}
          {!editingSlot && (
            <Field label="Trạng thái ban đầu">
              <select value={slotDraft.physicalStatus || "EMPTY"} onChange={e => setSlotDraft({ ...slotDraft, physicalStatus: e.target.value })}>
                {Object.entries(slotStatuses).map(([val, label]) => <option key={val} value={val}>{label}</option>)}
              </select>
            </Field>
          )}
          <label className="staff-check"><input type="checkbox" checked={slotDraft.isActive !== false} onChange={e => setSlotDraft({ ...slotDraft, isActive: e.target.checked })} />Đang sử dụng</label>
          <div className="operations-settings-actions">
            <button className="staff-button staff-button--primary" disabled={!editingSlot && (!slotDraft.floorId || !slotDraft.zoneId)}>{editingSlot ? "Lưu ô đỗ" : "Tạo ô"}</button>
            {editingSlot && <button type="button" className="staff-button staff-button--secondary" onClick={() => { setEditingSlot(null); setSlotDraft(emptySlotDraft("")); }}>Hủy sửa</button>}
          </div>
        </fieldset></form>
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: "10px", flexWrap: "wrap", marginBottom: "10px" }}>
            <h3 style={{ margin: 0 }}>Danh sách ô đỗ ({visibleSlots.length})</h3>
            <select aria-label="Chọn tầng để xem ô đỗ" value={slotFloorFilter} onChange={e => { setSlotFloorFilter(e.target.value); setSlotZoneFilter(""); }} style={{ padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}>
              <option value="">Chọn tầng</option>
              {allFloors.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
            <select aria-label="Chọn khu vực để xem ô đỗ" value={slotZoneFilter} disabled={!slotFloorFilter} onChange={e => setSlotZoneFilter(e.target.value)} style={{ padding: "6px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}>
              <option value="">Chọn khu vực</option>{query.data?.zones.filter(z => String(z.floorId ?? z.floorIds?.[0] ?? "") === String(slotFloorFilter)).map(z => <option key={z.id} value={z.id}>{z.code} · {z.name}</option>)}
            </select>
          </div>
          {!slotFloorFilter || !slotZoneFilter
            ? <Notice>Chọn tầng, sau đó chọn khu vực để xem danh sách ô đỗ.</Notice>
            : !query.loading && !query.error && !visibleSlots.length && <Notice>Chưa có ô đỗ trong khu vực này.</Notice>}
          <div className="operations-slot-grid" style={{ display: "grid", gap: "10px", maxHeight: "480px", overflowY: "auto" }}>
            {visibleSlots.map(s => (
              <div key={s.id} style={{ padding: "10px 12px", borderRadius: "8px", border: "1px solid #e2e8f0", background: "#fff", display: "grid", gap: "8px" }}>
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <strong>{s.floorName} · {s.zoneName || "Chưa có khu"} · {s.slotCode}</strong>
                  <div style={{ display: "flex", gap: "6px" }}>
                    <button type="button" className="staff-button staff-button--secondary" disabled={busy} onClick={() => {
                      setEditingSlot(s);
                      setSlotDraft({
                        floorId: String(s.floorId || ""),
                        zoneId: String(s.zoneId || ""),
                        slotCode: s.slotCode,
                        physicalStatus: s.physicalStatus || "EMPTY",
                        positionX: s.positionX ?? "",
                        positionY: s.positionY ?? "",
                        positionZ: s.positionZ ?? "",
                        isActive: s.isActive !== false,
                      });
                    }}>Sửa</button>
                    <button type="button" className="staff-button staff-button--secondary" disabled={busy || s.physicalStatus === "OCCUPIED"} onClick={() => perform(() => apiRequest(`/v1/parking-slots/${s.id}`, { method: "DELETE" }))}>Xóa</button>
                  </div>
                </div>
                <div className="operations-form-grid">
                  <Field label="Trạng thái">
                    <select
                      disabled={busy || s.physicalStatus === "OCCUPIED"}
                      value={s.physicalStatus || "EMPTY"}
                      onChange={e => perform(() => apiRequest(`/v1/parking-slots/${s.id}/status`, {
                        method: "PATCH",
                        body: JSON.stringify({ physicalStatus: e.target.value, reason: "Cập nhật từ Quản lý bãi xe" }),
                      }))}
                    >
                      {Object.entries(slotStatuses).map(([val, label]) => <option key={val} value={val}>{label}</option>)}
                    </select>
                  </Field>
                  <Field label={`${s.floorName} · ${s.zoneName || "Khu vực"} · ${s.slotCode}`}>
                    <select disabled={busy} value={s.allowedFuelType || "ANY"} onChange={e => perform(() => operationsApi(lot.id, `slots/${s.id}/fuel`, { allowedFuelType: e.target.value }, "PUT"))}>
                      <option value="ANY">Mọi loại</option>
                      <option value="PETROL">Xăng/dầu</option>
                      <option value="ELECTRIC">Điện</option>
                      <option value="HYBRID">Hybrid</option>
                    </select>
                  </Field>
                </div>
              </div>
            ))}
          </div>
        </div></div>
      </section>}

      {view.id === "tickets" && <section className="staff-panel"><div><h2>Xử lý vé mất hoặc thu hồi</h2><p className="operations-settings-hint">Tra cứu vé trước khi thay hoặc thu hồi. Ghi rõ lý do và kết quả đối chiếu.</p></div>
        <div className="operations-settings-columns"><div>
        <form onSubmit={e => { e.preventDefault(); action.run(async () => setTicketResult(await operationsApi(lot.id, `tickets/${encodeURIComponent(ticket.code.trim())}`))); }}><Field label="Mã vé"><input required disabled={busy} value={ticket.code} onChange={e => { setTicket({ ...ticket, code: e.target.value }); setTicketResult(null); }} /></Field><button className="staff-button" disabled={busy}>Tra vé</button></form>
        {ticketResult && <p>Vé {ticketResult.ticketCode} · {ticketResult.ticketStatus} · Lượt #{ticketResult.id} · {ticketResult.plateNumber || "Chưa xác minh"}</p>}
        </div>
        <form onSubmit={e => { e.preventDefault(); perform(async () => { await operationsApi(lot.id, `tickets/${encodeURIComponent(ticket.code.trim())}/change`, ticket); setTicketResult(null); }); }}><fieldset className="operations-fields" disabled={busy || !ticketResult}>
          <Field label="Thao tác"><select value={ticket.action} onChange={e => setTicket({ ...ticket, action: e.target.value })}><option value="REPLACE">Thay vé sau đối chiếu</option><option value="REVOKE">Thu hồi vé</option></select></Field>
          {ticket.action === "REPLACE" && <Field label="Mã vé thay thế"><input required maxLength={40} value={ticket.replacementCode} onChange={e => setTicket({ ...ticket, replacementCode: e.target.value })} /></Field>}
          <Field label="Lý do và kết quả đối chiếu"><textarea required maxLength={500} value={ticket.reason} onChange={e => setTicket({ ...ticket, reason: e.target.value })} /></Field>
          <button className="staff-button staff-button--primary">Ghi nhận xử lý vé</button>
        </fieldset></form></div>
      </section>}
    </div>
  </>;
}

function Content() {
  const [params, setParams] = useSearchParams();
  const view = params.get("view") === "tickets" ? ticketView : (views.find(item => item.id === params.get("view")) || views[0]);
  const tabRefs = useRef({});
  const load = useCallback(signal => collectPages(page => apiRequest(`/v1/parking-lots?pageNumber=${page}&pageSize=100`, { signal }).then(r => r.data)), []);
  const lots = useStaffQuery(load);
  const [selected, select] = useState("");
  const [busy, setBusy] = useState(false);
  const [showLotModal, setShowLotModal] = useState(false);
  const [showDeleteLotModal, setShowDeleteLotModal] = useState(false);
  const [editingLot, setEditingLot] = useState(null);
  const [managers, setManagers] = useState([]);
  const [lotForm, setLotForm] = useState({
    name: "",
    address: "",
    managerId: "",
    floorCount: 1,
    zoneCount: 2,
    slotCount: 50,
    isActive: true,
  });
  const [lotModalBusy, setLotModalBusy] = useState(false);
  const [lotModalError, setLotModalError] = useState("");
  const [lotActionMessage, setLotActionMessage] = useState("");
  const [addressSuggestions, setAddressSuggestions] = useState([]);
  const [addressLookupBusy, setAddressLookupBusy] = useState(false);
  const addressLookupController = useRef(null);

  useEffect(() => {
    apiRequest("/v1/staff-assignments/candidates?role=MANAGER&page=1&pageSize=100")
      .then(r => setManagers(r?.data?.items || []))
      .catch(() => {});
  }, []);

  const lot = lots.data?.find(l => String(l.id) === selected) || lots.data?.[0];
  const openView = id => setParams(previous => { const next = new URLSearchParams(previous); next.set("view", id); return next; });

  const openCreateLotModal = () => {
    setEditingLot(null);
    setLotForm({
      name: "",
      address: "",
      latitude: null,
      longitude: null,
      managerId: "",
      floorCount: 1,
      zoneCount: 2,
      slotCount: 50,
      isActive: true,
    });
    setLotModalError("");
    setAddressSuggestions([]);
    setShowLotModal(true);
  };

  const openEditLotModal = async () => {
    if (!lot) return;
    setEditingLot(lot);
    let currentManagerId = "";
    let currentManagerAssignmentId = null;
    try {
      const assignmentsRes = await apiRequest(`/v1/staff-assignments?lotId=${lot.id}&page=1&pageSize=100`);
      const items = assignmentsRes?.data?.items || [];
      const managerAssignment = items.find(a => a.userRole === "MANAGER" || a.role === "MANAGER");
      if (managerAssignment) {
        currentManagerId = String(managerAssignment.userId);
        currentManagerAssignmentId = managerAssignment.id;
      }
    } catch {}
    setLotForm({
      name: lot.name || "",
      address: lot.address || "",
      latitude: lot.latitude ?? null,
      longitude: lot.longitude ?? null,
      managerId: currentManagerId,
      _originalManagerId: currentManagerId,
      _originalManagerAssignmentId: currentManagerAssignmentId,
      floorCount: 1,
      zoneCount: 2,
      slotCount: 50,
      isActive: lot.isActive !== false,
    });
    setLotModalError("");
    setAddressSuggestions([]);
    setShowLotModal(true);
  };

  const lookupNearbyAddresses = async ({ latitude, longitude }) => {
    addressLookupController.current?.abort();
    const controller = new AbortController();
    addressLookupController.current = controller;
    setLotModalError("");
    setLotForm(previous => ({ ...previous, latitude, longitude, address: "" }));
    setAddressSuggestions([]);
    setAddressLookupBusy(true);
    try {
      const query = new URLSearchParams({ lat: String(latitude), lon: String(longitude), limit: "8" });
      const response = await fetch(`https://photon.komoot.io/reverse?${query}`, { signal: controller.signal });
      if (!response.ok) throw new Error("Không lấy được gợi ý địa chỉ.");
      const data = await response.json();
      const suggestions = [...new Set((data.features || []).map(feature => {
        const p = feature.properties || {};
        const street = [p.housenumber, p.street || (p.type === "street" ? p.name : "")].filter(Boolean).join(" ");
        const place = p.name && p.name !== p.street && p.name !== street ? p.name : "";
        return [place, street || p.name, p.district, p.city || p.locality, p.state, p.country].filter(Boolean).join(", ");
      }).filter(Boolean))].slice(0, 8);
      setAddressSuggestions(suggestions);
      if (suggestions.length) setLotForm(previous => ({ ...previous, address: suggestions[0] }));
      else setLotModalError("Chưa tìm thấy địa chỉ gần điểm này. Bạn có thể tự nhập địa chỉ bên dưới.");
    } catch (error) {
      if (error.name !== "AbortError") setLotModalError("Không tải được gợi ý địa chỉ. Bạn vẫn có thể tự nhập địa chỉ.");
    } finally {
      if (addressLookupController.current === controller) {
        setAddressLookupBusy(false);
        addressLookupController.current = null;
      }
    }
  };

  const handleSaveLot = async (e) => {
    e.preventDefault();
    setLotModalBusy(true);
    setLotModalError("");
    setLotActionMessage("");
    try {
      if (editingLot) {
        await apiRequest(`/v1/parking-lots/${editingLot.id}`, {
          method: "PUT",
          body: JSON.stringify({
            name: lotForm.name.trim(),
            address: lotForm.address.trim() || null,
            totalCapacity: editingLot.totalCapacity || 100,
            isActive: Boolean(lotForm.isActive),
          }),
        });

        if (lotForm.managerId && String(lotForm.managerId) !== String(lotForm._originalManagerId)) {
          if (lotForm._originalManagerAssignmentId) {
            await apiRequest(`/v1/staff-assignments/${lotForm._originalManagerAssignmentId}/revoke`, {
              method: "POST",
              body: JSON.stringify({ reason: "Thay Manager phụ trách bãi" }),
            });
          }
          try {
            await apiRequest("/v1/staff-assignments", {
              method: "POST",
              body: JSON.stringify({
                userId: Number(lotForm.managerId),
                lotId: editingLot.id,
                duty: "GATE",
                notes: "Manager phụ trách bãi",
              }),
            });
          } catch (mErr) {
            console.warn("Could not update manager assignment:", mErr);
          }
        }

        setShowLotModal(false);
        await lots.reload();
        setLotActionMessage(`Đã cập nhật bãi đỗ "${lotForm.name.trim()}".`);
      } else {
        if (lotForm.latitude == null || lotForm.longitude == null || !Number.isFinite(Number(lotForm.latitude)) || !Number.isFinite(Number(lotForm.longitude))) {
          throw new Error("Hãy chọn vị trí bãi trên bản đồ trước khi tạo.");
        }
        if (!lotForm.address.trim()) throw new Error("Hãy chọn hoặc nhập địa chỉ bãi đỗ.");
        const floorCount = Math.max(1, Number(lotForm.floorCount) || 1);
        const zoneCount = Math.max(1, Number(lotForm.zoneCount) || 1);
        const slotCount = Math.max(1, Number(lotForm.slotCount) || 1);
        const totalCapacity = floorCount * zoneCount * slotCount;

        const res = await apiRequest("/v1/parking-lots", {
          method: "POST",
          body: JSON.stringify({
            name: lotForm.name.trim(),
            address: lotForm.address.trim(),
            latitude: Number(lotForm.latitude),
            longitude: Number(lotForm.longitude),
            totalCapacity,
            isActive: Boolean(lotForm.isActive),
          }),
        });

        if (res?.data?.id) {
          const newLotId = res.data.id;

          // Tự động tạo cấu trúc: Số tầng, Số khu vực, Số slot
          try {
            for (let f = 0; f < floorCount; f++) {
              const isGround = f === 0;
              const fName = isGround ? "Tầng G" : `Tầng ${f}`;
              const fNumber = f;
              const fZoneType = isGround ? "FIXED" : "TRANSIENT";

              const floorRes = await apiRequest(`/v1/parking-lots/${newLotId}/floors`, {
                method: "POST",
                body: JSON.stringify({
                  name: fName,
                  floorNumber: fNumber,
                  totalSlots: zoneCount * slotCount,
                  isActive: true,
                }),
              });
              const createdFloor = floorRes.data;

              for (let z = 1; z <= zoneCount; z++) {
                const zoneLetter = String.fromCharCode(64 + z);
                const prefix = isGround ? "G" : String(f);
                const zoneCode = `KHU-${prefix}-${zoneLetter}`;
                const zoneName = `Khu ${zoneLetter} - ${fName}`;

                const zone = await operationsApi(newLotId, "zones", {
                  code: zoneCode,
                  name: zoneName,
                  type: fZoneType,
                  layout: "INSIDE",
                  isActive: true,
                  floorId: createdFloor.id,
                }, "POST");

                for (let s = 1; s <= slotCount; s++) {
                  const slotCode = `${prefix}-${zoneLetter}${String(s).padStart(3, "0")}`;
                  await apiRequest(`/v1/parking-floors/${createdFloor.id}/slots`, {
                    method: "POST",
                    body: JSON.stringify({
                      zoneId: zone.id,
                      slotCode,
                      physicalStatus: "EMPTY",
                      isActive: true,
                    }),
                  });
                }
              }
            }
          } catch (createErr) {
            console.error("Auto provision error:", createErr);
          }

          setShowLotModal(false);
          await lots.reload();
          select(String(newLotId));
          const managerInfo = res.data.managerAccount;
          setLotActionMessage(`Đã tạo bãi "${lotForm.name.trim()}" và Manager phụ trách. Tài khoản: ${managerInfo?.username || "đã tạo"} · Email: ${managerInfo?.email || "—"} · Mật khẩu: ${managerInfo?.initialPassword || "Test123@"}. Cấu hình: ${floorCount} tầng, ${floorCount * zoneCount} khu vực, ${totalCapacity} slot.`);
        }
      }
    } catch (err) {
      setLotModalError(err.message || "Không thể lưu thông tin bãi đỗ.");
    } finally {
      setLotModalBusy(false);
    }
  };

  const handleDeleteLot = () => {
    if (!lot) return;
    setLotModalError("");
    setLotActionMessage("");
    setShowDeleteLotModal(true);
  };

  const confirmDeleteLot = async () => {
    if (!lot) return;
    setLotModalBusy(true);
    try {
      await apiRequest(`/v1/parking-lots/${lot.id}`, { method: "DELETE" });
      setShowDeleteLotModal(false);
      setSelectedAfterDelete();
      await lots.reload();
      setLotActionMessage(`Đã xóa bãi đỗ "${lot.name}".`);
    } catch (err) {
      setLotModalError(err.message || "Không thể xóa bãi. Hãy kiểm tra lịch sử phiên xe, đặt chỗ, điều phối hoặc phân công nhân viên.");
    } finally {
      setLotModalBusy(false);
    }
  };

  useEffect(() => {
    if (!showDeleteLotModal || lotModalBusy) return undefined;
    const closeOnEscape = event => {
      if (event.key === "Escape") setShowDeleteLotModal(false);
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [showDeleteLotModal, lotModalBusy]);

  const setSelectedAfterDelete = () => {
    const remaining = (lots.data || []).filter(l => l.id !== lot?.id);
    select(remaining[0] ? String(remaining[0].id) : "");
  };

  return <div className="staff-workspace operations-settings container">
    <StaffError error={lots.error || (!showLotModal && !showDeleteLotModal ? lotModalError : "")} />{lots.error && <button className="staff-button" onClick={lots.reload}>Tải lại bãi</button>}
    {lotActionMessage && <Notice kind="success">{lotActionMessage}</Notice>}
    {lots.loading && <Notice>Đang tải bãi…</Notice>}
    {!lots.loading && !lots.error && !lots.data?.length && (
      <div className="workspace-panel workspace-empty" style={{ textAlign: "center", padding: "32px 16px" }}>
        <h2>Chưa có bãi đỗ</h2>
        <p style={{ margin: "12px 0 20px" }}>Hệ thống chưa có bãi đỗ nào. Hãy tạo bãi đỗ đầu tiên để bắt đầu quản lý tầng, khu vực và slot.</p>
        <button type="button" className="btn btn--primary" onClick={openCreateLotModal}>+ Thêm bãi đỗ mới</button>
      </div>
    )}
    {lot && <>
      <section className="staff-context-bar" aria-label="Bãi đang cấu hình"><div className="staff-context-select" style={{ flexWrap: "wrap", gap: "10px" }}><StaffIcon name="pin" size={23} />
        <label htmlFor="settings-lot">Bãi cần cấu hình<select id="settings-lot" disabled={busy || lotModalBusy} value={lot.id} onChange={e => select(e.target.value)}>{lots.data.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", marginLeft: "8px" }}>
          <button type="button" className="btn btn--primary" style={{ whiteSpace: "nowrap" }} disabled={busy || lotModalBusy} onClick={openCreateLotModal}>+ Thêm bãi đỗ</button>
          <button type="button" className="btn btn--outline" style={{ whiteSpace: "nowrap", background: "#fff" }} disabled={busy || lotModalBusy} onClick={openEditLotModal}>✎ Sửa bãi đỗ</button>
          <button type="button" className="btn btn--outline" style={{ whiteSpace: "nowrap", background: "#fff", color: "#b91c1c" }} disabled={busy || lotModalBusy} onClick={handleDeleteLot}>🗑 Xóa bãi</button>
        </div>
      </div></section>
      <div className="staff-tabs" role="tablist" aria-label="Quản lý bãi xe">
        {views.map((item, index) => <button key={item.id} type="button" role="tab" id={`settings-tab-${item.id}`} aria-controls={`settings-panel-${item.id}`} aria-selected={view.id === item.id} tabIndex={view.id === item.id ? 0 : -1} disabled={busy}
          ref={node => { tabRefs.current[item.id] = node; }} onClick={() => openView(item.id)} onKeyDown={event => {
            const nextIndex = event.key === "ArrowRight" ? (index + 1) % views.length : event.key === "ArrowLeft" ? (index + views.length - 1) % views.length : event.key === "Home" ? 0 : event.key === "End" ? views.length - 1 : null;
            if (nextIndex === null || busy) return;
            event.preventDefault(); openView(views[nextIndex].id); tabRefs.current[views[nextIndex].id]?.focus();
          }}><StaffIcon name={item.icon} /><span>{item.name}</span></button>)}
      </div>
      <section className="operations-settings-view" role="tabpanel" id={`settings-panel-${view.id}`} aria-label={view.name} aria-labelledby={view.id !== "tickets" ? `settings-tab-${view.id}` : undefined} tabIndex={0}>
        <LotSettings lot={lot} view={view} key={lot.id} onBusyChange={setBusy} />
      </section>
    </>}

    {showLotModal && (
      <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="modal-title-lot" style={{ position: "fixed", inset: 0, background: "rgba(16,36,59,0.6)", zIndex: 100, display: "flex", alignItems: "center", justifyContent: "center", padding: "16px" }}>
        <div className="workspace-panel" style={{ width: "100%", maxWidth: editingLot ? "640px" : "920px", maxHeight: "92vh", overflowY: "auto", background: "#fff", borderRadius: "14px", boxShadow: "0 20px 50px rgba(0,0,0,0.3)" }}>
          <h2 id="modal-title-lot" style={{ marginTop: 0 }}>{editingLot ? `Sửa bãi đỗ: ${editingLot.name}` : "Thêm bãi đỗ xe mới"}</h2>
          <p className="operations-settings-hint" style={{ margin: "4px 0 16px" }}>{editingLot ? "Cập nhật thông tin tòa nhà / bãi đỗ xe." : "Khai báo tòa nhà / bãi đỗ xe mới trong hệ thống."}</p>
          {lotModalError && <p className="feedback feedback--error" role="alert" style={{ color: "#d14343", margin: "8px 0" }}>{lotModalError}</p>}
          <form onSubmit={handleSaveLot}>
            <fieldset disabled={lotModalBusy} style={{ border: 0, padding: 0, margin: 0, display: "grid", gap: "18px" }}>
              <section style={{ border: "1px solid #d8e2ef", borderRadius: 12, padding: 16, display: "grid", gap: 12 }}>
                <div><h3 style={{ margin: 0 }}>1. Thông tin bãi</h3><p className="operations-settings-hint" style={{ margin: "4px 0 0" }}>Đặt tên và chọn người quản lý bãi.</p></div>
                <div className="operations-form-grid" style={{ gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))" }}>
                  <Field label="Tên bãi đỗ xe *">
                    <input required minLength={2} maxLength={100} placeholder="Ví dụ: Bãi xe FPT University HCMC" value={lotForm.name} onChange={e => setLotForm({ ...lotForm, name: e.target.value })} />
                  </Field>
                  {editingLot ? <Field label="Manager phụ trách">
                    <select required value={lotForm.managerId} onChange={e => setLotForm({ ...lotForm, managerId: e.target.value })}>
                      <option value="">Chọn manager</option>
                      {managers.map(m => <option key={m.id} value={m.id}>{m.fullName || m.username} ({m.username})</option>)}
                    </select>
                  </Field> : <div style={{ alignSelf: "end", padding: "12px 14px", borderRadius: 10, background: "#f2f6fc", color: "#203d5d" }}>Hệ thống sẽ tự tạo tài khoản Manager riêng cho bãi với mật khẩu ban đầu <strong>Test123@</strong>.</div>}
                </div>
              </section>

              {!editingLot && <section style={{ border: "1px solid #d8e2ef", borderRadius: 12, padding: 16, display: "grid", gap: 12 }}>
                <div><h3 style={{ margin: 0 }}>2. Chọn vị trí trên bản đồ</h3><p className="operations-settings-hint" style={{ margin: "4px 0 0" }}>Bấm vào vị trí bãi xe. Hệ thống sẽ tìm tối đa 8 địa chỉ lân cận để bạn chọn; địa chỉ gợi ý dùng dữ liệu OpenStreetMap.</p></div>
                <ParkingLotLocationPicker value={lotForm.latitude != null && lotForm.longitude != null ? { latitude: lotForm.latitude, longitude: lotForm.longitude } : null} onChange={lookupNearbyAddresses} />
                <Field label="Địa chỉ bãi *">
                  <input required maxLength={255} placeholder="Chọn điểm trên bản đồ hoặc nhập địa chỉ" value={lotForm.address} onChange={e => setLotForm({ ...lotForm, address: e.target.value })} />
                </Field>
                {addressLookupBusy && <p role="status" style={{ margin: 0, color: "#52657c" }}>Đang tìm địa chỉ gần điểm đã chọn…</p>}
                {addressSuggestions.length > 0 && <div aria-label="Địa chỉ gần điểm đã chọn" style={{ display: "grid", gap: 6 }}>
                  <strong style={{ fontSize: 13 }}>Địa chỉ gần vị trí (bấm để điền):</strong>
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 6 }}>
                    {addressSuggestions.map((address, index) => <button key={`${address}-${index}`} type="button" className="btn btn--outline" aria-pressed={lotForm.address === address} onClick={() => setLotForm(previous => ({ ...previous, address }))} style={{ textAlign: "left", justifyContent: "flex-start", whiteSpace: "normal" }}>{address}</button>)}
                  </div>
                </div>}
                {lotForm.latitude != null && lotForm.longitude != null && <small style={{ color: "#52657c" }}>Tọa độ: {Number(lotForm.latitude).toFixed(6)}, {Number(lotForm.longitude).toFixed(6)}</small>}
              </section>}

              {!editingLot && (
                <>
                  <section style={{ border: "1px solid #d8e2ef", borderRadius: 12, padding: 16, display: "grid", gap: 12 }}>
                  <div><h3 style={{ margin: 0 }}>3. Quy mô bãi</h3><p className="operations-settings-hint" style={{ margin: "4px 0 0" }}>Nhập số tầng, khu vực mỗi tầng và ô đỗ mỗi khu vực.</p></div>
                  <div className="operations-form-grid" style={{ gridTemplateColumns: "repeat(3, minmax(0, 1fr))" }}>
                    <Field label="Số tầng">
                      <input
                        type="number"
                        min={1}
                        max={20}
                        required
                        value={lotForm.floorCount}
                        onChange={e => setLotForm({ ...lotForm, floorCount: Math.max(1, parseInt(e.target.value) || 1) })}
                      />
                    </Field>
                    <Field label="Số khu vực (mỗi tầng)">
                      <input
                        type="number"
                        min={1}
                        max={10}
                        required
                        value={lotForm.zoneCount}
                        onChange={e => setLotForm({ ...lotForm, zoneCount: Math.max(1, parseInt(e.target.value) || 1) })}
                      />
                    </Field>
                    <Field label="Số slot (mỗi khu vực)">
                      <input
                        type="number"
                        min={1}
                        max={500}
                        required
                        value={lotForm.slotCount}
                        onChange={e => setLotForm({ ...lotForm, slotCount: Math.max(1, parseInt(e.target.value) || 1) })}
                      />
                    </Field>
                  </div>
                  </section>
                </>
              )}

              <label className="staff-check">
                <input type="checkbox" checked={lotForm.isActive} onChange={e => setLotForm({ ...lotForm, isActive: e.target.checked })} />
                Bãi đang hoạt động, cho phép tiếp nhận xe
              </label>

              <div style={{ display: "flex", gap: "10px", justifyContent: "flex-end", marginTop: "14px" }}>
                <button type="button" className="btn btn--outline" disabled={lotModalBusy} onClick={() => { setShowLotModal(false); setLotModalError(""); }}>Hủy</button>
                <button type="submit" className="btn btn--primary" disabled={lotModalBusy || addressLookupBusy || !lotForm.name.trim() || (!editingLot && (lotForm.latitude == null || lotForm.longitude == null || !lotForm.address.trim()))}>
                  {lotModalBusy ? "Đang tạo bãi…" : editingLot ? "Lưu cập nhật" : "Tạo bãi và sơ đồ chỗ đỗ"}
                </button>
              </div>
            </fieldset>
          </form>
        </div>
      </div>
    )}
    {showDeleteLotModal && lot && (
      <div
        className="modal-backdrop"
        role="dialog"
        aria-modal="true"
        aria-labelledby="delete-lot-title"
        aria-describedby="delete-lot-description"
        onMouseDown={event => { if (event.target === event.currentTarget && !lotModalBusy) setShowDeleteLotModal(false); }}
        style={{ position: "fixed", inset: 0, background: "rgba(16,36,59,0.6)", zIndex: 110, display: "flex", alignItems: "center", justifyContent: "center", padding: "16px" }}
      >
        <section className="workspace-panel" style={{ width: "100%", maxWidth: "500px", background: "#fff", borderRadius: "14px", boxShadow: "0 20px 50px rgba(0,0,0,0.3)" }}>
          <h2 id="delete-lot-title" style={{ marginTop: 0 }}>Xóa bãi đỗ?</h2>
          <p id="delete-lot-description" style={{ marginBottom: "14px" }}>
            Bạn sắp xóa bãi <strong>{lot.name}</strong> cùng toàn bộ tầng, khu vực và ô đỗ.
          </p>
          <div style={{ padding: "12px 14px", borderRadius: "10px", background: "#fff7ed", color: "#9a3412", border: "1px solid #fed7aa", lineHeight: 1.55 }}>
            Bãi có lịch sử phiên xe, đặt chỗ, ô đang có xe/giữ chỗ, điều phối hoặc phân công nhân viên sẽ được giữ lại để bảo toàn dữ liệu.
          </div>
          {lotModalError && <p className="feedback feedback--error" role="alert" style={{ color: "#b91c1c", margin: "12px 0 0" }}>{lotModalError}</p>}
          <div style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "20px" }}>
            <button type="button" className="btn btn--outline" autoFocus disabled={lotModalBusy} onClick={() => setShowDeleteLotModal(false)}>Hủy</button>
            <button type="button" className="btn" disabled={lotModalBusy} onClick={confirmDeleteLot} style={{ background: "#dc2626", borderColor: "#dc2626", color: "#fff" }}>
              {lotModalBusy ? "Đang xóa…" : "Xóa bãi"}
            </button>
          </div>
        </section>
      </div>
    )}
  </div>;
}
export default function AdminParkingSettings() { return <RequireRole role="ADMIN"><Content /></RequireRole>; }
