import { describe, expect, it } from "vitest";
import { sceneToDefinition } from "./parkingScene.js";
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
});
