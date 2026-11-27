const kinds = {
  pass: { label: "Vé tháng", next: null },
  "walk-in": { label: "Vãng lai", next: { view: "verify", text: "Đối chiếu tại ô" } },
  booking: { label: "Đặt chỗ", next: { view: "booking-dispatch", text: "Phân ô" } },
};

const time = (value) => value.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit", second: "2-digit" });

/** Các xe hệ thống vừa tự cho vào ở màn này, để Staff xem lại và sửa nếu AI đọc sai. */
export default function GateActivity({ items, onOpen }) {
  if (!items.length) return null;
  return (
    <section className="staff-panel" aria-label="Xe vừa được tự động cho vào">
      <h2>Vừa cho vào</h2>
      <p>AI có thể đọc sai biển số. Xe vãng lai và xe đặt chỗ được đối chiếu lại ở bước tiếp theo, sửa biển số ở đó.</p>
      <ul className="operations-session-list">
        {items.map((item) => {
          const kind = kinds[item.kind] || { label: item.kind, next: null };
          return (
            <li key={item.id} className="operations-session">
              <strong>{item.plate}</strong>
              <span>
                {kind.label}
                {item.detail ? ` · ${item.detail}` : ""}
                {item.sessionId ? ` · lượt #${item.sessionId}` : ""}
              </span>
              <small>{time(item.at)}</small>
              {kind.next && onOpen && (
                <button
                  type="button"
                  className="staff-text-button"
                  onClick={() => onOpen(kind.next.view, item.kind === "walk-in" ? item.sessionId : undefined)}
                >
                  {kind.next.text}
                </button>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
