import { getAuthSession } from "./authSession.js";
import { apiRequest } from "./api.js";

const baseUrl = import.meta.env.VITE_OPERATIONS_API_BASE_URL || "";

export async function operationsRequest(path, options = {}) {
  const token = getAuthSession()?.accessToken;
  const url = `${baseUrl}/api/v1/operations${path}`;
  const response = await fetch(url, {
    ...options,
    headers: {
      ...(options.body ? { "Content-Type": "application/json" } : {}),
      ...options.headers,
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok)
    throw Object.assign(new Error(body.message || "Không thể thực hiện thao tác handover."), {
      status: response.status,
    });
  return body;
}

export function operationsApiBaseConfigured() {
  return true;
}

// Preserve the Core-backed Staff Operations adapter already used for tickets.
export const operationsApi = async (lotId, path, body, method, signal) => {
  const response = await apiRequest(`/v1/parking-lots/${lotId}/operations/${path}`, {
    method: method || (body ? "POST" : "GET"),
    ...(body ? { body: JSON.stringify(body) } : {}),
    signal,
  });
  return response.data;
};
