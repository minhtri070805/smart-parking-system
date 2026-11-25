import { useEffect, useMemo, useState, useSyncExternalStore } from "react";
import { bookingWorkflowStore } from "../services/bookingWorkflowStore.js";
import { formatVietnameseCarPlateInput, validateVietnameseCarPlate } from "../services/plateNumber.js";
import "../booking-workflow.css";

const roles = [
  { id: "customer", label: "Khách hàng", description: "Đăng ký giữ chỗ" },
  { id: "gate", label: "Staff Cổng", description: "Xác nhận xe đến" },
  { id: "coordinator", label: "Staff Điều phối", description: "Ghi nhận xe vào bãi" },
];

const paymentLabels = {
  QR_CODE: "QR Code ngân hàng",
  VNPAY: "Ví VNPay",
  CASH: "Tiền mặt",
};

function formatRemaining(expireTime, now) {
  const seconds = Math.max(
    0,
    Math.ceil((new Date(expireTime).getTime() - now) / 1000),
  );
  return `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
}

function Countdown({ expireTime, now }) {
  const remaining = formatRemaining(expireTime, now);
  return (
    <span className={`booking-demo__countdown ${remaining === "00:00" ? "is-expired" : ""}`}>
      {remaining}
    </span>
  );
}

function CustomerBooking({ bookings, now }) {
  const [createdBookingId, setCreatedBookingId] = useState("");
  const [message, setMessage] = useState("");
  const createdBooking = bookings.find((booking) => booking.id === createdBookingId);

  function submitBooking(event) {
    event.preventDefault();
    const formData = new FormData(event.currentTarget);
    try {
      const booking = bookingWorkflowStore.createBooking({
        customerName: formData.get("customerName"),
        phoneNumber: formData.get("phoneNumber"),
        licensePlate: validateVietnameseCarPlate(formData.get("licensePlate")),
        vehicleType: formData.get("vehicleType"),
        paymentMethod: formData.get("paymentMethod"),
      });
      setCreatedBookingId(booking.id);
      setMessage("");
      event.currentTarget.reset();
    } catch (error) {
      setMessage(error.message);
    }
  }

  return (
    <div className="booking-demo__layout">
      <section className="booking-demo__panel">
        <div className="booking-demo__section-heading">
          <span className="booking-demo__eyebrow">Đặt chỗ trực tuyến</span>
          <h2>Thông tin chuyến xe</h2>
          <p>Điền thông tin để tạo lượt đặt chỗ và chuyển tiếp qua các bước vận hành.</p>
        </div>
        <div className="booking-demo__hold-notice" role="note">
          <span aria-hidden="true">◷</span>
          <p>Chỗ của bạn sẽ được giữ trong vòng 30 phút kể từ lúc xác nhận.</p>
        </div>
        <form className="booking-demo__form" onSubmit={submitBooking}>
          <label>
            Họ và tên
            <input name="customerName" autoComplete="name" required maxLength={100} placeholder="Nguyễn Văn An" />
          </label>
          <label>
            Số điện thoại
            <input name="phoneNumber" type="tel" autoComplete="tel" required maxLength={20} pattern="[0-9+(). -]{8,20}" placeholder="090 123 4567" />
          </label>
          <label>
            Biển số xe
            <input name="licensePlate" required maxLength={12} pattern="[0-9]{2}[A-Za-z][ -]?[0-9]{3}[.]?[0-9]{2}" title="Nhập theo dạng biển số ô tô Việt Nam, ví dụ 51F-123.45" placeholder="51F-123.45" onChange={event => { event.target.value = formatVietnameseCarPlateInput(event.target.value); }} />
          </label>
          <label>
            Loại xe
            <select name="vehicleType" defaultValue="Ô tô">
              <option>Ô tô</option>
              <option>Xe máy</option>
            </select>
          </label>
          <fieldset className="booking-demo__payments">
            <legend>Phương thức thanh toán</legend>
            <label><input type="radio" name="paymentMethod" value="QR_CODE" defaultChecked /> <span>QR Code ngân hàng</span></label>
            <label><input type="radio" name="paymentMethod" value="VNPAY" /> <span>Ví VNPay</span></label>
            <label><input type="radio" name="paymentMethod" value="CASH" /> <span>Tiền mặt</span></label>
          </fieldset>
          {message && <p className="booking-demo__error" role="alert">{message}</p>}
          <button className="booking-demo__button booking-demo__button--primary" type="submit">Xác nhận đặt chỗ</button>
        </form>
      </section>

      <section className="booking-demo__panel booking-demo__reservation" aria-live="polite">
        <div className="booking-demo__section-heading">
          <span className="booking-demo__eyebrow">Theo dõi giữ chỗ</span>
          <h2>Lượt đặt gần nhất</h2>
          <p>Đồng hồ bắt đầu tính ngay khi xác nhận thành công.</p>
        </div>
        {createdBooking ? (
          <div className="booking-demo__created">
            <span className="booking-demo__success-mark" aria-hidden="true">✓</span>
            <span className="booking-demo__eyebrow">Đặt chỗ thành công</span>
            <strong className="booking-demo__booking-id">{createdBooking.id}</strong>
            <span className={`booking-demo__status booking-demo__status--${createdBooking.status.toLowerCase()}`}>
              {createdBooking.status === "CANCELLED" ? "Đã hết hạn" : "Chờ xe đến"}
            </span>
            <div className="booking-demo__countdown-card">
              <span>{createdBooking.status === "CANCELLED" ? "Thời gian giữ chỗ đã hết" : "Thời gian giữ chỗ còn lại"}</span>
              {createdBooking.status === "PENDING_ARRIVAL"
                ? <Countdown expireTime={createdBooking.expireTime} now={now} />
                : <strong>{createdBooking.status === "CANCELLED" ? "00:00" : "Đã xác nhận tại cổng"}</strong>}
            </div>
            <dl className="booking-demo__detail-list">
              <div><dt>Biển số xe</dt><dd>{createdBooking.licensePlate}</dd></div>
              <div><dt>Loại xe</dt><dd>{createdBooking.vehicleType}</dd></div>
              <div><dt>Thanh toán</dt><dd>{paymentLabels[createdBooking.paymentMethod]}</dd></div>
            </dl>
            <p className="booking-demo__hint">Đưa mã đặt chỗ cho nhân viên tại cổng khi xe đến.</p>
          </div>
        ) : (
          <div className="booking-demo__empty">
            <span aria-hidden="true">▤</span>
            <strong>Chưa có lượt đặt chỗ</strong>
            <p>Đơn vừa tạo và đồng hồ giữ chỗ sẽ hiển thị tại đây.</p>
          </div>
        )}
      </section>
    </div>
  );
}

function GateStaffDashboard({ bookings, now }) {
  const [query, setQuery] = useState("");
  const [notice, setNotice] = useState("");
  const pendingBookings = bookings.filter((booking) => booking.status === "PENDING_ARRIVAL");
  const filteredBookings = pendingBookings.filter((booking) =>
    `${booking.id} ${booking.licensePlate}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()),
  );

  function checkIn(booking) {
    const checkedIn = bookingWorkflowStore.checkInAtGate(booking.id, now);
    setNotice(checkedIn
      ? `Đã xác nhận ${booking.licensePlate} và chuyển ${booking.id} sang hàng chờ Điều phối.`
      : `Không thể check-in ${booking.id}: đơn đã hết hạn hoặc không còn chờ xe đến.`);
  }

  return (
    <section className="booking-demo__panel">
      <div className="booking-demo__dashboard-heading">
        <div className="booking-demo__section-heading">
          <span className="booking-demo__eyebrow">Điểm tiếp nhận</span>
          <h2>Xe chờ xác nhận tại cổng</h2>
          <p>Đối chiếu biển số và mã đặt chỗ trước khi chuyển cho Staff Điều phối.</p>
        </div>
        <span className="booking-demo__queue-count"><strong>{pendingBookings.length}</strong> đang chờ</span>
      </div>
      <label className="booking-demo__search">
        Tìm theo biển số hoặc mã đặt chỗ
        <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Ví dụ: PK-001 hoặc 51A-123.45" />
      </label>
      {notice && <p className="booking-demo__notice" role="status">{notice}</p>}
      {filteredBookings.length ? (
        <div className="booking-demo__queue">
          {filteredBookings.map((booking) => (
            <article className="booking-demo__vehicle-card" key={booking.id}>
              <div className="booking-demo__vehicle-main">
                <span className="booking-demo__plate">{booking.licensePlate}</span>
                <span className="booking-demo__vehicle-subtitle">{booking.id} <i /> {booking.customerName} <i /> {booking.vehicleType}</span>
              </div>
              <div className="booking-demo__vehicle-meta">
                <span>Giữ chỗ còn</span>
                <Countdown expireTime={booking.expireTime} now={now} />
              </div>
              <button className="booking-demo__button booking-demo__button--primary" onClick={() => checkIn(booking)}>
                Xác nhận xe đến &amp; Chuyển Điều phối
              </button>
            </article>
          ))}
        </div>
      ) : (
        <div className="booking-demo__empty booking-demo__empty--wide">
          <span aria-hidden="true">✓</span>
          <strong>{pendingBookings.length ? "Không tìm thấy lượt phù hợp" : "Không có xe chờ check-in"}</strong>
          <p>{pendingBookings.length ? "Thử tìm bằng mã đặt chỗ hoặc biển số khác." : "Đơn mới tạo từ màn hình Khách hàng sẽ xuất hiện tại đây."}</p>
        </div>
      )}
    </section>
  );
}

function readImage(file) {
  return new Promise((resolve, reject) => {
    if (file.size > 5 * 1024 * 1024) {
      reject(new Error("Ảnh cần nhỏ hơn 5 MB để lưu trong dữ liệu demo."));
      return;
    }
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error("Không đọc được ảnh. Vui lòng chọn lại tệp."));
    reader.readAsDataURL(file);
  });
}

function CoordinatorCard({ booking, onParked }) {
  const [imageData, setImageData] = useState("");
  const [imageName, setImageName] = useState("");
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  async function selectImage(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    setError("");
    try {
      const data = await readImage(file);
      setImageData(data);
      setImageName(file.name);
    } catch (readError) {
      setImageData("");
      setImageName("");
      setError(readError.message);
      event.target.value = "";
    }
  }

  async function saveBooking(event) {
    event.preventDefault();
    setSaving(true);
    setError("");
    const formData = new FormData(event.currentTarget);
    try {
      bookingWorkflowStore.parkBooking(booking.id, {
        customerName: formData.get("customerName"),
        phoneNumber: formData.get("phoneNumber"),
        vehicleColor: formData.get("vehicleColor"),
        parkingSlot: formData.get("parkingSlot"),
        vehicleNotes: formData.get("vehicleNotes").trim(),
        plateImageUrl: imageData,
      });
      onParked(booking);
    } catch (saveError) {
      setError(saveError.message);
    } finally {
      setSaving(false);
    }
  }

  return (
    <article className="booking-demo__coordinator-card">
      <div className="booking-demo__coordinator-summary">
        <div><span className="booking-demo__plate">{booking.licensePlate}</span><span className="booking-demo__vehicle-subtitle">{booking.id} · {booking.vehicleType}</span></div>
        <span className="booking-demo__status booking-demo__status--checked_in_gate">Đã qua cổng</span>
      </div>
      <form className="booking-demo__form" onSubmit={saveBooking}>
        <div className="booking-demo__form-grid">
          <label>
            Họ tên khách hàng
            <input name="customerName" defaultValue={booking.customerName} required maxLength={100} />
          </label>
          <label>
            Số điện thoại
            <input name="phoneNumber" defaultValue={booking.phoneNumber} type="tel" required maxLength={20} />
          </label>
          <label>
            Màu xe
            <input name="vehicleColor" required maxLength={40} placeholder="Ví dụ: Trắng" />
          </label>
          <label>
            Vị trí ô đỗ
            <input name="parkingSlot" required maxLength={60} placeholder="Khu A - Ô 12" />
          </label>
        </div>
        <label className="booking-demo__upload">
          Ảnh biển số xe <span>(bắt buộc, tối đa 5 MB)</span>
          <input type="file" accept="image/*" capture="environment" required onChange={selectImage} />
        </label>
        {imageData && (
          <div className="booking-demo__image-preview">
            <img src={imageData} alt={`Ảnh biển số xe ${booking.licensePlate}`} />
            <span>{imageName}</span>
          </div>
        )}
        <label>
          Ghi chú tình trạng xe / khách hàng
          <textarea name="vehicleNotes" rows="3" maxLength={500} placeholder="Ví dụ: Trầy nhẹ cửa bên phải" />
        </label>
        {error && <p className="booking-demo__error" role="alert">{error}</p>}
        <button className="booking-demo__button booking-demo__button--primary" type="submit" disabled={saving}>
          {saving ? "Đang lưu…" : "Lưu thông tin & Xác nhận đã đỗ bãi"}
        </button>
      </form>
    </article>
  );
}

function CoordinatorDashboard({ bookings }) {
  const [tab, setTab] = useState("queue");
  const [notice, setNotice] = useState("");
  const checkedIn = bookings.filter((booking) => booking.status === "CHECKED_IN_GATE");
  const parked = bookings.filter((booking) => booking.status === "PARKED");

  function markParked(booking) {
    setNotice(`Đã lưu ${booking.licensePlate} tại bãi. Hồ sơ ${booking.id} chuyển sang Đang đỗ.`);
  }

  return (
    <section className="booking-demo__panel">
      <div className="booking-demo__dashboard-heading">
        <div className="booking-demo__section-heading">
          <span className="booking-demo__eyebrow">Khu vực điều phối</span>
          <h2>Tiếp nhận xe vào bãi</h2>
          <p>Chụp ảnh biển số, xác nhận thông tin thực tế và ghi nhận vị trí đỗ xe.</p>
        </div>
        <span className="booking-demo__queue-count"><strong>{checkedIn.length}</strong> chờ xử lý</span>
      </div>
      <div className="booking-demo__tabs" role="tablist" aria-label="Danh sách xe điều phối">
        <button role="tab" aria-selected={tab === "queue"} onClick={() => setTab("queue")}>Hàng chờ <span>{checkedIn.length}</span></button>
        <button role="tab" aria-selected={tab === "parked"} onClick={() => setTab("parked")}>Xe đang đỗ <span>{parked.length}</span></button>
      </div>
      {notice && <p className="booking-demo__notice" role="status">{notice}</p>}
      {tab === "queue" ? (
        checkedIn.length ? (
          <div className="booking-demo__coordinator-list">
            {checkedIn.map((booking) => (
              <CoordinatorCard key={booking.id} booking={booking} onParked={markParked} />
            ))}
          </div>
        ) : (
          <div className="booking-demo__empty booking-demo__empty--wide">
            <span aria-hidden="true">↗</span>
            <strong>Hàng chờ đang trống</strong>
            <p>Xe được Staff Cổng xác nhận sẽ tự động chuyển đến đây.</p>
          </div>
        )
      ) : parked.length ? (
        <div className="booking-demo__parked-list">
          {parked.map((booking) => (
            <article className="booking-demo__parked-card" key={booking.id}>
              <img src={booking.plateImageUrl} alt={`Biển số ${booking.licensePlate}`} />
              <div className="booking-demo__parked-info">
                <span className="booking-demo__plate">{booking.licensePlate}</span>
                <span>{booking.id} · {booking.customerName} · {booking.vehicleType}</span>
                <span>{booking.vehicleColor || "Chưa ghi màu"} · {booking.parkingSlot}</span>
                {booking.vehicleNotes && <p>{booking.vehicleNotes}</p>}
              </div>
              <span className="booking-demo__status booking-demo__status--parked">Đang đỗ</span>
            </article>
          ))}
        </div>
      ) : (
        <div className="booking-demo__empty booking-demo__empty--wide">
          <span aria-hidden="true">▣</span>
          <strong>Chưa có xe trong bãi</strong>
          <p>Hồ sơ hoàn tất sẽ được hiển thị ở tab này cùng ảnh biển số và vị trí ô đỗ.</p>
        </div>
      )}
    </section>
  );
}

export default function BookingWorkflowDemo() {
  const [role, setRole] = useState("customer");
  const snapshot = useSyncExternalStore(
    bookingWorkflowStore.subscribe,
    bookingWorkflowStore.getSnapshot,
    bookingWorkflowStore.getSnapshot,
  );
  const [now, setNow] = useState(Date.now);
  const bookings = snapshot.bookings;
  const activeRole = useMemo(() => roles.find((item) => item.id === role), [role]);

  useEffect(() => {
    const timer = window.setInterval(() => {
      const currentTime = Date.now();
      setNow(currentTime);
      bookingWorkflowStore.expireDueBookings(currentTime);
    }, 1000);
    return () => window.clearInterval(timer);
  }, []);

  return (
    <section className="booking-demo" aria-labelledby="booking-demo-title">
      <div className="booking-demo__container">
        <header className="booking-demo__hero">
          <div>
            <span className="booking-demo__eyebrow">Smart-Parking · Quy trình vận hành</span>
            <h1 id="booking-demo-title">Đặt chỗ &amp; Vận hành xe vào bãi</h1>
            <p>Một luồng xuyên suốt từ khách đặt chỗ đến khi xe được ghi nhận tại ô đỗ.</p>
          </div>
          <div className="booking-demo__hero-stats">
            <span><strong>{bookings.filter((booking) => booking.status === "PENDING_ARRIVAL").length}</strong>Chờ đến</span>
            <span><strong>{bookings.filter((booking) => booking.status === "CHECKED_IN_GATE").length}</strong>Chờ điều phối</span>
            <span><strong>{bookings.filter((booking) => booking.status === "PARKED").length}</strong>Đang đỗ</span>
          </div>
        </header>

        {snapshot.persistenceError && <p className="booking-demo__error booking-demo__storage-error" role="alert">{snapshot.persistenceError}</p>}

        <nav className="booking-demo__role-nav" aria-label="Chuyển đổi vai trò demo">
          {roles.map((item, index) => (
            <button key={item.id} className={role === item.id ? "is-active" : ""} aria-current={role === item.id ? "page" : undefined} onClick={() => setRole(item.id)}>
              <span className="booking-demo__role-number">0{index + 1}</span>
              <span><strong>{item.label}</strong><small>{item.description}</small></span>
              <span className="booking-demo__role-arrow" aria-hidden="true">→</span>
            </button>
          ))}
        </nav>

        <div className="booking-demo__active-heading">
          <span className="booking-demo__active-dot" />
          <span>Giao diện hiện tại: <strong>{activeRole.label}</strong></span>
          <span className="booking-demo__sync-label">Mock data đồng bộ giữa các vai trò</span>
        </div>

        {role === "customer" && <CustomerBooking bookings={bookings} now={now} />}
        {role === "gate" && <GateStaffDashboard bookings={bookings} now={now} />}
        {role === "coordinator" && <CoordinatorDashboard bookings={bookings} />}
      </div>
    </section>
  );
}
