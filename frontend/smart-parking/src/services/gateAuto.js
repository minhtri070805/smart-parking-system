import { apiRequest } from "./api.js";
import { operationsApi } from "./operationsApi.js";
import { platesMatch, staffApi } from "./staffApi.js";

// Độ tin cậy tối thiểu của AI để hệ thống tự cho xe vào. Dưới mức này, Staff phải xem lại biển số.
export const AUTO_MIN_CONFIDENCE = 0.8;
// Thời gian chờ trước khi tự cho vào: Staff có thể hủy hoặc sửa biển số trong lúc đó.
export const AUTO_DELAY_SECONDS = 5;

export const isLowConfidence = (recognition) =>
  Boolean(recognition) && Number.isFinite(recognition.confidence) && recognition.confidence < AUTO_MIN_CONFIDENCE;

const activeZones = (zones, type) => (zones || []).filter((zone) => zone.type === type && zone.isActive);

// Chỉ tự chọn khu khi bãi có đúng một khu phù hợp; nhiều khu thì để Staff chọn.
export const singleZone = (zones, type) => {
  const list = activeZones(zones, type);
  return list.length === 1 ? list[0] : null;
};

// Mã vé điện tử do hệ thống cấp (3-40 ký tự chữ, số hoặc gạch nối). Core vẫn kiểm tra trùng.
export const newTicketCode = (now = Date.now(), random = Math.random) =>
  `VL-${now.toString(36)}${Math.floor(random() * 36 ** 3).toString(36).padStart(3, "0")}`.toUpperCase();

// Tìm booking còn chờ xe đến của biển số này. Booking trả trước mà chưa thanh toán thì không được cho vào.
export function findBooking(bookings, plate) {
  const matches = (bookings || []).filter((booking) => platesMatch(booking.plateNumber, plate));
  if (!matches.length) return { booking: null };
  const ready = matches.find((booking) => booking.paymentMode !== "PREPAY" || booking.paymentStatus === "PAID");
  return ready ? { booking: ready } : { booking: null, unpaid: matches[0] };
}

// Lưu ảnh của lần quét. Trả về { id, url } để xe vãng lai/vé tháng dùng mã ảnh còn xe đặt chỗ dùng địa chỉ ảnh.
export async function uploadImage(file) {
  if (!file) return null;
  const body = new FormData();
  body.append("file", file);
  const uploaded = await apiRequest("/v1/images", { method: "POST", body });
  return { id: uploaded.data.id, url: uploaded.data.url };
}

// Lỗi Core từ chối rõ ràng (4xx): chưa có lượt nào được tạo. Lỗi mạng/5xx/timeout là chưa rõ kết quả.
export const isClearRefusal = (error) =>
  error?.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429;

/**
 * Mỗi hàm admit* nhận một "kế hoạch" đã có sẵn eventId, nên gọi lại sau lỗi mạng không tạo thêm lượt xe.
 * Trả về kết quả chuẩn hóa để hiển thị trong danh sách "vừa xử lý".
 */
export async function admitPass({ lot, plate, zoneId, file, eventId, note }) {
  const image = await uploadImage(file);
  const result = await operationsApi(lot.lotId, "pass-entries", {
    eventId, zoneId: Number(zoneId), plateNumber: plate, imageId: image?.id ?? null, note: note || "Tự động theo biển số AI",
  });
  return { kind: "pass", sessionId: result.session.id, plate: result.session.plateNumber || plate, replay: Boolean(result.replay), detail: result.pass?.planName };
}

export async function admitWalkIn({ lot, plate, zoneId, file, eventId, ticketCode }) {
  const image = await uploadImage(file);
  const session = await operationsApi(lot.lotId, "entries", {
    eventId, zoneId: Number(zoneId), ticketCode, imageId: image?.id ?? null, manualReason: `Tự động: AI đọc biển số ${plate}`, plateNumber: plate,
  });
  return { kind: "walk-in", sessionId: session.id, plate, detail: session.ticketCode || ticketCode };
}

export async function admitBooking({ booking, plate, file }) {
  // Core cần ảnh biển số do chính Staff này lưu mới xác nhận xe đặt chỗ đã tới.
  const image = await uploadImage(file);
  if (!image?.url) throw new Error("Chưa lưu được ảnh biển số để xác nhận xe đặt chỗ.");
  const result = await staffApi.checkInBookingAtGate(booking.id, plate, image.url);
  const session = result?.data ?? result;
  return { kind: "booking", sessionId: session?.id, plate, detail: `Booking #${booking.id}` };
}
