# TM:PE Feature Sync Checklist

Use this checklist when verifying TM:PE synchronisation in a CSM session.

## Pre-flight

- [ ] CSM and TM:PE load without Harmony errors in the game log.
- [ ] `CSM.TmpeSync` appears in the mod list and the log file `%APPDATA%\CSM.TmpeSync\csm.tmpe-sync.log` is created.
- [ ] All participating clients run the same TM:PE version as the host.

## Core features

- [ ] **Speed limits** – Change a lane speed on the host; every client sees the updated value and vehicles respect it. Undo by resetting to default.
- [ ] **Lane arrows** – Modify turn arrows on an intersection. Confirm the host broadcasts the final bitmask and clients converge.
- [ ] **Lane connections** – Force a manual lane connection and ensure the same path renders on each client.
- [ ] **Vehicle restrictions** – Restrict a lane to a single vehicle type. Vehicles on all instances obey the new rule.
- [ ] **Junction restrictions** – Toggle “enter when blocked” and “turn on red” flags. Lights and behaviour stay in sync.
- [ ] **Priority signs** – Place stop/yield/priority signs. Verify the applied message matches the TM:PE UI on clients.
- [ ] **Parking restrictions** – Disable parking per direction on a segment; cars disappear on every instance.
- [ ] **Timed traffic lights** - Create or delete a timed light plan. State should broadcast immediately once the bridge applies it on the host.
- [ ] **Manual traffic lights** - Toggle manual control. The icon and behaviour stay aligned.
- [ ] **Hide Crosswalks** - Hide a crosswalk on the host and confirm the visual change persists on every client through the session cache.

## Failure scenarios

- [ ] Break a lane/segment/node intentionally. Requests targeting missing entities are rejected and the client receives a `RequestRejected` response.
- [ ] Run a client without TM:PE to ensure host-driven broadcasts still converge and no unauthorised edits slip through.

## Post-session

- [ ] Review the log file for `WARN` or `ERROR` entries that hint at missing API hooks.
- [ ] Archive logs when reporting issues; the rotating files capture detailed bridge diagnostics when `CSM_TMPE_SYNC_DEBUG=1`.
