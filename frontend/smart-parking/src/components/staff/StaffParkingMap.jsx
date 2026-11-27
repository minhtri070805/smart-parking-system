import { slotIsSelectable } from "../../services/staffApi.js";

const ROWS = ["A", "B", "C", "D"];

const physicalLabels = {
  EMPTY: "Cảm biến: trống",
  OCCUPIED: "Cảm biến: có xe",
  MAINTENANCE: "Bảo trì",
  UNAVAILABLE: "Không khả dụng",
};

function slotRows(slots) {
  const orderedSlots = [...slots].sort((a, b) => {
    const codeA = String(a.slotCode || a.code || "");
    const codeB = String(b.slotCode || b.code || "");
    return codeA.localeCompare(codeB, "en") || Number(a.id) - Number(b.id);
  });
  const base = Math.floor(slots.length / ROWS.length);
  const remainder = slots.length % ROWS.length;
  let offset = 0;

  return ROWS.map((row, index) => {
    const count = base + (index < remainder ? 1 : 0);
    const rowSlots = orderedSlots.slice(offset, offset + count);
    offset += count;
    return { row, slots: rowSlots };
  }).filter((row) => row.slots.length > 0);
}

export default function StaffParkingMap({
  floors,
  slots,
  sessions,
  session,
  mode,
  selectedSlotId,
  disabled,
  onSelect,
  canSelectSlot,
}) {
  const visibleFloors = floors.filter(
    (floor) =>
      slots.some((slot) => String(slot.floorId) === String(floor.id)),
  );

  return (
    <div className="staff-parking-map" aria-label="Sơ đồ 2D bãi đỗ xe">
      {visibleFloors.map((floor) => {
        const floorSlots = slots.filter(
          (slot) => String(slot.floorId) === String(floor.id),
        );
        const rows = slotRows(floorSlots);

        return (
          <section
            className="staff-map-floor"
            key={floor.id}
            aria-label={`Sơ đồ 2D tầng ${floor.name}`}
          >
            <header className="staff-map-floor-heading">
              <strong>{floor.name}</strong>
              <span>{floorSlots.length} ô · lối lưu thông ở giữa các dãy</span>
            </header>
            <div className="staff-map-entry" aria-hidden="true">
              <span>LỐI VÀO / RA</span>
              <span className="staff-map-entry-arrow">↓</span>
            </div>
            <div className="staff-map-banks">
              {rows.map(({ row, slots: rowSlots }, index) => (
                <div className="staff-map-bank" key={row}>
                  <div className="staff-map-row">
                    <span className="staff-map-row-label">Dãy {row}</span>
                    <div className="staff-map-row-slots">
                      {rowSlots.map((slot) => {
                        const selectable = canSelectSlot
                          ? canSelectSlot(slot)
                          : slotIsSelectable(slot, session, sessions, mode);
                        const linkedSession = sessions.find(
                          (item) =>
                            String(item.currentSlotId) === String(slot.id),
                        );
                        const displayStatus =
                          slot.isActive === false ||
                          slot.floorActive === false ||
                          slot.zoneActive === false
                            ? "UNAVAILABLE"
                            : linkedSession
                              ? "OCCUPIED"
                              : slot.status ||
                                (slot.isAvailable === false &&
                                slot.physicalStatus === "EMPTY"
                                  ? "RESERVED"
                                  : slot.physicalStatus) ||
                                "UNAVAILABLE";
                        const status = String(
                          displayStatus,
                        ).toLowerCase();
                        const isSelected =
                          String(selectedSlotId) === String(slot.id);
                        const plate =
                          linkedSession?.plateNumber ||
                          linkedSession?.temporaryPlateNumber;

                        return (
                          <button
                            type="button"
                            key={slot.id}
                            className={`staff-map-slot staff-map-slot--${status}${isSelected ? " is-selected" : ""}`}
                            disabled={disabled || !selectable}
                            aria-pressed={isSelected}
                            aria-label={`${slot.slotCode || slot.code}, ${floor.name}, ${plate ? `biển số ${plate}, ` : ""}${physicalLabels[displayStatus] || "Không khả dụng"}${selectable ? "" : ", không thể chọn"}`}
                            onClick={() => onSelect(slot)}
                          >
                            <strong>{slot.slotCode || slot.code}</strong>
                            {plate && <span>{plate}</span>}
                            <small>
                              {physicalLabels[displayStatus] ||
                                "Không khả dụng"}
                            </small>
                          </button>
                        );
                      })}
                    </div>
                  </div>
                  {index < rows.length - 1 && (
                    <div className="staff-map-aisle" aria-hidden="true">
                      <span>LÀN LƯU THÔNG</span>
                      <span>↕</span>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </section>
        );
      })}
      {!visibleFloors.length && (
        <p className="staff-empty-small">Tầng này chưa có ô hoạt động.</p>
      )}
    </div>
  );
}
