# TM:PE synchronisation checklist

The following overview covers all **Traffic Manager: President Edition (TM:PE)**
features that CSM.TmpeSync already synchronises via requests, applied events,
snapshots and the `TmpeAdapter`. Each section lists the relevant aspects so you
can validate that every variant (per lane, per direction, per node, …) is handled
in multiplayer.

## Speed limits
- Synchronised per lane through `SetSpeedLimitRequest` → `SpeedLimitApplied`. Each
  request validates that the lane exists on the server, applies the change inside
the simulation and broadcasts the confirmed result to all clients.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L14-L55】
- `TmpeAdapter` stores the target speed for each lane ID and exposes the default
  50 km/h value if no individual limit is set, allowing TM:PE to translate bulk
  actions (e.g. "both directions") into lane commands.【F:src/Tmpe/TmpeAdapter.cs†L67-L138】
- Snapshot export iterates over every lane, reads the stored km/h value and sends
  it as `SpeedLimitApplied` so new players receive all existing limits.【F:src/Snapshot/SpeedLimitSnapshotProvider.cs†L11-L22】
- Missing lanes during import are handled by deferred operations once the lane
  object becomes available.【F:src/Net/Handlers/SpeedLimitAppliedHandler.cs†L9-L23】【F:src/Net/Handlers/SpeedLimitDeferredOp.cs†L1-L34】

## Lane arrows
- Left/straight/right are transmitted per lane via the `LaneArrowFlags`
  combination. Handlers remove flags entirely when needed to restore the vanilla
  state without TM:PE.【F:src/Tmpe/TmpeAdapter.cs†L114-L205】【F:src/Net/Contracts/States/TmpeStates.cs†L6-L14】
- Snapshot export skips `None` to avoid broadcasting the default state.【F:src/Snapshot/LaneArrowSnapshotProvider.cs†L10-L23】
- Deferred operations ensure that late lanes receive their arrows.【F:src/Net/Handlers/LaneArrowDeferredOp.cs†L1-L34】

## Lane connector
- Each source lane stores distinct target lane IDs; empty lists remove the
  connection. This covers simple 1:1 assignments as well as multiple
  connections.【F:src/Tmpe/TmpeAdapter.cs†L207-L236】
- Snapshot export replays every target ID to all clients.【F:src/Snapshot/LaneConnectionsSnapshotProvider.cs†L10-L22】
- Deferred operations handle connections whose lanes are not loaded yet.【F:src/Net/Handlers/LaneConnectionsDeferredOp.cs†L1-L36】

## Vehicle restrictions
- Supported vehicle classes: passenger car, truck, bus, taxi, service, emergency
  and tram. Every combination is synchronised as a bit mask; the adapter removes
  `None` entries automatically.【F:src/Net/Contracts/States/TmpeStates.cs†L16-L28】【F:src/Tmpe/TmpeAdapter.cs†L140-L205】
- Snapshot export only sends restrictions that are actually set.【F:src/Snapshot/VehicleRestrictionsSnapshotProvider.cs†L10-L28】
- Deferred operations handle missing lanes.【F:src/Net/Handlers/VehicleRestrictionsDeferredOp.cs†L1-L36】

## Junction restrictions
- All five toggles (U-turn, lane changing, blocking, pedestrians, turn on red)
  are stored in `JunctionRestrictionsState`. `IsDefault()` removes the entry once
  every option is allowed again.【F:src/Net/Contracts/States/TmpeStates.cs†L30-L52】【F:src/Tmpe/TmpeAdapter.cs†L276-L320】
- Snapshots transmit the full state per node.【F:src/Snapshot/JunctionRestrictionsSnapshotProvider.cs†L10-L31】

## Priority signs
- The combination of node ID and segment ID mirrors TM:PE's distinction between
  main road, yield and stop. `PrioritySignType.None` removes the entry
  completely.【F:src/Tmpe/TmpeAdapter.cs†L325-L370】【F:src/Net/Contracts/States/TmpeStates.cs†L55-L62】
- Snapshots replicate all configured signs.【F:src/Snapshot/PrioritySignSnapshotProvider.cs†L10-L32】

## Parking restrictions
- Direction-dependent flags (`AllowParkingForward/Backward`) ensure that "both",
  "forward only" or "backward only" are represented correctly. The default is
  parking allowed in both directions.【F:src/Net/Contracts/States/TmpeStates.cs†L64-L85】【F:src/Tmpe/TmpeAdapter.cs†L372-L419】
- Snapshots synchronise all configured bans.【F:src/Snapshot/ParkingRestrictionSnapshotProvider.cs†L10-L35】
- Deferred operations cover segments that are loaded later.【F:src/Net/Handlers/ParkingRestrictionDeferredOp.cs†L1-L32】

## Hide Crosswalks
- Each node/segment pair tracks whether the crosswalk is hidden. When the mod is
  not available locally the adapter falls back to internal storage, otherwise it
  reflects the Hide Crosswalks API.【F:src/HideCrosswalks/HideCrosswalksAdapter.cs†L8-L76】
- Server-side requests apply the change and broadcast the result as
  `CrosswalkHiddenApplied` to all clients.【F:src/Net/Handlers/SetCrosswalkHiddenRequestHandler.cs†L10-L67】【F:src/Net/Contracts/Applied/CrosswalkHiddenApplied.cs†L6-L12】
- Snapshots forward all hidden crosswalks to joining players, while deferred
  operations buffer missing network objects.【F:src/Snapshot/CrosswalkHiddenSnapshotProvider.cs†L6-L25】【F:src/Net/Handlers/CrosswalkHiddenDeferredOp.cs†L1-L35】

## Timed traffic lights
- The state covers activation, phase count and cycle length. Disabled setups are
  removed so vanilla lights remain untouched.【F:src/Net/Contracts/States/TmpeStates.cs†L87-L108】【F:src/Tmpe/TmpeAdapter.cs†L421-L468】
- Snapshots replicate the full configuration per node.【F:src/Snapshot/TimedTrafficLightSnapshotProvider.cs†L10-L31】
- Deferred operations wait until the junction is loaded before applying the
  change.【F:src/Net/Handlers/TimedTrafficLightDeferredOp.cs†L1-L32】

## Shared infrastructure
- Every handler is host authoritative: only the server processes requests and
  then broadcasts the confirmed result, preventing conflicts between players.【F:src/Net/Handlers/SetSpeedLimitRequestHandler.cs†L16-L45】【F:src/Net/Handlers/SetLaneArrowRequestHandler.cs†L15-L46】
- `DeferredApply` buffers operations for missing lanes/segments/nodes and retries
  them once the network objects appear.【F:src/Util/DeferredApply.cs†L1-L58】
- `TmpeAdapter` encapsulates all access so the multiplayer logic remains focused
  on API contracts instead of duplicating TM:PE internals.【F:src/Tmpe/TmpeAdapter.cs†L51-L468】

Use this checklist to track TM:PE-related functionality and validate that special
cases are covered. If TM:PE introduces new tools (for example additional
editors), extend the list following the same pattern.
