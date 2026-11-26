import { getAuthSession } from "./authSession.js";
import { apiRequest } from "./api.js";

const baseUrl = import.meta.env.VITE_OPERATIONS_API_BASE_URL || "";

export async function operationsRequest(path, options = {}) {
  const token = getAuthSession()?.accessToken;
  const url = `${baseUrl}/api/v1/operations${path}`;
  const isFormData = typeof FormData !== "undefined" && options.body instanceof FormData;
  const response = await fetch(url, {
    ...options,
    headers: {
      ...(options.body && !isFormData ? { "Content-Type": "application/json" } : {}),
      ...options.headers,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    let friendlyMessage = body.message;
    if (body.code === "RECOGNITION_SERVICE_UNAVAILABLE" || response.status === 502 || response.status === 503) {
      friendlyMessage = "Dịch vụ AI đang khởi động hoặc tạm thời gián đoạn. Vui lòng nhập biển số tay.";
    } else if (body.code === "GATEWAY_TIMEOUT" || response.status === 504) {
      friendlyMessage = "Nhận diện biển số quá thời gian chờ (timeout). Vui lòng thử lại hoặc nhập tay.";
    } else if (body.code === "CONFLICT" || response.status === 409) {
      friendlyMessage = "Thao tác nhận diện đang được xử lý đồng thời. Vui lòng không nhấn liên tục.";
    }

    throw Object.assign(new Error(friendlyMessage || "Không thể thực hiện thao tác vận hành."), {
      status: response.status,
      code: body.code,
      data: body,
    });
  }
  return body;
}

export function operationsApiBaseConfigured() {
  return true;
}

// Operations Recognition APIs
export const operationsRecognitionApi = {
  detect: async (file, lotId, purpose = "ENTRY", imageId = null) => {
    const body = new FormData();
    body.append("file", file);
    const queryParams = new URLSearchParams({
      lotId: String(lotId),
      purpose,
      ...(imageId ? { imageId } : {}),
    });
    return operationsRequest(`/recognition/detect?${queryParams.toString()}`, {
      method: "POST",
      body,
    });
  },

  confirm: (id, payload) =>
    operationsRequest(`/recognition/${id}/confirm`, {
      method: "POST",
      body: payload,
    }),

  confirmExit: (id, payload) =>
    operationsRequest(`/recognition/${id}/confirm-exit`, {
      method: "POST",
      body: payload,
    }),
};

// Preserve the Core-backed Staff Operations adapter already used for tickets.
export const operationsApi = async (lotId, path, body, method, signal) => {
  const response = await apiRequest(`/v1/parking-lots/${lotId}/operations/${path}`, {
    method: method || (body ? "POST" : "GET"),
    ...(body ? { body: JSON.stringify(body) } : {}),
    signal,
  });
  return response.data;
};
