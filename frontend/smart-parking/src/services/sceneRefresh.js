export const SCENE_REFRESH_INTERVAL_MS = 15000;
export const SCENE_RETRY_BASE_MS = 5000;
export const SCENE_RETRY_MAX_MS = 60000;

export function sceneRetryDelay(attempt, random = Math.random) {
  const exponent = Math.max(0, Number(attempt || 1) - 1);
  const base = Math.min(SCENE_RETRY_MAX_MS, SCENE_RETRY_BASE_MS * (2 ** exponent));
  const jitter = 0.8 + Math.min(1, Math.max(0, random())) * 0.4;
  return Math.min(SCENE_RETRY_MAX_MS, Math.round(base * jitter));
}

export function createSceneRefreshController({
  load,
  onOffline = () => {},
  cancelInFlight = () => {},
  windowTarget = globalThis.window,
  documentTarget = globalThis.document,
  navigatorTarget = globalThis.navigator,
  setTimer = globalThis.setTimeout,
  clearTimer = globalThis.clearTimeout,
  random = Math.random,
}) {
  let stopped = false;
  let timer = null;
  let inFlight = false;
  let refreshPending = false;
  let retryAttempt = 0;

  const isOnline = () => navigatorTarget?.onLine !== false;
  const clearScheduled = () => {
    if (timer !== null) clearTimer(timer);
    timer = null;
  };
  const schedule = delay => {
    clearScheduled();
    if (!stopped && isOnline()) timer = setTimer(() => { void refresh(); }, delay);
  };

  async function refresh() {
    clearScheduled();
    if (stopped) return;
    if (!isOnline()) {
      cancelInFlight();
      onOffline();
      return;
    }
    if (inFlight) {
      refreshPending = true;
      return;
    }

    inFlight = true;
    let succeeded = false;
    try {
      await load();
      retryAttempt = 0;
      succeeded = true;
    } catch {
      retryAttempt += 1;
    } finally {
      inFlight = false;
      if (stopped) return;
      if (refreshPending) {
        refreshPending = false;
        void refresh();
        return;
      }
      if (!isOnline()) {
        onOffline();
        return;
      }
      schedule(succeeded ? SCENE_REFRESH_INTERVAL_MS : sceneRetryDelay(retryAttempt, random));
    }
  }

  const handleOnline = () => {
    retryAttempt = 0;
    void refresh();
  };
  const handleOffline = () => {
    clearScheduled();
    cancelInFlight();
    onOffline();
  };
  const handleFocus = () => { void refresh(); };
  const handleVisibility = () => {
    if (documentTarget?.visibilityState === "visible") void refresh();
  };

  windowTarget?.addEventListener?.("online", handleOnline);
  windowTarget?.addEventListener?.("offline", handleOffline);
  windowTarget?.addEventListener?.("focus", handleFocus);
  documentTarget?.addEventListener?.("visibilitychange", handleVisibility);
  void refresh();

  return {
    refresh,
    stop() {
      stopped = true;
      clearScheduled();
      cancelInFlight();
      windowTarget?.removeEventListener?.("online", handleOnline);
      windowTarget?.removeEventListener?.("offline", handleOffline);
      windowTarget?.removeEventListener?.("focus", handleFocus);
      documentTarget?.removeEventListener?.("visibilitychange", handleVisibility);
    },
  };
}
