import { describe, expect, it, vi } from "vitest";
import { sceneToDefinition } from "./parkingScene.js";
import { createSceneRefreshController, sceneRetryDelay } from "./sceneRefresh.js";
import { initialSceneSnapshotState, sceneRequestKey, sceneSnapshotReducer } from "./sceneSnapshot.js";

function snapshot(version, id = 1, overrides = {}) {
  return { id, snapshotVersion: version, generatedAtUtc: `2026-11-17T00:00:0${version}Z`, floors: [], ...overrides };
}

describe("parking scene contract", () => {
  it("maps multiple zones and uses each slot's authoritative zone", () => {
    const definition = sceneToDefinition({
      id: 1,
      name: "Bãi 1",
      floors: [{
        id: 10,
        name: "Tầng 1",
        floorNumber: 1,
        zones: [
          { id: 100, name: "Khách", type: "TRANSIENT" },
          { id: 101, name: "Vé tháng", type: "FIXED" },
        ],
        slots: [
          { id: 1, code: "A-01", zoneId: 100, zoneName: "Khách" },
          { id: 2, code: "M-01", zoneId: 101, zoneName: "Vé tháng" },
        ],
      }],
    });

    expect(definition.liveFloors[0].zones).toHaveLength(2);
    expect(definition.liveFloors[0].slots.map(x => x.zoneName)).toEqual(["Khách", "Vé tháng"]);
    expect(definition.liveFloors[0].slots.every(x => x.floorId === 10)).toBe(true);
  });

  it("keeps temporary compatibility with the former single-zone response", () => {
    const definition = sceneToDefinition({
      id: 1,
      name: "Bãi cũ",
      floors: [{ id: 10, name: "Tầng 1", floorNumber: 1, zone: { id: 100, name: "Khu cũ", type: "TRANSIENT" }, slots: [{ id: 1, code: "A-01" }] }],
    });

    expect(definition.liveFloors[0].zones).toHaveLength(1);
    expect(definition.liveFloors[0].slots[0].zoneName).toBe("Khu cũ");
  });
});

describe("scene snapshot stale guard", () => {
  it("rejects an older request that completes after the active request", () => {
    const key = sceneRequestKey(1, "user:ADMIN");
    let state = sceneSnapshotReducer(initialSceneSnapshotState, { type: "begin", key, requestId: 1 });
    state = sceneSnapshotReducer(state, { type: "begin", key, requestId: 2 });
    state = sceneSnapshotReducer(state, { type: "success", key, requestId: 2, lotId: 1, data: snapshot(2) });
    state = sceneSnapshotReducer(state, { type: "success", key, requestId: 1, lotId: 1, data: snapshot(1) });

    expect(state.data.snapshotVersion).toBe(2);
  });

  it("rejects a lower snapshot version from the active request", () => {
    const key = sceneRequestKey(1, "user:ADMIN");
    let state = { ...initialSceneSnapshotState, key, activeRequestId: 1, data: snapshot(8) };
    state = sceneSnapshotReducer(state, { type: "begin", key, requestId: 2 });
    state = sceneSnapshotReducer(state, { type: "success", key, requestId: 2, lotId: 1, data: snapshot(7) });

    expect(state.data.snapshotVersion).toBe(8);
  });

  it("does not accept a response for the previous lot", () => {
    const oldKey = sceneRequestKey(1, "user:ADMIN");
    const newKey = sceneRequestKey(2, "user:ADMIN");
    let state = sceneSnapshotReducer(initialSceneSnapshotState, { type: "begin", key: oldKey, requestId: 1 });
    state = sceneSnapshotReducer(state, { type: "begin", key: newKey, requestId: 2 });
    state = sceneSnapshotReducer(state, { type: "success", key: oldKey, requestId: 1, lotId: 1, data: snapshot(3) });

    expect(state.key).toBe(newKey);
    expect(state.data).toBeNull();
  });

  it("keeps the last good snapshot and marks it stale after a refresh failure", () => {
    const key = sceneRequestKey(1, "user:ADMIN");
    let state = { ...initialSceneSnapshotState, key, activeRequestId: 1, data: snapshot(5) };
    state = sceneSnapshotReducer(state, { type: "begin", key, requestId: 2 });
    state = sceneSnapshotReducer(state, { type: "failure", key, requestId: 2, error: "Mất kết nối" });

    expect(state.data.snapshotVersion).toBe(5);
    expect(state.stale).toBe(true);
    expect(state.error).toBe("Mất kết nối");
  });

  it("rejects a payload whose lot id does not match the request", () => {
    const key = sceneRequestKey(1, "user:ADMIN");
    let state = sceneSnapshotReducer(initialSceneSnapshotState, { type: "begin", key, requestId: 1 });
    state = sceneSnapshotReducer(state, { type: "success", key, requestId: 1, lotId: 1, data: snapshot(1, 2) });

    expect(state.data).toBeNull();
    expect(state.error).toContain("không khớp");
  });

  it("clears data from the previous source when the selected lot goes offline", () => {
    const oldKey = sceneRequestKey(1, "user:ADMIN");
    const newKey = sceneRequestKey(2, "user:ADMIN");
    const state = sceneSnapshotReducer(
      { ...initialSceneSnapshotState, key: oldKey, data: snapshot(5) },
      { type: "offline", key: newKey },
    );

    expect(state.data).toBeNull();
    expect(state.error).toContain("ngoại tuyến");
  });
});

describe("scene reconnect scheduling", () => {
  it("uses capped exponential backoff with jitter", () => {
    expect(sceneRetryDelay(1, () => 0.5)).toBe(5000);
    expect(sceneRetryDelay(2, () => 0.5)).toBe(10000);
    expect(sceneRetryDelay(9, () => 0.5)).toBe(60000);
    expect(sceneRetryDelay(9, () => 1)).toBe(60000);
    expect(sceneRetryDelay(1, () => 0)).toBe(4000);
    expect(sceneRetryDelay(1, () => 1)).toBe(6000);
  });

  it("does not poll offline and reloads immediately when the browser reconnects", async () => {
    const windowTarget = new EventTarget();
    const documentTarget = new EventTarget();
    documentTarget.visibilityState = "hidden";
    const navigatorTarget = { onLine: false };
    const load = vi.fn().mockResolvedValue(undefined);
    const onOffline = vi.fn();
    const setTimer = vi.fn(() => 1);
    const controller = createSceneRefreshController({
      load,
      onOffline,
      windowTarget,
      documentTarget,
      navigatorTarget,
      setTimer,
      clearTimer: vi.fn(),
      random: () => 0.5,
    });

    expect(load).not.toHaveBeenCalled();
    expect(onOffline).toHaveBeenCalledOnce();
    expect(setTimer).not.toHaveBeenCalled();

    navigatorTarget.onLine = true;
    windowTarget.dispatchEvent(new Event("online"));
    await Promise.resolve();
    await Promise.resolve();

    expect(load).toHaveBeenCalledOnce();
    expect(setTimer).toHaveBeenCalledWith(expect.any(Function), 15000);
    controller.stop();
  });

  it("reloads a snapshot as soon as a visible tab receives focus", async () => {
    const windowTarget = new EventTarget();
    const documentTarget = new EventTarget();
    documentTarget.visibilityState = "visible";
    const load = vi.fn().mockResolvedValue(undefined);
    const controller = createSceneRefreshController({
      load,
      windowTarget,
      documentTarget,
      navigatorTarget: { onLine: true },
      setTimer: vi.fn(() => 1),
      clearTimer: vi.fn(),
    });
    await Promise.resolve();
    await Promise.resolve();

    windowTarget.dispatchEvent(new Event("focus"));
    await Promise.resolve();
    await Promise.resolve();

    expect(load).toHaveBeenCalledTimes(2);
    controller.stop();
  });
});

describe("parking scene entry-to-exit snapshot integration", () => {
  function flowSnapshot(version, status, overrides = {}) {
    return {
      id: 1,
      name: "Bãi tích hợp",
      snapshotVersion: version,
      generatedAtUtc: `2026-11-24T00:00:0${version}Z`,
      floors: [{
        id: 10,
        name: "B1",
        floorNumber: -1,
        zones: [{ id: 100, name: "Khách", type: "TRANSIENT" }],
        slots: [{
          id: 1000,
          code: "B1-01",
          zoneId: 100,
          zoneName: "Khách",
          status,
          physicalStatus: status === "OCCUPIED" ? "OCCUPIED" : "EMPTY",
          isAvailable: status === "EMPTY",
          isProtected: status !== "EMPTY",
          plate: null,
          sessionId: null,
          ...overrides,
        }],
      }],
    };
  }

  function accept(state, requestId, data) {
    const key = sceneRequestKey(1, "staff:STAFF");
    const loading = sceneSnapshotReducer(state, { type: "begin", key, requestId });
    return sceneSnapshotReducer(loading, { type: "success", key, requestId, lotId: 1, data });
  }

  function currentSlot(state) {
    return sceneToDefinition(state.data).liveFloors[0].slots[0];
  }

  it("follows EMPTY -> RESERVED -> OCCUPIED -> EMPTY without accepting a stale rollback", () => {
    let state = initialSceneSnapshotState;

    state = accept(state, 1, flowSnapshot(1, "EMPTY"));
    expect(currentSlot(state)).toMatchObject({ status: "EMPTY", isAvailable: true, isProtected: false });

    state = accept(state, 2, flowSnapshot(2, "RESERVED"));
    expect(currentSlot(state)).toMatchObject({ status: "RESERVED", isAvailable: false, isProtected: true });

    state = accept(state, 3, flowSnapshot(3, "OCCUPIED", { plate: "51F-123.45", sessionId: 99 }));
    expect(currentSlot(state)).toMatchObject({ status: "OCCUPIED", plate: "51F-123.45", sessionId: 99 });

    state = accept(state, 4, flowSnapshot(2, "RESERVED"));
    expect(currentSlot(state)).toMatchObject({ status: "OCCUPIED", sessionId: 99 });

    state = accept(state, 5, flowSnapshot(4, "EMPTY"));
    expect(currentSlot(state)).toMatchObject({ status: "EMPTY", isAvailable: true, isProtected: false, sessionId: null });
  });
});
