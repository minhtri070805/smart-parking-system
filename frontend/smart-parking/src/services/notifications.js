import { apiRequest } from "./api.js";

const itemsOf = (value) => Array.isArray(value) ? value : value?.items || [];
const statusText = {
  PENDING: "Chờ xe đến",
  CHECKED_IN: "Đã qua cổng",
  PARKED: "Đang đỗ bãi",
  ACTIVE: "Đang hoạt động",
  COMPLETED: "Đã hoàn tất",
  CANCELLED: "Đã hủy",
  EXPIRED: "Đã hết hạn",
};

export async function loadNotifications(role, signal) {
  if (!role) return [];
  if (role === "ADMIN") {
    const response = await apiRequest("/v1/admin/audit-logs?page=1&pageSize=8", { signal });
    return itemsOf(response?.data).map((item) => ({
      id: `audit-${item.id}`,
      title: `Nhật ký ${item.action}`,
      body: `${item.actorName || item.actorUsername || "Hệ thống"} · ${item.targetEntity}${item.targetId ? ` #${item.targetId}` : ""}`,
      href: "/admin/audit-logs",
      createdAt: item.createdAt,
    }));
  }

  if (role === "USER") {
    const response = await apiRequest("/v1/bookings/my?pageNumber=1&pageSize=8", { signal });
    return itemsOf(response?.data).map((item) => ({
      id: `booking-${item.id}`,
      title: `Vé #${item.id} · ${statusText[item.status] || item.status || "Cập nhật"}`,
      body: item.lotName || item.parkingLotName || "Đặt chỗ của bạn vừa được cập nhật.",
      href: "/customer/my-tickets",
      createdAt: item.updatedAt || item.createdAt,
    }));
  }

  const lotsResponse = await apiRequest("/v1/staff-assignments/my-lots", { signal });
  const lots = itemsOf(lotsResponse?.data || lotsResponse);
  const notices = lots.slice(0, 4).map((lot) => ({
    id: `lot-${lot.lotId || lot.id}`,
    title: role === "MANAGER" ? "Bãi đang phụ trách" : "Bãi được phân công",
    body: `${lot.lotName || lot.name || "Bãi xe"}${lot.duty ? ` · ${lot.duty}` : ""}`,
    href: role === "MANAGER" ? "/manager" : "/staff?view=entry",
  }));

  if (role === "MANAGER" && lots[0]?.lotId) {
    const response = await apiRequest(`/v1/parking-sessions/active?lotId=${lots[0].lotId}&page=1&pageSize=5`, { signal });
    const active = itemsOf(response?.data);
    if (active.length) notices.unshift({
      id: "manager-active-sessions",
      title: `${active.length} phiên đang hoạt động`,
      body: "Có xe đang gửi tại bãi bạn phụ trách.",
      href: "/manager?view=sessions",
    });
  }
  return notices;
}
