# TM:PE Feature Sync Checklist

Use this checklist when verifying TM:PE synchronisation in a CSM session.

## Pre-flight

- [ ] CSM and TM:PE load without Harmony errors in the game log.
- [ ] `CSM.TmpeSync` appears in the mod list and the log file `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\CSM.TmpeSync\log-YYYY-MM-DD.log` (UTC+02:00) is created.
- [ ] All participating clients run the same TM:PE version as the host.

## Lane mapping infrastructure

- [ ] After the host enters the city, the log contains `Lane mapping snapshot broadcast`.
- [ ] Joining clients log `Lane mapping snapshot imported` and no `reason=mapping_missing` warnings appear afterwards.
- [ ] Upgrading or bulldozing a road writes `Lane mapping broadcast` or `Lane mapping removed` entries and the same manoeuvre is visible on every client.

## Core features

- [ ] **Speed limits** – Change a lane speed on the host; every client sees the updated value and vehicles respect it. Undo by resetting to default.
- [ ] **Lane arrows** – Modify turn arrows on an intersection. Confirm the host broadcasts the final bitmask and clients converge.
- [ ] **Lane connections** – Force a manual lane connection and ensure the same path renders on each client.
- [ ] **Vehicle restrictions** – Restrict a lane to a single vehicle type. Vehicles on all instances obey the new rule.
- [ ] **Junction restrictions** – Toggle “enter when blocked” and “turn on red” flags. Lights and behaviour stay in sync.
- [ ] **Priority signs** – Place stop/yield/priority signs. Verify the applied message matches the TM:PE UI on clients.
- [ ] **Parking restrictions** – Disable parking per direction on a segment; cars disappear on every instance.
- [ ] **Toggle traffic lights** - Toggle a vanilla traffic light on the host; every client should see the same icon state.
- [ ] **Timed traffic lights** - Not supported by TM:PE sync (feature currently unavailable).
- [ ] **Manual traffic lights** - Not supported by TM:PE sync (feature currently unavailable).
- [ ] **Hide Crosswalks** - Hide a crosswalk on the host and confirm the visual change persists on every client through the session cache.

## Failure scenarios

- [ ] Break a lane/segment/node intentionally. Requests targeting missing entities are rejected and the client receives a `RequestRejected` response.
- [ ] Run a client without TM:PE to ensure host-driven broadcasts still converge and no unauthorised edits slip through.

## Post-session

- [ ] Review the log file for `WARN` or `ERROR` entries that hint at missing API hooks.
- [ ] Archive logs when reporting issues; the rotating files include full debug traces for support.
