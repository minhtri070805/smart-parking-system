import { apiRequest } from "./api.js";

export const passStatusLabels = {
  ACTIVE: "Đang hiệu lực",
  SCHEDULED: "Sắp hiệu lực",
  PENDING: "Chờ thanh toán",
  EXPIRED: "Đã hết hạn",
  SUSPENDED: "Tạm khóa",
  CANCELLED: "Đã hủy",
};

export const passStatusLabel = (status) => passStatusLabels[status] || status || "—";

// Trạng thái đơn thanh toán do Payment Service trả về.
const orderStates = {
  CREATING: { label: "Đang tạo link thanh toán…", tone: "wait" },
  PENDING: { label: "Chờ bạn thanh toán", tone: "wait" },
  PAID_PENDING_CORE_CONFIRMATION: { label: "Đã nhận thanh toán, đang kích hoạt vé…", tone: "wait" },
  CONFIRMED: { label: "Thanh toán thành công, vé đã được kích hoạt", tone: "ok" },
  FAILED: { label: "Thanh toán không thành công", tone: "bad" },
  CANCELLED: { label: "Bạn đã hủy thanh toán", tone: "bad" },
  LINK_CREATION_FAILED: { label: "Không tạo được link thanh toán", tone: "bad" },
};

export const orderState = (status) =>
  orderStates[status] || { label: status || "Không rõ trạng thái", tone: "wait" };
export const isOrderFinal = (status) => Boolean(orderStates[status]) && orderStates[status].tone !== "wait";

const errorMessages = {
  IDEMPOTENCY_KEY_REQUIRED: "Thiếu mã chống gửi trùng. Hãy tải lại trang và thử lại.",
  INVALID_BODY: "Thông tin gói hoặc xe không hợp lệ.",
  INVALID_USER_CLAIM: "Phiên đăng nhập không hợp lệ. Vui lòng đăng nhập lại.",
  PAYMENT_ATTEMPT_CONFLICT: "Đơn thanh toán này đã tồn tại với nội dung khác. Hãy chọn lại gói và xe.",
  PAYMENT_LINK_CREATION_FAILED: "Cổng thanh toán chưa tạo được link. Vui lòng thử lại sau ít phút.",
  PAYOS_NOT_CONFIGURED: "Thanh toán vé tháng chưa được cấu hình. Vui lòng liên hệ quản trị.",
  ACTIVE_PASS_CONFLICT: "Xe này đã có vé tháng còn hiệu lực hoặc đang chờ thanh toán tại bãi này.",
  PLAN_UNAVAILABLE: "Gói này hiện không còn được bán.",
  PLAN_NOT_PAYOS_COMPATIBLE: "Gói này chưa hỗ trợ thanh toán trực tuyến.",
  PLAN_CHANGE_NOT_SUPPORTED: "Chưa hỗ trợ đổi sang gói khác khi vé đang còn hiệu lực.",
  VEHICLE_NOT_OWNED_OR_INACTIVE: "Xe được chọn không thuộc tài khoản của bạn hoặc đã ngừng sử dụng.",
  SUBSCRIPTIONS_DISABLED: "Chức năng vé tháng đang tạm dừng.",
  INVALID_LOT: "Bãi của gói này không còn khả dụng.",
  CORE_CONFLICT: "Xe này đã có vé tháng còn hiệu lực hoặc đang chờ thanh toán tại bãi này.",
  PAYMENT_ALREADY_IN_PROGRESS: "Bạn đang có một đơn thanh toán chờ xử lý cho xe này.",
};

export const purchaseErrorMessage = (error) =>
  errorMessages[error?.code] || error?.message || "Không thể tạo thanh toán. Vui lòng thử lại.";

// Chỉ chuyển người dùng tới link https do Payment Service trả về.
export const safeCheckoutUrl = (value) => {
  try {
    const url = new URL(value);
    return url.protocol === "https:" ? url.toString() : null;
  } catch {
    return null;
  }
};

export const newIdempotencyKey = () =>
  globalThis.crypto?.randomUUID?.() ||
  `pass-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`;

export const formatMoney = (amount, currency = "VND") =>
  currency === "VND"
    ? `${new Intl.NumberFormat("vi-VN").format(Number(amount) || 0)} ₫`
    : `${amount} ${currency}`;

export const termLabel = (months) => (months === 1 ? "1 tháng" : `${months} tháng`);

export const dayLabel = (value) => (value ? new Date(value).toLocaleDateString("vi-VN") : "—");

export const daysLeft = (endsAt, now = Date.now()) => {
  const ms = new Date(endsAt).getTime() - now;
  return Number.isFinite(ms) ? Math.max(0, Math.ceil(ms / 86_400_000)) : null;
};

const listOf = (response) => {
  const data = response?.data;
  return Array.isArray(data) ? data : data?.items || [];
};

export const monthlyPassApi = {
  plans: (signal) => apiRequest("/v1/subscription-plans", { signal }).then(listOf),
  myPasses: (signal) => apiRequest("/v1/parking-passes/my", { signal }).then(listOf),
  vehicles: (signal) => apiRequest("/v1/vehicles?page=1&pageSize=100", { signal }).then(listOf),
  lots: (signal) =>
    apiRequest("/v1/parking-lots?pageNumber=1&pageSize=100&isActive=true", { signal }).then(listOf),
  // Số tiền, thời hạn và chủ sở hữu do server quyết định; client chỉ gửi gói và xe.
  checkout: (planId, vehicleId, idempotencyKey) =>
    apiRequest("/payments/payos/subscriptions", {
      method: "POST",
      headers: { "Idempotency-Key": idempotencyKey },
      body: JSON.stringify({ planId: Number(planId), vehicleId: Number(vehicleId) }),
    }),
  order: (orderCode, signal) => apiRequest(`/payments/payos/subscriptions/${orderCode}`, { signal }),
};
