const STORAGE_KEY = "smart-parking.booking-workflow.v1";
const HOLD_MINUTES = 30;

/** @typedef {"QR_CODE" | "VNPAY" | "CASH"} BookingPaymentMethod */
/** @typedef {"PENDING_ARRIVAL" | "CHECKED_IN_GATE" | "PARKED" | "CANCELLED"} BookingStatus */
/**
 * @typedef {object} ParkingBooking
 * @property {string} id
 * @property {string} customerName
 * @property {string} phoneNumber
 * @property {string} licensePlate
 * @property {"Ô tô" | "Xe máy"} vehicleType
 * @property {string} bookingTime
 * @property {string} expireTime
 * @property {BookingPaymentMethod} paymentMethod
 * @property {BookingStatus} status
 * @property {string} parkingSlot
 * @property {string} plateImageUrl
 * @property {string} vehicleNotes
 * @property {string} vehicleColor
 */

const listeners = new Set();
let snapshot = { bookings: [], persistenceError: "" };

function notify() {
  listeners.forEach((listener) => listener());
}

function readBookings() {
  if (typeof window === "undefined") return [];
  const saved = window.localStorage.getItem(STORAGE_KEY);
  if (!saved) return [];
  const parsed = JSON.parse(saved);
  if (!Array.isArray(parsed)) throw new Error("Dữ liệu đặt chỗ không hợp lệ.");
  return parsed;
}

try {
  snapshot = { bookings: readBookings(), persistenceError: "" };
} catch (error) {
  snapshot = {
    bookings: [],
    persistenceError: `Không đọc được dữ liệu demo đã lưu: ${error.message}`,
  };
}

function commit(bookings) {
  snapshot = { bookings, persistenceError: "" };
  try {
    if (typeof window !== "undefined")
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(bookings));
  } catch (error) {
    snapshot = {
      bookings,
      persistenceError: `Không thể lưu dữ liệu demo trên trình duyệt: ${error.message}`,
    };
  }
  notify();
}

function expireDueBookings(now = Date.now()) {
  let changed = false;
  const bookings = snapshot.bookings.map((booking) => {
    if (
      booking.status === "PENDING_ARRIVAL" &&
      new Date(booking.expireTime).getTime() <= now
    ) {
      changed = true;
      return { ...booking, status: "CANCELLED" };
    }
    return booking;
  });
  if (changed) commit(bookings);
}

function nextBookingId(bookings) {
  const highest = bookings.reduce((max, booking) => {
    const match = /^PK-(\d+)$/.exec(booking.id);
    return match ? Math.max(max, Number(match[1])) : max;
  }, 0);
  return `PK-${String(highest + 1).padStart(3, "0")}`;
}

export const bookingWorkflowStore = {
  getSnapshot: () => snapshot,
  subscribe(listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  createBooking(details, now = Date.now()) {
    const bookingTime = new Date(now);
    /** @type {ParkingBooking} */
    const booking = {
      id: nextBookingId(snapshot.bookings),
      customerName: details.customerName.trim(),
      phoneNumber: details.phoneNumber.trim(),
      licensePlate: details.licensePlate.trim().toUpperCase(),
      vehicleType: details.vehicleType,
      bookingTime: bookingTime.toISOString(),
      expireTime: new Date(now + HOLD_MINUTES * 60_000).toISOString(),
      paymentMethod: details.paymentMethod,
      status: "PENDING_ARRIVAL",
      parkingSlot: "",
      plateImageUrl: "",
      vehicleNotes: "",
      vehicleColor: "",
    };
    commit([booking, ...snapshot.bookings]);
    return booking;
  },
  checkInAtGate(id, now = Date.now()) {
    expireDueBookings(now);
    const booking = snapshot.bookings.find((item) => item.id === id);
    if (!booking || booking.status !== "PENDING_ARRIVAL") return false;
    commit(
      snapshot.bookings.map((item) =>
        item.id === id ? { ...item, status: "CHECKED_IN_GATE" } : item,
      ),
    );
    return true;
  },
  parkBooking(id, details) {
    const booking = snapshot.bookings.find((item) => item.id === id);
    if (!booking || booking.status !== "CHECKED_IN_GATE") return false;
    if (!details.parkingSlot.trim() || !details.plateImageUrl)
      throw new Error("Cần nhập vị trí ô đỗ và tải ảnh biển số.");
    commit(
      snapshot.bookings.map((item) =>
        item.id === id
          ? {
              ...item,
              ...details,
              customerName: details.customerName.trim(),
              phoneNumber: details.phoneNumber.trim(),
              parkingSlot: details.parkingSlot.trim(),
              status: "PARKED",
            }
          : item,
      ),
    );
    return true;
  },
  expireDueBookings,
};

if (typeof window !== "undefined") {
  window.addEventListener("storage", (event) => {
    if (event.key !== STORAGE_KEY && event.key !== null) return;
    try {
      snapshot = { bookings: readBookings(), persistenceError: "" };
    } catch (error) {
      snapshot = {
        bookings: snapshot.bookings,
        persistenceError: `Không đồng bộ được dữ liệu demo: ${error.message}`,
      };
    }
    notify();
  });
}
