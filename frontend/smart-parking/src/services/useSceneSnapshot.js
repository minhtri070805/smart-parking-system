import { useEffect, useReducer, useRef } from "react";
import { apiRequest } from "./api.js";
import { createSceneRefreshController } from "./sceneRefresh.js";
import { initialSceneSnapshotState, sceneRequestKey, sceneSnapshotReducer } from "./sceneSnapshot.js";

export function useSceneSnapshot(lotId, revision, identity) {
  const [state, dispatch] = useReducer(sceneSnapshotReducer, initialSceneSnapshotState);
  const requestSequence = useRef(0);

  useEffect(() => {
    if (!lotId) {
      dispatch({ type: "reset" });
      return undefined;
    }

    const key = sceneRequestKey(lotId, identity);
    let active = true;
    let requestController = null;

    const refreshController = createSceneRefreshController({
      async load() {
        const requestId = ++requestSequence.current;
        requestController = new AbortController();
        dispatch({ type: "begin", key, requestId });
        try {
          const result = await apiRequest(`/v1/parking-lots/${lotId}/scene`, { signal: requestController.signal });
          if (!result?.data) throw new Error("API không trả về snapshot bãi đỗ.");
          if (active && !requestController.signal.aborted)
            dispatch({ type: "success", key, requestId, lotId, data: result.data });
        } catch (error) {
          if (active && !requestController?.signal.aborted)
            dispatch({ type: "failure", key, requestId, error: error.message });
          throw error;
        } finally {
          requestController = null;
        }
      },
      cancelInFlight() {
        requestController?.abort();
      },
      onOffline() {
        if (active) dispatch({ type: "offline", key });
      },
    });

    return () => {
      active = false;
      refreshController.stop();
    };
  }, [lotId, revision, identity]);

  return state;
}
