function snapshotVersionOf(snapshot) {
  const explicit = Number(snapshot?.snapshotVersion);
  if (Number.isFinite(explicit) && explicit >= 0) return explicit;
  const generated = Date.parse(snapshot?.generatedAtUtc || "");
  return Number.isFinite(generated) ? generated : 0;
}

export function sceneRequestKey(lotId, identity) {
  return `${identity || "guest"}:${Number(lotId) || 0}`;
}

export const initialSceneSnapshotState = {
  key: "",
  activeRequestId: 0,
  data: null,
  error: "",
  loading: false,
  stale: false,
};

export function sceneSnapshotReducer(state, action) {
  switch (action.type) {
    case "reset":
      return { ...initialSceneSnapshotState };
    case "begin": {
      const sameSource = state.key === action.key;
      return {
        ...state,
        key: action.key,
        activeRequestId: action.requestId,
        data: sameSource ? state.data : null,
        error: "",
        loading: true,
        stale: sameSource && Boolean(state.data) ? state.stale : false,
      };
    }
    case "success": {
      if (action.key !== state.key || action.requestId !== state.activeRequestId) return state;
      if (Number(action.data?.id) !== Number(action.lotId)) return {
        ...state,
        error: "Dữ liệu bãi không khớp với bãi đang chọn.",
        loading: false,
        stale: Boolean(state.data),
      };
      if (state.data && snapshotVersionOf(action.data) < snapshotVersionOf(state.data))
        return { ...state, loading: false };
      return { ...state, data: action.data, error: "", loading: false, stale: false };
    }
    case "failure":
      if (action.key !== state.key || action.requestId !== state.activeRequestId) return state;
      return {
        ...state,
        error: action.error || "Không thể đồng bộ dữ liệu bãi đỗ.",
        loading: false,
        stale: Boolean(state.data),
      };
    default:
      return state;
  }
}

export { snapshotVersionOf };
