# Sprint 5 - 3D reconnect and Entry-to-Exit evidence

## Scope

This check proves that the 3D view remains read-only over the authoritative Core
snapshot and follows one slot through the operational flow:

```text
EMPTY -> RESERVED -> OCCUPIED -> EMPTY
```

It also proves that an offline/stale snapshot cannot be used for a write and that
the browser performs a full reload immediately after reconnecting.

## Automated checks

From `frontend/smart-parking` run:

```bash
npx vitest run src/services/parkingScene.test.js --config vitest.config.js
node --test ../tests/parking-3d-viewer.test.cjs
```

The snapshot integration test covers the state sequence above and verifies that a
late `RESERVED` snapshot cannot replace a newer `OCCUPIED` snapshot.

## Shared-environment prerequisites

- Core API and frontend run from the same commit.
- The Core API is connected to the Sprint test database.
- One active lot has a floor, zone, and available slot.
- Test accounts exist for gate staff, slot-verifier staff, and a checkout-capable
  staff member.
- The browser Network panel is open with `Preserve log` enabled.

Record the commit, environment, lot ID, floor ID, slot ID, session ID, account,
and browser before starting.

## Entry-to-Exit scenario

1. Open `/app/parking?lotId=<lotId>&floorId=<floorId>` and select an empty slot.
2. Record the response from `GET /api/v1/parking-lots/<lotId>/scene`:
   `snapshotVersion`, `generatedAtUtc`, slot status, and slot ID.
3. Complete gate entry and dispatch that session to the selected slot.
4. Return to the 3D page or focus its tab. Confirm a new full snapshot is loaded
   immediately and the slot is `RESERVED` and protected from manual editing.
5. Complete physical slot verification. Confirm a newer snapshot shows
   `OCCUPIED`, the expected session ID, and the plate only for an authorized user.
6. Complete exit recognition, payment clearance, and checkout.
7. Confirm the next snapshot shows `EMPTY`, has no session ID, and makes the slot
   available again.

For every step save the network response and a screenshot containing the 3D/list
state plus the displayed snapshot version.

## Reconnect scenario

1. Start from a fresh snapshot, then set the browser Network panel to `Offline`.
2. Confirm the last good snapshot stays visible, the stale/offline warning is
   shown, and status-changing controls are disabled.
3. While the browser is offline, change the slot from a second authorized device.
4. Restore `Online` on the first browser.
5. Confirm it sends a scene request immediately without waiting for the normal
   15-second interval.
6. Confirm the returned higher-version snapshot replaces the stale view and
   enables writes again. A lower version must not roll the UI back.

## Expected evidence

| Step | Expected status | Required fields |
|---|---|---|
| Initial | `EMPTY` | version, time, lot/floor/slot IDs |
| Dispatch | `RESERVED` | newer version, protected slot |
| Verified parked | `OCCUPIED` | newer version, session ID, authorized plate |
| Offline | last good status + stale warning | write controls disabled |
| Reconnected | current authoritative status | immediate full snapshot request |
| Checked out | `EMPTY` | newest version, no session ID, available slot |

If a step fails, record expected/actual, correlation ID, request/response, owner,
severity, and reproduction steps. Do not repair Session, Payment, or Recognition
state directly from the 3D client.
